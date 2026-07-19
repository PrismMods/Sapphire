using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire.UI
{
    /* Shared scaffolding for Sapphire's floating tool panels (Magic Shape, Track Tools,
       Deco Tools…): own overlay canvas + draggable rounded panel with title/close, plus
       the flat cell/label/input primitives, so each module only writes its rows. One
       instance per module; Rebuild() keeps the panel's dragged position across rebuilds. */
    internal class PanelKit
    {
        internal const float RowH = 24f, Gap = 5f, Pad = 10f;

        private readonly string _canvasName;
        private readonly int _sortingOrder;
        internal readonly float W;
        internal GameObject CanvasGo, PanelGo;
        private Canvas _canvas;
        private GraphicRaycaster _raycaster;

        /* Set by owners that keep a widget (e.g. the selector's reopen chip) parented to the
           canvas while the panel itself is hidden — keeps the canvas rendering/raycasting for
           that widget even though PanelGo is inactive. */
        internal bool ChipAlive;

        internal PanelKit(string canvasName, int sortingOrder, float width)
        {
            _canvasName = canvasName;
            _sortingOrder = sortingOrder;
            W = width;
        }

        internal bool Built => CanvasGo != null && PanelGo != null;
        internal bool Visible => PanelGo != null && PanelGo.activeSelf;

        // fired when a header drag releases — hook SnapDockOnDragEnd for edge docking
        internal Action OnDragEnd;

        /* Adobe-style edge docking. DockSide is a persistent STATE (0 float · 1 left ·
           2 right): the owner calls TickDock each frame with the chrome insets (below the
           file row/toolbar, above the timeline) and the docked panel keeps following them
           — timeline grows, panel shrinks. Releasing a header drag near an edge docks;
           releasing anywhere else undocks. Width stays user-resizable while docked. */
        internal int DockSide;
        private DragHandle _drag;

        internal bool HeaderDragging => _drag != null && _drag.Dragging;

        internal void SnapDockOnDragEnd(float threshold = 28f)
        {
            if (PanelGo == null || CanvasGo == null) return;
            var r = (RectTransform)PanelGo.transform;
            var c = (RectTransform)CanvasGo.transform;
            float cw = c.rect.width;
            float w = r.sizeDelta.x;
            var p = r.anchoredPosition;
            if (p.x <= threshold) DockSide = 1;
            else if (p.x + w >= cw - threshold) DockSide = 2;
            else DockSide = 0;
        }

        internal void TickDock(float topMargin, float bottomInset, float margin = 8f)
        {
            if (DockSide == 0 || PanelGo == null || CanvasGo == null || HeaderDragging) return;
            var r = (RectTransform)PanelGo.transform;
            var c = (RectTransform)CanvasGo.transform;
            float cw = c.rect.width, ch = c.rect.height;
            float w = r.sizeDelta.x;
            float x = DockSide == 1 ? margin : cw - w - margin;
            float h = Mathf.Max(200f, ch - topMargin - bottomInset - margin);
            var want = new Vector2(x, -topMargin);
            // deadbands (2px pos, 3px height): a resize marks the whole panel + its RectMask2D
            // layout dirty, forcing Unity to reclip ALL masked content that frame. If the dock
            // target jitters sub-pixel (timeline height, canvas rect), that would rebuild the
            // panel every frame — invisible to the Tick profiler. Only move on a real change.
            if ((r.anchoredPosition - want).sqrMagnitude > 4f) r.anchoredPosition = want;
            if (Mathf.Abs(r.sizeDelta.y - h) > 3f) r.sizeDelta = new Vector2(w, h);
        }

        internal void Show(bool on)
        {
            if (PanelGo != null && PanelGo.activeSelf != on) PanelGo.SetActive(on);
            SyncCanvasActive();
        }

        /* Disable the Canvas + GraphicRaycaster while nothing on this canvas is visible: an
           idle-but-active overlay canvas is still a render batch and a raycaster the EventSystem
           walks every pointer frame. Disabling the components (vs SetActive) skips rendering and
           raycasting without tearing down the built hierarchy, so re-showing is churn-free. */
        internal void SyncCanvasActive()
        {
            if (CanvasGo == null) return;
            bool need = (PanelGo != null && PanelGo.activeSelf) || ChipAlive;
            if (_canvas != null && _canvas.enabled != need) _canvas.enabled = need;
            if (_raycaster != null && _raycaster.enabled != need) _raycaster.enabled = need;
        }

        internal void Dispose()
        {
            if (CanvasGo != null) UnityEngine.Object.Destroy(CanvasGo);
            CanvasGo = null; PanelGo = null;
        }

        internal void Rebuild(string title, Action onClose, Vector2 defaultPos)
        {
            EnsureCanvas();
            Vector2 keepPos = defaultPos;
            if (PanelGo != null)
            {
                keepPos = ((RectTransform)PanelGo.transform).anchoredPosition;
                UnityEngine.Object.Destroy(PanelGo);
            }

            PanelGo = new GameObject("Panel", typeof(RectTransform));
            PanelGo.transform.SetParent(CanvasGo.transform, false);
            var r = (RectTransform)PanelGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = keepPos;
            var bg = PanelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.94f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            var headGo = new GameObject("Head", typeof(RectTransform));
            headGo.transform.SetParent(PanelGo.transform, false);
            var hr = (RectTransform)headGo.transform;
            hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f);
            hr.pivot = new Vector2(0.5f, 1f);
            hr.anchoredPosition = Vector2.zero;
            hr.sizeDelta = new Vector2(0f, 28f);
            var headBg = headGo.AddComponent<RoundedRectGraphic>();
            headBg.Radius = 10f;
            headBg.color = new Color(1f, 1f, 1f, 0.04f);
            headBg.raycastTarget = true;
            _drag = headGo.AddComponent<DragHandle>();
            _drag.DragEnd = () => OnDragEnd?.Invoke();
            var titleGo = new GameObject("T", typeof(RectTransform));
            titleGo.transform.SetParent(headGo.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(Pad, 0f); tr.offsetMax = new Vector2(-30f, 0f);
            var titleTmp = UIBuilder.Tmp(titleGo, title, 13.5f, TextAnchor.MiddleLeft, Theme.Text);
            titleTmp.raycastTarget = false;

            // Close × anchored to the panel's TOP-RIGHT so it rides the right edge when the
            // panel is resized wider than the template width W (Cell anchors top-left, which
            // left the × stranded mid-header on resizable panels).
            var xGo = new GameObject("Close", typeof(RectTransform));
            xGo.transform.SetParent(PanelGo.transform, false);
            var xr = (RectTransform)xGo.transform;
            xr.anchorMin = xr.anchorMax = new Vector2(1f, 1f);
            xr.pivot = new Vector2(1f, 1f);
            xr.anchoredPosition = new Vector2(-6f, -4f);
            xr.sizeDelta = new Vector2(20f, 20f);
            var xbg = xGo.AddComponent<RoundedRectGraphic>();
            xbg.Radius = 5f;
            xbg.color = new Color(1f, 1f, 1f, 0.08f);
            xbg.BorderWidth = 1f;
            xbg.BorderColor = new Color(1f, 1f, 1f, 0.1f);
            xbg.raycastTarget = true;
            var xlGo = new GameObject("L", typeof(RectTransform));
            xlGo.transform.SetParent(xGo.transform, false);
            var xlr = (RectTransform)xlGo.transform;
            xlr.anchorMin = Vector2.zero; xlr.anchorMax = Vector2.one;
            xlr.offsetMin = xlr.offsetMax = Vector2.zero;
            var xtmp = UIBuilder.Tmp(xlGo, "×", 12f, TextAnchor.MiddleCenter, Theme.Text);
            xtmp.raycastTarget = false;
            ClickHandler.Attach(xGo, onClose);
        }

        internal void SetHeight(float yEnd)
        {
            if (PanelGo != null) ((RectTransform)PanelGo.transform).sizeDelta = new Vector2(W, -yEnd + Pad);
        }

        internal RoundedRectGraphic Cell(string text, float x, float y, float w, float h,
            Action onClick, bool button, bool accent = false, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Cell", typeof(RectTransform));
            go.transform.SetParent(PanelGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = accent
                ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f)
                : button ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.05f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.1f);
            bg.raycastTarget = true;
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(anchor == TextAnchor.MiddleLeft ? 8f : 0f, 0f);
            lr.offsetMax = Vector2.zero;
            var tmp = UIBuilder.Tmp(lblGo, text, 12f, anchor, Theme.Text);
            tmp.raycastTarget = false;
            ClickHandler.Attach(go, onClick);
            return bg;
        }

        internal TextMeshProUGUI Label(string text, float x, float y, float w, float h, Color color, float size = 12.5f)
        {
            var go = new GameObject("Lbl", typeof(RectTransform));
            go.transform.SetParent(PanelGo.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var tmp = UIBuilder.Tmp(go, text, size, TextAnchor.MiddleLeft, color);
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            return tmp;
        }

        internal void InputField(float x, float y, float w, string value, Action<string> commit)
        {
            var go = new GameObject("F", typeof(RectTransform));
            go.transform.SetParent(PanelGo.transform, false);
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
            var txt = UIBuilder.Tmp(txtGo, value, 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            txt.richText = false;
            var field = UIBuilder.BuildInputField(go, txt);
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = value;
            field.onEndEdit.AddListener(v => commit(v));
        }

        // muted multi-line status block; caller keeps the returned TMP to update it live
        internal TextMeshProUGUI Status(string text, float y)
        {
            var go = new GameObject("Status", typeof(RectTransform));
            go.transform.SetParent(PanelGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(Pad, y);
            r.sizeDelta = new Vector2(W - Pad * 2f, 30f);
            var tmp = UIBuilder.Tmp(go, text, 11.5f, TextAnchor.UpperLeft, Theme.TextMuted);
            tmp.enableWordWrapping = true;
            return tmp;
        }

        internal void Footer(string text, float y)
            => Label(text, Pad, y, W - Pad * 2f, 12f, new Color(0.42f, 0.42f, 0.47f, 1f), 9.5f);

        internal static Color Tint(bool on) => on
            ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
            : new Color(1f, 1f, 1f, 0.05f);

        // ── level helpers shared by the tool panels ──────────────────────────

        internal static int LevelLen()
        {
            try { return ADOBase.lm.floorAngles.Length; } catch { return 0; }
        }

        internal static int ResolveTile(int v) => v < 0 ? LevelLen() : v; // -1 = end of level

        internal static bool SelectionRange(out int min, out int max)
        {
            min = int.MaxValue; max = int.MinValue;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null || ed.selectedFloors == null) return false;
            foreach (var f in ed.selectedFloors)
            {
                if (f == null) continue;
                if (f.seqID < min) min = f.seqID;
                if (f.seqID > max) max = f.seqID;
            }
            return min != int.MaxValue;
        }

        // ── shared row builders (all return the next y) ──────────────────────

        internal float LblW = 104f;

        internal float FieldRow(float y, string label, string value, Action<string> commit)
        {
            Label(label, Pad, y, LblW, RowH, Theme.TextMuted);
            InputField(Pad + LblW + 4f, y, W - Pad * 2f - LblW - 4f, value, commit);
            return y - (RowH + Gap);
        }

        internal float FloatRow(float y, string label, float value, Action<float> set, string fmt = "0.###")
            => FieldRow(y, label, value.ToString(fmt), v => { float f; if (float.TryParse(v, out f)) set(f); });

        internal float IntRow(float y, string label, int value, Action<int> set)
            => FieldRow(y, label, value.ToString(), v => { int n; if (int.TryParse(v, out n)) set(n); });

        // [label] [a] [b] — two int fields
        internal float PairRow(float y, string label, Func<int> getA, Func<int> getB, Action<int, int> set)
        {
            Label(label, Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            const float fw = 56f;
            InputField(x, y, fw, getA().ToString(), v => { int n; if (int.TryParse(v, out n)) set(n, getB()); });
            InputField(x + fw + Gap, y, fw, getB().ToString(), v => { int n; if (int.TryParse(v, out n)) set(getA(), n); });
            return y - (RowH + Gap);
        }

        // [label] [a] [b] — two float fields, no toggle
        internal float FloatPairRow(float y, string label, Func<float> getA, Func<float> getB, Action<float, float> set)
        {
            Label(label, Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            float fw = (W - Pad * 2f - LblW - 4f - Gap) * 0.5f;
            InputField(x, y, fw, getA().ToString("0.###"), v => { float f; if (float.TryParse(v, out f)) set(f, getB()); });
            InputField(x + fw + Gap, y, fw, getB().ToString("0.###"), v => { float f; if (float.TryParse(v, out f)) set(getA(), f); });
            return y - (RowH + Gap);
        }

        // [on-toggle label] [min] [max] — a disableable random range / from-to pair
        internal float RandRow(float y, string label, Func<bool> getOn, Action<bool> setOn,
            Func<float> getA, Func<float> getB, Action<float, float> set)
        {
            RoundedRectGraphic bgRef = null;
            bgRef = Cell(label, Pad, y, LblW, RowH, () =>
            {
                setOn(!getOn());
                if (bgRef != null) bgRef.color = Tint(getOn());
            }, false, false, TextAnchor.MiddleLeft);
            bgRef.color = Tint(getOn());
            float x = Pad + LblW + 4f;
            float fw = (W - Pad * 2f - LblW - 4f - Gap) * 0.5f;
            InputField(x, y, fw, getA().ToString("0.###"), v => { float f; if (float.TryParse(v, out f)) set(f, getB()); });
            InputField(x + fw + Gap, y, fw, getB().ToString("0.###"), v => { float f; if (float.TryParse(v, out f)) set(getA(), f); });
            return y - (RowH + Gap);
        }

        // [on-toggle label] [value]
        internal float ToggleFieldRow(float y, string label, Func<bool> getOn, Action<bool> setOn,
            Func<float> get, Action<float> set)
        {
            RoundedRectGraphic bgRef = null;
            bgRef = Cell(label, Pad, y, LblW, RowH, () =>
            {
                setOn(!getOn());
                if (bgRef != null) bgRef.color = Tint(getOn());
            }, false, false, TextAnchor.MiddleLeft);
            bgRef.color = Tint(getOn());
            InputField(Pad + LblW + 4f, y, W - Pad * 2f - LblW - 4f, get().ToString("0.###"),
                v => { float f; if (float.TryParse(v, out f)) set(f); });
            return y - (RowH + Gap);
        }

        // full-width toggle cell
        internal float ToggleRow(float y, string label, bool value, Action<bool> set)
        {
            RoundedRectGraphic bgRef = null;
            bool cur = value;
            bgRef = Cell(label, Pad, y, W - Pad * 2f, RowH, () =>
            {
                cur = !cur;
                set(cur);
                if (bgRef != null) bgRef.color = Tint(cur);
            }, false, false, TextAnchor.MiddleLeft);
            bgRef.color = Tint(cur);
            return y - (RowH + Gap);
        }

        // [label] [opt0|opt1|…] — exclusive choice cells
        internal float SegRow(float y, string label, string[] options, Func<int> get, Action<int> set)
        {
            Label(label, Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            float w = (W - Pad * 2f - LblW - 4f - Gap * (options.Length - 1)) / options.Length;
            var cells = new RoundedRectGraphic[options.Length];
            for (int i = 0; i < options.Length; i++)
            {
                int idx = i;
                cells[i] = Cell(options[i], x, y, w, RowH, () =>
                {
                    set(idx);
                    for (int k = 0; k < cells.Length; k++) if (cells[k] != null) cells[k].color = Tint(get() == k);
                }, false);
                cells[i].color = Tint(get() == i);
                x += w + Gap;
            }
            return y - (RowH + Gap);
        }

        // [Tiles] [from] [to] [Sel] [All] — tile range with selection/whole-level fills
        internal float RangeRow(float y, Func<int> getA, Func<int> getB, Action<int, int> set,
            Action refresh, Action<string> status)
        {
            Label(Loc.T("Tiles"), Pad, y, 40f, RowH, Theme.TextMuted);
            float x = Pad + 44f;
            const float fw = 56f;
            InputField(x, y, fw, Mathf.Clamp(ResolveTile(getA()), 0, LevelLen()).ToString(), v =>
            { int n; if (int.TryParse(v, out n)) set(Math.Max(0, n), getB()); });
            x += fw + Gap;
            InputField(x, y, fw, Mathf.Clamp(ResolveTile(getB()), 0, LevelLen()).ToString(), v =>
            { int n; if (int.TryParse(v, out n)) set(getA(), Math.Max(0, n)); });
            x += fw + Gap;
            Cell(Loc.T("Sel"), x, y, 42f, RowH, () =>
            {
                if (SelectionRange(out int min, out int max)) { set(min, max); refresh(); }
                else status(Loc.T("Nothing selected"));
            }, true);
            x += 42f + Gap;
            Cell(Loc.T("All"), x, y, 42f, RowH, () => { set(0, -1); refresh(); }, true);
            return y - (RowH + Gap);
        }

        // [Ease] [name] — opens the shared curve-grid picker
        internal float EaseRow(float y, Func<DG.Tweening.Ease> get, Action<DG.Tweening.Ease> set)
        {
            Label(Loc.T("Ease"), Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            float w = W - Pad * 2f - LblW - 4f;
            RoundedRectGraphic nameBg = null;
            TextMeshProUGUI nameTmp = null;
            nameBg = Cell(get().ToString(), x, y, w, RowH, () =>
            {
                Vector2 pos = nameBg != null ? (Vector2)nameBg.transform.position : (Vector2)Input.mousePosition;
                EditorEasePicker.Open(Loc.T("Ease"), get(), pos, e =>
                {
                    set(e);
                    if (nameTmp != null) nameTmp.text = e.ToString();
                });
            }, true);
            nameTmp = nameBg.GetComponentInChildren<TextMeshProUGUI>();
            return y - (RowH + Gap);
        }

        private void EnsureCanvas()
        {
            if (CanvasGo != null) return;
            CanvasGo = new GameObject(_canvasName, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(CanvasGo);
            _canvas = CanvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = _sortingOrder;
            var scaler = CanvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _raycaster = CanvasGo.AddComponent<GraphicRaycaster>();
        }
    }
}
