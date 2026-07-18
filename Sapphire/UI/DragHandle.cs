using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Sapphire.UI
{
    // Attach to a child of the panel (e.g. titlebar). Drags the parent RectTransform.
    internal class DragHandle : MonoBehaviour, IDragHandler, IPointerDownHandler, IBeginDragHandler, IEndDragHandler
    {
        internal Action DragEnd;   // e.g. PanelKit edge-dock snapping
        internal bool Dragging { get; private set; }

        public void OnBeginDrag(PointerEventData e) => Dragging = true;

        private RectTransform _panel;
        private Canvas _canvas;
        private Vector2 _offset;

        public void OnEndDrag(PointerEventData e) { Dragging = false; DragEnd?.Invoke(); }

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
