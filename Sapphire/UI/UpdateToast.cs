using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire.UI
{
    /* Top-right update toast. Slides in when UpdateService finds a release, shows download
       progress, then parks in a "restart to apply" state.

       Naming is load-bearing, not cosmetic: the canvas is "SapphireUpdateToastCanvas" and the
       card "UpdateToast" so ToastStack (and any other mod using the same convention) can find
       us. Renaming either breaks cross-mod slotting — see ToastStack.

       State comes from polling UpdateService rather than from events, because that service runs
       on a worker thread and Unity objects may only be touched here. */
    internal static class UpdateToast
    {
        internal const string CanvasName = "Sapphire" + ToastStack.CanvasSuffix;

        private const float W = 380f, RestartW = 400f, H = 66f;
        private const float Inset = 24f;          // margin from the screen corner
        private const float AutoHideSeconds = 12f;
        private const float SlideSpeed = 7f;      // exponential approach, per second

        private static GameObject _canvasGo, _cardGo;
        private static RectTransform _card;
        private static CanvasGroup _group;
        private static RoundedRectGraphic _bg;
        private static TextMeshProUGUI _title, _hint;
        private static GameObject _barGo;
        private static RectTransform _barFill;

        private static bool _visible;
        private static string _shownKey;          // what the card is currently showing
        private static string _dismissedKey;      // user pressed × on this one
        private static float _hideAt;             // Time.unscaledTime deadline, 0 = no auto-hide
        private static float _slotY;              // smoothed screen-px offset from ToastStack
        private static bool _hovered;
        private static bool _checkedOnce;
        internal static void Tick()
        {
            var s = MainClass.Settings;
            if (s == null) return;

            // One check per session, once the engine is actually up.
            if (!_checkedOnce && s.AutoCheckUpdates && Time.frameCount > 120)
            {
                _checkedOnce = true;
                UpdateService.Check(false);
            }

            string key = DesiredKey();
            if (key == null || key == _dismissedKey)
            {
                if (_visible) Hide();
            }
            else if (!_visible || key != _shownKey)
            {
                Show(key);
            }

            if (_canvasGo == null) return;
            if (_visible) RefreshText();
            Animate();
        }

        // Identity of what SHOULD be on screen right now; null = nothing. Doubles as the
        // dismiss key, so dismissing "update available" doesn't also suppress the later
        // "installed" state for the same version.
        private static string DesiredKey()
        {
            switch (UpdateService.Status)
            {
                case UpdateStatus.Available:
                    return UpdateService.Available != null ? "avail:" + UpdateService.Available.Tag : null;
                case UpdateStatus.Installing:
                    return "installing";
                case UpdateStatus.Installed:
                    return "installed:" + UpdateService.Message;
                case UpdateStatus.Failed:
                    // Silent when the failing check was the automatic startup one — being
                    // offline is not news. Message is in the key so a retry that fails
                    // differently re-shows instead of being swallowed as already-dismissed.
                    return UpdateService.AnnounceFailures ? "failed:" + UpdateService.Message : null;
                default:
                    return null;
            }
        }

        private static void Show(string key)
        {
            Build();
            _shownKey = key;
            _visible = true;
            _cardGo.SetActive(true);
            bool restart = UpdateService.Status == UpdateStatus.Installed;
            _card.sizeDelta = new Vector2(restart ? RestartW : W, H);
            // Installing/installed must not vanish mid-flight; only the actionable
            // "there's an update" card times out.
            _hideAt = UpdateService.Status == UpdateStatus.Available
                ? Time.unscaledTime + AutoHideSeconds : 0f;
            RefreshText();
        }

        /* Marking the key hidden is what stops an auto-hide from looping. Tick() re-derives the
           desired key EVERY frame, so without this the timeout would hide the card and the very
           next frame would see the same still-valid key with _visible false and slide it right
           back in, forever. A timed-out toast counts as seen, same as pressing ×. */
        private static void Hide()
        {
            if (_shownKey != null) _dismissedKey = _shownKey;
            _visible = false;
            _hideAt = 0f;
        }

        private static void Dismiss()
        {
            Hide();
        }

        private static void Animate()
        {
            float w = _card.sizeDelta.x;
            /* ToastStack measures in SCREEN pixels (it has to — foreign canvases may scale
               differently). anchoredPosition is in OUR canvas's units, so divide by our scale
               factor or the gap is wrong at every resolution except the reference one. */
            float target = ToastStack.OffsetFor(CanvasName) / CanvasScale();
            _slotY = _visible ? Mathf.Lerp(_slotY, target, 1f - Mathf.Exp(-SlideSpeed * Time.unscaledDeltaTime))
                              : target;
            float x = _visible ? -Inset : w + Inset;   // off the right edge when hidden
            Vector2 want = new Vector2(x, -(Inset + _slotY));

            Vector2 cur = _card.anchoredPosition;
            float t = 1f - Mathf.Exp(-SlideSpeed * Time.unscaledDeltaTime);
            _card.anchoredPosition = Vector2.Lerp(cur, want, t);
            _group.alpha = Mathf.Lerp(_group.alpha, _visible ? 1f : 0f, t);

            if (!_visible && _group.alpha < 0.02f && _cardGo.activeSelf) _cardGo.SetActive(false);

            if (_bg != null)
                _bg.color = _hovered ? new Color(0.16f, 0.16f, 0.19f, 0.98f)
                                     : new Color(0.10f, 0.10f, 0.13f, 0.96f);

            if (_hideAt > 0f && !_hovered && Time.unscaledTime >= _hideAt) Hide();

            if (_barGo != null)
            {
                float p = UpdateService.Progress;
                bool showBar = UpdateService.Status == UpdateStatus.Installing && p >= 0f;
                if (_barGo.activeSelf != showBar) _barGo.SetActive(showBar);
                if (showBar) _barFill.anchorMax = new Vector2(Mathf.Clamp01(p), 1f);
            }
        }

        private static float CanvasScale()
        {
            if (_canvasGo == null) return 1f;
            var c = _canvasGo.GetComponent<Canvas>();
            float f = c != null ? c.scaleFactor : 1f;
            return f > 0.0001f ? f : 1f;
        }

        private static void RefreshText()
        {
            if (_title == null) return;
            switch (UpdateService.Status)
            {
                case UpdateStatus.Available:
                    var info = UpdateService.Available;
                    _title.text = Loc.T("Update available") + ": " + (info != null ? info.Tag : "");
                    _hint.text = info != null && !string.IsNullOrEmpty(info.Name)
                        ? info.Name : Loc.T("Click to update");
                    break;
                case UpdateStatus.Installing:
                    _title.text = Loc.T("Downloading update…");
                    float p = UpdateService.Progress;
                    _hint.text = p >= 0f ? Mathf.RoundToInt(p * 100f) + "%" : Loc.T("Please wait");
                    break;
                case UpdateStatus.Installed:
                    _title.text = Loc.T("Update installed");
                    _hint.text = Loc.T("Restart the game to apply it");
                    break;
                case UpdateStatus.Failed:
                    _title.text = Loc.T("Update failed");
                    _hint.text = UpdateService.Message;
                    break;
            }
        }

        private static void OnCardClick()
        {
            switch (UpdateService.Status)
            {
                case UpdateStatus.Available:
                    UpdateService.Install();
                    _hideAt = 0f;
                    _shownKey = null;   // force a re-Show so the card re-sizes for the new state
                    break;
                case UpdateStatus.Failed:
                    UpdateService.Check(true);
                    break;
                default:
                    break;   // installing / installed: clicking does nothing destructive
            }
        }

        // ── build ────────────────────────────────────────────────────────────

        private static void Build()
        {
            if (_canvasGo != null) return;

            _canvasGo = new GameObject(CanvasName, typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32700;   // above Sapphire's panels, below Quartz's toast layer
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            _cardGo = new GameObject(ToastStack.CardName, typeof(RectTransform));
            _cardGo.transform.SetParent(_canvasGo.transform, false);
            _card = (RectTransform)_cardGo.transform;
            _card.anchorMin = _card.anchorMax = new Vector2(1f, 1f);
            _card.pivot = new Vector2(1f, 1f);
            _card.sizeDelta = new Vector2(W, H);
            _card.anchoredPosition = new Vector2(W + Inset, -Inset);
            _group = _cardGo.AddComponent<CanvasGroup>();
            _group.alpha = 0f;
            _bg = _cardGo.AddComponent<RoundedRectGraphic>();
            _bg.Radius = 10f;
            _bg.color = new Color(0.10f, 0.10f, 0.13f, 0.96f);
            _bg.BorderWidth = 1f;
            _bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            _bg.raycastTarget = true;
            ClickHandler.Attach(_cardGo, OnCardClick);
            var hov = _cardGo.AddComponent<HoverFlag>();
            hov.OnEnter = () => _hovered = true;
            hov.OnExit = () => _hovered = false;

            // accent pip down the left edge — cheap identity marker, no glyph needed
            var pipGo = new GameObject("Pip", typeof(RectTransform));
            pipGo.transform.SetParent(_cardGo.transform, false);
            var pr = (RectTransform)pipGo.transform;
            pr.anchorMin = new Vector2(0f, 0f); pr.anchorMax = new Vector2(0f, 1f);
            pr.pivot = new Vector2(0f, 0.5f);
            pr.offsetMin = new Vector2(10f, 12f); pr.offsetMax = new Vector2(14f, -12f);
            var pip = pipGo.AddComponent<RoundedRectGraphic>();
            pip.Radius = 2f;
            pip.color = Theme.Accent;
            pip.raycastTarget = false;
            pipGo.AddComponent<AccentFill>();

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(_cardGo.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0f, 1f);
            tr.offsetMin = new Vector2(26f, -32f); tr.offsetMax = new Vector2(-40f, -10f);
            _title = UIBuilder.Tmp(titleGo, "", 15f, TextAnchor.MiddleLeft, Theme.Text);
            _title.textWrappingMode = TextWrappingModes.NoWrap;
            _title.overflowMode = TextOverflowModes.Ellipsis;
            _title.raycastTarget = false;

            var hintGo = new GameObject("Hint", typeof(RectTransform));
            hintGo.transform.SetParent(_cardGo.transform, false);
            var hr = (RectTransform)hintGo.transform;
            hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f);
            hr.pivot = new Vector2(0f, 1f);
            hr.offsetMin = new Vector2(26f, -52f); hr.offsetMax = new Vector2(-40f, -32f);
            _hint = UIBuilder.Tmp(hintGo, "", 12.5f, TextAnchor.MiddleLeft, Theme.TextMuted);
            _hint.textWrappingMode = TextWrappingModes.NoWrap;
            _hint.overflowMode = TextOverflowModes.Ellipsis;
            _hint.raycastTarget = false;

            BuildProgressBar();
            BuildClose();
            _cardGo.SetActive(false);
        }

        private static void BuildProgressBar()
        {
            _barGo = new GameObject("Bar", typeof(RectTransform));
            _barGo.transform.SetParent(_cardGo.transform, false);
            var br = (RectTransform)_barGo.transform;
            br.anchorMin = new Vector2(0f, 0f); br.anchorMax = new Vector2(1f, 0f);
            br.pivot = new Vector2(0.5f, 0f);
            br.offsetMin = new Vector2(26f, 8f); br.offsetMax = new Vector2(-16f, 11f);
            var track = _barGo.AddComponent<RoundedRectGraphic>();
            track.Radius = 1.5f;
            track.color = new Color(1f, 1f, 1f, 0.10f);
            track.raycastTarget = false;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(_barGo.transform, false);
            _barFill = (RectTransform)fillGo.transform;
            _barFill.anchorMin = new Vector2(0f, 0f); _barFill.anchorMax = new Vector2(0f, 1f);
            _barFill.pivot = new Vector2(0f, 0.5f);
            _barFill.offsetMin = Vector2.zero; _barFill.offsetMax = Vector2.zero;
            var fill = fillGo.AddComponent<RoundedRectGraphic>();
            fill.Radius = 1.5f;
            fill.color = Theme.Accent;
            fill.raycastTarget = false;
            fillGo.AddComponent<AccentFill>();
            _barGo.SetActive(false);
        }

        private static void BuildClose()
        {
            var go = new GameObject("Close", typeof(RectTransform));
            go.transform.SetParent(_cardGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(1f, 1f);
            r.anchoredPosition = new Vector2(-8f, -8f);
            r.sizeDelta = new Vector2(22f, 22f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 4f;
            bg.color = new Color(1f, 1f, 1f, 0f);
            bg.raycastTarget = true;
            var x = UIBuilder.Tmp(go, "×", 16f, TextAnchor.MiddleCenter, Theme.TextMuted);
            x.raycastTarget = false;
            ClickHandler.Attach(go, Dismiss);
            var hov = go.AddComponent<HoverFlag>();
            hov.OnEnter = () => { bg.color = Theme.CloseHover; x.color = Color.white; };
            hov.OnExit = () => { bg.color = new Color(1f, 1f, 1f, 0f); x.color = Theme.TextMuted; };
        }

        internal static void Dispose()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _cardGo = null; _card = null; _group = null; _bg = null;
            _title = null; _hint = null; _barGo = null; _barFill = null;
            _visible = false; _shownKey = null; _hovered = false; _slotY = 0f;
        }

        private class HoverFlag : MonoBehaviour,
            UnityEngine.EventSystems.IPointerEnterHandler, UnityEngine.EventSystems.IPointerExitHandler
        {
            public Action OnEnter, OnExit;
            public void OnPointerEnter(UnityEngine.EventSystems.PointerEventData e) { if (OnEnter != null) OnEnter(); }
            public void OnPointerExit(UnityEngine.EventSystems.PointerEventData e) { if (OnExit != null) OnExit(); }
        }
    }
}
