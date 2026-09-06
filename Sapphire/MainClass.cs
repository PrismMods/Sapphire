using System;
using System.Collections.Generic;
using System.Reflection;
using Sapphire.UI;
using Sapphire.UI.Pages;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityModManagerNet;

namespace Sapphire
{
    public static class MainClass
    {
        public static bool IsEnabled { get; private set; }
        public static Settings Settings { get; private set; }
        // Editor-suite master switch (the corner button) — every editor feature gates on this.
        internal static bool EditorSuiteOn => Settings == null || Settings.EditorSuiteOn;
        // Wheel delta for Sapphire's own scroll surfaces, honoring the invert setting.
        internal static float WheelY
        {
            get
            {
                float w = UnityEngine.Input.mouseScrollDelta.y;
                return Settings != null && Settings.InvertScroll ? -w : w;
            }
        }
        public static UnityModManager.ModEntry.ModLogger Logger { get; private set; }
        public static string ModPath { get; private set; }
        // Info.json's Version, read by the updater to decide what counts as newer.
        public static string ModVersion { get; private set; }

        private static Harmony harmony;
        private static List<FontLoader.FontEntry> availableFonts = new List<FontLoader.FontEntry>();
        private static UnityModManager.ModEntry _modEntry;
        // Retry init on first scene load when koren UMM loaded us before game statics were ready.
        private static bool _deferredApplyPending;
        private static bool _forceReloadPending;
        private static GameObject _tickerGo;

        internal static void Setup(UnityModManager.ModEntry modEntry)
        {
            Logger = modEntry.Logger;
            ModPath = modEntry.Path;
            try { ModVersion = modEntry.Info.Version; } catch { ModVersion = ""; }
            Settings = Settings.Load<Settings>(modEntry);
            Settings.EnsureDefaults();
            modEntry.OnToggle = OnToggle;
            modEntry.OnGUI = OnGUI;
            modEntry.OnSaveGUI = OnSaveGUI;
            modEntry.OnUpdate = (_, __) =>
            {
                UICore.HandleUpdate();
                if (_forceReloadPending) { _forceReloadPending = false; DoForceReload(); }
            };
            // Opting into OnUnload makes the mod hot-reloadable.
            modEntry.OnUnload = OnUnload;
        }

        private static bool OnToggle(UnityModManager.ModEntry modEntry, bool value)
        {
            IsEnabled = value;
            if (value) StartMod(modEntry);
            else StopMod(modEntry);
            return true;
        }

        private static void OnGUI(UnityModManager.ModEntry modEntry)
        {
            GUILayout.Label("Settings live in the in-game panel (Ctrl+E).");
            if (GUILayout.Button("Open Settings Panel", GUILayout.ExpandWidth(false)))
                UICore.Open();
        }

        private static void OnSaveGUI(UnityModManager.ModEntry modEntry) => Settings.Save(modEntry);

        // Persist Settings on demand (custom shapes / categories change outside the UMM menu).
        internal static void SaveSettings()
        {
            try { if (Settings != null && _modEntry != null) Settings.Save(_modEntry); } catch { }
        }

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            OnSaveGUI(modEntry);
            if (IsEnabled) StopMod(modEntry);
            return true;
        }

        private static void StartMod(UnityModManager.ModEntry modEntry)
        {
            _modEntry = modEntry;
            SapphireLog.Init();
            harmony = new Harmony(modEntry.Info.Id);
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            SceneManager.sceneLoaded += OnSceneLoaded;

            if (IsEngineReady() && TryEagerInit())
                return;
            _deferredApplyPending = true;
        }

        // Time.frameCount == 0 during koren UMM's static-ctor injection window; asset APIs
        // crash the engine there.
        private static bool IsEngineReady()
        {
            try { return Time.frameCount > 0; }
            catch { return false; }
        }

        private static bool TryEagerInit()
        {
            try
            {
                availableFonts = FontLoader.ScanFonts(_modEntry.Path);
                BuildUI();
                EnsureTicker();
                return true;
            }
            catch (Exception ex)
            {
                SapphireLog.Log("Eager init deferred (game/engine not ready): " + ex.Message);
                return false;
            }
        }

        private static void BuildUI()
        {
            UICore.Initialize(_modEntry, Settings, () => { }, availableFonts);
            UICore.SetTabBuilder(BuildTabs); // re-runnable, so RebuildBody() can re-localize the body
            BuildTabs();
        }

        private static void BuildTabs() => UICore.Tabs.AddTab("Editor", PageEditor.Build);

