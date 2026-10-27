using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
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

        private static string D(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /* The gradient models are public fields on 2.9 and auto-properties on 3.x, and a key's
           time/alpha is float there, decimal here — so their members go by name and numbers convert
           to whatever this build declares. Naming one binds the dll to one build's shape. Plain
           reflection, not AccessTools: MelonLoader logs a warning for every AccessTools miss. */
        private static readonly Dictionary<string, MemberInfo> _mem = new Dictionary<string, MemberInfo>();

        private static MemberInfo Mem(Type t, string name)
        {
            string k = t.FullName + "." + name;
            MemberInfo m;
            if (!_mem.TryGetValue(k, out m))
            {
                const BindingFlags F = BindingFlags.Public | BindingFlags.Instance;
                m = (MemberInfo)t.GetProperty(name, F) ?? t.GetField(name, F);
                _mem[k] = m;
            }
            return m;
        }

        private static object Get(object o, string name)
        {
            var m = Mem(o.GetType(), name);
            var p = m as System.Reflection.PropertyInfo;
            return p != null ? p.GetValue(o, null) : ((FieldInfo)m).GetValue(o);
        }

        // Returns the changed copy: these are structs.
        private static T Set<T>(T o, string name, object v)
        {
            object box = o;
            var m = Mem(typeof(T), name);
            var p = m as System.Reflection.PropertyInfo;
            Type mt = p != null ? p.PropertyType : ((FieldInfo)m).FieldType;
            if (v is double) v = Convert.ChangeType(v, mt, CultureInfo.InvariantCulture);
            if (p != null) p.SetValue(box, v, null); else ((FieldInfo)m).SetValue(box, v);
            return (T)box;
        }

        private static double Num(object key, string name) => Convert.ToDouble(Get(key, name), CultureInfo.InvariantCulture);

        private static ADOFAI.Editor.Models.SerializedMinMaxGradient Grad(
            ADOFAI.LevelEvent evt, string key, ADOFAI.Editor.Models.SerializedMinMaxGradient fallback)
        {
            var v = Raw(evt, key);
            return v is ADOFAI.Editor.Models.SerializedMinMaxGradient g ? g : fallback;
        }

        private static ADOFAI.Editor.Models.SerializedGradient SlotGrad(
            ADOFAI.Editor.Models.SerializedMinMaxGradient mm, int slot)
        {
            var gr = Get(mm, slot == 1 ? "gradient1" : "gradient2");
            // Not `new SerializedGradient()`: that leaves BOTH key arrays null, so adding only a
            // colour stop still ships a null alphaKeys into ToGradient's unguarded Select.
            return gr != null ? (ADOFAI.Editor.Models.SerializedGradient)gr : DefaultGrad();
        }

        /* Vanilla's PropertyControl_MinMaxGradient backfills on every mode change, and it must:
           ToMinMaxGradient derefs gradient1/2 and ToGradient walks their key arrays, both without
           a null check — so a mode switch that leaves them null writes an event the game throws on
           at load. */
        private static void Backfill(ref ADOFAI.Editor.Models.SerializedMinMaxGradient g)
        {
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    if (Get(g, "color1") == null) g = Set(g, "color1", "ffffff");
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    if (Get(g, "color1") == null) g = Set(g, "color1", "ffffff");
                    if (Get(g, "color2") == null) g = Set(g, "color2", "ffffff");
                    break;
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:   // reads gradient1, not color1
                    if (Get(g, "gradient1") == null) g = Set(g, "gradient1", DefaultGrad());
                    break;
                case ParticleSystemGradientMode.TwoGradients:
                    if (Get(g, "gradient1") == null) g = Set(g, "gradient1", DefaultGrad());
                    if (Get(g, "gradient2") == null) g = Set(g, "gradient2", DefaultGrad());
                    break;
            }
        }

        // mirrors the game's private PropertyControl_MinMaxGradient.DefaultGradient()
        private static ADOFAI.Editor.Models.SerializedGradient DefaultGrad()
        {
            var g = new ADOFAI.Editor.Models.SerializedGradient();
            var c0 = Set(Set(new ADOFAI.Editor.Models.SerializedGradient.ColorKey(), "color", "ffffff"), "time", 0.0);
            var c1 = Set(Set(new ADOFAI.Editor.Models.SerializedGradient.ColorKey(), "color", "ffffff"), "time", 1.0);
            var a0 = Set(Set(new ADOFAI.Editor.Models.SerializedGradient.AlphaKey(), "time", 0.0), "alpha", 1.0);
            var a1 = Set(Set(new ADOFAI.Editor.Models.SerializedGradient.AlphaKey(), "time", 1.0), "alpha", 1.0);
            g = Set(g, "colorKeys", new[] { c0, c1 });
            g = Set(g, "alphaKeys", new[] { a0, a1 });
            return g;
        }

        private static void PutSlotGrad(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, ADOFAI.Editor.Models.SerializedMinMaxGradient val,
            int slot, ADOFAI.Editor.Models.SerializedGradient g)
        {
            var mm = Grad(evt, key, val);
            mm = Set(mm, slot == 1 ? "gradient1" : "gradient2", g);
            EventRows.Commit(c, ed, evt, pi, key, mm);
        }

        // [time] [hex] [×] per stop, then a [+] add row
        private static float ColorStops(EventRows.Ctx c, scnEditor ed, ADOFAI.LevelEvent evt,
            ADOFAI.PropertyInfo pi, string key, ADOFAI.Editor.Models.SerializedMinMaxGradient val,
            int slot, float x, float w, float y)
        {
            var g = SlotGrad(Grad(evt, key, val), slot);
            var keys = (ADOFAI.Editor.Models.SerializedGradient.ColorKey[])Get(g, "colorKeys") ?? new ADOFAI.Editor.Models.SerializedGradient.ColorKey[0];
            EventRows.Label(c.Content, Loc.T("Colour stops"), x, y, w, 16f, Theme.TextMuted);
            y -= 18f;
            const float tw = 52f, bw = 24f;
            float hw = w - tw - bw - Gap * 2f;
            for (int i = 0; i < keys.Length; i++)
            {
                int idx = i;
                EventRows.InputRow(c.Content, x, y, tw, D(Num(keys[i], "time")), s =>
                {
                    double d;
                    if (!ExprEval.TryParseDouble(s, out d)) return;
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    var arr = (ADOFAI.Editor.Models.SerializedGradient.ColorKey[])Get(cg, "colorKeys");
                    if (arr == null || idx >= arr.Length) return;
                    // Clone: the array is the SAME reference the undo snapshot's shallow-copied
                    // boxed gradient points at (SaveStateScope's ctor snapshots BEFORE this runs) —
                    // mutating in place corrupts undo. Append/RemoveAt already allocate fresh arrays.
                    var arr2 = (ADOFAI.Editor.Models.SerializedGradient.ColorKey[])arr.Clone();
                    arr2[idx] = Set(arr2[idx], "time", d);
                    cg = Set(cg, "colorKeys", arr2);
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                });
                EventRows.InputRow(c.Content, x + tw + Gap, y, hw, (string)Get(keys[i], "color") ?? "", s =>
                {
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    var arr = (ADOFAI.Editor.Models.SerializedGradient.ColorKey[])Get(cg, "colorKeys");
                    if (arr == null || idx >= arr.Length) return;
                    var arr2 = (ADOFAI.Editor.Models.SerializedGradient.ColorKey[])arr.Clone();
                    arr2[idx] = Set(arr2[idx], "color", (s ?? "").Trim().TrimStart('#'));
                    cg = Set(cg, "colorKeys", arr2);
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                });
                EventRows.Cell(c.Content, "×", x + tw + hw + Gap * 2f, y, bw, RowH, () =>
                {
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    // Re-read the LIVE array: the rebuild is deferred, so two quick clicks would
                    // otherwise empty it — and ToGradient/SetKeys can't take a zero-length array.
                    var live = (ADOFAI.Editor.Models.SerializedGradient.ColorKey[])Get(cg, "colorKeys");
                    if (live == null || live.Length <= 1) return;
                    cg = Set(cg, "colorKeys", RemoveAt(live, idx));
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                }, true);
                y -= RowH + Gap;
            }
            EventRows.Cell(c.Content, "+", x, y, w, RowH, () =>
            {
                var cg = SlotGrad(Grad(evt, key, val), slot);
                var add = Set(Set(new ADOFAI.Editor.Models.SerializedGradient.ColorKey(), "color", "ffffff"), "time", 1.0);
                cg = Set(cg, "colorKeys", Append((ADOFAI.Editor.Models.SerializedGradient.ColorKey[])Get(cg, "colorKeys"), add));
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
            var keys = (ADOFAI.Editor.Models.SerializedGradient.AlphaKey[])Get(g, "alphaKeys") ?? new ADOFAI.Editor.Models.SerializedGradient.AlphaKey[0];
            EventRows.Label(c.Content, Loc.T("Alpha stops"), x, y, w, 16f, Theme.TextMuted);
            y -= 18f;
            const float tw = 52f, bw = 24f;
            float aw = w - tw - bw - Gap * 2f;
            for (int i = 0; i < keys.Length; i++)
            {
                int idx = i;
                EventRows.InputRow(c.Content, x, y, tw, D(Num(keys[i], "time")), s =>
                {
                    double d;
                    if (!ExprEval.TryParseDouble(s, out d)) return;
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    var arr = (ADOFAI.Editor.Models.SerializedGradient.AlphaKey[])Get(cg, "alphaKeys");
                    if (arr == null || idx >= arr.Length) return;
                    // Clone: see ColorStops — mutating the live array corrupts the undo snapshot.
                    var arr2 = (ADOFAI.Editor.Models.SerializedGradient.AlphaKey[])arr.Clone();
                    arr2[idx] = Set(arr2[idx], "time", d);
                    cg = Set(cg, "alphaKeys", arr2);
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                });
                EventRows.InputRow(c.Content, x + tw + Gap, y, aw, D(Num(keys[i], "alpha")), s =>
                {
                    double d;
                    if (!ExprEval.TryParseDouble(s, out d)) return;
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    var arr = (ADOFAI.Editor.Models.SerializedGradient.AlphaKey[])Get(cg, "alphaKeys");
                    if (arr == null || idx >= arr.Length) return;
                    var arr2 = (ADOFAI.Editor.Models.SerializedGradient.AlphaKey[])arr.Clone();
                    arr2[idx] = Set(arr2[idx], "alpha", d);
                    cg = Set(cg, "alphaKeys", arr2);
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                });
                EventRows.Cell(c.Content, "×", x + tw + aw + Gap * 2f, y, bw, RowH, () =>
                {
                    var cg = SlotGrad(Grad(evt, key, val), slot);
                    var live = (ADOFAI.Editor.Models.SerializedGradient.AlphaKey[])Get(cg, "alphaKeys");
                    if (live == null || live.Length <= 1) return;   // see ColorStops
                    cg = Set(cg, "alphaKeys", RemoveAt(live, idx));
                    PutSlotGrad(c, ed, evt, pi, key, val, slot, cg);
                }, true);
                y -= RowH + Gap;
            }
            EventRows.Cell(c.Content, "+", x, y, w, RowH, () =>
            {
                var cg = SlotGrad(Grad(evt, key, val), slot);
                var add = Set(Set(new ADOFAI.Editor.Models.SerializedGradient.AlphaKey(), "time", 1.0), "alpha", 1.0);
                cg = Set(cg, "alphaKeys", Append((ADOFAI.Editor.Models.SerializedGradient.AlphaKey[])Get(cg, "alphaKeys"), add));
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
                    Backfill(ref g);   // before Commit: Commit writes evt[key] first, so a bad value lands
                    EventRows.Commit(c, ed, evt, pi, key, g);
                }), false, TextAnchor.MiddleLeft);
            y -= RowH + Gap;

            switch (val.mode)
            {
                case ParticleSystemGradientMode.Color:
                    y = HexRow(c, ed, evt, pi, key, val, 1, x, w, y);
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    y = HexRow(c, ed, evt, pi, key, val, 1, x, w, y);
                    y = HexRow(c, ed, evt, pi, key, val, 2, x, w, y);
                    break;
                case ParticleSystemGradientMode.Gradient:
                case ParticleSystemGradientMode.RandomColor:   // ToMinMaxGradient reads gradient1 here too
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
            string hex = (string)Get(val, slot == 1 ? "color1" : "color2") ?? "";
            float fw = w - RowH - Gap;
            EventRows.InputRow(c.Content, x, y, fw, hex, s =>
            {
                var g = Grad(evt, key, val);
                string h = (s ?? "").Trim().TrimStart('#');
                g = Set(g, slot == 1 ? "color1" : "color2", h);
                EventRows.Commit(c, ed, evt, pi, key, g);
            });
            EventRows.Swatch(c.Content, hex, x + fw + Gap, y, null, nv =>
            {
                var g = Grad(evt, key, val);
                g = Set(g, slot == 1 ? "color1" : "color2", nv);
                EventRows.Commit(c, ed, evt, pi, key, g);
            });
            return y - (RowH + Gap);
        }

        // ── ParticlePlayback ─────────────────────────────────────────────────

        /* AddParticle's `playbackControl` is a transport, not a value: it has NO entry in
           LevelEvent.data, so EventRows.Render must dispatch it before its TryGetValue bail.
           Preview only — writes nothing, takes no SaveStateScope. */
        internal static float PlaybackRow(EventRows.Ctx c, ADOFAI.LevelEvent evt,
            float x, float w, float y)
        {
            EventRows.Label(c.Content, Loc.T("Particle preview"), x, y, w, 16f, Theme.TextMuted);
            y -= 18f;
            float bw = (w - Gap * 2f) / 3f;
            EventRows.Cell(c.Content, Loc.T("Play"), x, y, bw, RowH, () => Transport(evt, 0), true);
            EventRows.Cell(c.Content, Loc.T("Stop"), x + bw + Gap, y, bw, RowH, () => Transport(evt, 1), true);
            EventRows.Cell(c.Content, Loc.T("Restart"), x + (bw + Gap) * 2f, y, bw, RowH, () => Transport(evt, 2), true);
            return y - (RowH + Gap);
        }

        private static void Transport(ADOFAI.LevelEvent evt, int mode)
        {
            try
            {
                var p = scrDecorationManager.GetDecoration(evt) as scrParticleDecoration;
                if (p == null) return;
                if (mode == 2) { p.ResetParticle(evt, true); return; }
                var ps = p.particleSystem;
                if (ps == null) return;
                if (mode == 0) ps.Play(true); else ps.Stop(true);
            }
            catch (Exception ex) { SapphireLog.Log("EventRows: particle transport failed: " + ex.Message); }
        }
    }
}
