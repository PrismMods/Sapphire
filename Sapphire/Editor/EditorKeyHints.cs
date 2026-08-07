using System.Collections.Generic;
using UnityEngine;
using Sapphire.UI;

namespace Sapphire
{
    /* Bottom-right key card. Rides above the event timeline (EditorEvents.BottomStripTop),
       heights itself to its rows, and shows only the keys that are live in the CURRENT
       context — the full list is long and mostly irrelevant at any one moment. The table is
       hand-maintained here; EditorHelp's per-topic "Keys" sections are prose and parsing them
       would break on the first rewrite. */
    internal static class EditorKeyHints
    {
        // context bits
        private const int Always = 0, NoSel = 1, Sel = 2, ToolNum = 4, Quick = 8;

        private struct Hint
        {
            public readonly int Ctx; public readonly string Keys, What;
            public Hint(int ctx, string keys, string what) { Ctx = ctx; Keys = keys; What = what; }
        }

        private static readonly Hint[] Table =
        {
            new Hint(Always, "Ctrl+E",  "Sapphire settings"),
            new Hint(Always, "ESC",     "Close panel / disarm tool"),
            new Hint(NoSel,  "1–0",     "Select tool"),
            new Hint(NoSel,  ",",       "Previous tool"),
            new Hint(NoSel,  ".",       "Saved tool slot"),
            new Hint(NoSel,  "Shift+.", "Save current tool to slot"),
            new Hint(Sel,    "Alt",     "Hold: free-angle aim"),
            new Hint(ToolNum,"Digits",  "Set key count"),
            new Hint(Quick,  "I",       "Swirl on/off"),
            new Hint(Quick,  "O",       "Set speed"),
            new Hint(Quick,  "[ / ]",   "Halve / double speed"),
            new Hint(Quick,  "Shift+P", "Pause event"),
            new Hint(Quick,  "Shift+L", "Tile location event"),
            new Hint(Quick,  "Shift+G", "Angle pad"),
        };

        private static readonly PanelKit K = new PanelKit("SapphireKeyHints", 904, CardW);
        private const float CardW = 236f, RowH = 18f, Pad = 8f, HeadH = 20f;
        private static bool _collapsed;
        private static int _sig = int.MinValue;
        private static float _lastBottom = -1f;
        private static readonly List<Hint> _rows = new List<Hint>();

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool want = ed != null && !ed.playMode && MainClass.EditorSuiteOn
                        && s != null && s.EditorKeyHints;
            if (!want) { K.Show(false); return; }

            int ctx = Context(ed, s);
            if (ctx != _sig || !K.Built) { _sig = ctx; Build(ctx); }
            K.Show(true);
            Place();
        }

        internal static void Dispose()
        {
            K.Dispose();
            _sig = int.MinValue; _lastBottom = -1f;
        }

        private static int Context(scnEditor ed, Settings s)
        {
            int ctx = 0;
            int sel = 0;
            try { sel = ed.selectedFloors != null ? ed.selectedFloors.Count : 0; } catch { }
            ctx |= sel == 0 ? NoSel : Sel;
            try { if (EditorToolbar.PseudoToolOn) ctx |= ToolNum; } catch { }
            try { if (s.FeatQuickChart) ctx |= Quick; } catch { }
            if (_collapsed) ctx |= 1 << 20;   // collapse is part of the layout signature
            return ctx;
        }

        private static void Build(int ctx)
        {
            _rows.Clear();
            foreach (var h in Table)
                if (h.Ctx == Always || (ctx & h.Ctx) != 0) _rows.Add(h);

            K.Rebuild(Loc.T("Keys"), () => { _collapsed = !_collapsed; _sig = int.MinValue; },
                new Vector2(0f, 0f));
            float y = -HeadH - 2f;
            if (!_collapsed)
            {
                const float keyW = 74f;
                foreach (var h in _rows)
                {
                    K.Label(h.Keys, Pad, y, keyW, RowH, Theme.Text, 10.5f);
                    K.Label(Loc.T(h.What), Pad + keyW + 4f, y, CardW - Pad * 2f - keyW - 4f, RowH,
                        Theme.TextMuted, 10.5f);
                    y -= RowH;
                }
                y -= 4f;
            }
            K.SetHeight(y);
            _lastBottom = -1f;   // force a reposition at the new height
        }

        /* Bottom-right, clear of the timeline strip. The card is deliberately position-LOCKED:
           PanelKit gives its header a DragHandle, but this re-asserts the anchored position
           every frame, so a drag snaps straight back. Only writes when something actually
           moved — an unconditional transform write per frame re-batches the canvas. */
        private static void Place()
        {
            if (!K.Built) return;
            float strip = 0f;
            try { strip = EditorEvents.BottomStripTop; } catch { }
            float bottom = strip > 0f ? strip + 8f : 12f;
            var r = (RectTransform)K.PanelGo.transform;
            var canvas = (RectTransform)K.CanvasGo.transform;
            float x = canvas.rect.width - r.sizeDelta.x - 12f;
            float y = -(canvas.rect.height - bottom - r.sizeDelta.y);
            var want = new Vector2(x, y);
            if (!Mathf.Approximately(bottom, _lastBottom) || (r.anchoredPosition - want).sqrMagnitude > 0.5f)
            {
                r.anchoredPosition = want;
                _lastBottom = bottom;
            }
        }
    }
}
