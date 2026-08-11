using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* BULK EVENT EDITOR — edit one property across every matching event in a multi-tile
       selection. Opened from the copy panel's type rows (that tree is already the "which event
       types am I acting on" filter, so it is the target picker too — no second list to keep in
       sync).

       The form is a TEMPLATE event of the chosen type, rendered by the shared EventRows engine,
       so every control (enum dropdown, ease grid, colour, Vector2, tile ref) behaves exactly as
       it does in the inspector. It is a scratch object: Ctx.Scratch keeps its edits out of the
       undo stack.

       WHAT GETS WRITTEN: only the fields that DIFFER from a freshly-constructed event of that
       type. "Touched" is inferred from that diff rather than tracked per-keystroke, so setting a
       field back to its own default is not a bulk edit — see the ponytail note on Changed().

       Targets = events of that type on the selected tiles, plus any selected decorations of it,
       narrowed by an optional tag filter (matched against `tag` and `eventTag`). Applying always
       goes through a confirmation with the exact counts, inside one SaveStateScope. */
    internal static class EditorBulkEdit
    {
        private static readonly PanelKit K = new PanelKit("SapphireBulkEdit", 904, PanelW, focusable: true);
        private const float PanelW = 380f, HeaderH = 28f;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private static Vector2 _size = new Vector2(PanelW, 600f);
        private static RectTransform _viewport, _content;
        private static float _scroll;
        private static bool _open;
        private static int _type = -1;
        private static ADOFAI.LevelEvent _template, _defaults;
        private static string _tagFilter = "";
        private static long _sig;
        private static long _targetSig = long.MinValue;
        private static int _scanCd;
        private static bool _rebuilt;   // shell built for the current _type

        internal static bool IsOpen => _open;

        internal static void Open(int eventType)
        {
            _type = eventType;
            _template = NewEvent(eventType);
            _defaults = NewEvent(eventType);
            _tagFilter = "";
            _scroll = 0f;
            _open = _template != null && _defaults != null;
            _rebuilt = false;
            _sig = 0;
        }

        internal static void Close() { _open = false; _template = null; _defaults = null; _sig = 0; }

        internal static void Dispose()
        {
            K.Dispose();
            _viewport = null; _content = null;
            Close();
            _rebuilt = false;
        }

        private static ADOFAI.LevelEvent NewEvent(int type)
        {
            try { return new ADOFAI.LevelEvent(0, (ADOFAI.LevelEventType)type); }
            catch (Exception ex) { SapphireLog.Log("BulkEdit: template failed: " + ex.Message); return null; }
        }

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool live = _open && ed != null && !ed.playMode && MainClass.EditorSuiteOn && _template != null;
            if (!live) { if (_open && (ed == null || ed.playMode)) Close(); K.Show(false); return; }

            if (!K.Built || !_rebuilt) { BuildShell(); _rebuilt = true; _sig = 0; }
            // The target set lives outside this panel (tile selection, other people's edits), so
            // poll a cheap signature instead of trusting our own dirty flag — otherwise the
            // "N matching events" line and the Apply button go stale the moment the selection
            // changes. Rebuilds still only happen when the number actually moves.
            if (--_scanCd <= 0)
            {
                _scanCd = 12;
                long s = TargetSig(ed);
                if (s != _targetSig) { _targetSig = s; _sig = 0; }
            }
            if (_sig == 0) { _sig = 1; BuildContent(ed); }

            K.Show(true);
            ClampIntoView();
            TickScroll();
            TickResize();
        }

        // ── targets ─────────────────────────────────────────────────────────

        private static List<ADOFAI.LevelEvent> Targets(scnEditor ed)
        {
            var outp = new List<ADOFAI.LevelEvent>();
            if (ed == null || _type < 0) return outp;
            var sel = new HashSet<int>();
            try { foreach (var f in ed.selectedFloors) if (f != null) sel.Add(f.seqID); } catch { }
            var tags = TagTerms();
            try
            {
                foreach (var e in ed.events)
                    if (e != null && (int)e.eventType == _type && sel.Contains(e.floor) && TagMatch(e, tags))
                        outp.Add(e);
            }
            catch { }
            try
            {
                foreach (var d in ed.selectedDecorations)
                    if (d != null && (int)d.eventType == _type && TagMatch(d, tags) && !outp.Contains(d))
                        outp.Add(d);
            }
            catch { }
            return outp;
        }

        // Selection + how many of our type it holds. Counting is O(events) but runs on a
        // 12-frame cadence, not per frame.
        private static long TargetSig(scnEditor ed)
        {
            long h = 17;
            if (ed == null) return h;
            try { foreach (var f in ed.selectedFloors) if (f != null) h = h * 31 + f.seqID; } catch { }
            try
            {
                int n = 0;
                foreach (var e in ed.events) if (e != null && (int)e.eventType == _type) n++;
                h = h * 31 + n;
            }
            catch { }
            return h;
        }

        private static List<string> TagTerms()
        {
            var list = new List<string>();
            foreach (var raw in (_tagFilter ?? "").Split(',', ' '))
            {
                var t = raw.Trim();
                if (t.Length > 0) list.Add(t);
            }
            return list;
        }

        // Empty filter = everything. Otherwise the event's tag OR eventTag must contain one of
        // the terms — decoration targeting lives in `tag`, event grouping in `eventTag`, and a
        // charter thinks of both as "the tag".
        private static bool TagMatch(ADOFAI.LevelEvent e, List<string> terms)
        {
            if (terms.Count == 0) return true;
            string a = StrOf(e, "tag"), b = StrOf(e, "eventTag");
            foreach (var t in terms)
                if (a.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0
                    || b.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }

        private static string StrOf(ADOFAI.LevelEvent e, string key)
        {
            try
            {
                var d = EditorEvents.EventData(e);
                object v;
                if (d != null && d.TryGetValue(key, out v) && v is string s) return s;
            }
            catch { }
            return "";
        }

        /* ponytail: "the user specified this field" = its template value differs from a fresh
           event's. No per-keystroke touch tracking — the cost is that bulk-setting a field back
           to its OWN default is a no-op. Track commits by key in the Ctx if that ever bites. */
        private static List<string> Changed()
        {
            var keys = new List<string>();
            try
            {
                var t = EditorEvents.EventData(_template);
                var d = EditorEvents.EventData(_defaults);
                if (t == null || d == null) return keys;
                foreach (var kv in t)
                {
                    object dv;
                    if (!d.TryGetValue(kv.Key, out dv)) { keys.Add(kv.Key); continue; }
                    if (!ValueEq(kv.Value, dv)) keys.Add(kv.Key);
                }
            }
            catch { }
            return keys;
        }

        // Boxed values from the registry: Equals is right for strings/numbers/enums/Vector2, and
        // reference-equal structs like the particle tuples fall back to ToString rather than
        // reporting every one of them as "changed" every time.
        private static bool ValueEq(object a, object b)
        {
            if (a == null || b == null) return ReferenceEquals(a, b);
            if (a.Equals(b)) return true;
            return a.GetType() == b.GetType() && a.ToString() == b.ToString();
        }

        private static void Apply(scnEditor ed)
        {
            var keys = Changed();
            var targets = Targets(ed);
            if (keys.Count == 0 || targets.Count == 0) return;
            int n = 0;
            try
            {
                using (new SaveStateScope(ed))
                {
                    foreach (var e in targets)
                    {
                        foreach (var k in keys)
                        {
                            try
                            {
                                e[k] = _template[k];
                                // carry the per-property enable state with the value, or a field the
                                // template disabled would apply as an active value
                                if (_template.disabled != null && _template.disabled.TryGetValue(k, out bool dv)
                                    && e.disabled != null) e.disabled[k] = dv;
                            }
                            catch { }
                        }
                        n++;
                    }
                    try { ed.ApplyEventsToFloors(); } catch { }
                    try { ed.RemakePath(true, true); } catch { }
                    try { ed.UpdateDecorationObjects(); } catch { }
                }
            }
            catch (Exception ex) { SapphireLog.Log("BulkEdit: apply failed: " + ex.Message); }
            SapphireLog.Log("BulkEdit: " + keys.Count + " field(s) → " + n + " event(s) of " + Name(_type));
            _sig = 0;
        }

        private static string Name(int type)
        {
            try { return ((ADOFAI.LevelEventType)type).ToString(); } catch { return type.ToString(); }
        }

        private static string Title(int type)
        {
            try
            {
                bool ex;
                var loc = RDString.GetWithCheck("editor." + (ADOFAI.LevelEventType)type, out ex, null);
                if (ex && !string.IsNullOrEmpty(loc)) return loc;
            }
            catch { }
            return Name(type);
        }

        // ── UI ──────────────────────────────────────────────────────────────

        private static readonly EventRows.Ctx _ctx = new EventRows.Ctx
        {
            PanelW = PanelW,
            MarkDirty = () => _sig = 0,
            Scratch = true,     // the template is not in the chart — no undo states, no game refresh
        };

        private static void BuildShell()
        {
            K.LblW = 118f;
            K.Rebuild(Loc.T("Bulk edit") + " · " + Title(_type), Close, new Vector2(760f, -72f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 300f, 260f);
            K.OnDragEnd = () => K.SnapDockOnDragEnd();

            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(0f, 8f);
            _viewport.offsetMax = new Vector2(0f, -HeaderH - 2f);
            vpGo.AddComponent<RectMask2D>();
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.01f);
            vpImg.raycastTarget = true;   // wheel target

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void BuildContent(scnEditor ed)
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
            _ctx.Content = _content;
            _ctx.PanelW = _size.x;

            float w = _size.x - Pad * 2f;
            float y = -2f;
            var targets = Targets(ed);
            var keys = Changed();

            EventRows.Label(_content, targets.Count + " " + Loc.T("matching events") + " · "
                + keys.Count + " " + Loc.T("field(s) set"), Pad, y, w, RowH, Theme.TextMuted);
            y -= RowH + Gap;

            EventRows.Label(_content, Loc.T("Tag filter (blank = all)"), Pad, y, w, 16f, Theme.TextMuted);
            y -= 18f;
            EventRows.InputRow(_content, Pad, y, w, _tagFilter, v => { _tagFilter = v ?? ""; _sig = 0; });
            y -= RowH + Gap + 4f;

            ADOFAI.LevelEventInfo info = null;
            try { GCS.levelEventsInfo.TryGetValue(((ADOFAI.LevelEventType)_type).ToString(), out info); } catch { }
            if (info == null)
            {
                EventRows.Label(_content, Loc.T("(settings unavailable)"), Pad, y, w, RowH, Theme.TextMuted);
                y -= RowH + Gap;
            }
            else y = EventRows.Render(_ctx, ed, info, _template, y);

            y -= 6f;
            var applyBg = EventRows.Cell(_content, Loc.T("Apply to") + " " + targets.Count, Pad, y, w, RowH + 4f,
                () => AskApply(ed), true);
            applyBg.color = keys.Count > 0 && targets.Count > 0
                ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
                : new Color(1f, 1f, 1f, 0.06f);
            y -= RowH + 4f + Gap;
            EventRows.Cell(_content, Loc.T("Reset fields"), Pad, y, w, RowH,
                () => { _template = NewEvent(_type); _sig = 0; }, true);
            y -= RowH + Gap;

            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        private static void AskApply(scnEditor ed)
        {
            var keys = Changed();
            var targets = Targets(ed);
            if (keys.Count == 0 || targets.Count == 0)
            {
                ConfirmBox.Ask(Loc.T("Nothing to apply — change a field first, or widen the tag filter."),
                    Loc.T("OK"), null, false);
                return;
            }
            string fields = string.Join(", ", keys.ToArray());
            ConfirmBox.Ask(Loc.T("Bulk edit") + "\n\n" + Title(_type) + " · " + targets.Count + " "
                + Loc.T("events") + "\n" + fields, Loc.T("Apply"), () => Apply(ed));
        }

        // ── scroll / resize / clamp (same shape as the other native panels) ──

        private static void TickScroll()
        {
            if (_viewport == null || _content == null) return;
            float wheel = MainClass.WheelY;
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_viewport, Input.mousePosition, null)) return;
            _scroll = Mathf.Clamp(_scroll + wheel * 60f, 0f,
                Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void ClampScroll()
        {
            if (_viewport == null || _content == null) return;
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void TickResize()
        {
            if (!K.Built) return;
            var r = (RectTransform)K.PanelGo.transform;
            if ((r.sizeDelta - _size).sqrMagnitude > 1f) { _size = r.sizeDelta; _sig = 0; ClampScroll(); }
        }

        private static void ClampIntoView()
        {
            if (!K.Built) return;
            var r = (RectTransform)K.PanelGo.transform;
            var canvas = (RectTransform)K.CanvasGo.transform;
            var p = r.anchoredPosition;
            p.x = Mathf.Clamp(p.x, 0f, Mathf.Max(0f, canvas.rect.width - 80f));
            p.y = Mathf.Clamp(p.y, -(canvas.rect.height - 40f), 0f);
            if ((p - r.anchoredPosition).sqrMagnitude > 0.01f) r.anchoredPosition = p;
        }

        internal static bool Hovered
        {
            get
            {
                try
                {
                    return K.Visible && RectTransformUtility.RectangleContainsScreenPoint(
                        (RectTransform)K.PanelGo.transform, Input.mousePosition, null);
                }
                catch { return false; }
            }
        }
    }
}
