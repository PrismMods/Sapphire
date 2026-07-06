using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sapphire.UI
{
    internal enum ResizeEdge { Top, Left, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    internal class ResizeHandle : MonoBehaviour, IPointerDownHandler, IDragHandler
    {
        public ResizeEdge Edge;
        public RectTransform Panel;

        public const float MinWidth = 480f;
        public const float MinHeight = 320f;

        // Hit zones are larger than visual cues so users can grab edges without precise aim.
        // No cursor change on hover (would need bundled cursor textures); thresholds compensate.
        // BR is the visible grip corner, so it's larger; the other corners stay subtle.
        private const float CornerSize = 12f;
        private const float CornerSizeBr = 22f;
        private const float SideSize = 8f;

        private static float CornerSizeFor(ResizeEdge edge)
        {
            return edge == ResizeEdge.BottomRight ? CornerSizeBr : CornerSize;
        }

        private Vector2 _startMouse;
        private Vector2 _startSize;
        private Vector2 _startPos;

        public void OnPointerDown(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Panel.parent as RectTransform, e.position, null, out _startMouse);
            _startSize = Panel.sizeDelta;
            _startPos = Panel.anchoredPosition;
        }

        public void OnDrag(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Panel.parent as RectTransform, e.position, null, out Vector2 cur);
            Vector2 d = cur - _startMouse;
            float w = _startSize.x, h = _startSize.y;
            Vector2 p = _startPos;

            if (Edge == ResizeEdge.Right || Edge == ResizeEdge.TopRight || Edge == ResizeEdge.BottomRight)
            {
                float nw = Mathf.Max(MinWidth, _startSize.x + d.x);
                p.x += (nw - _startSize.x) * 0.5f;
                w = nw;
            }
            if (Edge == ResizeEdge.Left || Edge == ResizeEdge.TopLeft || Edge == ResizeEdge.BottomLeft)
            {
                float nw = Mathf.Max(MinWidth, _startSize.x - d.x);
                p.x -= (nw - _startSize.x) * 0.5f;
                w = nw;
            }
            if (Edge == ResizeEdge.Top || Edge == ResizeEdge.TopLeft || Edge == ResizeEdge.TopRight)
            {
                float nh = Mathf.Max(MinHeight, _startSize.y + d.y);
                p.y += (nh - _startSize.y) * 0.5f;
                h = nh;
            }
            if (Edge == ResizeEdge.Bottom || Edge == ResizeEdge.BottomLeft || Edge == ResizeEdge.BottomRight)
            {
                float nh = Mathf.Max(MinHeight, _startSize.y - d.y);
                p.y -= (nh - _startSize.y) * 0.5f;
                h = nh;
            }

            Panel.sizeDelta = new Vector2(w, h);
            Panel.anchoredPosition = p;
        }

        public static void AttachAll(RectTransform panel)
        {
            foreach (ResizeEdge edge in System.Enum.GetValues(typeof(ResizeEdge)))
                Make(panel, edge);
        }

        private static void Make(RectTransform panel, ResizeEdge edge)
        {
            var go = new GameObject("Resize_" + edge, typeof(RectTransform));
            go.transform.SetParent(panel, false);
            var rect = (RectTransform)go.transform;

            bool corner = edge == ResizeEdge.TopLeft || edge == ResizeEdge.TopRight
                       || edge == ResizeEdge.BottomLeft || edge == ResizeEdge.BottomRight;
            if (corner)
            {
                float cs = CornerSizeFor(edge);
                rect.sizeDelta = new Vector2(cs, cs);
            }

            // Side hit zones leave gaps at each end matching the adjacent corner's size.
            // The BR corner is larger than the others, so the bottom and right sides use a
            // larger inset at the BR end.
            switch (edge)
            {
                case ResizeEdge.Top:
                    rect.anchorMin = new Vector2(0, 1); rect.anchorMax = new Vector2(1, 1);
                    rect.offsetMin = new Vector2(CornerSize, -SideSize); rect.offsetMax = new Vector2(-CornerSize, SideSize);
                    break;
                case ResizeEdge.Bottom:
                    rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(1, 0);
                    rect.offsetMin = new Vector2(CornerSize, -SideSize); rect.offsetMax = new Vector2(-CornerSizeBr, SideSize);
                    break;
                case ResizeEdge.Left:
                    rect.anchorMin = new Vector2(0, 0); rect.anchorMax = new Vector2(0, 1);
                    rect.offsetMin = new Vector2(-SideSize, CornerSize); rect.offsetMax = new Vector2(SideSize, -CornerSize);
                    break;
                case ResizeEdge.Right:
                    rect.anchorMin = new Vector2(1, 0); rect.anchorMax = new Vector2(1, 1);
                    rect.offsetMin = new Vector2(-SideSize, CornerSizeBr); rect.offsetMax = new Vector2(SideSize, -CornerSize);
                    break;
                // Corner pivots anchor the handle's matching corner to the panel's corner,
                // so the full CornerSize box sits INSIDE the panel (was half-inside, half-outside
                // with a centered pivot — the visible grip in the BR didn't fully overlap the hit zone).
                case ResizeEdge.TopLeft:
                    rect.anchorMin = rect.anchorMax = new Vector2(0, 1); rect.pivot = new Vector2(0, 1);
                    break;
                case ResizeEdge.TopRight:
                    rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(1, 1);
                    break;
                case ResizeEdge.BottomLeft:
                    rect.anchorMin = rect.anchorMax = new Vector2(0, 0); rect.pivot = new Vector2(0, 0);
                    break;
                case ResizeEdge.BottomRight:
                    rect.anchorMin = rect.anchorMax = new Vector2(1, 0); rect.pivot = new Vector2(1, 0);
                    break;
            }
            if (corner) rect.anchoredPosition = Vector2.zero;

            // Invisible hit target. raycastTarget=true (Image default).
            var img = go.AddComponent<Image>();
            img.sprite = Theme.White;
            img.color = Color.clear;

            var h = go.AddComponent<ResizeHandle>();
            h.Edge = edge;
            h.Panel = panel;
        }
    }
}
