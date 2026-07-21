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

        private static bool OnUnload(UnityModManager.ModEntry modEntry)
        {
            if (EditorUiEditor.IsActive) EditorUiEditor.Close();
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
            UICore.Tabs.AddTab("Editor", PageEditor.Build);
        }

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

            /* Per-module tick timing: accumulated and debug-logged every ~15s so lag
               reports point at a module instead of "the mod". Overhead is one Stopwatch
               restart per module per frame. */
            private static readonly string[] PerfNames =
            {
                "Tweaks", "EditorEvents", "EditorSkin", "EditorUiLayout", "EditorChrome",
                "EditorInspector", "EditorPopups", "EditorToolbar", "EditorTileMenu",
                "EditorCopyPanel", "EditorCameraPath", "EditorPitch", "EditorLevelMenu",
                "EditorGameSettings", "EditorVfxPreview", "EditorHelp", "EditorPresets",
                "EditorEasePicker", "EditorBezier", "EditorGraph", "EditorFilterPicker",
                "EditorMagicShape", "EditorTrackTools", "EditorDecoTools", "EditorMasterSwitch",
                "EditorEventPanel", "EditorEventSelector",
            };
            // Reused by the UI census so it doesn't allocate an array per canvas.
            private static readonly List<UnityEngine.UI.Graphic> _censusBuf = new List<UnityEngine.UI.Graphic>();
            private static readonly double[] _perfMs = new double[27];
            private static readonly double[] _perfMax = new double[27];
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

                _lap = System.Diagnostics.Stopwatch.GetTimestamp();
                Tweaks.TickTileAngle(); Tweaks.TickEditorMode(); Tweaks.TickWasdPan(); Acc(0);
                EditorEvents.Tick(); Acc(1);
                EditorSkin.Tick(); Acc(2);
                EditorUiLayout.Tick(); Acc(3);
                EditorChrome.Tick(); Acc(4);
                EditorInspector.Tick(); Acc(5);
                EditorPopups.Tick(); Acc(6);
                EditorToolbar.Tick(); Acc(7);
                EditorTileMenu.Tick(); Acc(8);
                EditorCopyPanel.Tick(); Acc(9);
                EditorCameraPath.Tick(); Acc(10);
                EditorPitch.Tick(); Acc(11);
                EditorLevelMenu.Tick(); Acc(12);
                EditorGameSettings.Tick(); Acc(13);
                EditorVfxPreview.Tick(); Acc(14);
                EditorHelp.Tick(); Acc(15);
                EditorPresets.Tick(); Acc(16);
                EditorEasePicker.Tick(); Acc(17);
                EditorBezier.Tick(); Acc(18);
                EditorGraph.Tick(); Acc(19);
                EditorFilterPicker.Tick(); Acc(20);
                EditorMagicShape.Tick(); Acc(21);
                EditorTrackTools.Tick(); Acc(22);
                EditorDecoTools.Tick(); Acc(23);
                EditorEventPanel.Tick(); Acc(25);
                EditorEventSelector.Tick(); Acc(26);
                EditorMasterSwitch.Tick(); Acc(24);
                UI.PanelKit.TickFocus(); // DE-style bring-to-front for floating windows
                if (EditorSuiteOn)       // central multi-window sidebar dock layout
                {
                    float strip = 0f;
                    try { strip = EditorEvents.BottomStripTop; } catch { }
                    UI.PanelKit.TickDocks(56f, strip > 0f ? strip + 100f : 12f);
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

        private static void StopMod(UnityModManager.ModEntry modEntry)
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _deferredApplyPending = false;
            Tweaks.ReleaseBismuthSuppress();
            Tweaks.DisposeEditorMode();
            Tweaks.DisposeTileAngle();
            EditorEvents.Dispose();
            EditorSkin.Dispose();
            EditorUiLayout.RestoreAll();
            EditorChrome.Dispose();
            EditorInspector.Dispose();
            EditorPopups.Dispose();
            EditorToolbar.Dispose();
            EditorTileMenu.Dispose();
            EditorCopyPanel.Dispose();
            EditorCameraPath.Dispose();
            EditorPitch.Dispose();
            EditorLevelMenu.Dispose();
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
            EditorMasterSwitch.Dispose();
            EditorUiEditor.Close();
            harmony.UnpatchSelf();
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
