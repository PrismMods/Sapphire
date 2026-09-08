using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Sapphire.UI.Pages
{
    /* "Keybinds" tab: every key Sapphire listens for, on one screen. The rebindable ones come
       from Keybinds.All grouped as the table declares them, the autoplay-pause key joins them
       (it used to hide on the Autoplay page), and the keys that are deliberately fixed are
       listed read-only underneath so the whole map is here and nobody hunts the help for
       "what does 1 do".

       Rebind flow: click a row to arm the page's hidden per-frame KeyListener (it reads input
       directly, so it works with the panel open); the next non-modifier key becomes the bind,
       with the Shift state captured AS the user presses it — Shift+G binds Shift+G, a bare G
       binds G. There is no cancel: any watched key commits, and Escape is not watched, so it
       can never be bound. */
    internal static class PageKeybinds
    {
        public static void Build(PageStack stack)
        {
            var content = stack.Root;
            var s = UICore.Settings;
            var notify = UICore.OnSettingsChanged;

            UIBuilder.Label(content, Loc.T("KeybindsHelp"));

            // One listener serves every row on the page; OnKey is re-pointed per armed row.
            var listener = UIBuilder.Rect("KeybindListener", content).AddComponent<KeyListener>();
            var refresh = new List<Action>();

            string seen = null;
            foreach (var d in Keybinds.All)
            {
                if (d.Group != seen) { seen = d.Group; UIBuilder.SectionHeader(content, Loc.T(d.Group)); }
                refresh.Add(BindRow(content, d, listener, refresh));
            }

            UIBuilder.SectionHeader(content, Loc.T("Autoplay pause"));
            refresh.Add(AutoplayRow(content, listener, s, notify));

            /* Not rebindable, and listed so the map is complete. Ctrl+E is the panel's own
               hotkey; Escape closes whatever is on top; the digits pick tools or dock events
               depending on selection; Alt is the free-angle hold; WASD pans. */
            UIBuilder.SectionHeader(content, Loc.T("Fixed keys"));
            FixedRow(content, "Ctrl+E", Loc.T("Sapphire settings"));
            FixedRow(content, "ESC", Loc.T("Close panel / disarm tool"));
            FixedRow(content, "1-0", Loc.T("Select tool") + " / " + Loc.T("pick dock event"));
            FixedRow(content, "Alt", Loc.T("Hold: free-angle aim"));
            FixedRow(content, "WASD", Loc.T("Pan camera"));

            UIBuilder.Spacer(content);
            UIBuilder.Button(content, Loc.T("Reset all keybinds"), () =>
            {
                Keybinds.ResetAll();
                s.AutoplayPauseKey = KeyCode.Space;
                notify?.Invoke();
                foreach (var r in refresh) r();
            });
        }

        private static Action BindRow(Transform body, Keybinds.Def d, KeyListener listener, List<Action> refreshAll)
        {
            TextMeshProUGUI label = null;
            Action refresh = () =>
            {
                if (label == null) return;
                string conflict = Keybinds.ConflictLabel(d.Id);
                label.text = Loc.T(d.Label) + ":  " + Keybinds.Label(d.Id)
                           + (conflict != null ? "   (" + Loc.T("also") + " " + conflict + ")" : "");
            };
            var btn = UIBuilder.Button(body, "", () =>
            {
                listener.Active = true;
                listener.OnKey = kc =>
                {
                    if (Keybinds.IsModifier(kc)) return;   // Shift fires first in "Shift+G" — wait
                    listener.Active = false;
                    Keybinds.Set(d.Id, kc, Keybinds.ShiftHeld);
                    UICore.OnSettingsChanged?.Invoke();
                    foreach (var r in refreshAll) r();      // a new bind can create/clear conflicts
                };
                if (label != null) label.text = Loc.T(d.Label) + ":  " + Loc.T("Press a key…");
            });
            label = btn.GetComponentInChildren<TextMeshProUGUI>();
            refresh();
            return refresh;
        }

        // Same flow, different store: the pause key predates the Keybinds table and is a plain
        // KeyCode on Settings (no Shift half), so it keeps its own row rather than being forced
        // through Bind.
        private static Action AutoplayRow(Transform body, KeyListener listener, Settings s, Action notify)
        {
            TextMeshProUGUI label = null;
            Action refresh = () =>
            {
                if (label != null)
                    label.text = Loc.T("Pause key") + ":  "
                               + KeyTokens.PrettyTokenLabel(KeyTokens.TokenFromKeyCode(s.AutoplayPauseKey));
            };
            var btn = UIBuilder.Button(body, "", () =>
            {
                listener.Active = true;
                listener.OnKey = kc =>
                {
                    if (Keybinds.IsModifier(kc)) return;
                    listener.Active = false;
                    s.AutoplayPauseKey = kc;
                    notify?.Invoke();
                    refresh();
                };
                if (label != null) label.text = Loc.T("Pause key") + ":  " + Loc.T("Press a key…");
            });
            label = btn.GetComponentInChildren<TextMeshProUGUI>();
            refresh();
            return refresh;
        }

        private static void FixedRow(Transform body, string keys, string what)
            => UIBuilder.Label(body, "<color=#" + ColorUtility.ToHtmlStringRGB(Theme.Text) + ">" + keys
                                     + "</color>   <color=#" + ColorUtility.ToHtmlStringRGB(Theme.TextMuted) + ">"
                                     + what + "</color>");
    }
}
