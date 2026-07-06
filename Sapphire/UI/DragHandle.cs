using UnityEngine;
using UnityEngine.EventSystems;

namespace Sapphire.UI
{
    // Attach to a child of the panel (e.g. titlebar). Drags the parent RectTransform.
    internal class DragHandle : MonoBehaviour, IDragHandler, IPointerDownHandler
    {
        private RectTransform _panel;
        private Canvas _canvas;
        private Vector2 _offset;

        private void Awake()
        {
            _panel = transform.parent as RectTransform;
            _canvas = GetComponentInParent<Canvas>();
        }

        public void OnPointerDown(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _panel.parent as RectTransform, e.position, _canvas.worldCamera, out Vector2 local);
            _offset = _panel.anchoredPosition - local;
        }

        public void OnDrag(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _panel.parent as RectTransform, e.position, _canvas.worldCamera, out Vector2 local);
            _panel.anchoredPosition = local + _offset;
        }
    }
}
