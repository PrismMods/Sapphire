using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sapphire.UI
{
    /* A short label that appears beside the cursor after a brief hover — for buttons that are only
       an icon or a glyph. One shared card on its own top canvas.

       Enter/exit only. A pointer-DOWN handler here would become the press target for its host and
       starve a parent's drag or down handler, so a click is noticed by polling instead. */
    internal class HoverTip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        private const float Delay = 0.4f, MaxW = 280f, PadX = 8f, PadY = 5f;

        public string Text;

        private static HoverTip _hot;
        private static float _enterAt;
        private static bool _dismissed;
        private static GameObject _canvasGo, _cardGo;
        private static RectTransform _canvasRect, _card;
        private static TextMeshProUGUI _tmp;

        internal static void Attach(GameObject go, string text)
        {
            if (go == null || string.IsNullOrEmpty(text)) return;
            var t = go.GetComponent<HoverTip>() ?? go.AddComponent<HoverTip>();
            t.Text = text;
        }

        public void OnPointerEnter(PointerEventData e) { _hot = this; _enterAt = Time.unscaledTime; _dismissed = false; }

        public void OnPointerExit(PointerEventData e) { if (_hot == this) { _hot = null; Hide(); } }

        private void OnDisable() { if (_hot == this) { _hot = null; Hide(); } }

        private void Update()
        {
            if (_hot != this) return;
            if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) { _dismissed = true; Hide(); }
            if (_dismissed || Time.unscaledTime - _enterAt < Delay) return;
            Show(Text);
        }

        private static void Show(string text)
        {
            Ensure();
            if (_tmp.text != text) _tmp.text = text;
            Vector2 pref = _tmp.GetPreferredValues(text, MaxW, 0f);
            var size = new Vector2(Mathf.Min(pref.x, MaxW) + PadX * 2f, pref.y + PadY * 2f);
            if (_card.sizeDelta != size) _card.sizeDelta = size;
            // Canvas-local, pivot-relative (the canvas pivot is its centre); card pivot is top-left.
            Vector2 lp;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, Input.mousePosition, null, out lp);
            var r = _canvasRect.rect;
            float x = lp.x + 14f, y = lp.y - 20f;
            if (x + size.x > r.xMax - 6f) x = lp.x - 10f - size.x;     // flip left at the right edge
            if (y - size.y < r.yMin + 6f) y = lp.y + 10f + size.y;     // flip up at the bottom edge
            x = Mathf.Max(x, r.xMin + 6f); y = Mathf.Min(y, r.yMax - 6f);
            var pos = new Vector2(x, y);
            if (_card.anchoredPosition != pos) _card.anchoredPosition = pos;
            if (!_cardGo.activeSelf) _cardGo.SetActive(true);
        }

        private static void Hide() { if (_cardGo != null && _cardGo.activeSelf) _cardGo.SetActive(false); }

        private static void Ensure()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SapphireHoverTip", typeof(RectTransform));
            DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 970;   // over the key-hint card (960); nothing here raycasts
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
            bg.Radius = 5f;
            bg.color = new Color(0.05f, 0.05f, 0.07f, 0.96f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.16f);
            bg.raycastTarget = false;
            var tGo = new GameObject("T", typeof(RectTransform));
            tGo.transform.SetParent(_cardGo.transform, false);
            var tr = (RectTransform)tGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(PadX, PadY); tr.offsetMax = new Vector2(-PadX, -PadY);
            _tmp = UIBuilder.Tmp(tGo, "", 12f, TextAnchor.MiddleLeft, Theme.Text);
            _tmp.textWrappingMode = TextWrappingModes.Normal;
            _tmp.raycastTarget = false;
            _cardGo.SetActive(false);
        }
    }
}
