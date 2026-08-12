using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire.UI
{
    /* Filled simple polygon, in the RectTransform's own local space (the same coordinates the
       toolbar's bar/dot helpers use). Ear-clipped rather than fanned: hand-authored icon shapes
       have reflex corners, and a fan from vertex 0 spills outside the silhouette there. Winding
       is irrelevant — the UI material doesn't cull. */
    internal class PolyGraphic : MaskableGraphic
    {
        private Vector2[] _pts = new Vector2[0];
        private int[] _tris = new int[0];

        internal void SetPolygon(Vector2[] pts)
        {
            _pts = pts ?? new Vector2[0];
            _tris = Triangulate(_pts);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (_pts.Length < 3 || _tris.Length < 3) return;
            var c = color;
            for (int i = 0; i < _pts.Length; i++)
                vh.AddVert(new Vector3(_pts[i].x, _pts[i].y, 0f), c, Vector2.zero);
            for (int i = 0; i + 2 < _tris.Length; i += 3)
                vh.AddTriangle(_tris[i], _tris[i + 1], _tris[i + 2]);
        }

        private static float Cross(Vector2 u, Vector2 w) => u.x * w.y - u.y * w.x;

        private static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c) =>
            Cross(b - a, p - a) > 0f && Cross(c - b, p - b) > 0f && Cross(a - c, p - c) > 0f;

        internal static int[] Triangulate(Vector2[] p)
        {
            int n = p.Length;
            var tris = new List<int>(Mathf.Max(0, (n - 2) * 3));
            if (n < 3) return tris.ToArray();

            float area = 0f;
            for (int i = 0; i < n; i++)
            {
                var a = p[i]; var b = p[(i + 1) % n];
                area += a.x * b.y - b.x * a.y;
            }
            // Work counter-clockwise so "convex" is a single sign test below.
            var v = new List<int>(n);
            if (area >= 0f) for (int i = 0; i < n; i++) v.Add(i);
            else for (int i = n - 1; i >= 0; i--) v.Add(i);

            int guard = n * n;
            while (v.Count > 2 && guard-- > 0)
            {
                bool clipped = false;
                for (int i = 0; i < v.Count; i++)
                {
                    int ia = v[(i + v.Count - 1) % v.Count], ib = v[i], ic = v[(i + 1) % v.Count];
                    Vector2 a = p[ia], b = p[ib], c = p[ic];
                    if (Cross(b - a, c - b) <= 0f) continue;   // reflex corner: never an ear
                    bool ear = true;
                    for (int j = 0; j < v.Count && ear; j++)
                    {
                        int ip = v[j];
                        if (ip == ia || ip == ib || ip == ic) continue;
                        if (Inside(p[ip], a, b, c)) ear = false;
                    }
                    if (!ear) continue;
                    tris.Add(ia); tris.Add(ib); tris.Add(ic);
                    v.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;   // self-intersecting input: fill what was clipped so far
            }
            return tris.ToArray();
        }
    }
}
