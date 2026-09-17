using UnityEngine;

namespace Sapphire
{
    /* "Suggest offset" in the Audio window.

       The relation is simpler than it looks, and the first version of this had it wrong by a
       whole bar. From the game's IL: scrConductor.addoffset = offset / 1000, and
       AsyncInputUtils.GetSongPosition returns (elapsed audio time) − addoffset. So chart time
       ZERO happens at audio time offset/1000 — and chart time zero is where the count-in starts,
       which charters put on the song's first beat. The first tile is struck a count-in later, at
       entryTime[1]; subtracting that term (as this once did) lands the answer countdownTicks
       beats early, which is exactly what showed up when the suggestion was measured against 115
       published levels: a median error of −4.045 beats, and 62 of them within a twelfth of a beat
       of a whole number. Drop the term and the same measurement centres on zero.

           offset_ms = first_onset_seconds × 1000

       Two modes. A level with no offset yet gets the song's first onset. A level that already has
       one gets a CORRECTION — the nearest attack to where its offset currently points — because a
       chart that starts three minutes into a long song must not be dragged back to the song's
       opening sound. (Eight of those 115 levels are exactly that.) */
    internal static class OffsetSuggest
    {
        internal static string LastNote;   // shown under the field after a suggestion

        // int? so the caller can tell "no answer" from "zero is the answer".
        internal static int? Compute(scnEditor ed)
        {
            LastNote = null;
            var res = AudioAnalysis.Get();
            if (res == null)
            {
                LastNote = AudioAnalysis.StatusNote ?? Loc.T("Reading the song…");
                return null;
            }

            int current = CurrentOffset(ed);
            /* Refine, when there is something to refine and it is inside the analysed head. Past
               that the envelope simply has not been read, so a correction is not available and
               the cold answer would be worse than leaving the value alone. */
            if (current != 0)
            {
                float t0 = current / 1000f;
                if (t0 <= 0f || t0 >= AudioAnalysis.OnsetWindowSec - 0.5f)
                {
                    /* Past the analysed head there is no envelope to correct against, and the
                       cold answer would be catastrophically wrong for exactly the levels that
                       land here — a chart three minutes into a long song would have its offset
                       replaced by the song's opening sound. Refuse instead. */
                    LastNote = Loc.T("Offset is past the analysed head — no suggestion.");
                    return null;
                }
                /* TWO estimators, and which one to trust depends on whether they agree.

                   The nearest single attack is the more precise of the two when it locks onto
                   the right transient (better median on both corpora); the beat grid — hundreds
                   of onsets voting on the phase — is far steadier when it does not (p90 197ms vs
                   77ms on one corpus, 302ms vs 120ms on the other). Disagreement is the tell: if
                   they land within 25ms of each other the attack was the right one, so take its
                   precision; if they do not, the attack is on the wrong transient and the grid is
                   the safer answer.

                   Measured both ways on 115 levels and again on a held-out 317: the combination
                   beats either estimator alone on both, at 88% and 76% within 30ms. */
                float bpm = Bpm(ed);
                float near = AudioAnalysis.OnsetNear(res, t0);
                float grid = -1f;
                if (bpm > 0f)
                {
                    float phase = AudioAnalysis.BeatPhase(res, bpm);
                    if (phase >= 0f) grid = AudioAnalysis.SnapToBeat(t0, phase, bpm);
                }
                if (near < 0f && grid < 0f)
                {
                    LastNote = Loc.T("No attack near the current offset.");
                    return null;
                }
                float pick; string how;
                if (near < 0f) { pick = grid; how = Loc.T("snapped to the song's beat grid"); }
                else if (grid < 0f) { pick = near; how = Loc.T("nearest attack"); }
                else if (Mathf.Abs(near - grid) <= 0.025f) { pick = near; how = Loc.T("attack, on the beat grid"); }
                else { pick = grid; how = Loc.T("snapped to the song's beat grid"); }
                int msp = Mathf.RoundToInt(pick * 1000f);
                LastNote = string.Format("{0}  ({1:+#;-#;0} ms)", how, msp - current);
                return msp;
            }

            if (res.Onset < 0f) { LastNote = Loc.T("No clear onset found."); return null; }
            int ms = Mathf.RoundToInt(res.Onset * 1000f);
            LastNote = string.Format("{0} {1:0.000}s → {2} ms", Loc.T("onset at"), res.Onset, ms);
            return ms;
        }

        internal static float Bpm(scnEditor ed)
        {
            try
            {
                var ld = ed != null ? ed.levelData : null;
                return ld != null ? ld.bpm : 0f;
            }
            catch { return 0f; }
        }

        private static int CurrentOffset(scnEditor ed)
        {
            try
            {
                var ld = ed != null ? ed.levelData : null;
                return ld != null ? ld.offset : 0;
            }
            catch { return 0; }
        }
    }
}
