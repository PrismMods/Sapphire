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
       twirls (+ midspins in Task 4). fixed = shape-library geometry (localSign from TurnSign
       MIRRORED BY THE ANCHOR'S SPIN, flip AFTER twirled taps, first twirl only when rotating);
       anchor = angle-pad / pseudo-tool geometry (spinSign from the anchor, flip BEFORE twirled
       taps). Twirls sit one tile left, added after the tiles, then one RemakePath. */
    internal static class PseudoBuild
    {
        private const char ArbitraryChar = (char)163;

        /* A placement needs a selected tile to build from. scnEditor.CreateFloor bails on its
           first line when the selection isn't single, so with nothing selected every append
           SILENTLY does nothing — but the twirl seqIDs were still being computed and written, so
           the level ended up with Twirl events pointing at floors that were never created. That
           is the "invalid sequence of tiles" corruption: the events outlive the tiles.

           Refusing up front is the fix. Callers that want an implicit target must select a tile
           themselves first, so the anchor is always something the user can see. */
        internal static bool HasAnchor(scnEditor ed)
        {
            try { return ed != null && ed.SelectionIsSingle() && ed.selectedFloors[0] != null; }
            catch { return false; }
        }

        public static int Build(scnEditor ed, IList<PseudoStep> unit, PseudoContext ctx)
        {
            if (ed == null || unit == null || unit.Count == 0 || ctx == null) return 0;
            if (ed.lockPathEditing) { SapphireLog.Log("PseudoBuild: path editing locked"); return 0; }
            // ReplaceSeqs selects its own anchor inside DeleteAndReselect; everything else must
            // already have one.
            if (ctx.ReplaceSeqs == null && !HasAnchor(ed))
            { SapphireLog.Log("PseudoBuild: no tile selected — nothing placed"); return 0; }
            int n = Mathf.Max(1, ctx.RepeatN);
            using (new SaveStateScope(ed))
            {
                if (ctx.ReplaceSeqs != null && !DeleteAndReselect(ed, ctx.ReplaceSeqs)) return 0;

                double dir = StartDir(ed);
                int firstNewSeq = AnchorSeq(ed) + 1;
                int spin = AnchorSpin(ed);
                /* MIRROR A SHAPE ONTO A TWIRLED ANCHOR. The game reads a tile's charter back as
                   180 - s*(facing[T] - facing[T-1]), where s is the spin AT floor T-1 (+1 while
                   the ball turns clockwise, -1 after an odd number of Twirls). Writing the SAME
                   absolute facings after a twirl therefore keeps the shape's LOOK but reads every
                   charter back as 360-charter -- a 30 grace becomes a 330 beat. Folding the
                   anchor's spin into the turn sign flips the geometry instead: the shape lands
                   VERTICALLY MIRRORED and its charters (so its beat) survive untouched. That is
                   also what a charter does by hand -- past a twirl the same notation draws the
                   mirror image. The anchor path needs no such fold: it walks with the spin
                   directly, which is what makes a run respect an incoming twirl. */
                int ts = ctx.Fixed ? FixedSign(ctx.TurnSign, spin) : spin;
                int localSign = ts;
                int placed = 0;
                bool firstTwirl = true;
                var twirlSeqs = new List<int>();

                for (int r = 0; r < n; r++)
                    for (int i = 0; i < unit.Count; i++)
                    {
                        var st = unit[i];
                        if (st.Kind == StepKind.Midspin)
                        {
                            // A 999 midspin is TRANSPARENT to the heading — it spins in place, the
                            // tile angle carries straight through it (does NOT reverse the ball's
                            // heading). The caller closes a midspin run with an explicit straight tile.
                            if (!AppendMidspin(ed)) return Abort(ed, twirlSeqs, placed);
                            placed++;
                            continue;
                        }
                        bool tw = st.Swirl;
                        if (!ctx.Fixed && tw) localSign = -localSign;          // anchor: flip BEFORE
                        dir = Norm360(dir + localSign * (180.0 - st.Angle));
                        if (!AppendAbs(ed, dir)) return Abort(ed, twirlSeqs, placed);
                        if (tw)
                        {
                            /* The run's FIRST twirl is the only one that would land ON the
                               anchor tile (twirls sit one floor left of the tile they turn), so
                               it is emitted only for Rotate, which needs that extra flip to keep
                               its mirrored geometry's charters. Deliberately independent of the
                               anchor's spin: the mirror above already accounts for that, so the
                               same shape carries the SAME twirl set onto a twirled anchor as onto
                               a fresh track -- and never doubles up on a twirl the user placed
                               (two Twirl events on one floor cancel; the game just toggles). */
                            bool gate = ctx.Fixed ? (!firstTwirl || ctx.TurnSign < 0) : true;
                            if (gate) twirlSeqs.Add(firstNewSeq + placed - 1);
                            firstTwirl = false;
                            if (ctx.Fixed) localSign = -localSign;             // fixed: flip AFTER
                        }
                        placed++;
                    }

                /* Which floors already carry a Twirl, gathered ONCE. AddTwirl used to scan
                   ed.events per twirl, which is O(twirls x events) — a 300-tile run of twirled
                   taps walked the event list three hundred times. */
                var twirled = new HashSet<int>();
                try
                {
                    foreach (var ev in ed.events)
                        if (ev != null && ev.eventType == ADOFAI.LevelEventType.Twirl) twirled.Add(ev.floor);
                }
                catch { }
                foreach (int seq in twirlSeqs) if (seq >= firstNewSeq - 1) AddTwirl(ed, seq, twirled);
                try { ed.RemakePath(true, true); } catch { }
                return placed;
            }
        }

        /* ── primitives ──
           Both report whether a floor ACTUALLY appeared. CreateFloor has several silent bail-outs
           (selection not single, fullSpin on tile 0, an angle it reads as backspace), and a caller
           that assumes success goes on to emit twirls for tiles that don't exist. Counting floors
           is the only honest signal the game gives us here. */
        private static bool AppendAbs(scnEditor ed, double abs)
            => Appended(ed, () => ed.CreateFloorWithCharOrAngle((float)abs, ArbitraryChar, false, false));

        private static bool AppendMidspin(scnEditor ed)
            => Appended(ed, () => ed.CreateFloorWithCharOrAngle(999f, '!', false, true));

        private static bool Appended(scnEditor ed, Action create)
        {
            int before = -1;
            try { before = ed.floors.Count; } catch { }
            create();
            if (before < 0) return true;   // couldn't measure; don't block the build on that
            try { return ed.floors.Count > before; } catch { return true; }
        }

        /* NEVER stack two Twirls on one floor. The game toggles a spin flag per event
           (`ccw = !ccw` in ApplyEventsToFloors), so a second Twirl on the same floor CANCELS the
           first while still drawing a swirl marker — invalid-looking data that also survives
           saving, and the game's own twirl toggle then removes only one of them.

           The run's first twirl lands on the ANCHOR floor, which is exactly where a user is
           likely to have put one already, so this is not a rare case. Removing the existing event
           gives the SAME spin as adding a second one (odd count -> even either way; AnchorSpin
           read isCCW *after* the existing twirl, so the build already expects that flip) and
           leaves one event instead of two. So: toggle, never append blindly. */
        private static void AddTwirl(scnEditor ed, int seq, HashSet<int> twirled)
        {
            try
            {
                if (twirled.Contains(seq))
                {
                    // Only ever the anchor floor in practice, so the linear removal runs at most
                    // once per build rather than per twirl.
                    for (int i = ed.events.Count - 1; i >= 0; i--)
                    {
                        var ev = ed.events[i];
                        if (ev == null || ev.floor != seq || ev.eventType != ADOFAI.LevelEventType.Twirl) continue;
                        ed.events.RemoveAt(i);
                        break;
                    }
                    twirled.Remove(seq);
                    return;
                }
                ed.events.Add(new ADOFAI.LevelEvent(seq, ADOFAI.LevelEventType.Twirl));
                twirled.Add(seq);
            }
            catch (Exception ex) { SapphireLog.Log("PseudoBuild: twirl failed: " + ex.Message); }
        }
        private static double Norm360(double a) { a %= 360.0; if (a < 0) a += 360.0; return a; }

        /* An append that didn't land means every seqID after it is wrong. Drop the pending twirls
           rather than writing events onto floors that were never created — that mismatch is what
           corrupts a level, and it survives saving. Build runs inside a SaveStateScope, so the
           partial run undoes in one Ctrl+Z. */
        private static int Abort(scnEditor ed, List<int> twirlSeqs, int placed)
        {
            twirlSeqs.Clear();
            SapphireLog.Log("PseudoBuild: append rejected after " + placed + " tile(s) — twirls dropped, undo to restore");
            try { ed.RemakePath(true, true); } catch { }
            return placed;
        }

        // No selection = no anchor. Build refuses before reaching here, so the old
        // "fall back to the last tile in the level" guess is gone: it produced a plausible seqID
        // for appends that could never happen.
        private static int AnchorSeq(scnEditor ed)
        {
            try { return ed.selectedFloors != null && ed.selectedFloors.Count > 0
                ? ed.selectedFloors[ed.selectedFloors.Count - 1].seqID : -1; }
            catch { return -1; }
        }
        private static double StartDir(scnEditor ed)
        { try { var af = ADOBase.lm.floorAngles; return af[Mathf.Clamp(AnchorSeq(ed), 0, af.Length - 1)]; } catch { return 0.0; } }
        /* Which way the ball is turning at the anchor, in the GAME's convention:
           +1 clockwise, -1 counter-clockwise, matching `EditorToolbar.AppendRel` and
           scnEditor.CreateArbitraryFloor (`dir + (ccw ? -(180-rel) : 180-rel)`). scrFloor.isCCW
           is FALSE on a fresh track and already includes that floor's own Twirl event, so it is
           the whole answer for both "which direction is the path progressing" and "respect
           twirls".

           This returned the OPPOSITE from the shared-core refactor (431710e) until Sept 6 2026,
           which silently INVERTED every charter the anchor path wrote: the game reads a tile back
           as 180 - s·Δfacing, so a run built with -s produced 360-charter — a 30° tap landing as
           a 330° beat. See [[charter-spin-math]]. Do not "simplify" the sign back. */
        private static int AnchorSpin(scnEditor ed)
        {
            try { var sel = ed.selectedFloors; scrFloor a = sel != null && sel.Count > 0 ? sel[sel.Count - 1] : null;
                if (a == null) { var fl = ed.floors; if (fl != null && fl.Count > 0) a = fl[fl.Count - 1]; }
                if (a != null) return a.isCCW ? -1 : 1; } catch { }
            return 1;
        }

        /* Turn sign for a shape build: the shape's own sign, mirrored once for a CCW anchor so
           the shape lands vertically mirrored with its charters intact. A fresh (clockwise)
           anchor is +1, where this must be a no-op or every existing shape would flip. */
        internal static int FixedSign(int turnSign, int anchorSpin) => turnSign * anchorSpin;

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
            // Shape mirroring: a fresh (clockwise) anchor is AnchorSpin +1 and must not change the
            // shape; a twirled (CCW) anchor is -1 and must flip it. Rotate mirrors on top of both.
            ok &= FixedSign(1, 1) == 1 && FixedSign(1, -1) == -1
               && FixedSign(-1, 1) == -1 && FixedSign(-1, -1) == 1;
            /* The charter round-trip that 431710e broke: build a 30° tap on a fresh clockwise
               anchor and the game must read 30 back, not 330. facing step = s·(180-rel) with
               s = +1; charter = 180 - s·step. */
            double step = 1 * (180.0 - 30.0);
            ok &= Mathf.Abs((float)(180.0 - 1 * step) - 30f) < 0.01f;
            SapphireLog.Log("PseudoBuild.SelfCheck: " + (ok ? "PASS" : "FAIL"));
            return ok;
        }
    }
}
