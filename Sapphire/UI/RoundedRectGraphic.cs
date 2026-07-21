using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire
{
    // Procedural rounded-rect graphic built at the cell's actual size, so corners stay
    // smooth at any resolution/scale. Fill uses MaskableGraphic.color; an optional border
    // ring uses BorderColor/BorderWidth. A small alpha-falloff fringe outside the outline
    // keeps corners smooth without supersampling or MSAA.
    internal class RoundedRectGraphic : MaskableGraphic
    {
        [SerializeField] private float _radius        = 8f;
        [SerializeField] private float _borderWidth   = 0f;
        [SerializeField] private Color _borderColor   = Color.white;
        [SerializeField] private int   _maxCornerSegs = 48;
        [SerializeField] private float _aaFringe      = 1.25f;

        public float Radius
        {
            get => _radius;
            set { if (!Mathf.Approximately(_radius, value)) { _radius = value; SetVerticesDirty(); } }
        }

        public float BorderWidth
        {
            get => _borderWidth;
            set { if (!Mathf.Approximately(_borderWidth, value)) { _borderWidth = value; SetVerticesDirty(); } }
        }

        public Color BorderColor
        {
            get => _borderColor;
            set { if (_borderColor != value) { _borderColor = value; SetVerticesDirty(); } }
        }

        public int MaxCornerSegments
        {
            get => _maxCornerSegs;
            set { var v = Mathf.Max(1, value); if (_maxCornerSegs != v) { _maxCornerSegs = v; SetVerticesDirty(); } }
        }

        public float AAFringe
        {
            get => _aaFringe;
            set { if (!Mathf.Approximately(_aaFringe, value)) { _aaFringe = Mathf.Max(0f, value); SetVerticesDirty(); } }
        }

        private readonly List<Vector2> _outer  = new List<Vector2>(64);
        private readonly List<Vector2> _inner  = new List<Vector2>(64);
        private readonly List<Vector2> _fringe = new List<Vector2>(64);

        /* Geometry the cached outlines above were built for; NaN = nothing cached yet. The
           corner centres derive from rect.xMin/yMin as well as the size, and a pivot change
           moves those while leaving width/height alone — so the origin is part of the key. */
        private float _cX = float.NaN, _cY, _cW, _cH, _cR, _cBw, _cFringe;
        private int _cSegs;

        private bool GeometryCached(Rect rect, float r, float bw, float fringe, int segs) =>
            _cSegs == segs && _cX == rect.xMin && _cY == rect.yMin
            && _cW == rect.width && _cH == rect.height
            && _cR == r && _cBw == bw && _cFringe == fringe;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = GetPixelAdjustedRect();
            float w = rect.width, h = rect.height;
            if (w <= 0f || h <= 0f) return;

            float halfMin = Mathf.Min(w, h) * 0.5f;
            float r       = Mathf.Clamp(_radius, 0f, halfMin);
            float bw      = Mathf.Clamp(_borderWidth, 0f, halfMin);
            float innerR  = Mathf.Max(0f, r - bw);
            float fringe  = Mathf.Max(0f, _aaFringe);

            // Aim for sub-pixel chord length: arc length per quadrant is (r+fringe)·π/2, so
            // segs ≈ that keeps each chord ≤ 1 unit. Capped to avoid runaway triangle counts on
            // huge radii.
            int segs = Mathf.Clamp(Mathf.CeilToInt((r + fringe) * Mathf.PI * 0.5f), 4, Mathf.Max(4, _maxCornerSegs));

            // Outer arc centers (within the full rect). The fringe outline shares these centers
            // with radius r+fringe — that produces an outline 1 fringe-unit outside the outer at
            // every point (arcs and straights alike).
            Vector2 oTL = new Vector2(rect.xMin + r, rect.yMax - r);
            Vector2 oTR = new Vector2(rect.xMax - r, rect.yMax - r);
            Vector2 oBR = new Vector2(rect.xMax - r, rect.yMin + r);
            Vector2 oBL = new Vector2(rect.xMin + r, rect.yMin + r);

            bool hasBorder = bw > 0f && _borderColor.a > 0f;
            bool hasFringe = fringe > 0f;

            /* The outlines depend only on geometry, never on color — but uGUI re-runs
               OnPopulateMesh for a plain tint change too (Graphic.color → SetVerticesDirty),
               and hover/selection tints are the common case. Rebuild the trig only when the
               geometry actually moved; otherwise reuse the cached outlines and just re-emit
               verts with the new color. Cache is per-instance: a static one would thrash
               between differently-sized graphics. */
            if (!GeometryCached(rect, r, bw, fringe, segs))
            {
                _cX = rect.xMin; _cY = rect.yMin; _cW = w; _cH = h;
                _cR = r; _cBw = bw; _cFringe = fringe; _cSegs = segs;

                _outer.Clear();
                BuildOutline(_outer, oTL, oTR, oBR, oBL, r, segs);

                /* bw == 0 ⇒ the inset rect IS the rect and innerR == r, so the inner outline
                   would come out point-for-point identical to the outer one. Skip the second
                   (identical) trig pass — this is the common case, most shapes have no border. */
                if (bw <= 0f)
                {
                    _inner.Clear();
                }
                else
                {
                    // Inner outline (fill perimeter, and the inside of the ring when border > 0).
                    // Computed from the bw-inset rect so it stays correct even when bw > r.
                    float ixMin = rect.xMin + bw, ixMax = rect.xMax - bw;
                    float iyMin = rect.yMin + bw, iyMax = rect.yMax - bw;
                    Vector2 iTL = new Vector2(ixMin + innerR, iyMax - innerR);
                    Vector2 iTR = new Vector2(ixMax - innerR, iyMax - innerR);
                    Vector2 iBR = new Vector2(ixMax - innerR, iyMin + innerR);
                    Vector2 iBL = new Vector2(ixMin + innerR, iyMin + innerR);

                    _inner.Clear();
                    BuildOutline(_inner, iTL, iTR, iBR, iBL, innerR, segs);
                }

                _fringe.Clear();
                if (hasFringe) BuildOutline(_fringe, oTL, oTR, oBR, oBL, r + fringe, segs);
            }

            // With no border the fill perimeter is the outer outline itself (see above).
            var inner = bw <= 0f ? _outer : _inner;

            // Fill: triangle fan from center to the inner outline.
            Color32 fillColor = color;
            Vector2 center    = rect.center;
            int n             = inner.Count;
            int centerIdx     = vh.currentVertCount;
            vh.AddVert(center, fillColor, Vector2.zero);
            int fillBase = vh.currentVertCount;
            for (int i = 0; i < n; i++)
                vh.AddVert(inner[i], fillColor, Vector2.zero);
            for (int i = 0; i < n; i++)
                vh.AddTriangle(centerIdx, fillBase + i, fillBase + ((i + 1) % n));

            // Border: quad strip between outer and inner outlines (counts match — same segs).
            Color32 borderColor = _borderColor;
            if (hasBorder)
            {
                int outerBase = vh.currentVertCount;
                for (int i = 0; i < _outer.Count; i++)
                    vh.AddVert(_outer[i], borderColor, Vector2.zero);
                int innerBase = vh.currentVertCount;
                for (int i = 0; i < inner.Count; i++)
                    vh.AddVert(inner[i], borderColor, Vector2.zero);
                for (int i = 0; i < n; i++)
                {
                    int j = (i + 1) % n;
                    vh.AddTriangle(outerBase + i, outerBase + j, innerBase + j);
                    vh.AddTriangle(outerBase + i, innerBase + j, innerBase + i);
                }
            }

            // AA fringe: alpha-fade strip outside the outer outline. Color = border (if any) or fill,
            // alpha at the inner edge = source alpha, at the outer edge = 0. This both softens corner
            // staircase artifacts and gives straight edges a sub-pixel feathered edge.
            if (hasFringe)
            {
                Color32 edgeColor = hasBorder ? borderColor : fillColor;
                Color32 fadeColor = edgeColor; fadeColor.a = 0;
                int innerEdgeBase = vh.currentVertCount;
                for (int i = 0; i < _outer.Count; i++)
                    vh.AddVert(_outer[i], edgeColor, Vector2.zero);
                int outerEdgeBase = vh.currentVertCount;
                for (int i = 0; i < _fringe.Count; i++)
                    vh.AddVert(_fringe[i], fadeColor, Vector2.zero);
                int fn = _outer.Count;
                for (int i = 0; i < fn; i++)
                {
                    int j = (i + 1) % fn;
                    vh.AddTriangle(innerEdgeBase + i, innerEdgeBase + j, outerEdgeBase + j);
                    vh.AddTriangle(innerEdgeBase + i, outerEdgeBase + j, outerEdgeBase + i);
                }
            }
        }

        private static void BuildOutline(List<Vector2> outline,
            Vector2 tl, Vector2 tr, Vector2 br, Vector2 bl, float r, int segs)
        {
            AddArc(outline, br, r, -Mathf.PI * 0.5f, 0f,                segs);
            AddArc(outline, tr, r, 0f,               Mathf.PI * 0.5f,   segs);
            AddArc(outline, tl, r, Mathf.PI * 0.5f,  Mathf.PI,          segs);
            AddArc(outline, bl, r, Mathf.PI,         Mathf.PI * 1.5f,   segs);
        }

        private static void AddArc(List<Vector2> outline, Vector2 c, float r, float a0, float a1, int segs)
        {
            for (int i = 0; i <= segs; i++)
            {
                float t = (float)i / segs;
                float a = Mathf.Lerp(a0, a1, t);
                outline.Add(new Vector2(c.x + Mathf.Cos(a) * r, c.y + Mathf.Sin(a) * r));
            }
        }
    }
}
