namespace Sapphire
{
    /* Shape content. A shape is a BASE ANGLE (a charter sum) subdivided by KEY COUNT k into a
       pseudo UNIT: an nk pseudo is k tiles whose RELATIVE charters SUM to the base. The default
       decomposition is (k−1) grace 30s + a remainder (base − 30·(k−1)), but a KeyVariant may carry
       an explicit UnitOverride (e.g. 90° · 2-key = 33.75 / 56.25) and/or an N (default repeat).

       Inserting REPEATS the unit n times. The generic default n is 360 / gcd(360−base, 360) — the
       count that closes the base's 2-key star (90°→4, 120°→3, 240°→3, 270°→4) — overridable per
       variant.

       StepKind/PseudoStep remain only for ShapePathGraphic's renderer; the panel drives the preview
       through SetPath built from these. */
    internal enum StepKind { Tap, Midspin, Abs }

    internal struct PseudoStep
    {
        public double Angle;
        public StepKind Kind;
        public bool Swirl;
        public bool SwirlBlue;   // preview only: true = blue (CCW) swirl, false = red (CW)
        public PseudoStep(double angle, StepKind kind = StepKind.Tap, bool swirl = false)
        { Angle = angle; Kind = kind; Swirl = swirl; SwirlBlue = false; }
    }

    internal class KeyVariant
    {
        public readonly int K;
        public readonly double[] UnitOverride;   // null = generic decomposition
        public readonly bool[] TwirlMask;        // null = every tile twirls iff TwirlEvery(k)
        public readonly int NOverride;           // 0 = generic default n
        public KeyVariant(int k, double[] unit = null, bool[] twirls = null, int n = 0)
        { K = k; UnitOverride = unit; TwirlMask = twirls; NOverride = n; }
    }

    internal class ShapeDef
    {
        public readonly double Base;
        public readonly KeyVariant[] Variants;
        public ShapeDef(double baseAngle, params KeyVariant[] variants) { Base = baseAngle; Variants = variants; }

        public string Name => ((int)System.Math.Round(Base)) + "°";

        private int GenericDefaultN
        {
            get { int d = ((int)System.Math.Round(Base)) % 360; int g = Gcd(360 - d, 360); return g == 0 ? 1 : 360 / g; }
        }

        public int DefaultN(int k)
        {
            foreach (var v in Variants) if (v.K == k && v.NOverride > 0) return v.NOverride;
            return GenericDefaultN;
        }

        // RELATIVE charters of the k-key unit (the user's notation, e.g. 30 90): the variant's
        // explicit override if any, else (k−1) grace 30s + a remainder that sums to Base.
        public double[] Unit(int k)
        {
            foreach (var v in Variants)
                if (v.K == k && v.UnitOverride != null) return (double[])v.UnitOverride.Clone();
            if (k < 1) k = 1;
            var u = new double[k];
            for (int i = 0; i < k - 1; i++) u[i] = 30.0;
            u[k - 1] = Base - 30.0 * (k - 1);
            return u;
        }

        // Default twirl for a variant with no explicit mask: a 2-key pseudo twirls both tiles when
        // that traces the gentler turn (90°→−30°, 120°→−60°); 240/270 and higher-k stay no-twirl.
        public bool TwirlEvery(int k)
        {
            if (k != 2) return false;
            return System.Math.Abs(Norm180(60 - Base)) < System.Math.Abs(Norm180(360 - Base));
        }

        // Per-tile twirl mask: the variant's explicit TwirlMask if set (e.g. 240° · 3k = t t .),
        // else every tile = TwirlEvery(k).
        public bool[] Twirls(int k)
        {
            foreach (var v in Variants)
                if (v.K == k && v.TwirlMask != null) return v.TwirlMask;
            bool tw = TwirlEvery(k);
            var m = new bool[k];
            for (int i = 0; i < k; i++) m[i] = tw;
            return m;
        }

        private static double Norm180(double a) { a %= 360.0; if (a > 180) a -= 360; if (a < -180) a += 360; return a; }

        private static int Gcd(int a, int b)
        { a = System.Math.Abs(a); b = System.Math.Abs(b); while (b != 0) { int t = a % b; a = b; b = t; } return a; }
    }

    internal static class ShapeLibrary
    {
        // Each entry is a base angle with the key variants it offers — angles AND per-tile twirls
        // hardcoded per variant (these shapes don't share one rule). Add a row = add a shape.
        internal static readonly ShapeDef[] All =
        {
            new ShapeDef(90,  new KeyVariant(2, new double[] { 33.75, 56.25 }, new bool[] { true, true }, 8)),
            new ShapeDef(120, new KeyVariant(2, new double[] { 30, 90 }, new bool[] { true, true })),
            new ShapeDef(240, new KeyVariant(2, new double[] { 30, 210 }, new bool[] { false, false }),
                              new KeyVariant(3, new double[] { 30, 30, 180 }, new bool[] { true, true, false })),
            new ShapeDef(270, new KeyVariant(2, new double[] { 30, 240 }, new bool[] { false, false })),
        };
    }
}
