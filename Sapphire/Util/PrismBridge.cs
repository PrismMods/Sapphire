using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using PrismLib.UI.Toolkit;
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

            // What this mod contributes to the shared debug window. Pulled, never pushed: the
            // delegates only run while someone is looking at that tab.
            me.AddLog("log", () => SapphireLog.ReadTail().Split('\n'), SapphireLog.LogPath);
            me.AddFields("Editor", EditorFields);
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
                me.BindKey("DebugPanel", (int)KeyCode.D, PrismLib.KeyMods.Ctrl | PrismLib.KeyMods.Shift, "Open the debug panel");
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

        // ── shared debug window ──────────────────────────────────────────────

        /* Ctrl+Shift+D, the SAME chord in every Prism mod, because the window itself is shared: it
           shows every registered mod's log and fields, not just this one's. Both mods poll the
           chord; the StateKey.DebugPanel claim decides which one actually draws, so the user gets
           one window instead of two identical ones.

           A typed PrismLib.UI field is fine here. That assembly ships inside the mod folder, unlike
           PrismLib.dll, which may never install — which is why the handles above stay object. */
        private static DebugPanel _debug;
        private static object _debugClaim;

        internal static void TickDebug()
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                     || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (ctrl && shift && Input.GetKeyDown(KeyCode.D) && !UI.FieldNav.Typing) ToggleDebug();
            if (_debug != null) _debug.Tick();
        }

        internal static void ToggleDebug()
        {
            /* Everything here is logged, because the game disables exception capturing: without
               these lines a broken panel and a hotkey that never fired look identical. */
            try
            {
                if (_debug == null)
                {
                    if (Available && !TakeDebugClaim())
                    {
                        SapphireLog.Log("[prism] debug window: another Prism mod owns it, not opening a second");
                        return;
                    }
                    PrismLib.UI.Toolkit.Ui.Log = m => SapphireLog.Log("[prism] " + m);
                    _debug = new DebugPanel("Prism · Debug", DebugTabs);
                    // UI Toolkit draws through TextCore, so hand it the legacy Font the TMP asset
                    // was built from. Without a font the panel renders but every label is blank.
                    var tmp = UI.Theme.TmpFont;
                    _debug.SetFont(tmp != null ? tmp.sourceFontFile : null);
                }
                _debug.Toggle();
            }
            catch (Exception e) { SapphireLog.Log("[prism] debug window FAILED: " + e); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TakeDebugClaim()
        {
            try
            {
                var me = _me as PrismLib.ModHandle;
                if (me == null) return true;
                _debugClaim = me.ClaimState(PrismLib.StateKey.DebugPanel, "Ctrl+Shift+D");
                return _debugClaim != null;
            }
            catch { return true; }
        }

        private static IEnumerable<DebugTab> DebugTabs()
        {
            var tabs = new List<DebugTab>();
            if (Available) AddPrismTabs(tabs);
            // Standalone: nothing registered anywhere because PrismLib never installed. Show ours.
            if (tabs.Count == 0)
                tabs.Add(new DebugTab { Name = "Sapphire · log", Lines = () => SapphireLog.ReadTail().Split('\n') });
            return tabs;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void AddPrismTabs(List<DebugTab> tabs)
        {
            foreach (var log in PrismLib.Diagnostics.Logs)
            {
                var src = log;
                tabs.Add(new DebugTab { Name = src.Owner + " · " + src.Name, Lines = src.Tail });
            }
            foreach (var group in PrismLib.Diagnostics.Fields)
            {
                var g = group;
                tabs.Add(new DebugTab { Name = g.Owner + " · " + g.Name, Lines = () => Pairs(g.Read()) });
            }
            tabs.Add(new DebugTab { Name = "Prism", Lines = PrismState });
        }

        private static IEnumerable<string> Pairs(IEnumerable<KeyValuePair<string, string>> src)
        {
            foreach (var kv in src) yield return kv.Key + " = " + kv.Value;
        }

        /// Who is loaded, who owns what, and which hotkeys collide — the answer to "autoplay is
        /// broken" that used to take three log files to guess at.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IEnumerable<string> PrismState()
        {
            var outp = new List<string>();
            try
            {
                outp.Add("PrismLib " + PrismLib.Prism.Version);
                foreach (var id in PrismLib.Prism.LoadedMods)
                {
                    Version v;
                    PrismLib.Prism.IsLoaded(id, out v);
                    outp.Add("  mod  " + id + " " + v);
                }
                outp.Add("");
                outp.Add("— state claims —");
                foreach (var kv in PrismLib.Claims.Held) outp.Add("  " + kv.Key + "  ->  " + kv.Value);
                outp.Add("");
                outp.Add("— key conflicts —");
                bool any = false;
                foreach (var c in PrismLib.Keys.Conflicts()) { outp.Add("  " + c.Key + "  vs  " + c.Value); any = true; }
                if (!any) outp.Add("  (none)");
                outp.Add("");
                outp.Add("— registered keys —");
                foreach (var k in PrismLib.Keys.All) outp.Add("  " + k);
            }
            catch (Exception e) { outp.Add("(unavailable: " + e.Message + ")"); }
            return outp;
        }

        private static IEnumerable<KeyValuePair<string, string>> EditorFields()
        {
            var outp = new List<KeyValuePair<string, string>>();
            Action<string, string> add = (k, v) => outp.Add(new KeyValuePair<string, string>(k, v));
            try
            {
                add("Sapphire", MainClass.ModVersion ?? "?");
                add("editor suite", MainClass.EditorSuiteOn ? "on" : "off");
                var ed = scnEditor.instance;
                add("editor", ed != null ? "open" : "not open");
                if (ed != null)
                {
                    add("play mode", ed.playMode ? "playing" : "editing");
                    add("selected tiles", (ed.selectedFloors != null ? ed.selectedFloors.Count : 0).ToString());
                }
                add("scene", UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
                add("autoplay", RDC.auto ? "on" : "off");
            }
            catch (Exception e) { add("(read failed)", e.Message); }
            return outp;
        }

        /* Callable with PrismLib absent, so it mentions no PrismLib type: the debug window is a
           PrismLib.UI object that exists either way, and everything else is behind Available in a
           method that is only ever JITted when the library is there. */
        internal static void Shutdown()
        {
            if (_debug != null) { _debug.Dispose(); _debug = null; }
            if (!Available) return;
            Available = false;
            ReleasePrism();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ReleasePrism()
        {
            try
            {
                _hud = null; _debugClaim = null;
                PrismLib.Claims.ReleaseAll("Sapphire");
                PrismLib.Keys.Unregister("Sapphire");
                PrismLib.Diagnostics.Unregister("Sapphire");
            }
            catch { }
        }
    }
}
