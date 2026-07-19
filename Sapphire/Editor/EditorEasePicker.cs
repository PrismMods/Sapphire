using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Visual ease picker: right-click a keyframe in the timeline's CAM mode and pick an
       ease from a grid of DRAWN curve previews instead of the game's bare dropdown. Each
       cell plots DOTween's own EaseManager.Evaluate for that ease (the exact runtime
       curve, overshoots included). Click = apply to the event's "ease" field inside a
       SaveStateScope (undo works) and refresh the open inspector panel. */
    internal static class EditorEasePicker
    {
        private static GameObject _canvasGo;
        private static RectTransform _canvasRect;
        private static GameObject _popupGo;
        private static ADOFAI.LevelEvent _target;
        private static readonly List<Texture2D> _curveTex = new List<Texture2D>();

        private const int Cols = 6;
        private const float CellW = 82f, CellH = 66f, Pad = 10f, Gap = 5f;

        internal static bool IsOpen => _popupGo != null;

        internal static void Tick()
        {
            if (_popupGo == null) return;
            bool want = false;
            try
            {
                var ed = scnEditor.instance;
                want = ed != null && !ed.playMode && MainClass.EditorSuiteOn;
            }
            catch { }
            if (!want || Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        internal static void Dispose()
        {
            Close();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _canvasRect = null;
        }

        // Everything DOTween can run, minus internals; Unset renders as Linear in-game.
        private static List<DG.Tweening.Ease> EaseList()
        {
            var list = new List<DG.Tweening.Ease>();
            foreach (DG.Tweening.Ease e in Enum.GetValues(typeof(DG.Tweening.Ease)))
            {
                var n = e.ToString();
                if (n == "Unset" || n.StartsWith("INTERNAL_")) continue;
                if (!list.Contains(e)) list.Add(e);
            }
            return list;
        }

        internal static void Open(ADOFAI.LevelEvent evt, Vector2 screenPos)
        {
            if (evt == null) return;
            DG.Tweening.Ease current = DG.Tweening.Ease.Linear;
            try { if (evt.ContainsKey("ease") && evt["ease"] is DG.Tweening.Ease cur) current = cur; }
            catch { }
            OpenCore(Loc.T("Ease") + " · " + evt.eventType, current, screenPos, ApplyToTarget, true);
            _target = evt;
        }

        // Generic entry: any module can use the curve grid; onPick receives the choice.
        internal static void Open(string title, DG.Tweening.Ease current, Vector2 screenPos, Action<DG.Tweening.Ease> onPick)
            => OpenCore(title, current, screenPos, onPick, false);

        private static Action<DG.Tweening.Ease> _onPick;

        private static void OpenCore(string titleText, DG.Tweening.Ease current, Vector2 screenPos,
            Action<DG.Tweening.Ease> onPick, bool showCustom)
        {
            Close();
            _target = null; // event path re-sets it after OpenCore; generic path must not inherit one
            _onPick = onPick;
            if (_canvasGo == null) BuildCanvas();
            else if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

            var eases = EaseList();
            int cellCount = eases.Count + (showCustom ? 1 : 0); // +1: the Custom bezier cell
            int rows = (cellCount + Cols - 1) / Cols;
            float w = Pad * 2f + Cols * CellW + (Cols - 1) * Gap;
            float h = Pad * 2f + 22f + rows * (CellH + Gap);

            _popupGo = new GameObject("EasePopup", typeof(RectTransform));
            _popupGo.transform.SetParent(_canvasGo.transform, false);
            var blocker = (RectTransform)_popupGo.transform;
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = Vector2.zero; blocker.offsetMax = Vector2.zero;
            var blockImg = _popupGo.AddComponent<Image>();
            blockImg.color = new Color(0f, 0f, 0f, 0.01f);
            blockImg.raycastTarget = true;
            UI.ClickHandler.Attach(_popupGo, Close);

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(_popupGo.transform, false);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 0f);
            panel.pivot = new Vector2(0.5f, 0f);
            panel.sizeDelta = new Vector2(w, h);
            var bg = panelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.97f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;

            // Title: ease + which event it applies to.
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(panelGo.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.sizeDelta = new Vector2(0f, 20f);
            tr.anchoredPosition = new Vector2(0f, -Pad * 0.7f);
            var title = UIBuilder.Tmp(titleGo, titleText, 13f,
                TextAnchor.MiddleCenter, Theme.TextMuted);
            title.raycastTarget = false;

            for (int i = 0; i < eases.Count; i++)
            {
                int col = i % Cols, row = i / Cols;
                float x = Pad + col * (CellW + Gap);
                float y = -(Pad + 22f + row * (CellH + Gap));
                MakeCell(panelGo.transform, eases[i], eases[i] == current, x, y);
            }
            if (showCustom)
            {
                // Custom bezier: decomposes the tween into linear segments (own editor)
                int i = eases.Count;
                int col = i % Cols, row = i / Cols;
                MakeCustomCell(panelGo.transform, Pad + col * (CellW + Gap), -(Pad + 22f + row * (CellH + Gap)));
            }

            // At the cursor, clamped on-screen; opens upward (the timeline is at the bottom).
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPos, null, out local))
            {
                var half = _canvasRect.rect.size * 0.5f;
                local.x = Mathf.Clamp(local.x, -half.x + w * 0.5f + 4f, half.x - w * 0.5f - 4f);
                local.y = Mathf.Clamp(local.y + 12f, -half.y + 4f, half.y - h - 4f);
                panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
                panel.anchoredPosition = local;
            }
        }

        private static void MakeCell(Transform parent, DG.Tweening.Ease ease, bool current, float x, float y)
        {
            var cellGo = new GameObject("E_" + ease, typeof(RectTransform));
            cellGo.transform.SetParent(parent, false);
            var r = (RectTransform)cellGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(CellW, CellH);
            var bg = cellGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = current
                ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f)
                : new Color(1f, 1f, 1f, 0.05f);
            bg.raycastTarget = true;

            var curveGo = new GameObject("Curve", typeof(RectTransform));
            curveGo.transform.SetParent(cellGo.transform, false);
            var cr = (RectTransform)curveGo.transform;
            cr.anchorMin = new Vector2(0.5f, 1f); cr.anchorMax = new Vector2(0.5f, 1f);
            cr.pivot = new Vector2(0.5f, 1f);
            cr.anchoredPosition = new Vector2(0f, -3f);
            cr.sizeDelta = new Vector2(CellW - 12f, CellH - 20f);
            var img = curveGo.AddComponent<RawImage>();
            img.texture = CurveTexture(ease);
            img.raycastTarget = false;

            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(cellGo.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = new Vector2(0f, 0f); lr.anchorMax = new Vector2(1f, 0f);
            lr.pivot = new Vector2(0.5f, 0f);
            lr.sizeDelta = new Vector2(0f, 14f);
            lr.anchoredPosition = new Vector2(0f, 2f);
            var lbl = UIBuilder.Tmp(lblGo, ease.ToString(), 9.5f, TextAnchor.MiddleCenter,
                current ? Theme.Text : Theme.TextMuted);
            lbl.textWrappingMode = TextWrappingModes.NoWrap;
            lbl.overflowMode = TextOverflowModes.Ellipsis;
            lbl.raycastTarget = false;

            UI.ClickHandler.Attach(cellGo, () => Pick(ease));
        }

        // "Custom" opens the Sapphire bezier editor for this event.
        private static void MakeCustomCell(Transform parent, float x, float y)
        {
            var cellGo = new GameObject("E_Custom", typeof(RectTransform));
            cellGo.transform.SetParent(parent, false);
            var r = (RectTransform)cellGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(CellW, CellH);
            var bg = cellGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(1f, 1f, 1f, 0.05f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.25f); // outlined = "opens an editor", not selected
            bg.raycastTarget = true;
            var lblGo = new GameObject("L", typeof(RectTransform));
            lblGo.transform.SetParent(cellGo.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            var lbl = UIBuilder.Tmp(lblGo, Loc.T("Custom"), 12f, TextAnchor.MiddleCenter, Theme.Text);
            lbl.raycastTarget = false;
            UI.ClickHandler.Attach(cellGo, () =>
            {
                var evt = _target;
                Close();
                EditorBezier.Open(evt);
            });
        }

        /* Plot the REAL runtime curve: EaseManager.Evaluate over t∈[0,1], y mapped from
           [-0.35, 1.35] so Back/Elastic overshoots stay inside the cell. Faint baselines
           at 0 and 1 anchor the eye. */
        private static Texture2D CurveTexture(DG.Tweening.Ease ease)
        {
            const int W = 70, H = 46;
            var tex = new Texture2D(W, H, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            var px = new Color32[W * H];
            var baseCol = new Color32(255, 255, 255, 28);
            int yZero = MapY(0f, H), yOne = MapY(1f, H);
            for (int gx = 0; gx < W; gx++) { px[yZero * W + gx] = baseCol; px[yOne * W + gx] = baseCol; }
            var col = new Color32(255, 255, 255, 235);
            int prevY = -1;
            for (int gx = 0; gx < W; gx++)
            {
                float t = gx / (float)(W - 1);
                float v;
                try { v = DG.Tweening.Core.Easing.EaseManager.Evaluate(ease, null, t, 1f, 1.70158f, 0f); }
                catch { v = t; }
                int gy = MapY(v, H);
                // connect vertically to the previous column so steep curves stay solid
                if (prevY >= 0)
                {
                    int lo = Mathf.Min(prevY, gy), hi = Mathf.Max(prevY, gy);
                    for (int yy = lo; yy <= hi; yy++) px[yy * W + gx] = col;
                }
                else px[gy * W + gx] = col;
                prevY = gy;
            }
            tex.SetPixels32(px);
            tex.Apply(false);
            _curveTex.Add(tex);
            return tex;
        }

        private static int MapY(float v, int h) =>
            Mathf.Clamp(Mathf.RoundToInt((v + 0.35f) / 1.7f * (h - 1)), 0, h - 1);

        private static void Pick(DG.Tweening.Ease ease)
        {
            var cb = _onPick;
            Close();
            try { cb?.Invoke(ease); }
            catch (Exception ex) { SapphireLog.Log("EasePicker: apply failed: " + ex.Message); }
        }

        // Event-bound pick path: write evt["ease"] as one undo + refresh the inspector.
        private static void ApplyToTarget(DG.Tweening.Ease ease)
        {
            var evt = _target;
            _target = null;
            var ed = scnEditor.instance;
            if (ed == null || evt == null) return;
            using (new SaveStateScope(ed))
                evt["ease"] = ease;
            // Refresh the inspector if this event is on screen (same instance-index
            // dance as the timeline's marker click).
            int idx = 0;
            foreach (var e in ed.events)
            {
                if (e == null || e.floor != evt.floor || e.eventType != evt.eventType) continue;
                if (ReferenceEquals(e, evt)) break;
                idx++;
            }
            ed.levelEventsPanel.ShowPanel(evt.eventType, idx);
        }

        private static void Close()
        {
            if (_popupGo != null) UnityEngine.Object.Destroy(_popupGo);
            _popupGo = null; _onPick = null;
            foreach (var t in _curveTex) if (t != null) UnityEngine.Object.Destroy(t);
            _curveTex.Clear();
            if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
        }

        private static void BuildCanvas()
        {
            _canvasGo = new GameObject("SapphireEasePicker", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 946; // above timeline/dock, below help
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRect = (RectTransform)_canvasGo.transform;
        }
    }
}
