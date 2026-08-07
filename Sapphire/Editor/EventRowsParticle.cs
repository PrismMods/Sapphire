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

        // ── MinMaxGradient ───────────────────────────────────────────────────

        private static string D(decimal v)
            => ((double)v).ToString("0.####", CultureInfo.InvariantCulture);

        private static bool TryDec(string raw, out decimal d)
        {
            d = 0m;
            double v;
            if (!ExprEval.TryParseDouble(raw, out v)) return false;
            d = (decimal)v;
            return true;
        }

        private static ADOFAI.Editor.Models.SerializedMinMaxGradient Grad(
            ADOFAI.LevelEvent evt, string key, ADOFAI.Editor.Models.SerializedMinMaxGradient fallback)
        {
            var v = Raw(evt, key);
            return v is ADOFAI.Editor.Models.SerializedMinMaxGradient g ? g : fallback;
        }

        private static ADOFAI.Editor.Models.SerializedGradient SlotGrad(
            ADOFAI.Editor.Models.SerializedMinMaxGradient mm, int slot)
        {
            var gr = slot == 1 ? mm.gradient1 : mm.gradient2;
            return gr.HasValue ? gr.Value : new ADOFAI.Editor.Models.SerializedGradient();
        }

        private static void PutSlotGrad(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, ADOFAI.Editor.Models.SerializedMinMaxGradient val,
            int slot, ADOFAI.Editor.Models.SerializedGradient g)
        {
            var mm = Grad(evt, key, val);
            if (slot == 1) mm.gradient1 = g; else mm.gradient2 = g;
            EventRows.Commit(c, ed, evt, pi, key, mm);
        }

        // [time] [hex] [×] per stop, then a [+] add row
        private static float ColorStops(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, ADOFAI.Editor.Models.SerializedMinMaxGradient val,
            int slot, float x, float w, float y)
        {
            var g = SlotGrad(Grad(evt, key, val), slot);
            var keys = g.colorKeys ?? new ADOFAI.Editor.Models.SerializedGradient.ColorKey[0];
            EventRows.Label(c.Content, Loc.T("Colour stops"), x, y, w, 16f, Theme.TextMuted);
            y -= 18f;
            const float tw = 52f, bw = 24f;
            float hw = w - tw - bw - Gap * 2f;
            for (int i = 0; i < keys.Length; i++)
            {
                int idx = i;
                EventRows.InputRow(c.Content, x, y, tw, D(keys[i].time), s =>
                {
                    decimal d;
                    if (!TryDec(s, out d)) return;
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    var arr = cg.colorKeys;
                    if (arr == null || idx >= arr.Length) return;
                    // Clone: the array is the SAME reference the undo snapshot's shallow-copied
                    // boxed gradient points at (SaveStateScope's ctor snapshots BEFORE this runs) —
                    // mutating in place corrupts undo. Append/RemoveAt already allocate fresh arrays.
                    var arr2 = (ADOFAI.Editor.Models.SerializedGradient.ColorKey[])arr.Clone();
                    var kk = arr2[idx]; kk.time = d; arr2[idx] = kk;
                    cg.colorKeys = arr2;
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                });
                EventRows.InputRow(c.Content, x + tw + Gap, y, hw, keys[i].color ?? "", s =>
                {
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    var arr = cg.colorKeys;
                    if (arr == null || idx >= arr.Length) return;
                    var arr2 = (ADOFAI.Editor.Models.SerializedGradient.ColorKey[])arr.Clone();
                    var kk = arr2[idx]; kk.color = (s ?? "").Trim().TrimStart('#'); arr2[idx] = kk;
                    cg.colorKeys = arr2;
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                });
                EventRows.Cell(c.Content, "×", x + tw + hw + Gap * 2f, y, bw, RowH, () =>
                {
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    cg.colorKeys = RemoveAt(cg.colorKeys, idx);
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                }, true);
                y -= RowH + Gap;
            }
            EventRows.Cell(c.Content, "+", x, y, w, RowH, () =>
            {
                var cg = SlotGrad(Grad(evt, key, val), slot);
                var add = new ADOFAI.Editor.Models.SerializedGradient.ColorKey();
                add.time = 1m; add.color = "ffffff";
                cg.colorKeys = Append(cg.colorKeys, add);
                PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
            }, true);
            return y - (RowH + Gap);
        }

        // [time] [alpha 0–1] [×] per stop, then a [+] add row
        private static float AlphaStops(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, ADOFAI.Editor.Models.SerializedMinMaxGradient val,
            int slot, float x, float w, float y)
        {
            var g = SlotGrad(Grad(evt, key, val), slot);
            var keys = g.alphaKeys ?? new ADOFAI.Editor.Models.SerializedGradient.AlphaKey[0];
            EventRows.Label(c.Content, Loc.T("Alpha stops"), x, y, w, 16f, Theme.TextMuted);
            y -= 18f;
            const float tw = 52f, bw = 24f;
            float aw = w - tw - bw - Gap * 2f;
            for (int i = 0; i < keys.Length; i++)
            {
                int idx = i;
                EventRows.InputRow(c.Content, x, y, tw, D(keys[i].time), s =>
                {
                    decimal d;
                    if (!TryDec(s, out d)) return;
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    var arr = cg.alphaKeys;
                    if (arr == null || idx >= arr.Length) return;
                    // Clone: see ColorStops — mutating the live array corrupts the undo snapshot.
                    var arr2 = (ADOFAI.Editor.Models.SerializedGradient.AlphaKey[])arr.Clone();
                    var kk = arr2[idx]; kk.time = d; arr2[idx] = kk;
                    cg.alphaKeys = arr2;
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                });
                EventRows.InputRow(c.Content, x + tw + Gap, y, aw, D(keys[i].alpha), s =>
                {
                    decimal d;
                    if (!TryDec(s, out d)) return;
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    var arr = cg.alphaKeys;
                    if (arr == null || idx >= arr.Length) return;
                    var arr2 = (ADOFAI.Editor.Models.SerializedGradient.AlphaKey[])arr.Clone();
                    var kk = arr2[idx]; kk.alpha = d; arr2[idx] = kk;
                    cg.alphaKeys = arr2;
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                });
                EventRows.Cell(c.Content, "×", x + tw + aw + Gap * 2f, y, bw, RowH, () =>
                {
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    cg.alphaKeys = RemoveAt(cg.alphaKeys, idx);
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                }, true);
                y -= RowH + Gap;
            }
            EventRows.Cell(c.Content, "+", x, y, w, RowH, () =>
            {
                var cg = SlotGrad(Grad(evt, key, val), slot);
                var add = new ADOFAI.Editor.Models.SerializedGradient.AlphaKey();
                add.time = 1m; add.alpha = 1m;
                cg.alphaKeys = Append(cg.alphaKeys, add);
                PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
            }, true);
            return y - (RowH + Gap);
        }

        private static T[] Append<T>(T[] arr, T item)
        {
            int n = arr == null ? 0 : arr.Length;
            var outArr = new T[n + 1];
            for (int i = 0; i < n; i++) outArr[i] = arr[i];
            outArr[n] = item;
            return outArr;
        }

        private static T[] RemoveAt<T>(T[] arr, int idx)
        {
            if (arr == null || idx < 0 || idx >= arr.Length) return arr;
            var outArr = new T[arr.Length - 1];
            for (int i = 0, j = 0; i < arr.Length; i++)
                if (i != idx) outArr[j++] = arr[i];
            return outArr;
        }

        // [label] / [mode ▾] / colour hex rows or stop lists, by mode
        internal static float GradientRow(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, ADOFAI.Editor.Models.SerializedMinMaxGradient val,
            string lbl, Color lblCol, float x, float w, float y)
        {
            EventRows.Label(c.Content, lbl, x, y, w, 16f, lblCol); y -= 18f;

            var modes = (ParticleSystemGradientMode[])Enum.GetValues(typeof(ParticleSystemGradientMode));
            var names = new System.Collections.Generic.List<string>(modes.Length);
            int cur = 0;
            for (int i = 0; i < modes.Length; i++)
            {
                names.Add(EventRows.LocEnum("ParticleSystemGradientMode", modes[i].ToString()));
                if (modes[i] == val.mode) cur = i;
            }
            RoundedRectGraphic mb = null;
            mb = EventRows.Cell(c.Content, names[cur] + "  ▾", x, y, w, RowH,
                () => UI.EditorDropdown.Open((RectTransform)mb.transform, names, cur, i =>
                {
                    var g = Grad(evt, key, val);
                    g.mode = modes[i];
                    EventRows.Commit(c, ed, evt, pi, key, g);
                }), false, TextAnchor.MiddleLeft);
            y -= RowH + Gap;

            switch (val.mode)
            {
                case ParticleSystemGradientMode.Color:
                case ParticleSystemGradientMode.RandomColor:
                    y = HexRow(c, ed, evt, pi, key, val, 1, x, w, y);
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    y = HexRow(c, ed, evt, pi, key, val, 1, x, w, y);
                    y = HexRow(c, ed, evt, pi, key, val, 2, x, w, y);
                    break;
                case ParticleSystemGradientMode.Gradient:
                    y = ColorStops(c, ed, evt, pi, key, val, 1, x, w, y);
                    y = AlphaStops(c, ed, evt, pi, key, val, 1, x, w, y);
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    y = ColorStops(c, ed, evt, pi, key, val, 1, x, w, y);
                    y = AlphaStops(c, ed, evt, pi, key, val, 1, x, w, y);
                    y = ColorStops(c, ed, evt, pi, key, val, 2, x, w, y);
                    y = AlphaStops(c, ed, evt, pi, key, val, 2, x, w, y);
                    break;
            }
            return y;
        }

        // hex field + swatch for color1 / color2
        private static float HexRow(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, ADOFAI.Editor.Models.SerializedMinMaxGradient val,
            int slot, float x, float w, float y)
        {
            string hex = (slot == 1 ? val.color1 : val.color2) ?? "";
            float fw = w - RowH - Gap;
            EventRows.InputRow(c.Content, x, y, fw, hex, s =>
            {
                var g = Grad(evt, key, val);
                string h = (s ?? "").Trim().TrimStart('#');
                if (slot == 1) g.color1 = h; else g.color2 = h;
                EventRows.Commit(c, ed, evt, pi, key, g);
            });
            EventRows.Swatch(c.Content, hex, x + fw + Gap, y);
            return y - (RowH + Gap);
        }
    }
}
