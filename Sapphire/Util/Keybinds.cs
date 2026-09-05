using System.Collections.Generic;
using UnityEngine;
using Sapphire.UI.Pages;

namespace Sapphire
{
    /* One rebindable key: a KeyCode plus a Shift flag. Serialized inside Settings, so public. */
    public class KeyBindDto
    {
        public string Id = "";
        public KeyCode Key = KeyCode.None;
        public bool Shift = false;
    }

    // Stable ids. Persisted BY NAME, so the enum can be reordered freely; only renaming an entry
    // loses a user's binding (it falls back to the default).
    internal enum Bind
    {
        QuickChart,
        QcSwirl, QcSetSpeed, QcSpeedDown, QcSpeedUp, QcPause, QcLocate, QcAnglePad,
        ToolPrev, ToolSlot, ToolSlotSave, HzTool,
    }

    /* Sapphire's rebindable keys.

       COLLISION RULE (verified in the scnEditor keybind-registration IL): the editor registers
       its 17 keyboard tile-placement keys — a b c d e h j m n q s t v w x y z — under BOTH
       KeyModifier.None AND Shift, matched by EXACT modifier equality. So Shift+<tile letter> is
       NOT free; only the letters the game never places a tile with are — f g i k l o p r u — plus
       any key under Ctrl/Cmd/Alt (which every Sapphire hotkey path returns on anyway, since those
       belong to game chords). Every default below sits inside that safe set. A user can rebind
       outside it; that is their call, and the settings page says so.

       Defaults match what Sapphire shipped with, so an existing install feels unchanged. */
    internal static class Keybinds
    {
        internal struct Def
        {
            public readonly Bind Id;
            public readonly string Group, Label;   // both are Loc keys
            public readonly KeyCode Key;
            public readonly bool Shift;
            public Def(Bind id, string group, string label, KeyCode key, bool shift)
            { Id = id; Group = group; Label = label; Key = key; Shift = shift; }
        }

        internal static readonly Def[] All =
        {
            // 'K' is not a tile-placement key, so Shift+K is free; the toolbar's Q cell toggles
            // the same mode (Shift+Q would collide — 'q' IS a tile key).
            new Def(Bind.QuickChart,   "Modes",       "Quick chart mode",           KeyCode.K, true),

            new Def(Bind.QcSwirl,      "Quick chart", "Swirl on/off",               KeyCode.I, false),
            new Def(Bind.QcSetSpeed,   "Quick chart", "Set speed",                  KeyCode.O, false),
            new Def(Bind.QcSpeedDown,  "Quick chart", "Halve speed",                KeyCode.LeftBracket, false),
            new Def(Bind.QcSpeedUp,    "Quick chart", "Double speed",               KeyCode.RightBracket, false),
            new Def(Bind.QcPause,      "Quick chart", "Pause event",                KeyCode.P, true),
            new Def(Bind.QcLocate,     "Quick chart", "Tile location event",        KeyCode.L, true),
            new Def(Bind.QcAnglePad,   "Quick chart", "Angle pad",                  KeyCode.G, true),

            new Def(Bind.ToolPrev,     "Tools",       "Previous tool",              KeyCode.Comma, false),
            new Def(Bind.ToolSlot,     "Tools",       "Saved tool slot",            KeyCode.Period, false),
            new Def(Bind.ToolSlotSave, "Tools",       "Save current tool to slot",  KeyCode.Period, true),
            // 'F' is one of the free letters (not a tile-placement key), so Shift+F is clear.
            new Def(Bind.HzTool,       "Tools",       "Hz tool",                    KeyCode.F, true),
        };

        // Bumped on every change. Per-frame consumers (the key-hint card) fold this into their
        // change signature instead of diffing every bind.
        internal static int Revision { get; private set; }

        private static readonly KeyCode[] _key = new KeyCode[All.Length];
        private static readonly bool[] _shift = new bool[All.Length];
        private static bool _loaded;

