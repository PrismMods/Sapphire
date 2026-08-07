using System;
using System.Globalization;
using UnityEngine;
using Sapphire.UI;

namespace Sapphire
{
    /* Property rows for the value shapes AddParticle uses that EventRows' primitive branches
       don't cover. data[key] holds these BOXED and TYPED (LevelEvent.Get<T> is TryGetValue +
       cast, no parsing), so every commit must write the SAME type back — a string here stops
       the decoration loading. */
    internal static class EventRowsParticle
    {
        private const float RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        // Re-read at commit time: the captured `val` is a snapshot and each field commits
        // independently, so writing from the snapshot would clobber the sibling field.
        private static object Raw(ADOFAI.LevelEvent evt, string key)
        {
            try
            {
                var d = EditorEvents.EventData(evt);
                object v;
                if (d != null && d.TryGetValue(key, out v)) return v;
            }
            catch { }
            return null;
        }

        // [label]
        // [item1] [item2]
        internal static float FloatPairRow(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, Tuple<float, float> val,
            string lbl, Color lblCol, float x, float w, float y)
        {
            EventRows.Label(c.Content, lbl, x, y, w, 16f, lblCol); y -= 18f;
            float fw = (w - Gap) * 0.5f;
            EventRows.InputRow(c.Content, x, y, fw, F(val.Item1),
                s => SetPair(c, ed, evt, pi, key, val, s, true));
            EventRows.InputRow(c.Content, x + fw + Gap, y, fw, F(val.Item2),
                s => SetPair(c, ed, evt, pi, key, val, s, false));
            return y - (RowH + Gap);
        }

        private static void SetPair(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, Tuple<float, float> fallback, string raw, bool first)
        {
            float f;
            if (!ExprEval.TryParseFloat(raw, out f)) return;
            var cur = Raw(evt, key) as Tuple<float, float> ?? fallback;
            EventRows.Commit(c, ed, evt, pi, key,
                first ? Tuple.Create(f, cur.Item2) : Tuple.Create(cur.Item1, f));
        }

        // [label]
        // [from x] [from y]
        // [to x]   [to y]
        internal static float Vector2RangeRow(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, Tuple<Vector2, Vector2> val,
            string lbl, Color lblCol, float x, float w, float y)
        {
            EventRows.Label(c.Content, lbl, x, y, w, 16f, lblCol); y -= 18f;
            float fw = (w - Gap) * 0.5f;
            EventRows.InputRow(c.Content, x, y, fw, F(val.Item1.x),
                s => SetRange(c, ed, evt, pi, key, val, s, 0));
            EventRows.InputRow(c.Content, x + fw + Gap, y, fw, F(val.Item1.y),
                s => SetRange(c, ed, evt, pi, key, val, s, 1));
            y -= RowH + Gap;
            EventRows.InputRow(c.Content, x, y, fw, F(val.Item2.x),
                s => SetRange(c, ed, evt, pi, key, val, s, 2));
            EventRows.InputRow(c.Content, x + fw + Gap, y, fw, F(val.Item2.y),
                s => SetRange(c, ed, evt, pi, key, val, s, 3));
            return y - (RowH + Gap);
        }

        private static void SetRange(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, Tuple<Vector2, Vector2> fallback, string raw, int slot)
        {
            float f;
            if (!ExprEval.TryParseFloat(raw, out f)) return;
            var cur = Raw(evt, key) as Tuple<Vector2, Vector2> ?? fallback;
            Vector2 a = cur.Item1, b = cur.Item2;
            switch (slot)
            {
                case 0: a.x = f; break;
                case 1: a.y = f; break;
                case 2: b.x = f; break;
                default: b.y = f; break;
            }
            EventRows.Commit(c, ed, evt, pi, key, Tuple.Create(a, b));
        }
    }
}
