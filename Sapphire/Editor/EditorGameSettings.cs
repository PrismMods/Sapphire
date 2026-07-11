using System;
using UnityEngine;

namespace Sapphire
{
    /* Shows the game's NATIVE settings menu from the editor. The SettingsMenu lives under the
       inactive PauseMenu(Clone) root (own canvas/scaler); rehosting it elsewhere mangles its
       fixed-offset layout, so instead we activate that root and drive the game's own
       PauseMenu.ShowSettingsMenu() — native canvas, native layout, native input handling.
       While open we watch pm.onSettingsMenu: when the user backs out (ESC/back inside the menu)
       the pause MAIN menu would appear — Resume/Quit make no sense in the editor — so the whole
       root is deactivated the moment settings close. */
    internal static class EditorGameSettings
    {
        private static PauseMenu _pm;
        private static bool _open;

        internal static bool IsOpen => _open;

        internal static void Toggle() { if (_open) Close(); else Open(); }

        internal static void Open()
        {
            var pm = UnityEngine.Object.FindObjectOfType<PauseMenu>(true);
            if (pm == null) { SapphireLog.Log("GameSettings: no PauseMenu in this scene"); return; }
            _pm = pm;
            try
            {
                pm.gameObject.SetActive(true);   // first activation runs its Awake (builds buttons)
                pm.ShowSettingsMenu();           // hides the pause main menu, shows settings
                _open = true;
            }
            catch (Exception ex)
            {
                SapphireLog.Log("GameSettings: open failed: " + ex);
                try { pm.gameObject.SetActive(false); } catch { }
                _pm = null;
            }
        }

        internal static void Close()
        {
            if (_pm != null)
            {
                try { _pm.HideSettingsMenu(); } catch { }
                try { _pm.gameObject.SetActive(false); } catch { }
            }
            _pm = null;
            _open = false;
        }

        internal static void Tick()
        {
            if (!_open) return;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null || ed.playMode || _pm == null || !MainClass.EditorSuiteOn)
            { Close(); return; }
            // The menu's own back/ESC returns to the pause MAIN menu — close everything instead.
            // (onSettingsMenu is private; the settings object deactivating is the public signal.)
            bool onSettings = true;
            try { onSettings = _pm.settingsMenu != null && _pm.settingsMenu.gameObject.activeInHierarchy; }
            catch { }
            if (!onSettings) Close();
        }

        internal static void Dispose()
        {
            if (_open) Close();
        }
    }
}
