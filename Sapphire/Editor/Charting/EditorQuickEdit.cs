using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Quick chart's editor for the selected tile's events (U by default). One event opens
       straight to its parameters; several open a numbered list — each row previews every
       parameter — and 1–9 or a click picks one. Fields are the event panel's own rows (EventRows),
       so types, dropdowns, colours and $formulas behave the same; each commit is an undo step.
       Enter after editing a field applies and closes, Esc closes, Tab moves between fields. */
    internal static class EditorQuickEdit
    {
        private static readonly PanelKit K = new PanelKit("SapphireQuickEdit", 910, PanelW, focusable: true);
        private const float PanelW = 380f, HeaderH = 28f, Pad = 8f, Gap = 4f;
        private static Vector2 _size = new Vector2(PanelW, 380f);
        private static RectTransform _viewport, _content;
        private static float _scroll;
        private static bool _open, _dirty, _focusFirst, _place, _typingHere;
        private static int _floor = -1;
        private static List<ADOFAI.LevelEvent> _events = new List<ADOFAI.LevelEvent>();
        private static ADOFAI.LevelEvent _edit;

        private static readonly EventRows.Ctx _ctx = new EventRows.Ctx
        {
            MarkDirty = () => _dirty = true,
            AfterCommit = (ed, evt, pi) => { EditorEventPanel.EventAfterCommit(ed, evt, pi); EditorEventPanel.Refresh(); },
        };

        // The list owns 1–9 while it's up, so the dock and palette don't also arm tools.
        internal static bool Picking => _open && _edit == null && K.Visible;

        /* Esc closes this window and must not ALSO deselect the tile (the game's DeselectAll).
           Whichever ticks first, one of these is true on that frame. */
        private static int _escFrame = -10;
        internal static bool SwallowsEsc => (_open && K.Visible) || _escFrame == Time.frameCount;

        internal static void Toggle(scnEditor ed)
        {
            if (_open) { Close(); return; }
            int seq = -1;
            try { if (ed.SelectionIsSingle()) seq = ed.selectedFloors[0].seqID; } catch { }
            if (seq < 0) { Notify(ed, Loc.T("Select one tile first")); return; }
            var evs = OnFloor(ed, seq);
            if (evs.Count == 0) { Notify(ed, Loc.T("No events on this tile")); return; }
            _floor = seq;
            _events = evs;
            _edit = evs.Count == 1 ? evs[0] : null;
            _open = true; _dirty = true; _place = true; _focusFirst = _edit != null; _scroll = 0f;
        }

        private static void Close() { _open = false; _edit = null; _events.Clear(); _floor = -1; }

        private static List<ADOFAI.LevelEvent> OnFloor(scnEditor ed, int seq)
        {
            var l = new List<ADOFAI.LevelEvent>();
            try { foreach (var e in ed.events) if (e != null && e.floor == seq) l.Add(e); } catch { }
            return l;
        }

        internal static void Tick()
        {
            if (!_open) { K.Show(false); return; }
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            int sel = -1;
            try { if (ed != null && ed.SelectionIsSingle()) sel = ed.selectedFloors[0].seqID; } catch { }
            if (ed == null || ed.playMode || !MainClass.EditorSuiteOn || sel != _floor) { Close(); K.Show(false); return; }

            // An undo or a delete can take events away underneath the window.
            var now = OnFloor(ed, _floor);
            if (_edit != null && !now.Contains(_edit)) { Close(); K.Show(false); return; }
            if (_edit == null && !now.SequenceEqual(_events))
            {
                _events = now;
                if (_events.Count == 0) { Close(); K.Show(false); return; }
                _dirty = true;
            }

            if (!K.Built) BuildShell();
            if (_dirty && !FieldNav.Typing) { _dirty = false; Rebuild(ed); }
            K.Show(true);
            if (_place) { _place = false; PlaceCentered(); }
            if (_focusFirst) { _focusFirst = false; FocusFirstField(); }
            TickKeys();
            TickScroll();
        }

        private static void TickKeys()
        {
            // Enter that just committed one of OUR fields applies and closes.
            bool enter = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter);
            if (enter && _typingHere) { Close(); return; }
            _typingHere = FieldNav.Typing && SelectedInWindow();
            if (FieldNav.Typing) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { _escFrame = Time.frameCount; Close(); return; }
            if (_edit != null) return;
            for (int i = 0; i < 9 && i < _events.Count; i++)
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i)) { Pick(_events[i]); return; }
        }

        private static bool SelectedInWindow()
        {
            try
            {
                var go = UnityEngine.EventSystems.EventSystem.current.currentSelectedGameObject;
                return go != null && K.PanelGo != null && go.transform.IsChildOf(K.PanelGo.transform);
            }
            catch { return false; }
        }

        private static void Pick(ADOFAI.LevelEvent e) { _edit = e; _dirty = true; _focusFirst = true; _scroll = 0f; }

        internal static void Dispose()
        {
            K.Dispose();
            _viewport = null; _content = null;
            Close();
        }

        // ── UI ────────────────────────────────────────────────────────────────

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Edit event"), Close, new Vector2(480f, -160f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 260f, 160f);
            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = Vector2.zero; _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = new Vector2(0f, 3f); _viewport.offsetMax = new Vector2(0f, -HeaderH);
            vpGo.AddComponent<RectMask2D>();
            var img = vpGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.01f);
            img.raycastTarget = true;
            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
        }

        private static void Rebuild(scnEditor ed)
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--) UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
            var r = (RectTransform)K.PanelGo.transform;
            _size = r.sizeDelta;
            float w = _size.x - Pad * 2f, y = -Pad;
            if (_edit == null)
            {
                Text(Loc.T("Tile") + " #" + _floor + " · " + Loc.T("pick an event (1–9)"), Pad, y, w, 20f, 12f, Theme.TextMuted);
                y -= 20f + Gap;
                for (int i = 0; i < _events.Count; i++) y = PickRow(i, _events[i], Pad, y, w) - Gap;
            }
            else
            {
                if (_events.Count > 1)
                {
                    EventRows.Cell(_content, "‹ " + Loc.T("back"), Pad, y, 70f, PanelKit.RowH, () => { _edit = null; _dirty = true; }, true);
                    y -= PanelKit.RowH + Gap;
                }
                var head = EventRows.Cell(_content, EditorEventPanel.EventTitle(_edit) + EditorEventPanel.TagSuffix(_edit) + "  · #" + _floor,
                    Pad, y, w, PanelKit.RowH, () => { }, false, TextAnchor.MiddleLeft);
                head.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.25f);
                y -= PanelKit.RowH + Gap;
                var info = EditorEventPanel.InfoOf(_edit);
                if (info != null)
                {
                    _ctx.Content = _content;
                    _ctx.PanelW = _size.x;
                    y = EventRows.Render(_ctx, ed, info, _edit, y);
                }
                Text(Loc.T("Enter applies · Esc closes · Tab moves between fields"), Pad, y - 2f, w, 18f, 11f, Theme.TextMuted);
                y -= 22f;
            }
            _content.sizeDelta = new Vector2(0f, -y + Pad);
            ClampScroll();
        }

        // Badge, icon, title, then every parameter's current value — enough to tell two apart.
        private static float PickRow(int i, ADOFAI.LevelEvent e, float x, float y, float w)
        {
            string preview = Preview(e);
            var go = new GameObject("Pick", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f; bg.color = new Color(1f, 1f, 1f, 0.07f); bg.raycastTarget = true;

            var badge = RowText(go, i < 9 ? (i + 1).ToString() : "", 6f, 6f, 16f, 18f, 12f, Theme.Accent);
            badge.fontStyle = FontStyles.Bold;
            var sp = EditorEventSelector.TypeIcon((int)e.eventType);
            if (sp != null)
            {
                var iGo = new GameObject("I", typeof(RectTransform));
                iGo.transform.SetParent(go.transform, false);
                var ir = (RectTransform)iGo.transform;
                ir.anchorMin = ir.anchorMax = new Vector2(0f, 1f); ir.pivot = new Vector2(0f, 1f);
                ir.anchoredPosition = new Vector2(24f, -7f); ir.sizeDelta = new Vector2(16f, 16f);
                var img = iGo.AddComponent<Image>();
                img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false;
            }
            RowText(go, EditorEventPanel.EventTitle(e) + EditorEventPanel.TagSuffix(e), 44f, 6f, w - 50f, 18f, 12.5f, Theme.Text);
            var pv = RowText(go, preview, 24f, 26f, w - 30f, 0f, 11f, Theme.TextMuted);
            pv.textWrappingMode = TextWrappingModes.Normal;
            float ph = Mathf.Min(pv.GetPreferredValues(preview, w - 30f, 0f).y, 70f);
            ((RectTransform)pv.transform).sizeDelta = new Vector2(w - 30f, ph);
            float h = 30f + ph + 6f;
            r.sizeDelta = new Vector2(w, h);
            var ev = e;
            ClickHandler.Attach(go, () => Pick(ev));
            return y - h;
        }

        internal static string Preview(ADOFAI.LevelEvent e)
        {
            var info = EditorEventPanel.InfoOf(e);
            var data = EditorEvents.EventData(e);
            if (info == null || info.propertiesInfo == null || data == null) return "";
            var sb = new StringBuilder();
            foreach (var kv in info.propertiesInfo)
            {
                if (kv.Value == null || kv.Key == "floor") continue;
                try { if (kv.Value.invisible) continue; } catch { }
                object v;
                if (!data.TryGetValue(kv.Key, out v)) continue;
                bool off = false;
                try { off = e.disabled != null && e.disabled.TryGetValue(kv.Key, out var dv) && dv; } catch { }
                if (sb.Length > 0) sb.Append("  ·  ");
                sb.Append(kv.Key).Append(' ').Append(off ? "—" : Short(v));
            }
            return sb.ToString();
        }

        private static string Short(object v)
        {
            if (v == null) return "∅";
            if (v is bool) return (bool)v ? "on" : "off";
            if (v is float) return ((float)v).ToString("0.###", CultureInfo.InvariantCulture);
            if (v is double) return ((double)v).ToString("0.###", CultureInfo.InvariantCulture);
            if (v is Vector2) { var p = (Vector2)v; return "(" + Short(p.x) + ", " + Short(p.y) + ")"; }
            var s = v.ToString();
            if (v is string) s = s.Length == 0 ? "\"\"" : (s.Length > 18 ? s.Substring(0, 17) + "…" : s);
            return s.Replace("<", "‹");
        }

        private static TextMeshProUGUI RowText(GameObject row, string s, float x, float top, float w, float h, float size, Color col)
        {
            var go = new GameObject("T", typeof(RectTransform));
            go.transform.SetParent(row.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -top); r.sizeDelta = new Vector2(w, h);
            var t = UIBuilder.Tmp(go, s, size, TextAnchor.UpperLeft, col);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            return t;
        }

        private static void Text(string s, float x, float y, float w, float h, float size, Color col)
        {
            var go = new GameObject("T", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            UIBuilder.Tmp(go, s, size, TextAnchor.MiddleLeft, col).raycastTarget = false;
        }

        private static void FocusFirstField()
        {
            if (_content == null) return;
            var f = _content.GetComponentsInChildren<TMP_InputField>().FirstOrDefault(x => x.isActiveAndEnabled && x.interactable);
            if (f == null) return;
            try { UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(f.gameObject); f.ActivateInputField(); } catch { }
        }

        // Centred on screen each time it opens; a docked window keeps its dock slot.
        private static void PlaceCentered()
        {
            if (K.DockSide != 0) return;
            try
            {
                var cr = ((RectTransform)K.CanvasGo.transform).rect;
                var panel = (RectTransform)K.PanelGo.transform;
                // The panel is anchored at the canvas's top-left.
                panel.anchoredPosition = new Vector2((cr.width - panel.sizeDelta.x) * 0.5f, -(cr.height - panel.sizeDelta.y) * 0.5f);
            }
            catch { }
        }

        private static void TickScroll()
        {
            if (_viewport == null || _content == null) return;
            float wheel = MainClass.WheelY;
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_viewport, Input.mousePosition, null)) return;
            _scroll += wheel * 60f;
            ClampScroll();
        }

        private static void ClampScroll()
        {
            if (_viewport == null || _content == null) return;
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void Notify(scnEditor ed, string msg)
        {
            try { ed.ShowNotification(msg, null, 0f); } catch { }
        }
    }
}
