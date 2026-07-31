namespace Sapphire
{
    /* Curated shape content. ONE ShapeDef drives BOTH the preview render (ShapePathGraphic)
       and the tile build (EditorShapeLibrary). Angles are ADOFAI relative CHARTERS (180 =
       straight, 180-f = a turn of f degrees), the same convention AppendRel uses. Adding a
       shape = adding a ShapeDef here; its preview auto-generates.

       Simple[] is the clean one-tile-per-segment form. Each PseudoForm is a beat-neutral
       subdivision of that same silhouette. Pseudo steps default to Tap (relative charter);
       Midspin = a zero-length 999 tile; Abs = an absolute facing (used by midspin pseudos
       whose facings are authored directly). Swirl = a Twirl event on that tile.

       NOTE: the seed pseudo forms below are the simple TAP subdivisions (no midspin) so they
       build and preview correctly on day one. The definitive midspin/swirl pseudo variants
       from the reference screenshot are authored WITH the user and verified in-game — see the
       plan's Task 5 in-game loop. */
    internal enum StepKind { Tap, Midspin, Abs }

    internal struct PseudoStep
    {
        public double Angle;
        public StepKind Kind;
        public bool Swirl;
        public PseudoStep(double angle, StepKind kind = StepKind.Tap, bool swirl = false)
        { Angle = angle; Kind = kind; Swirl = swirl; }
    }

    internal class PseudoForm
    {
        public string Name;
        public string Meta;
        public PseudoStep[] Steps;
        public PseudoForm(string name, string meta, PseudoStep[] steps)
        { Name = name; Meta = meta; Steps = steps; }
    }

    internal class ShapeDef
    {
        public string Name;
        public string SimpleMeta;
        public double[] Simple;
        public PseudoForm[] Pseudo;
        public ShapeDef(string name, string simpleMeta, double[] simple, PseudoForm[] pseudo)
        { Name = name; SimpleMeta = simpleMeta; Simple = simple; Pseudo = pseudo; }
    }

    internal static class ShapeLibrary
    {
        // Helper: a run of `n` taps each of charter `a` (beat-neutral when n*a is a whole beat).
        private static PseudoStep[] Taps(params double[] angles)
        {
            var s = new PseudoStep[angles.Length];
            for (int i = 0; i < angles.Length; i++) s[i] = new PseudoStep(angles[i]);
            return s;
        }

        internal static readonly ShapeDef[] All =
        {
            // C-shape: two right-angle turns (charter 90) around a straight middle.
            new ShapeDef("C", "3 tiles · 90 / 180 / 90",
                new double[] { 90, 180, 90 },
                new[] {
                    new PseudoForm("Half · 2 beats", "30 / 90 (adds to 120)",
                        Taps(90, 30, 90, 90, 30, 90)),
                }),

            // Comb / E: alternating in-out notches.
            new ShapeDef("Comb", "33.75 / 56.25 (adds to 90)",
                new double[] { 33.75, 56.25, 33.75, 56.25 },
                new[] {
                    new PseudoForm("Half · 4 beats", "33.75 / 56.25 (adds to 90)",
                        Taps(33.75, 56.25, 33.75, 56.25, 33.75, 56.25, 33.75, 56.25)),
                }),

            // Hexagon: six 120-degree exterior turns (charter 60), full loop.
            new ShapeDef("Hexagon", "6 tiles · 60 each (full)",
                new double[] { 60, 60, 60, 60, 60, 60 },
                new[] {
                    new PseudoForm("Full · 4 beats", "30 / 90 (adds to 120)",
                        Taps(30, 90, 30, 90, 30, 90, 30, 90, 30, 90, 30, 90)),
                }),
        };
    }
}
