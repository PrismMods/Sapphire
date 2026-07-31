using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire.UI
{
    /* Draws a chart tile-path from a charter sequence as beige bars + a planet dot at the
       start, colored marks on swirl tiles, dots on midspin tiles. Auto-fits/centers the path
       into the RectTransform. Same walk math the build uses, so the preview matches the
       inserted tiles. Stylized (not the game's own sprites) — deliberate, keeps it data-only. */
    internal class ShapePathGraphic : MaskableGraphic
    {
        private PseudoStep[] _steps = new PseudoStep[0];
        private static readonly Color TileCol = new Color(0.86f, 0.78f, 0.55f, 1f); // ADOFAI beige
        private const float BarW = 0.34f;      // bar thickness as a fraction of the unit step
        private const float Pad = 10f;

        internal void SetPath(IList<PseudoStep> steps)
        {
            _steps = new PseudoStep[steps.Count];
            for (int i = 0; i < steps.Count; i++) _steps[i] = steps[i];
            SetVerticesDirty();
        }

        internal void SetSimple(IList<double> charters)
        {
            var s = new PseudoStep[charters.Count];
            for (int i = 0; i < charters.Count; i++) s[i] = new PseudoStep(charters[i]);
            SetPath(s);
        }

        /* Walk a charter sequence into tile-center points. dir = outgoing facing (deg); a Tap
           turns it by spin*(180-charter); Midspin flips spin and adds a zero-length point; Abs
           sets the facing absolutely. Returns center points (count = tiles+1, incl. the start).
           spin (+1 = CW) is the preview convention; absolute orientation is irrelevant because
           the caller auto-fits. */
        internal static Vector2[] Walk(IList<PseudoStep> steps, out List<int> swirlIdx, out List<int> midspinIdx)
        {
            swirlIdx = new List<int>();
            midspinIdx = new List<int>();
            var pts = new List<Vector2>();
            Vector2 p = Vector2.zero;
            pts.Add(p);
            double dir = 0.0, spin = 1.0;
            for (int i = 0; i < steps.Count; i++)
            {
                var st = steps[i];
                if (st.Kind == StepKind.Midspin) { spin = -spin; midspinIdx.Add(pts.Count - 1); continue; }
                if (st.Kind == StepKind.Abs) dir = st.Angle;
                else dir += spin * (180.0 - st.Angle);
                double r = dir * Mathf.Deg2Rad;
                p += new Vector2((float)System.Math.Cos(r), (float)System.Math.Sin(r));
                pts.Add(p);
                if (st.Swirl) swirlIdx.Add(pts.Count - 1);
            }
            return pts.ToArray();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_steps.Length == 0) return;
            var pts = Walk(_steps, out var swirls, out var mids);
            if (pts.Length < 2) return;

            // fit: bounds of pts → scale/offset into rect with padding
            Vector2 lo = pts[0], hi = pts[0];
            foreach (var q in pts) { lo = Vector2.Min(lo, q); hi = Vector2.Max(hi, q); }
            Rect rc = GetPixelAdjustedRect();
            float availW = Mathf.Max(1f, rc.width - Pad * 2f), availH = Mathf.Max(1f, rc.height - Pad * 2f);
            float spanX = Mathf.Max(0.001f, hi.x - lo.x), spanY = Mathf.Max(0.001f, hi.y - lo.y);
            float scale = Mathf.Min(availW / (spanX + 1f), availH / (spanY + 1f));
            Vector2 mid = (lo + hi) * 0.5f;
            System.Func<Vector2, Vector2> map = v => new Vector2(
                rc.center.x + (v.x - mid.x) * scale,
                rc.center.y + (v.y - mid.y) * scale);

            float half = BarW * scale * 0.5f;
            for (int i = 1; i < pts.Length; i++)
                AddBar(vh, map(pts[i - 1]), map(pts[i]), half, TileCol);
            // planet dot at the start
            AddDot(vh, map(pts[0]), half * 1.6f, new Color(0.95f, 0.95f, 1f, 1f));
            foreach (int s in swirls) AddDot(vh, map(pts[s]), half * 1.1f, new Color(0.7f, 0.2f, 0.35f, 1f));
            foreach (int m in mids)   AddDot(vh, map(pts[m]), half * 0.8f, new Color(0.4f, 0.6f, 1f, 1f));
        }

        private static void AddBar(VertexHelper vh, Vector2 a, Vector2 b, float half, Color col)
        {
            Vector2 d = b - a; float len = d.magnitude; if (len < 0.001f) return;
            Vector2 n = new Vector2(-d.y, d.x) / len * half;
            int i0 = vh.currentVertCount;
            vh.AddVert(a - n, col, Vector2.zero); vh.AddVert(a + n, col, Vector2.zero);
            vh.AddVert(b + n, col, Vector2.zero); vh.AddVert(b - n, col, Vector2.zero);
            vh.AddTriangle(i0, i0 + 1, i0 + 2); vh.AddTriangle(i0, i0 + 2, i0 + 3);
        }

        private static void AddDot(VertexHelper vh, Vector2 c, float r, Color col)
        {
            const int seg = 10; int i0 = vh.currentVertCount;
            vh.AddVert(c, col, Vector2.zero);
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg * Mathf.PI * 2f;
                vh.AddVert(c + new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r, col, Vector2.zero);
            }
            for (int i = 1; i <= seg; i++) vh.AddTriangle(i0, i0 + i, i0 + i + 1);
        }

        // Known-case sanity: [180,180] = straight (2 unit steps east); [90,90] = turn-before-move,
        // dir 90 then 180, points (0,0)->(0,1)->(-1,1) (north then west). Logs PASS/FAIL; call once from the panel's first build.
        internal static bool SelfCheck()
        {
            var straight = Walk(new[] { new PseudoStep(180), new PseudoStep(180) }, out _, out _);
            var l = Walk(new[] { new PseudoStep(90), new PseudoStep(90) }, out _, out _);
            bool ok = straight.Length == 3
                      && Mathf.Abs(straight[2].x - 2f) < 0.01f && Mathf.Abs(straight[2].y) < 0.01f
                      && l.Length == 3
                      && Mathf.Abs(l[1].x) < 0.01f && Mathf.Abs(l[1].y - 1f) < 0.01f
                      && Mathf.Abs(l[2].x + 1f) < 0.01f && Mathf.Abs(l[2].y - 1f) < 0.01f;
            SapphireLog.Log("ShapePathGraphic.SelfCheck: " + (ok ? "PASS" : "FAIL"));
            return ok;
        }
    }
}