        // The editor features tick per frame; Sapphire has no overlay component to ride,
        // so it brings its own DDOL ticker.
        private static void EnsureTicker()
        {
            if (_tickerGo != null) return;
            _tickerGo = new GameObject("SapphireTicker");
            UnityEngine.Object.DontDestroyOnLoad(_tickerGo);
            _tickerGo.AddComponent<SapphireTicker>();
        }

        private class SapphireTicker : MonoBehaviour
        {
            private int _esFrame;

            // Effective UI language last seen (Loc.Korean already folds in UiLanguage AND, under
            // Auto, the game's RDString.language). Primed on the first frame so we never fire on
            // startup — only on an actual flip.
            private bool _langPrimed;
            private bool _lastKorean;

            /* One cheap read per frame; acts only on the FLIP. Already-built editor overlays
               bake their Loc.T strings at build time, so a language change leaves them stale.
               Dropping them (in-editor only) makes each rebuild with the new language on its
               very next Tick below — same frame, since this runs before the module ticks. */
            private void CheckLanguageFlip()
            {
                bool kor;
                try { kor = Loc.Korean; } catch { return; }
                if (!_langPrimed) { _lastKorean = kor; _langPrimed = true; return; }
                if (kor == _lastKorean) return;
                _lastKorean = kor;

                bool inEditor;
                try { inEditor = scnEditor.instance != null; } catch { inEditor = false; }
                if (!inEditor) return; // overlays rebuild fresh on entering the editor anyway
                try { DisposeEditorModules(); }
                catch (Exception ex) { SapphireLog.Log("[lang] overlay re-localize failed: " + ex); }
            }

            /* Per-module tick timing: accumulated and debug-logged every ~15s so lag
               reports point at a module instead of "the mod". Overhead is one Stopwatch
               restart per module per frame. */
            private static readonly string[] PerfNames =
            {
                // Slot 2 is retired with the editor-UI layout module; Acc(2) is never called, so it
                // stays at 0 and the report (which skips sub-threshold slots) never prints it.
                "Tweaks", "EditorEvents", "(retired)", "EditorChrome",
                "EditorInspector", "EditorPopups", "EditorToolbar", "EditorTileMenu",
                "EditorCopyPanel", "EditorCameraPath", "EditorPitch", "EditorLevelMenu",
                "EditorGameSettings", "EditorVfxPreview", "EditorHelp", "EditorPresets",
                "EditorEasePicker", "EditorBezier", "EditorGraph", "EditorFilterPicker",
                "EditorMagicShape", "EditorTrackTools", "EditorDecoTools", "EditorMasterSwitch",
                "EditorEventPanel", "EditorEventSelector", "EditorQuickChart", "EditorShapeLibrary",
            };
            // Reused by the UI census so it doesn't allocate an array per canvas.
            private static readonly List<UnityEngine.UI.Graphic> _censusBuf = new List<UnityEngine.UI.Graphic>();
            private static readonly double[] _perfMs = new double[28];
            private static readonly double[] _perfMax = new double[28];
            private static int _perfFrames;

            /* Lap timer: banks the elapsed slice into module i (no allocations). Reads the raw
               timestamp once per lap instead of Elapsed + Restart (two platform timer reads,
               54 per frame across 27 modules) and converts to ms only in the report block. */
            private static long _lap;
            private static readonly double TicksToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            private static void Acc(int i)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                double ms = (now - _lap) * TicksToMs;
                _lap = now;
                _perfMs[i] += ms;
                if (ms > _perfMax[i]) _perfMax[i] = ms;
            }

