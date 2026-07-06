using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sapphire
{
    [Serializable]
    public class ColorStop
    {
        public float Progress;
        public float R, G, B, A = 1f;
    }

    [Serializable]
    public class ColorGradient
    {
        public bool IsSolid;
        public bool HasPerfectColor;
        public float PR = 1f, PG = 1f, PB = 1f, PA = 1f;
        public List<ColorStop> Stops = new List<ColorStop>();

        public Color Evaluate(float t)
        {
            if (Stops.Count == 0) return Color.white;
            if (IsSolid) return ToColor(Stops[0]);
            if (HasPerfectColor && t >= 1f)
                return new Color(PR, PG, PB, PA);
            if (t <= Stops[0].Progress)
                return ToColor(Stops[0]);
            int last = Stops.Count - 1;
            if (t >= Stops[last].Progress)
                return ToColor(Stops[last]);
            for (int i = 0; i < last; i++)
            {
                if (t <= Stops[i + 1].Progress)
                {
                    float lerp = (t - Stops[i].Progress) / (Stops[i + 1].Progress - Stops[i].Progress);
                    return Color.Lerp(ToColor(Stops[i]), ToColor(Stops[i + 1]), lerp);
                }
            }
            return Color.white;
        }

        private static Color ToColor(ColorStop s) => new Color(s.R, s.G, s.B, s.A);
    }
}
