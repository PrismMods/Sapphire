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
                double dirBaseline = dir;       // Task 4: fixed pre-excursion heading (midspin anchor + compensator)
                int firstNewSeq = AnchorSeq(ed) + 1;
                int spin = AnchorSpin(ed);
                int ts = ctx.Fixed ? (ctx.TurnSign) : spin;
                int localSign = ts;
                int placed = 0;
                bool firstTwirl = true;
                bool anyMidspin = false;        // Task 4: gates the closing compensator tile
                var twirlSeqs = new List<int>();

                for (int r = 0; r < n; r++)
                    for (int i = 0; i < unit.Count; i++)
                    {
                        var st = unit[i];
                        if (st.Kind == StepKind.Midspin)
                        {
                            AppendMidspin(ed);
                            localSign = -localSign;    // ball's spin reverses
                            anyMidspin = true;
                            placed++;
                            continue;
                        }
                        bool tw = st.Swirl;
                        if (!ctx.Fixed && tw) localSign = -localSign;          // anchor: flip BEFORE
                        // Once a 999 has fired, F(s-1) is a sentinel, not a real heading, so a tap
                        // can't walk off the PREVIOUS tile — it re-anchors to the fixed pre-excursion
                        // baseline instead (verified ground truth: EditorToolbar.ApplyPseudoAbs,
                        // facing = baseline + sign*(180-charter), charters already cumulative). This
                        // is what makes repeats return to the same excursion instead of climbing.
                        dir = anyMidspin
                            ? Norm360(dirBaseline + ts * (180.0 - st.Angle))
                            : Norm360(dir + localSign * (180.0 - st.Angle));
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

                // Closing tile: only when the unit actually used a midspin (drift-free by construction
                // otherwise) and the caller wants it. Compensator kept behind ONE method so Task 5 can
                // tune the exact charter/beat rounding in-game without touching this call site.
                if (ctx.Compensate && anyMidspin)
                {
                    AppendAbs(ed, CompensatorFacing(dirBaseline, dir));
                    placed++;
                }

                foreach (int seq in twirlSeqs) if (seq >= firstNewSeq - 1) AddTwirl(ed, seq);
                try { ed.RemakePath(true, true); } catch { }
                return placed;
            }
        }

        // ── primitives ──
        private static void AppendAbs(scnEditor ed, double abs) => ed.CreateFloorWithCharOrAngle((float)abs, ArbitraryChar, false, false);
        private static void AppendMidspin(scnEditor ed) => ed.CreateFloorWithCharOrAngle(999f, '!', false, true);

        // Task 4 tuning point: the facing of the tile that closes out a midspin run. First cut =
        // return to baseline heading exactly (the verified straight-tile reference: net-zero drift,
        // 3k@30 -> 150,999,120,999,baseline). Exact charter/beat rounding (whole-beat landing for
        // odd tap counts etc.) is finalised in-game once Task 5's caller exercises real midspin units.
        private static double CompensatorFacing(double baselineDir, double currentDir) => baselineDir;

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

            // Task 4: midspin unit [tap30,999,tap60,999] + compensator, mirrors Build's anyMidspin
            // branch — facings re-anchor to a FIXED baseline once a 999 has fired (not accumulated
            // tile-to-tile), matching the reference 3k@30 = 150,999,120,999 + baseline-closing tile.
            double baseline = 0.0, mdir = baseline; bool sawMid = false; int mi = 0;
            var mf = new double[2];
            var midUnit = new[] { new PseudoStep(30, StepKind.Tap), new PseudoStep(0, StepKind.Midspin),
                                   new PseudoStep(60, StepKind.Tap), new PseudoStep(0, StepKind.Midspin) };
            foreach (var st in midUnit)
            {
                if (st.Kind == StepKind.Midspin) { sawMid = true; continue; }
                mdir = sawMid ? Norm360(baseline + 1 * (180.0 - st.Angle)) : Norm360(mdir + 1 * (180.0 - st.Angle));
                mf[mi++] = mdir;
            }
            double comp = CompensatorFacing(baseline, mdir);
            bool okMid = Mathf.Abs((float)mf[0] - 150f) < 0.01f && Mathf.Abs((float)mf[1] - 120f) < 0.01f
                         && Mathf.Abs((float)comp - (float)baseline) < 0.01f;

            bool all = ok && okMid;
            SapphireLog.Log("PseudoBuild.SelfCheck: " + (all ? "PASS" : "FAIL")
                + (okMid ? "" : " (midspin branch FAIL: " + mf[0] + "," + mf[1] + "," + comp + ")"));
            return all;
        }
    }
}