        internal static bool ShiftHeld =>
            Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        /* True on the frame this bind's key goes down WITH its exact Shift state. Callers must
           still filter Ctrl/Cmd/Alt themselves — matching Shift alone would let a game chord
           (Ctrl+I, say) fire a quick-chart action. */
        internal static bool Down(Bind b)
        {
            Ensure();
            int i = (int)b;
            var k = _key[i];
            if (k == KeyCode.None) return false;            // deliberately unbound
            return Input.GetKeyDown(k) && ShiftHeld == _shift[i];
        }

        internal static KeyCode Key(Bind b) { Ensure(); return _key[(int)b]; }
        internal static bool Shift(Bind b) { Ensure(); return _shift[(int)b]; }

        // "Shift+G" / "I" / "[" / "—" when unbound.
        internal static string Label(Bind b)
        {
            Ensure();
            int i = (int)b;
            if (_key[i] == KeyCode.None) return "—";
            string k = KeyTokens.PrettyTokenLabel(KeyTokens.TokenFromKeyCode(_key[i]));
            return _shift[i] ? "Shift+" + k : k;
        }

        // The other bind sharing this key+Shift, or null. Two binds on one key isn't fatal (the
        // first match in a caller's chain wins) but it silently kills the second, so the settings
        // page flags it rather than letting a user wonder.
        internal static string ConflictLabel(Bind b)
        {
            Ensure();
            int i = (int)b;
            if (_key[i] == KeyCode.None) return null;
            for (int j = 0; j < All.Length; j++)
                if (j != i && _key[j] == _key[i] && _shift[j] == _shift[i])
                    return Loc.T(All[j].Label);
            return null;
        }

        internal static void Set(Bind b, KeyCode key, bool shift)
        {
            Ensure();
            int i = (int)b;
            if (_key[i] == key && _shift[i] == shift) return;
            _key[i] = key; _shift[i] = shift;
            Revision++;
            Persist();
        }

        internal static void ResetAll()
        {
            Ensure();
            for (int i = 0; i < All.Length; i++) { _key[i] = All[i].Key; _shift[i] = All[i].Shift; }
            Revision++;
            Persist();
        }

        // ── storage ──────────────────────────────────────────────────────────

        private static void Ensure()
        {
            if (_loaded) return;
            for (int i = 0; i < All.Length; i++) { _key[i] = All[i].Key; _shift[i] = All[i].Shift; }
            var s = MainClass.Settings;
            // Don't latch on defaults if a tick beats settings loading — retry next call, or a
            // user's saved binds would be invisible for the rest of the session.
            if (s == null) return;
            _loaded = true;
            if (s.Keybinds == null) return;
            foreach (var dto in s.Keybinds)
            {
                if (dto == null || string.IsNullOrEmpty(dto.Id)) continue;
                for (int i = 0; i < All.Length; i++)
                {
                    if (All[i].Id.ToString() != dto.Id) continue;
                    _key[i] = dto.Key; _shift[i] = dto.Shift;
                    break;                                  // unknown ids just fall through
                }
            }
        }

        private static void Persist()
        {
            var s = MainClass.Settings;
            if (s == null) return;
            s.Keybinds = new List<KeyBindDto>();
            for (int i = 0; i < All.Length; i++)
                s.Keybinds.Add(new KeyBindDto { Id = All[i].Id.ToString(), Key = _key[i], Shift = _shift[i] });
            MainClass.SaveSettings();
        }

        // Modifier keys can't BE a bind — the rebind listener sees LeftShift before the real key
        // when a user presses Shift+G, and binding "LShift" there would be the wrong answer.
        internal static bool IsModifier(KeyCode kc)
        {
            switch (kc)
            {
                case KeyCode.LeftShift: case KeyCode.RightShift:
                case KeyCode.LeftControl: case KeyCode.RightControl:
                case KeyCode.LeftAlt: case KeyCode.RightAlt:
                case KeyCode.LeftCommand: case KeyCode.RightCommand:
                    return true;
                default: return false;
            }
        }
    }
}
