using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* A clipboard you can see. Every drop from the event panel becomes a FOLDER holding
       everything that drag carried — any mix of types — and dropping onto an existing folder adds
       to it. A folder opens to list its events; an event opens to its property rows, edited in
       place on the tray's own copy. Drag a folder (or one event in it) onto a tile to add, Shift
       to replace that tile's events. Items are detached copies, so the source is never touched;
       they last for the session. */
    internal static class EditorEventTray
    {
        private static readonly PanelKit K = new PanelKit("SapphireTray", 907, PanelW, focusable: true);
        private const float PanelW = 300f, HeaderH = 28f, RowH = 26f, Pad = 8f, Gap = 4f, DelW = 22f, Indent = 16f;
        private static Vector2 _size = new Vector2(PanelW, 320f);
        private static RectTransform _viewport, _content;
        private static Image _viewBg;
        private static float _scroll;
        private static bool _open, _dirty = true;

        internal class Folder
        {
            public List<ADOFAI.LevelEvent> Events = new List<ADOFAI.LevelEvent>();
            public bool Open;
            public readonly HashSet<ADOFAI.LevelEvent> Editing = new HashSet<ADOFAI.LevelEvent>();
        }
        private static readonly List<Folder> _folders = new List<Folder>();

        // Each folder's vertical extent in content space, for dropping ONTO a folder.
        private struct Span { public Folder F; public float Top, Bottom; public RoundedRectGraphic Head; public Color Base; }
        private static readonly List<Span> _spans = new List<Span>();

        private static readonly EventRows.Ctx _ctx = new EventRows.Ctx
        {
            Scratch = true,                 // tray copies aren't in the chart: no undo step per edit
            MarkDirty = () => _dirty = true,
        };

        internal static PanelKit Kit => K;
        internal static bool IsOpen => _open;
        internal static void SetOpen(bool v) { _open = v; }
        internal static void Toggle() { _open = !_open; }

        internal static bool TabAvailable()
        {
            if (MainClass.Settings == null || !MainClass.EditorSuiteOn) return false;
            try { var ed = scnEditor.instance; return ed != null && !ed.playMode; } catch { return false; }
        }

        internal static bool PointerOver() =>
            K.Visible && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)K.PanelGo.transform, Input.mousePosition, null);

        internal static Folder FolderUnderPointer()
        {
            if (!PointerOver() || _content == null) return null;
            Vector2 lp;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_content, Input.mousePosition, null, out lp)) return null;
            foreach (var sp in _spans) if (lp.y <= sp.Top && lp.y >= sp.Bottom) return sp.F;
            return null;
        }

        // Copies of `events` into `into` (an existing folder) or a new one.
        internal static void Add(List<ADOFAI.LevelEvent> events, Folder into)
        {
            if (events == null || events.Count == 0) return;
            var copies = new List<ADOFAI.LevelEvent>(events.Count);
            foreach (var e in events) { try { if (e != null) copies.Add(e.Copy()); } catch { } }
            if (copies.Count == 0) return;
            if (into != null && _folders.Contains(into)) into.Events.AddRange(copies);
            else _folders.Add(new Folder { Events = copies });
            _dirty = true;
        }

        internal static string LabelOf(Folder f)
        {
            if (f.Events.Count == 0) return "";
            var t = f.Events[0].eventType;
            bool same = true;
            foreach (var e in f.Events) if (e.eventType != t) { same = false; break; }
            if (same) return EditorEventSelector.TypeName((int)t) + (f.Events.Count > 1 ? "  ×" + f.Events.Count : "");
            return f.Events.Count + " " + Loc.T("events");
        }

        internal static void Tick()
        {
            EventDrag.Tick();   // before the gate: a drag from the event panel can end with the tray closed
            if (!_open || !TabAvailable()) { K.Show(false); return; }
            if (!K.Built) { BuildShell(); _dirty = true; }
            // Not while a field has the keyboard: the rebuild would destroy the field being typed in.
            if (_dirty && !FieldNav.Typing) { _dirty = false; Rebuild(); }
            K.Show(true);
            TickResize();
            TickScroll();
            PaintDropTarget();
        }

        // While a drag hovers the tray: the folder under the pointer lights up, else the whole
        // view (a new folder).
        private static void PaintDropTarget()
        {
            Folder hot = null;
            bool over = EventDrag.Active && PointerOver();
            if (over) hot = FolderUnderPointer();
            if (hot != null && EventDrag.SourceFolder == hot) { hot = null; over = false; }
            var ac = Theme.Accent;
            var view = over && hot == null ? new Color(ac.r, ac.g, ac.b, 0.16f) : new Color(0f, 0f, 0f, 0.01f);
            if (_viewBg != null && _viewBg.color != view) _viewBg.color = view;
            foreach (var sp in _spans)
            {
                if (sp.Head == null) continue;
                var c = sp.F == hot ? new Color(ac.r, ac.g, ac.b, 0.42f) : sp.Base;
                if (sp.Head.color != c) sp.Head.color = c;
            }
        }

        internal static void Dispose()
        {
            EventDrag.Cancel();
            K.Dispose();
            _viewport = null; _content = null; _viewBg = null;
            _spans.Clear();
            _dirty = true;
        }

        // ── UI ────────────────────────────────────────────────────────────────

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Tray"), () => _open = false, new Vector2(24f, -470f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            K.OnDragEnd = () => K.SnapDockOnDragEnd();
            ResizeHandle.AttachAll(panel, true, 220f, 140f);

            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = Vector2.zero; _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = new Vector2(0f, 3f); _viewport.offsetMax = new Vector2(0f, -HeaderH);
            vpGo.AddComponent<RectMask2D>();
            _viewBg = vpGo.AddComponent<Image>();
            _viewBg.color = new Color(0f, 0f, 0f, 0.01f);
            _viewBg.raycastTarget = true;   // wheel + drop target

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void Rebuild()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
            _spans.Clear();
            float w = _size.x - Pad * 2f, y = -Pad;
            if (_folders.Count == 0)
            {
                var ht = Text(Loc.T("Drag events here from the event panel — select several to take them all as one folder"),
                    Pad, y, w, RowH * 2f, 12f, Theme.TextMuted);
                ht.textWrappingMode = TextWrappingModes.Normal;
                y -= RowH * 2f;
            }
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            foreach (var f in _folders.ToArray())
            {
                float top = y + Gap * 0.5f;
                var head = FolderRow(f, Pad, y, w);
                y -= RowH + Gap;
                if (f.Open)
                    foreach (var e in f.Events.ToArray())
                    {
                        EventRow(f, e, Pad + Indent, y, w - Indent);
                        y -= RowH + Gap;
                        if (!f.Editing.Contains(e)) continue;
                        var info = EditorEventPanel.InfoOf(e);
                        if (info == null) continue;
                        _ctx.Content = _content;
                        _ctx.PanelW = _size.x;
                        y = EventRows.Render(_ctx, ed, info, e, y) - 4f;
                    }
                _spans.Add(new Span { F = f, Top = top, Bottom = y + Gap * 0.5f, Head = head, Base = head.color });
            }
            _content.sizeDelta = new Vector2(0f, -y + Pad);
            ClampScroll();
        }

        private static RoundedRectGraphic FolderRow(Folder f, float x, float y, float w)
        {
            var bg = Row(x, y, w, new Color(1f, 1f, 1f, f.Open ? 0.12f : 0.08f), out var rowGo);
            Text(f.Open ? "▾" : "▸", x + 4f, y, 14f, RowH, 13f, Theme.Text, rowGo).alignment = TextAlignmentOptions.Center;
            // Up to four of the folder's distinct type icons: a mixed folder reads at a glance.
            float ix = 20f;
            var seen = new HashSet<int>();
            foreach (var e in f.Events)
            {
                int t = (int)e.eventType;
                if (!seen.Add(t)) continue;
                if (seen.Count > 4) break;
                Icon(rowGo, EditorEventSelector.TypeIcon(t), ix);
                ix += 17f;
            }
            var lbl = Text(LabelOf(f), ix + 4f, 0f, w - ix - DelW - 10f, RowH, 12f, Theme.Text, rowGo);
            lbl.overflowMode = TextOverflowModes.Ellipsis;
            ClickHandler.Attach(rowGo, () => { f.Open = !f.Open; _dirty = true; });
            var src = rowGo.AddComponent<EventDragSource>();
            src.FromTray = true; src.Folder = f; src.Events = () => f.Events;
            HoverTip.Attach(rowGo, Loc.T("Click to open · drag onto a tile · Shift replaces its events"));
            DelButton(rowGo, () => { _folders.Remove(f); _dirty = true; });
            return bg;
        }

        private static void EventRow(Folder f, ADOFAI.LevelEvent e, float x, float y, float w)
        {
            bool editing = f.Editing.Contains(e);
            Row(x, y, w, new Color(1f, 1f, 1f, editing ? 0.10f : 0.05f), out var rowGo);
            Icon(rowGo, EditorEventSelector.TypeIcon((int)e.eventType), 6f);
            var lbl = Text(EditorEventPanel.EventTitle(e) + EditorEventPanel.TagSuffix(e), 28f, 0f, w - 28f - DelW - 8f, RowH, 12f,
                editing ? Theme.Text : new Color(Theme.Text.r, Theme.Text.g, Theme.Text.b, 0.8f), rowGo);
            lbl.overflowMode = TextOverflowModes.Ellipsis;
            ClickHandler.Attach(rowGo, () => { if (!f.Editing.Remove(e)) f.Editing.Add(e); _dirty = true; });
            var src = rowGo.AddComponent<EventDragSource>();
            src.FromTray = true; src.Folder = f; src.Events = () => new List<ADOFAI.LevelEvent> { e };
            HoverTip.Attach(rowGo, Loc.T("Click to edit · drag onto a tile"));
            DelButton(rowGo, () =>
            {
                f.Events.Remove(e); f.Editing.Remove(e);
                if (f.Events.Count == 0) _folders.Remove(f);
                _dirty = true;
            });
        }

        // ── small builders ──────────────────────────────────────────────────────

        private static RoundedRectGraphic Row(float x, float y, float w, Color col, out GameObject go)
        {
            go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, RowH);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f; bg.color = col; bg.raycastTarget = true;
            return bg;
        }

        private static void Icon(GameObject row, Sprite sp, float x)
        {
            if (sp == null) return;
            var go = new GameObject("I", typeof(RectTransform));
            go.transform.SetParent(row.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f); r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f); r.sizeDelta = new Vector2(16f, 16f);
            var img = go.AddComponent<Image>();
            img.sprite = sp; img.preserveAspect = true; img.raycastTarget = false;
        }

        // Row-local when `parent` is given (x from the row's left, vertically centred), else a
        // content-space label at (x, y).
        private static TextMeshProUGUI Text(string s, float x, float y, float w, float h, float size, Color col, GameObject parent = null)
        {
            var go = new GameObject("T", typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent.transform : _content, false);
            var r = (RectTransform)go.transform;
            if (parent != null)
            {
                r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 0.5f);
                r.anchoredPosition = new Vector2(x, 0f); r.sizeDelta = new Vector2(w, 0f);
            }
            else
            {
                r.anchorMin = r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 1f);
                r.anchoredPosition = new Vector2(x, y); r.sizeDelta = new Vector2(w, h);
            }
            var t = UIBuilder.Tmp(go, s, size, TextAnchor.MiddleLeft, col);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.raycastTarget = false;
            return t;
        }

        private static void DelButton(GameObject row, Action onClick)
        {
            var go = new GameObject("Del", typeof(RectTransform));
            go.transform.SetParent(row.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f); r.pivot = new Vector2(1f, 0.5f);
            r.anchoredPosition = new Vector2(-2f, 0f); r.sizeDelta = new Vector2(DelW, DelW);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 4f; bg.color = new Color(1f, 1f, 1f, 0.05f); bg.raycastTarget = true;
            Text("×", 0f, 0f, DelW, DelW, 13f, Theme.TextMuted, go).alignment = TextAlignmentOptions.Center;
            ClickHandler.Attach(go, onClick);
            HoverTip.Attach(go, Loc.T("Remove"));
        }

        private static void TickResize()
        {
            var r = (RectTransform)K.PanelGo.transform;
            if ((r.sizeDelta - _size).sqrMagnitude <= 1f) return;
            _size = r.sizeDelta;
            _dirty = true;
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
    }

    /* One event drag at a time, from either end. Rows start it; the tick carries it — a source row
       can be rebuilt out from under the pointer mid-drag (the event panel rescans), and a drag
       that waited for its source's OnEndDrag would then never finish. */
    internal static class EventDrag
    {
        private enum Target { None, Tray, Tile }

        private static List<ADOFAI.LevelEvent> _events;
        private static string _base;
        private static EditorEventTray.Folder _dropFolder;
        private static GameObject _canvasGo, _cardGo;
        private static RectTransform _canvasRect, _card;
        private static TextMeshProUGUI _label;

        internal static bool Active => _events != null;
        internal static bool FromTray { get; private set; }
        internal static EditorEventTray.Folder SourceFolder { get; private set; }

        internal static void Begin(List<ADOFAI.LevelEvent> events, bool fromTray, EditorEventTray.Folder source = null)
        {
            if (events == null || events.Count == 0) return;
            _events = new List<ADOFAI.LevelEvent>(events);
            FromTray = fromTray;
            SourceFolder = source;
            var probe = new EditorEventTray.Folder { Events = _events };
            _base = EditorEventTray.LabelOf(probe);
            Ensure();
            _cardGo.SetActive(true);
            Move();
        }

        internal static void Cancel()
        {
            _events = null;
            SourceFolder = null;
            if (_cardGo != null) _cardGo.SetActive(false);
        }

        internal static void Tick()
        {
            if (!Active) return;
            if (Input.GetKeyDown(KeyCode.Escape)) { Cancel(); return; }
            if (!Input.GetMouseButton(0)) { Drop(); return; }
            Move();
        }

        private static bool Shift => Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

        private static Target Resolve(out int seq)
        {
            seq = -1;
            _dropFolder = null;
            if (EditorEventTray.PointerOver())
            {
                var folder = EditorEventTray.FolderUnderPointer();
                if (FromTray && folder == SourceFolder) return Target.None;   // back where it came from
                _dropFolder = folder;
                return Target.Tray;
            }
            if (EditorEventPanel.PointerOver())
            {
                // Onto the event panel means "its tile" — only for tray items; a row dropped on its
                // own panel is no move at all.
                if (FromTray && EditorEventPanel.CurrentFloor >= 0) { seq = EditorEventPanel.CurrentFloor; return Target.Tile; }
                return Target.None;
            }
            if (EditorToolbar.PointerOverSapphireUI()) return Target.None;
            scrFloor f = null;
            try { f = EditorToolbar.FloorUnderCursor(scnEditor.instance); } catch { }
            if (f == null) return Target.None;
            seq = f.seqID;
            return Target.Tile;
        }

        private static void Drop()
        {
            var evs = _events;
            int seq;
            var t = Resolve(out seq);
            var into = _dropFolder;
            Cancel();
            if (t == Target.Tray) EditorEventTray.Add(evs, into);
            else if (t == Target.Tile) EditorToolbar.PasteEventsOnto(scnEditor.instance, evs, new[] { seq }, Shift);
        }

        private static void Move()
        {
            int seq;
            var t = Resolve(out seq);
            string where = t == Target.Tray
                             ? "  →  " + (_dropFolder != null ? EditorEventTray.LabelOf(_dropFolder) : Loc.T("New folder"))
                         : t == Target.Tile ? "  →  #" + seq + (Shift ? "  (" + Loc.T("Replace") + ")" : "")
                         : "";
            string text = _base + where;
            if (_label.text != text) _label.text = text;
            Vector2 pref = _label.GetPreferredValues(text, 400f, 0f);
            var size = new Vector2(pref.x + 16f, 24f);
            if (_card.sizeDelta != size) _card.sizeDelta = size;
            Vector2 lp;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, Input.mousePosition, null, out lp);
            _card.anchoredPosition = lp + new Vector2(14f, -12f);
        }

        private static void Ensure()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SapphireEventDrag", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 968;   // over every window; nothing here raycasts
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasRect = (RectTransform)_canvasGo.transform;

            _cardGo = new GameObject("Card", typeof(RectTransform));
            _cardGo.transform.SetParent(_canvasGo.transform, false);
            _card = (RectTransform)_cardGo.transform;
            _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 0.5f);
            _card.pivot = new Vector2(0f, 1f);
            var bg = _cardGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(Theme.Accent.r * 0.35f, Theme.Accent.g * 0.35f, Theme.Accent.b * 0.35f, 0.95f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.8f);
            bg.raycastTarget = false;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(_cardGo.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(8f, 0f); lr.offsetMax = new Vector2(-8f, 0f);
            _label = UIBuilder.Tmp(lGo, "", 12f, TextAnchor.MiddleLeft, Theme.Text);
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.raycastTarget = false;
            _cardGo.SetActive(false);
        }
    }

    // Starts an EventDrag from whatever row it sits on. Drop handling lives in EventDrag.Tick.
    internal class EventDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Func<List<ADOFAI.LevelEvent>> Events;
        public bool FromTray;
        public EditorEventTray.Folder Folder;   // the tray folder it came from

        public void OnBeginDrag(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            EventDrag.Begin(Events != null ? Events() : null, FromTray, Folder);
        }

        // Unity only starts a drag on an object with an IDragHandler; the tick does the moving.
        public void OnDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }
    }
}
