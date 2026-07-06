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
            private void Update()
            {
                Tweaks.TickTileAngle();
                Tweaks.TickEditorMode();
                EditorEvents.Tick();
                EditorSkin.Tick();
                EditorUiLayout.Tick();
                EditorChrome.Tick();
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
            Tweaks.DisposeTileAngle();
            EditorEvents.Dispose();
            EditorSkin.Dispose();
            EditorUiLayout.RestoreAll();
            EditorChrome.Dispose();
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
        }
    }
}
