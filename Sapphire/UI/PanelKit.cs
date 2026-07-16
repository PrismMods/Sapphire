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

        internal PanelKit(string canvasName, int sortingOrder, float width)
        {
            _canvasName = canvasName;
            _sortingOrder = sortingOrder;
            W = width;
        }

        internal bool Built => CanvasGo != null && PanelGo != null;
        internal bool Visible => PanelGo != null && PanelGo.activeSelf;

        internal void Show(bool on)
        {
            if (PanelGo != null && PanelGo.activeSelf != on) PanelGo.SetActive(on);
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
            headGo.AddComponent<DragHandle>();
            var titleGo = new GameObject("T", typeof(RectTransform));
            titleGo.transform.SetParent(headGo.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(Pad, 0f); tr.offsetMax = new Vector2(-30f, 0f);
            var titleTmp = UIBuilder.Tmp(titleGo, title, 13.5f, TextAnchor.MiddleLeft, Theme.Text);
            titleTmp.raycastTarget = false;
            Cell("×", W - 26f, -4f, 20f, 20f, onClose, true);
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
            var canvas = CanvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = _sortingOrder;
            var scaler = CanvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            CanvasGo.AddComponent<GraphicRaycaster>();
        }
    }
}
