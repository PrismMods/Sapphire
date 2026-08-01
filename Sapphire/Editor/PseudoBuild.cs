using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sapphire
{
    internal class PseudoContext
    {
        public bool Fixed;             // true = constant turn sign (shape); false = anchor-driven
        public int TurnSign = 1;       // used when Fixed; Rotate passes -1
        public int RepeatN = 1;
        public int[] ReplaceSeqs = null; // null = append onto anchor/level end; else delete + rebuild in place
        public bool Compensate = true;   // midspin beat/drift compensation (Task 4)
    }

    /* One builder for every pseudo/shape/angle-pad run. Lowers to PseudoStep[]; walks to tiles +
       twirls (+ midspins in Task 4). fixed = shape-library geometry (localSign from TurnSign,
       flip AFTER twirled taps, first-twirl spin gate); anchor = angle-pad / pseudo-tool geometry
       (spinSign from the anchor, flip BEFORE twirled taps). Twirls sit one tile left, added after
       the tiles, then one RemakePath. */
    internal static class PseudoBuild
    {
        private const char ArbitraryChar = (char)163;

        public static int Build(scnEditor ed, IList<PseudoStep> unit, PseudoContext ctx)
        {
            if (ed == null || unit == null || unit.Count == 0 || ctx == null) return 0;
            if (ed.lockPathEditing) { SapphireLog.Log("PseudoBuild: path editing locked"); return 0; }
            int n = Mathf.Max(1, ctx.RepeatN);
            using (new SaveStateScope(ed))
            {
                if (ctx.ReplaceSeqs != null && !DeleteAndReselect(ed, ctx.ReplaceSeqs)) return 0;

                double dir = StartDir(ed);
                int firstNewSeq = AnchorSeq(ed) + 1;
                int spin = AnchorSpin(ed);
                int ts = ctx.Fixed ? (ctx.TurnSign) : spin;
                int localSign = ts;
                int placed = 0;
                bool firstTwirl = true;
                var twirlSeqs = new List<int>();

                for (int r = 0; r < n; r++)
                    for (int i = 0; i < unit.Count; i++)
                    {
                        var st = unit[i];
                        // Task 4 inserts the midspin branch here.
                        bool tw = st.Swirl;
                        if (!ctx.Fixed && tw) localSign = -localSign;          // anchor: flip BEFORE
                        dir = Norm360(dir + localSign * (180.0 - st.Angle));
                        AppendAbs(ed, dir);
                        if (tw)
                        {
                            bool gate = ctx.Fixed ? (!firstTwirl || spin == ts) : true;
                            if (gate) twirlSeqs.Add(firstNewSeq + placed - 1);
                            firstTwirl = false;
                            if (ctx.Fixed) localSign = -localSign;             // fixed: flip AFTER
                        }
                        placed++;
                    }

                foreach (int seq in twirlSeqs) if (seq >= firstNewSeq - 1) AddTwirl(ed, seq);
                try { ed.RemakePath(true, true); } catch { }
                return placed;
            }
        }

        // ── primitives ──
        private static void AppendAbs(scnEditor ed, double abs) => ed.CreateFloorWithCharOrAngle((float)abs, ArbitraryChar, false, false);
        private static void AddTwirl(scnEditor ed, int seq)
        { try { ed.events.Add(new ADOFAI.LevelEvent(seq, ADOFAI.LevelEventType.Twirl)); } catch (Exception ex) { SapphireLog.Log("PseudoBuild: twirl failed: " + ex.Message); } }
        private static double Norm360(double a) { a %= 360.0; if (a < 0) a += 360.0; return a; }

        private static int AnchorSeq(scnEditor ed)
        {
            try { return ed.selectedFloors != null && ed.selectedFloors.Count > 0
                ? ed.selectedFloors[ed.selectedFloors.Count - 1].seqID : ADOBase.lm.floorAngles.Length - 1; }
            catch { return -1; }
        }
        private static double StartDir(scnEditor ed)
        { try { var af = ADOBase.lm.floorAngles; return af[Mathf.Clamp(AnchorSeq(ed), 0, af.Length - 1)]; } catch { return 0.0; } }
        private static int AnchorSpin(scnEditor ed)
        {
            try { var sel = ed.selectedFloors; scrFloor a = sel != null && sel.Count > 0 ? sel[sel.Count - 1] : null;
                if (a == null) { var fl = ed.floors; if (fl != null && fl.Count > 0) a = fl[fl.Count - 1]; }
                if (a != null) return a.isCCW ? 1 : -1; } catch { }
            return 1;
        }

        // Delete the given seqs high→low (mirrors the proven multi-tile delete), then reselect the
        // tile BEFORE the run so Build appends into the freed spot.
        private static bool DeleteAndReselect(scnEditor ed, int[] seqs)
        {
            if (seqs == null || seqs.Length == 0) return false;
            var sorted = (int[])seqs.Clone(); Array.Sort(sorted);
            int startSeq = sorted[0];
            for (int i = sorted.Length - 1; i >= 0; i--)
            {
                scrFloor t = null;
                try { var fl = ed.floors; if (sorted[i] >= 0 && sorted[i] < fl.Count) t = fl[sorted[i]]; } catch { }
                if (t == null) continue;
                try { ed.DeselectFloors(); ed.SelectFloor(t, false); if (ed.SelectionIsSingle()) ed.DeleteSingleSelection(false); } catch { }
            }
            scrFloor prev = null;
            try { if (startSeq > 0) prev = ed.floors[startSeq - 1]; } catch { }
            if (prev == null) { SapphireLog.Log("PseudoBuild: convert lost predecessor"); return false; }
            try { ed.DeselectFloors(); ed.SelectFloor(prev, false); } catch { }
            return true;
        }

        public static bool SelfCheck()
        {
            // fixed 120·2k [30t,90t], turnSign +1: turns +150,-90 -> facings 150,60
            var unit = new[] { new PseudoStep(30, StepKind.Tap, true), new PseudoStep(90, StepKind.Tap, true) };
            double dir = 0, ls = 1; var f = new double[2];
            for (int i = 0; i < unit.Length; i++) { dir = (dir + ls * (180 - unit[i].Angle)) % 360; if (dir < 0) dir += 360; f[i] = dir; ls = -ls; }
            bool ok = Mathf.Abs((float)f[0] - 150f) < 0.01f && Mathf.Abs((float)f[1] - 60f) < 0.01f;
            SapphireLog.Log("PseudoBuild.SelfCheck: " + (ok ? "PASS" : "FAIL"));
            return ok;
        }
    }
}
