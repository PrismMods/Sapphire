using System;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire.UI
{
    // Full-screen drag overlay for the GAME EDITOR's own UI chrome (file bar, panel
    // tabs…) — GameUiEditor's twin over EditorUiLayout targets. Drag moves, scroll
    // scales, right-click resets one element; writes apply live through the wrappers.
    // No force-show pass: editor chrome is always on screen.
    internal static class EditorUiEditor
    {
        public static bool IsActive => _canvasGo != null;

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static bool _reopenPanel;

        public static void Open()
        {
            if (IsActive) return;

            _reopenPanel = UICore.IsOpen;
            if (_reopenPanel) UICore.Close();
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;

            _canvasGo = new GameObject("SapphireEditorUiEditor");
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 31000;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            var dim = UIBuilder.Rect("Dim", _canvasGo.transform);
            var dimRect = (RectTransform)dim.transform;
            dimRect.anchorMin = Vector2.zero;
            dimRect.anchorMax = Vector2.one;
            dimRect.offsetMin = Vector2.zero;
            dimRect.offsetMax = Vector2.zero;
            var dimImg = UIBuilder.SolidImage(dim, new Color(0f, 0f, 0f, 0.35f));
            dimImg.raycastTarget = false;

            foreach (var t in EditorUiLayout.Targets)
                MakeElementHandle(t);
            MakeDoneButton();
            EditorUndo.Reset();
            _canvasGo.AddComponent<UndoPoller>();
        }

        public static void Close()
        {
            if (!IsActive) return;
            UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _canvas = null;
            UICore.OnSettingsChanged?.Invoke();
            if (_reopenPanel && UICore.CanvasRoot != null) UICore.Open();
            _reopenPanel = false;
        }

        private static void MakeElementHandle(EditorUiLayout.TargetDef t)
        {
            Vector2 startOff = Vector2.zero;
            float gameScale = 1f;

            var h = MakeHandle(t.Label, t.Get);
            h.ShowInactive = true;
            h.BeginDragCapture = () =>
            {
                var o = EditorUiLayout.GetOverride(t.Key, create: true);
                startOff = new Vector2(o.OffX, o.OffY);
                gameScale = CanvasScale(t.Get?.Invoke());
            };
            h.DragBy = d =>
            {
                var o = EditorUiLayout.GetOverride(t.Key, create: true);
                o.OffX = startOff.x + d.x / gameScale;
                o.OffY = startOff.y + d.y / gameScale;
                EditorUiLayout.ApplyOne(t.Key);
            };
            h.GetScale = () =>
            {
                var o = EditorUiLayout.GetOverride(t.Key, create: false);
                return o != null ? o.Scale : 1f;
            };
            h.SetScale = v =>
            {
                var o = EditorUiLayout.GetOverride(t.Key, create: true);
                o.Scale = Mathf.Clamp(v, 0.25f, 4f);
                EditorUiLayout.ApplyOne(t.Key);
            };
            h.ResetTarget = () => EditorUiLayout.ResetToDefault(t.Key);
            h.CaptureUndo = () =>
            {
                var o = EditorUiLayout.GetOverride(t.Key, create: false);
                bool had = o != null;
                float ox = had ? o.OffX : 0f, oy = had ? o.OffY : 0f, scl = had ? o.Scale : 1f;
                return () =>
                {
                    if (!had) { EditorUiLayout.RemoveOverride(t.Key); return; }
                    var r = EditorUiLayout.GetOverride(t.Key, create: true);
                    r.OffX = ox; r.OffY = oy; r.Scale = scl;
                    EditorUiLayout.ApplyOne(t.Key);
                };
            };
        }

        private static float CanvasScale(RectTransform rt)
        {
            var canvas = rt != null ? rt.GetComponentInParent<Canvas>() : null;
            float sf = canvas != null ? canvas.rootCanvas.scaleFactor : 1f;
            return sf > 0.0001f ? sf : 1f;
        }

        private static LocHandle MakeHandle(string label, Func<RectTransform> get)
        {
            var go = UIBuilder.Rect("Handle_" + label, _canvasGo.transform);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 4f;
            bg.AAFringe = 0.5f;
            bg.BorderWidth = 1.5f;
            bg.BorderColor = Theme.Accent;
            bg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.12f);
            bg.raycastTarget = true;

            var lbl = UIBuilder.Label(go.transform, label.ToUpperInvariant(),
                (int)UIBuilder.SmallCapsFontSize, TextAnchor.MiddleCenter, Theme.Text);
            lbl.fontStyle = TMPro.FontStyles.Bold;

            var cg = go.AddComponent<CanvasGroup>();

            var h = go.AddComponent<LocHandle>();
            h.GetTarget = get;
            h.EditorCanvas = _canvas;
            h.Group = cg;
            return h;
        }

        private static void MakeDoneButton()
        {
            var btn = UIBuilder.Rect("Done", _canvasGo.transform);
            var rect = (RectTransform)btn.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -16f);
            rect.sizeDelta = new Vector2(190f, 34f);

            var bg = btn.AddComponent<RoundedRectGraphic>();
            bg.Radius = 17f;
            bg.AAFringe = 0.5f;
            bg.color = Theme.Accent;
            bg.raycastTarget = true;

            var lbl = UIBuilder.Label(btn.transform, "✓ Done editing", (int)UIBuilder.LabelFontSize,
                TextAnchor.MiddleCenter, Color.black);
            lbl.fontStyle = TMPro.FontStyles.Bold;

            ClickHandler.Attach(btn, Close);

            var hint = UIBuilder.Label(_canvasGo.transform,
                "Drag to move (Shift: 1 axis)  ·  Grips / scroll to scale  ·  Right-click reset  ·  Ctrl/⌘+Z undo",
                (int)UIBuilder.SmallCapsFontSize, TextAnchor.MiddleCenter, Theme.TextMuted);
            var hintRect = hint.rectTransform;
            hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 1f);
            hintRect.pivot = new Vector2(0.5f, 1f);
            hintRect.anchoredPosition = new Vector2(0f, -54f);
            hintRect.sizeDelta = new Vector2(520f, 20f);
        }
    }
}
