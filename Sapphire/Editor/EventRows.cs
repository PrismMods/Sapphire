using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Shared property-row engine for the native inspectors. A LevelEvent's properties are
       rendered from the game's own registry (ADOFAI.PropertyInfo): localized labels,
       per-property disable checkbox, and a control matched to the value type — bool toggle,
       enum / enum-typed-string cycle, Ease curve grid, Vector2 (NaN = "keep"), tile
       reference, color hex + swatch, or a typed text field. Commits go through a
       SaveStateScope and the Ctx.AfterCommit hook (events → ShowPanel refresh; level
       settings → UpdateSongAndLevelSettings). Used by EditorEventPanel and EditorLevelPanel
       so both stay identical and update-proof. */
    internal static class EventRows
    {
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        internal class Ctx
        {
            public RectTransform Content;     // rows are parented here
            public float PanelW;              // for full-width row math
            public Action MarkDirty;          // toggles/enums need a content redraw
            public Action<scnEditor, ADOFAI.LevelEvent, ADOFAI.PropertyInfo> AfterCommit;
        }

        // render every visible property of `evt` (from its registry info), return ending y
        internal static float Render(Ctx c, scnEditor ed, ADOFAI.LevelEventInfo info, ADOFAI.LevelEvent evt, float y)
        {
            if (info == null || info.propertiesInfo == null) return y;
            var data = EditorEvents.EventData(evt);
            if (data == null) return y;
            foreach (var kvp in info.propertiesInfo)
            {
                var pi = kvp.Value;
                string key = kvp.Key;
                if (pi == null) continue;
                try { if (pi.invisible) continue; } catch { }
                string ctl = "";
                try { ctl = pi.controlType.ToString(); } catch { }
                if (ctl == "Note" || ctl == "Export") continue;
                bool shown = true;
                try { shown = pi.CheckIfShown(evt, null); } catch { }
                if (!shown) continue;
                object val;
                if (!data.TryGetValue(key, out val)) continue;
                y = PropertyRow(c, ed, evt, pi, key, val, y);
            }
            return y;
        }

        internal static float PropertyRow(Ctx c, scnEditor ed, ADOFAI.LevelEvent evt, ADOFAI.PropertyInfo pi,
            string key, object val, float y)
        {
            var content = c.Content;
            float panelW = c.PanelW;
            string lbl = key;
            try
            {
                string lockey = null;
                try { lockey = pi.customLocalizationKey; } catch { }
                bool ex = false;
                string loc = null;
                if (!string.IsNullOrEmpty(lockey)) loc = RDString.GetWithCheck(lockey, out ex, null);
                if (!ex) loc = RDString.GetWithCheck("editor." + evt.eventType + "." + key, out ex, null);
                if (!ex) loc = RDString.GetWithCheck("editor." + key, out ex, null);
                lbl = ex && !string.IsNullOrEmpty(loc) ? loc : Loc.T(key);
            }
            catch { }

            bool canDisable = false;
            try { canDisable = pi.canBeDisabled; } catch { }
            bool isDisabled = false;
            try { isDisabled = evt.disabled != null && evt.disabled.TryGetValue(key, out var dv) && dv; } catch { }
            float x = Pad;
            float w = panelW - Pad * 2f;
            if (canDisable)
            {
                Radio(content, !isDisabled, x, y, () =>
                {
                    try
                    {
                        using (new SaveStateScope(ed))
                            evt.disabled[key] = !isDisabled;
                        c.AfterCommit?.Invoke(ed, evt, pi);
                    }
                    catch (Exception ex2) { SapphireLog.Log("EventRows: disable toggle failed: " + ex2.Message); }
                    c.MarkDirty?.Invoke();
                });
                x += 24f;
                w -= 24f;
            }

            var lblCol = isDisabled ? new Color(0.45f, 0.45f, 0.5f, 1f) : Theme.TextMuted;
            string k = key;
            var e2 = evt;
            var p2 = pi;

            string[] strOpts = null;
            try { if (val is string && pi.enumType != null) strOpts = Enum.GetNames(pi.enumType); } catch { }

            if (val is bool bv)
            {
                var cellBg = Cell(content, lbl, x, y, w, RowH, () => Commit(c, ed, e2, p2, k, !bv), false, TextAnchor.MiddleLeft);
                cellBg.color = bv && !isDisabled
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f)
                    : new Color(1f, 1f, 1f, 0.05f);
                return y - (RowH + Gap);
            }
            if (val is DG.Tweening.Ease ez)
            {
                Label(content, lbl, x, y, w, 16f, lblCol); y -= 18f;
                RoundedRectGraphic bg = null;
                bg = Cell(content, LocEnum("Ease", ez.ToString()), x, y, w, RowH, () =>
                {
                    Vector2 pos = bg != null ? (Vector2)bg.transform.position : (Vector2)Input.mousePosition;
                    EditorEasePicker.Open(Loc.T("Ease"), ez, pos, nv => Commit(c, ed, e2, p2, k, nv));
                }, true);
                return y - (RowH + Gap);
            }
            if (val is Enum en)
            {
                Label(content, lbl, x, y, w, 16f, lblCol); y -= 18f;
                var cell = Cell(content, LocEnum(en.GetType().Name, en.ToString()), x, y, w, RowH,
                    () => Commit(c, ed, e2, p2, k, StepEnum(en, +1)), false);
                var ch = cell.gameObject.GetComponent<UI.ClickHandler>();
                if (ch != null) ch.OnRightClick = () => Commit(c, ed, e2, p2, k, StepEnum(en, -1));
                return y - (RowH + Gap);
            }
            if (strOpts != null && strOpts.Length > 0)
            {
                string sv = (string)val;
                string tn = null;
                try { tn = pi.enumType.Name; } catch { }
                int cur = Mathf.Max(0, Array.IndexOf(strOpts, sv));
                var opts = strOpts;
                Label(content, lbl, x, y, w, 16f, lblCol); y -= 18f;
                var cell = Cell(content, LocEnum(tn, sv), x, y, w, RowH,
                    () => Commit(c, ed, e2, p2, k, opts[(cur + 1) % opts.Length]), false);
                var ch = cell.gameObject.GetComponent<UI.ClickHandler>();
                if (ch != null) ch.OnRightClick = () => Commit(c, ed, e2, p2, k, opts[(cur - 1 + opts.Length) % opts.Length]);
                return y - (RowH + Gap);
            }
            if (val is Vector2 v2)
            {
                Label(content, lbl, x, y, w, 16f, lblCol); y -= 18f;
                float fw = (w - Gap) * 0.5f;
                InputRow(content, x, y, fw, VecComp(v2.x), sv =>
                    Commit(c, ed, e2, p2, k, new Vector2(ParseComp(sv), ((Vector2)ValOf(e2, k, v2)).y)));
                InputRow(content, x + fw + Gap, y, fw, VecComp(v2.y), sv =>
                    Commit(c, ed, e2, p2, k, new Vector2(((Vector2)ValOf(e2, k, v2)).x, ParseComp(sv))));
                return y - (RowH + Gap);
            }
            if (val is Tuple<int, TileRelativeTo> tile)
            {
                Label(content, lbl, x, y, w, 16f, lblCol); y -= 18f;
                float fw = (w - Gap) * 0.45f;
                InputRow(content, x, y, fw, tile.Item1.ToString(), sv =>
                {
                    int n;
                    if (int.TryParse(sv, out n))
                    {
                        var cur2 = ValOf(e2, k, tile) as Tuple<int, TileRelativeTo>;
                        Commit(c, ed, e2, p2, k, Tuple.Create(n, cur2 != null ? cur2.Item2 : tile.Item2));
                    }
                });
                Cell(content, LocEnum("TileRelativeTo", tile.Item2.ToString()), x + fw + Gap, y, w - fw - Gap, RowH, () =>
                {
                    var cur2 = ValOf(e2, k, tile) as Tuple<int, TileRelativeTo> ?? tile;
                    var next = (TileRelativeTo)(((int)cur2.Item2 + 1) % 3);
                    Commit(c, ed, e2, p2, k, Tuple.Create(cur2.Item1, next));
                }, false);
                return y - (RowH + Gap);
            }

            bool isColor = false;
            try { isColor = pi.controlType.ToString().IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0; }
            catch { }
            Label(content, lbl, x, y, w, 16f, lblCol); y -= 18f;
            float inputW = w - (isColor ? RowH + Gap : 0f);
            InputRow(content, x, y, inputW, FormatVal(val), sv => CommitText(c, ed, e2, p2, k, sv, val));
            if (isColor)
            {
                var swGo = new GameObject("Sw", typeof(RectTransform));
                swGo.transform.SetParent(content, false);
                var sr = (RectTransform)swGo.transform;
                sr.anchorMin = sr.anchorMax = new Vector2(0f, 1f);
                sr.pivot = new Vector2(0f, 1f);
                sr.anchoredPosition = new Vector2(x + inputW + Gap, y);
                sr.sizeDelta = new Vector2(RowH, RowH);
                var swBg = swGo.AddComponent<RoundedRectGraphic>();
                swBg.Radius = 5f;
                Color col;
                swBg.color = ColorUtility.TryParseHtmlString("#" + FormatVal(val).TrimStart('#'), out col)
                    ? col : Color.magenta;
                swBg.BorderWidth = 1f;
                swBg.BorderColor = new Color(1f, 1f, 1f, 0.2f);
            }
            return y - (RowH + Gap);
        }

        // ── commits ──────────────────────────────────────────────────────────

        private static void Commit(Ctx c, scnEditor ed, ADOFAI.LevelEvent evt, ADOFAI.PropertyInfo pi, string key, object v)
        {
            try
            {
                using (new SaveStateScope(ed))
                    evt[key] = v;
                c.AfterCommit?.Invoke(ed, evt, pi);
            }
            catch (Exception ex) { SapphireLog.Log("EventRows: edit failed: " + ex.Message); }
            c.MarkDirty?.Invoke();
        }

        private static void CommitText(Ctx c, scnEditor ed, ADOFAI.LevelEvent evt, ADOFAI.PropertyInfo pi,
            string key, string raw, object oldVal)
        {
            try
            {
                object v = CoerceLike(raw, oldVal);
                using (new SaveStateScope(ed))
                    evt[key] = v;
                c.AfterCommit?.Invoke(ed, evt, pi);
            }
            catch (Exception ex) { SapphireLog.Log("EventRows: edit failed: " + ex.Message); }
        }

        private static object CoerceLike(string raw, object oldVal)
        {
            raw = (raw ?? "").Trim();
            try
            {
                if (oldVal is int) return (int)float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (oldVal is float) return float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (oldVal is double) return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (oldVal is long) return (long)float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return oldVal; }
            return raw;
        }

        private static object ValOf(ADOFAI.LevelEvent evt, string key, object fallback)
        {
            try
            {
                var d = EditorEvents.EventData(evt);
                object v;
                if (d != null && d.TryGetValue(key, out v) && v != null) return v;
            }
            catch { }
            return fallback;
        }

        // ── builders (parented to the given content; reused by panels + tab rails) ──

        /* Per-property enable toggle — the ring+dot radio from the Ctrl+E settings menu
           (square-aligned, centered on the row), not the old glyph-in-a-box. `on` = enabled
           (not in the game's disabled dict). A 20×RowH transparent hit area holds it. */
        internal static void Radio(RectTransform content, bool on, float x, float y, Action onClick)
        {
            var hit = new GameObject("Radio", typeof(RectTransform));
            hit.transform.SetParent(content, false);
            var hr = (RectTransform)hit.transform;
            hr.anchorMin = hr.anchorMax = new Vector2(0f, 1f);
            hr.pivot = new Vector2(0f, 1f);
            hr.anchoredPosition = new Vector2(x, y);
            hr.sizeDelta = new Vector2(20f, RowH);
            var hbg = hit.AddComponent<Image>();
            hbg.color = new Color(0f, 0f, 0f, 0.01f);
            hbg.raycastTarget = true;

            const float ring = 16f, dotSz = 7f;
            var ringGo = new GameObject("Ring", typeof(RectTransform));
            ringGo.transform.SetParent(hit.transform, false);
            var rr = (RectTransform)ringGo.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0f, 0.5f);
            rr.pivot = new Vector2(0f, 0.5f);
            rr.anchoredPosition = new Vector2(1f, 0f);
            rr.sizeDelta = new Vector2(ring, ring);
            var rg = ringGo.AddComponent<RoundedRectGraphic>();
            rg.Radius = ring * 0.5f;
            rg.BorderWidth = 1.25f;
            rg.BorderColor = on ? Theme.ToggleOn : Theme.ToggleOff;
            rg.color = new Color(0f, 0f, 0f, 0f);
            rg.raycastTarget = false;
            if (on)
            {
                var dotGo = new GameObject("Dot", typeof(RectTransform));
                dotGo.transform.SetParent(ringGo.transform, false);
                var dr = (RectTransform)dotGo.transform;
                dr.anchorMin = dr.anchorMax = new Vector2(0.5f, 0.5f);
                dr.pivot = new Vector2(0.5f, 0.5f);
                dr.sizeDelta = new Vector2(dotSz, dotSz);
                var dg = dotGo.AddComponent<RoundedRectGraphic>();
                dg.Radius = dotSz * 0.5f;
                dg.color = Theme.ToggleOn;
                dg.raycastTarget = false;
            }
            UI.ClickHandler.Attach(hit, onClick);
        }

        internal static void Label(RectTransform content, string text, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject("L", typeof(RectTransform));
            go.transform.SetParent(content, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            var t = UIBuilder.Tmp(go, text, 11.5f, TextAnchor.MiddleLeft, color);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
        }

        internal static RoundedRectGraphic Cell(RectTransform content, string text, float x, float y, float w, float h,
            Action onClick, bool button, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("C", typeof(RectTransform));
            go.transform.SetParent(content, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = button ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.05f);
            bg.raycastTarget = true;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(anchor == TextAnchor.MiddleLeft ? 8f : 0f, 0f);
            lr.offsetMax = Vector2.zero;
            var t = UIBuilder.Tmp(lGo, text, 12f, anchor, Theme.Text);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            UI.ClickHandler.Attach(go, onClick);
            return bg;
        }

        internal static void InputRow(RectTransform content, float x, float y, float w, string value, Action<string> commit)
        {
            var go = new GameObject("F", typeof(RectTransform));
            go.transform.SetParent(content, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, RowH);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.08f);
            bg.raycastTarget = true;
            var txtGo = new GameObject("T", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(7f, 0f); tr.offsetMax = new Vector2(-7f, 0f);
            var txt = UIBuilder.Tmp(txtGo, value, 12f, TextAnchor.MiddleLeft, Theme.Text);
            txt.richText = false;
            var field = UIBuilder.BuildInputField(go, txt);
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = value;
            field.onEndEdit.AddListener(v => commit(v));
        }

        // ── small shared helpers ──────────────────────────────────────────────

        internal static string LocEnum(string enumType, string value)
        {
            if (!string.IsNullOrEmpty(enumType))
                try
                {
                    bool ex;
                    var loc = RDString.GetWithCheck("enum." + enumType + "." + value, out ex, null);
                    if (ex && !string.IsNullOrEmpty(loc)) return loc;
                }
                catch { }
            return Loc.T(value);
        }

        private static object StepEnum(Enum cur, int dir)
        {
            var vals = Enum.GetValues(cur.GetType());
            int idx = Array.IndexOf(vals, cur);
            return vals.GetValue(((idx + dir) % vals.Length + vals.Length) % vals.Length);
        }

        private static string VecComp(float v) => float.IsNaN(v) ? "" : v.ToString("0.###");

        private static float ParseComp(string sv)
        {
            float f;
            return float.TryParse((sv ?? "").Trim(), out f) ? f : float.NaN;
        }

        private static string FormatVal(object v)
        {
            if (v == null) return "";
            if (v is string s) return s;
            if (v is bool b) return b ? "true" : "false";
            if (v is float f) return f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            if (v is double d) return d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            if (v is Vector2 p) return p.x + ", " + p.y;
            return v.ToString();
        }
    }
}
