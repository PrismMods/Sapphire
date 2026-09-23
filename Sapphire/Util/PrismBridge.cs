using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace Sapphire
{
    /* Sapphire's side of PrismLib (github.com/PrismMods/PrismLib).

       Sapphire, Bismuth and Quartz write the same globals and read the same keyboard, and neither
       can see the other doing it. PrismLib is where they say so. It installs itself — see
       PrismBootstrap — so there is nothing for the user to add.

       EVERY PrismLib type stays inside this file, in METHOD BODIES only, and every method here that
       touches one is called solely after Available has been checked. Two separate rules, both
       load-bearing on a machine where the library never arrived — which is exactly the machine that
       must keep working:

         - No PrismLib type may appear in a FIELD, because field types are part of the class layout
           and are resolved when the type itself loads, before any method runs. A `ModHandle _me`
           field made this whole class unloadable and took the mod down with it:
           "TypeLoadException - Could not load type of field 'Sapphire.PrismBridge:_me'". Hence the
           object fields and the casts.
         - No caller outside this file may mention a PrismLib type, because the CLR resolves a
           method's types when that method is JITted. */
    internal static class PrismBridge
    {
        internal static bool Available { get; private set; }

        internal static void Init()
        {
            try
            {
                if (!PrismLib.Bootstrap.PrismBootstrap.Ensure(SapphireLog.Log, new Version(0, 2, 0))) return;
                Wire();
                Available = true;
            }
            catch (Exception e) { SapphireLog.Log("PrismLib unavailable: " + e.Message); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Wire()
        {
            Version v;
            Version.TryParse((MainClass.ModVersion ?? "0.0.0").Split('-')[0], out v);
            var me = PrismLib.Prism.Register("Sapphire", v ?? new Version(0, 0, 0));
            _me = me;
            PrismLib.Prism.Log = SapphireLog.Log;
            PrismLib.Keys.KeyName = i => ((KeyCode)i).ToString();
            RegisterKeys(me);
        }

        /// Register the EFFECTIVE binds (Keybinds.Def carries defaults, the user rebinds them), so
        /// a conflict report is about the keys actually live.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void SyncKeys() => RegisterKeys(_me as PrismLib.ModHandle);

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RegisterKeys(PrismLib.ModHandle me)
        {
            if (me == null) return;
            try
            {
                foreach (var d in Keybinds.All)
                {
                    var key = Keybinds.Key(d.Id);
                    if (key == KeyCode.None) continue;
                    var m = PrismLib.KeyMods.None;
                    if (Keybinds.Shift(d.Id)) m |= PrismLib.KeyMods.Shift;
                    if (Keybinds.Alt(d.Id)) m |= PrismLib.KeyMods.Alt;
                    me.BindKey(d.Id.ToString(), (int)key, m, Loc.T(d.Label));
                }
                // Not in the Bind table: the settings panel's own toggle, hardcoded in UICore.
                me.BindKey("SettingsPanel", (int)KeyCode.E, PrismLib.KeyMods.Ctrl, "Open the settings panel");
            }
            catch { }
        }

        // Opaque on purpose — see the class comment. Cast at use, never in the field type.
        private static object _me;
        private static object _hud;

        /* Editor Mode owns the gameplay HUD: the mode cluster carries difficulty / no-fail /
           autoplay itself, so the game's own corner icons come down. Bismuth's overlays sit in the
           same corner, hence the claim — and hence Tweaks' older reflection hook into
           Bismuth.Settings.ExternalEditorSuppress, which stays for Bismuth builds that predate
           PrismLib. */
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void ClaimHud(bool on)
        {
            try
            {
                var me = _me as PrismLib.ModHandle;
                if (on) { if (_hud == null && me != null) _hud = me.ClaimState(PrismLib.StateKey.GameHud, "Editor Mode"); }
                else if (_hud != null) { ((PrismLib.Claim)_hud).Release(); _hud = null; }
            }
            catch { }
        }

        /// True while ANOTHER mod is swallowing the keyboard game-wide (Bismuth does it while its
        /// panel is open). Sapphire's hotkeys stand down rather than firing on keys meant for it.
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static bool InputHeldElsewhere()
        {
            try
            {
                var owner = PrismLib.Claims.OwnerOf(PrismLib.StateKey.InputCapture);
                return owner != null && owner != "Sapphire";
            }
            catch { return false; }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static void Shutdown()
        {
            if (!Available) return;
            Available = false;
            try { _hud = null; PrismLib.Claims.ReleaseAll("Sapphire"); PrismLib.Keys.Unregister("Sapphire"); }
            catch { }
        }
    }
}
