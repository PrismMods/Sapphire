using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sapphire.UI
{
    internal enum ResizeEdge { Top, Left, Right, Bottom, TopLeft, TopRight, BottomLeft, BottomRight }

    internal class ResizeHandle : MonoBehaviour, IPointerDownHandler, IDragHandler,
        IBeginDragHandler, IEndDragHandler
    {
        /* A live resize drag freezes imperative rebuilds. The tool palettes rebuild by
           destroying and recreating their panel GameObject, which takes the handle being
           dragged down with it — the drag died on its first frame and the width snapped back,
           so width resize looked like it did nothing at all. */
        private static ResizeHandle _active;
        internal static bool Dragging => _active != null;

        public ResizeEdge Edge;
        public RectTransform Panel;

        public const float MinWidth = 480f;
        public const float MinHeight = 320f;
        // per-panel minimums (small palettes must be shrinkable below the settings-panel floor)
        public float MinW = MinWidth;
        public float MinH = MinHeight;

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

        public void OnBeginDrag(PointerEventData e) { _active = this; }
        public void OnEndDrag(PointerEventData e) { if (_active == this) _active = null; }
        // Panel torn down (module disposed, tab switched) with the button still held.
        private void OnDisable() { if (_active == this) _active = null; }

        public void OnDrag(PointerEventData e)
        {
            if (Panel == null) return;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Panel.parent as RectTransform, e.position, null, out Vector2 cur);
            Vector2 d = cur - _startMouse;
            float w = _startSize.x, h = _startSize.y;
            Vector2 p = _startPos;
            // Pivot-aware: the dragged edge follows the mouse, the OPPOSITE edge stays put.
            // (The old math hardcoded the settings panel's center pivot — top-left-pivot
            // panels drifted while resizing, and clamped ticks made the knob feel dead.)
            Vector2 pv = Panel.pivot;

            if (Edge == ResizeEdge.Right || Edge == ResizeEdge.TopRight || Edge == ResizeEdge.BottomRight)
            {
                float nw = Mathf.Max(MinW, _startSize.x + d.x);
                p.x += (nw - _startSize.x) * pv.x;
                w = nw;
            }
            if (Edge == ResizeEdge.Left || Edge == ResizeEdge.TopLeft || Edge == ResizeEdge.BottomLeft)
            {
                float nw = Mathf.Max(MinW, _startSize.x - d.x);
                p.x -= (nw - _startSize.x) * (1f - pv.x);
                w = nw;
            }
            if (Edge == ResizeEdge.Top || Edge == ResizeEdge.TopLeft || Edge == ResizeEdge.TopRight)
            {
                float nh = Mathf.Max(MinH, _startSize.y + d.y);
                p.y += (nh - _startSize.y) * pv.y;
                h = nh;
            }
            if (Edge == ResizeEdge.Bottom || Edge == ResizeEdge.BottomLeft || Edge == ResizeEdge.BottomRight)
            {
                float nh = Mathf.Max(MinH, _startSize.y - d.y);
                p.y -= (nh - _startSize.y) * (1f - pv.y);
                h = nh;
            }

            Panel.sizeDelta = new Vector2(w, h);
            Panel.anchoredPosition = p;
        }

        public static void AttachAll(RectTransform panel, bool grip = false,
            float minW = MinWidth, float minH = MinHeight)
        {
            // Handles now outlive a PanelKit rebuild (it reuses the panel GO), so re-attaching
            // on every build would stack duplicate hit zones.
            if (panel.Find("Resize_Right") != null) return;
            foreach (ResizeEdge edge in System.Enum.GetValues(typeof(ResizeEdge)))
                Make(panel, edge, minW, minH);
            if (grip) BuildGrip(panel);
        }

        /* WIDTH ONLY, for the tool palettes: their height is content-driven (PanelKit.SetHeight
           rewrites it on every rebuild), so a vertical handle would hand the user a size that
           vanishes on the next click. Left/right edges plus a grip cue on the right edge. */
        public static void AttachWidth(RectTransform panel, float minW)
        {
            if (panel.Find("Resize_Right") != null) return;
            Make(panel, ResizeEdge.Left, minW, 0f);
            var right = Make(panel, ResizeEdge.Right, minW, 0f);
            /* Right normally stops short of the BR corner to leave room for the BottomRight
               handle — which a width-only panel never gets, so that inset would make the corner
               grip a dead zone. Let the right edge own the corner instead, and use the SAME
               staircase cue as every other panel rather than a 3-dot sliver nobody finds. */
            right.offsetMin = new Vector2(right.offsetMin.x, 0f);
            BuildGrip(panel);
        }

        // The visible bottom-right resize cue (the Ctrl+E panel's dot staircase): 3-2-1 dots
        // pointing into the corner, raycast-transparent so the BR handle under it gets drags.
        public static void BuildGrip(RectTransform panel)
        {
            var gripGo = new GameObject("ResizeGrip", typeof(RectTransform));
            gripGo.transform.SetParent(panel, false);
            var gripRect = (RectTransform)gripGo.transform;
            gripRect.anchorMin = new Vector2(1, 0);
            gripRect.anchorMax = new Vector2(1, 0);
            gripRect.pivot = new Vector2(1, 0);
            gripRect.sizeDelta = new Vector2(16f, 16f);
            gripRect.anchoredPosition = new Vector2(-4f, 4f);

            Vector2[] dotPositions = new[]
            {
                new Vector2(0f, 0f),  new Vector2(6f, 0f),  new Vector2(12f, 0f),
                new Vector2(6f, 6f),  new Vector2(12f, 6f),
                new Vector2(12f, 12f),
            };
            foreach (var pos in dotPositions)
            {
                var dotGo = new GameObject("Dot", typeof(RectTransform));
                dotGo.transform.SetParent(gripGo.transform, false);
                var dotRect = (RectTransform)dotGo.transform;
                dotRect.anchorMin = new Vector2(0, 0);
                dotRect.anchorMax = new Vector2(0, 0);
                dotRect.pivot = new Vector2(0, 0);
                dotRect.sizeDelta = new Vector2(3f, 3f);
                dotRect.anchoredPosition = pos;
                var img = dotGo.AddComponent<Image>();
                img.sprite = Theme.White;
                img.color = Theme.TextMuted;
                img.raycastTarget = false;
            }
        }

        private static RectTransform Make(RectTransform panel, ResizeEdge edge,
            float minW = MinWidth, float minH = MinHeight)
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
            h.MinW = minW;
            h.MinH = minH;
            return rect;
        }
    }
}