            private void Update()
            {
                // Keep exactly one EventSystem alive (a stray DDOL one breaks carets/typing).
                // Same cadence drains the log buffer so a burst costs one write, not hundreds.
                if (++_esFrame >= 45) { _esFrame = 0; UICore.DedupEventSystem(); SapphireLog.Flush(); }

                CheckLanguageFlip(); // drop stale-language overlays before this frame's ticks rebuild them

                // Outside the perf laps below (the arrays are sized to the editor modules) and
                // deliberately ungated by EditorSuiteOn — an update notice is session-wide, not
                // an editor feature.
                UI.UpdateToast.Tick();

                _lap = System.Diagnostics.Stopwatch.GetTimestamp();
                Tweaks.TickTileAngle(); Tweaks.TickEditorMode(); Tweaks.TickWasdPan(); Tweaks.TickControlsTip(); Acc(0);
                EditorEvents.Tick(); Acc(1);
                EditorChrome.Tick(); Acc(3);
                EditorInspector.Tick(); Acc(4);
                EditorPopups.Tick(); Acc(5);
                EditorToolbar.Tick(); Acc(6);
                EditorTileMenu.Tick(); Acc(7);
                EditorCopyPanel.Tick(); Acc(8);
                EditorCameraPath.Tick(); Acc(9);
                EditorPitch.Tick(); Acc(10);
                EditorLevelMenu.Tick(); Acc(11);
                EditorDecoInspector.Tick();
                EditorArtistPicker.Tick();
                EditorGameSettings.Tick(); Acc(12);
                EditorVfxPreview.Tick(); Acc(13);
                EditorHelp.Tick(); Acc(14);
                EditorPresets.Tick(); Acc(15);
                EditorEasePicker.Tick(); Acc(16);
                EditorBezier.Tick(); Acc(17);
                EditorGraph.Tick(); Acc(18);
                EditorFilterPicker.Tick(); Acc(19);
                EditorMagicShape.Tick(); Acc(20);
                EditorTrackTools.Tick(); Acc(21);
                EditorDecoTools.Tick(); Acc(22);
                EditorEventPanel.Tick(); Acc(24);
                EditorBulkEdit.Tick();
                EditorEventSelector.Tick(); Acc(25);
                UI.EditorDropdown.Tick(); // auto-close its full-screen blocker when the trigger's gone
                EditorQuickChart.Tick(); Acc(26);
                EditorHzTool.Tick();
                PanelLayout.Tick();
                EditorShapeLibrary.Tick(); Acc(27);
                EditorMasterSwitch.Tick(); Acc(23);
                EditorKeyHints.Tick();
                UI.PanelKit.TickFocus(); // DE-style bring-to-front for floating windows
                // Run unconditionally: when the master switch turns off, the modules hide their
                // panels this frame and TickDocks then finds nothing visible and tears its own
                // chrome (dividers / drop indicator / canvas) down — gating it on EditorSuiteOn
                // left that chrome stranded on screen.
                {
                    float below = 0f;
                    try { below = EditorEvents.BottomChromeTop; } catch { }
                    UI.PanelKit.TickDocks(56f, below > 0f ? below : 12f);
                }

                if (++_perfFrames >= 900) // ≈15s at 60fps
                {
                    var sb = new System.Text.StringBuilder();
                    for (int i = 0; i < _perfMs.Length; i++)
                    {
                        double avg = _perfMs[i] / _perfFrames;
                        if (avg < 0.15 && _perfMax[i] < 8.0) continue; // only report offenders
                        if (sb.Length > 0) sb.Append("  ");
                        sb.Append(PerfNames[i]).Append(" avg=").Append(avg.ToString("0.00"))
                          .Append("ms max=").Append(_perfMax[i].ToString("0.0")).Append("ms");
                    }
                    /* UI census: the Tick timers above miss Unity's own per-frame UI cost
                       (draw calls + EventSystem raycasting), which scales with element count.
                       Count active graphics/raycast-targets under Sapphire canvases so a steady
                       fps drop that isn't in any Tick can be attributed to UI bloat.

                       Diagnostic only, and not cheap — a whole-scene Canvas scan plus a subtree
                       walk per canvas, which showed up as a visible hitch every 15s. Gate it on
                       DebugMode so ordinary players never pay for it. */
                    if (Settings != null && Settings.DebugMode)
                    {
                        try
                        {
                            int canvases = 0, graphics = 0, rc = 0;
                            // Sorted variant + per-Canvas name marshalling was needless overhead.
                            foreach (var cv in UnityEngine.Object.FindObjectsByType<UnityEngine.Canvas>(
                                         UnityEngine.FindObjectsSortMode.None))
                            {
                                if (cv == null || !cv.isRootCanvas || !cv.gameObject.activeInHierarchy) continue;
                                if (!cv.name.StartsWith("Sapphire", System.StringComparison.Ordinal)) continue;
                                canvases++;
                                cv.GetComponentsInChildren(false, _censusBuf);
                                for (int gi = 0; gi < _censusBuf.Count; gi++)
                                {
                                    graphics++;
                                    if (_censusBuf[gi].raycastTarget) rc++;
                                }
                            }
                            sb.Append(sb.Length > 0 ? "  " : "").Append("ui canvases=").Append(canvases)
                              .Append(" graphics=").Append(graphics).Append(" raycast=").Append(rc);
                        }
                        catch { }
                    }
                    if (sb.Length > 0) SapphireLog.Debug("[perf] " + sb);
                    System.Array.Clear(_perfMs, 0, _perfMs.Length);
                    System.Array.Clear(_perfMax, 0, _perfMax.Length);
                    _perfFrames = 0;
                }
            }
        }

        internal static void RequestForceReload() => _forceReloadPending = true;

        private static void DoForceReload()
        {
            try
            {
                bool wasOpen = UICore.IsOpen;
                var oldFonts = availableFonts;
                availableFonts = FontLoader.ScanFonts(_modEntry.Path);
                UICore.Dispose();
                BuildUI();
                if (wasOpen) UICore.Open();
                FontLoader.DestroyTmpAssets(oldFonts);
                SapphireLog.Log("[Sapphire] Force reload complete (" + availableFonts.Count + " fonts)");
            }
            catch (Exception ex)
            {
                SapphireLog.Log("[Sapphire] Force reload failed: " + ex);
            }
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            UICore.ArmDedup();   // a fresh scene may bring its own EventSystem
            if (!_deferredApplyPending) return;
            if (!IsEngineReady()) return;
            try { if (RDConstants.data == null) return; }
            catch { return; }
            if (TryEagerInit())
            {
                _deferredApplyPending = false;
                SapphireLog.Log("Deferred init succeeded on scene '" + scene.name + "'");
            }
        }

        // Tears down every Sapphire editor OVERLAY module. Each rebuilds itself from settings /
        // selection on its next Tick, so this is reused two ways: StopMod (final teardown) and
        // the language-flip handler (drop the overlays so they rebuild with the new language).
        // Deliberately excludes non-overlay state that can't be recreated cheaply (Tweaks'
        // editor-mode / control-tip / tile-angle patches).
        private static void DisposeEditorModules()
        {
            EditorEvents.Dispose();
            EditorChrome.Dispose();
            EditorInspector.Dispose();
            EditorPopups.Dispose();
            EditorToolbar.Dispose();
            EditorTileMenu.Dispose();
            EditorCopyPanel.Dispose();
            EditorCameraPath.Dispose();
            EditorPitch.Dispose();
            EditorLevelMenu.Dispose();
            EditorDecoInspector.Dispose();
            EditorArtistPicker.Dispose();
            EditorBulkEdit.Dispose();
            UI.ConfirmBox.Close();
            EditorGameSettings.Dispose();
            EditorVfxPreview.Dispose();
            EditorHelp.Dispose();
            EditorPresets.Dispose();
            EditorEasePicker.Dispose();
            EditorBezier.Dispose();
            EditorGraph.Dispose();
            EditorFilterPicker.Dispose();
            EditorMagicShape.Dispose();
            EditorTrackTools.Dispose();
            EditorDecoTools.Dispose();
            EditorEventPanel.Dispose();
            EditorEventSelector.Dispose();
            EditorQuickChart.Dispose();
            EditorHzTool.Dispose();
            GhostPreview.Dispose();
            EditorMasterSwitch.Dispose();
            EditorKeyHints.Dispose();
            EditorShapeLibrary.Dispose();
            UI.PanelKit.DisposeDockChrome(); // shared dock canvas isn't owned by any module
            UI.EditorDropdown.Dispose();
        }

        /* Unpatch only OUR id, on either loader. Neither call site is portable as written:
           the instance UnpatchAll(string) is Obsolete(error:true) from HarmonyX 2.10 (won't
           COMPILE against MelonLoader's 0Harmony), and its replacement — the static
           UnpatchID(string) — is absent from native UMM's older Harmony (MissingMethodException
           at unload, leaving every patch live). UnpatchSelf() is 2.10-only for the same reason.
           Bind whichever this loader actually ships. */
        private static void UnpatchOurId()
        {
            try
            {
                var t = typeof(Harmony);
                var inst = t.GetMethod("UnpatchAll", new[] { typeof(string) });
                if (inst != null && !inst.IsStatic) { inst.Invoke(harmony, new object[] { harmony.Id }); return; }
                var stat = t.GetMethod("UnpatchID", BindingFlags.Public | BindingFlags.Static,
                                       null, new[] { typeof(string) }, null);
                if (stat != null) { stat.Invoke(null, new object[] { harmony.Id }); return; }
                SapphireLog.Log("Unpatch: no UnpatchAll(string) or UnpatchID(string) on this Harmony");
            }
            catch (Exception ex) { SapphireLog.Log("Unpatch failed: " + ex); }
        }

        private static void StopMod(UnityModManager.ModEntry modEntry)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _deferredApplyPending = false;
            Tweaks.ReleaseBismuthSuppress();
            Tweaks.DisposeEditorMode();
            Tweaks.RestoreControlsTip();
            Tweaks.DisposeTileAngle();
            DisposeEditorModules();
            UI.UpdateToast.Dispose();
            UnpatchOurId();
            if (_tickerGo != null)
            {
                UnityEngine.Object.Destroy(_tickerGo);
                _tickerGo = null;
            }
            // Runtime-created TMP assets would otherwise pile up across hot reloads.
            FontLoader.DestroyTmpAssets(availableFonts);
            UICore.Dispose();
            SapphireLog.Flush();   // don't lose the tail of the session's log
        }
    }
}
