using System;
using TMPro;
using UnityEngine;
using Sapphire.UI;

namespace Sapphire
{
    /* Hz tool — charting by FREQUENCY instead of by angle.

       A tile whose charter is A° lasts A/180 beats, so at B BPM the run hits

           H = 180·B / A     hits per minute        A = 180·B / H = 3·B / F
           F = H / 60        hits per second (Hz)   H = F · 60

       i.e. a 20 Hz buzz at 175 BPM is 3·175/20 = 26.25° per tile. Type any one of Hz / hits-
       per-minute / angle and the other two follow; BPM defaults to the SELECTED tile's
       effective BPM (level BPM × that floor's speed multiplier), which is the number the run
       will actually play at.

       The keyboard overlay is a note picker, not MIDI hardware: click a key and the target Hz
       becomes that note's pitch (12-TET, A4 = 440 Hz), which is the whole point for charting a
       melody line as a buzz. */
    internal static class EditorHzTool
    {
        private static readonly PanelKit K = new PanelKit("SapphireHzTool", 944, PanelW, focusable: true);
        private static bool _open;
        private static long _layoutSig = NoSig;
        private const long NoSig = long.MinValue;

        /* THE MODEL — TWO different BPMs, which is the thing to get right here.

           SHAPE BPM is what the run has to play at, and it is an OUTPUT: pick the note you want
           and the angle the shape needs, and the tempo that produces it follows from
           `shapeBpm = angle · Hz / 3`. That is the whole point of the tool — you do not know the
           BPM in advance, you discover it.

           BASE BPM is what the chart is already at (level BPM × the anchor tile's speed). It is
           never an input to the pitch maths. It does exactly two jobs: it is the denominator of
           the SetSpeed multiplier that makes the shape BPM real, and it is the clock the run's
           length is quoted in — `beats = tiles · baseBpm / (60 · Hz)`, i.e. tiles/Hz seconds
           converted to beats of the section you are writing in.

           Pitch is three numbers on one equation, so two are free. All three are stored, not
           computed, because a locked value has to be able to STAY put while the others move.
           With nothing locked the SHAPE BPM absorbs every edit; lock it and the angle (or Hz)
           moves instead. */
        private enum Var { None, Bpm, Hz, Angle }

        private static double _baseBpm = 100.0;   // the section's tempo — detected, not designed
        private static bool _baseAuto = true;     // keep it mirroring the selected tile
        private static double _bpm = 900.0;       // SHAPE BPM: angle · Hz / 3, an output
        private static double _hz = 20.0;
        private static double _angle = 135.0;     // 8-sided circle
        private static Var _lock = Var.None;

        private static double _beats = 1.0;       // run length in beats of the BASE BPM
        private static int _tiles = 4;            // derived from _beats unless _lockTiles
        private static bool _lockTiles;
        private static bool _circle;              // close the run into a full 360° loop
        private static int _sides = 16;           // circle mode: tiles per revolution
        private static int _laps = 1;             // circle mode: how many times round
        private static bool _perfectCircle = true; // must the loop close, or may it stop partway?
        private static bool _pauseFill;           // pad the leftover time with a Pause event

        private static bool _writeSpeed = true;   // emit the SetSpeed that makes _bpm real
        private static int _octave = 4;
        private static string _status = "";
        private static TextMeshProUGUI _statusTmp;
        // Highlighted key, as (octave, step-within-octave). Step -1 = nothing picked.
        private static int _selOct, _selStep = -1;

        /* Tuning. The picker is EDO-generic: an octave is _edo equal steps, and one reference
           pitch pins the whole grid. Defaults are ordinary concert tuning — 12-EDO with step 9
           ("A") of octave 4 at 440 Hz — so a user who never opens this menu sees exactly the
           standard keyboard. */
        private static int _edo = 12;
        private static double _refHz = 440.0;
        private static int _refOct = 4, _refStep = 9;
        private static bool _tuningOpen;

        internal static bool IsOpen => _open;
        internal static PanelKit Kit => K;
        internal static void SetOpen(bool v) { if (v != _open) Toggle(); }

        internal static void Toggle()
        {
            _open = !_open;
            _status = "";
            _layoutSig = NoSig;
            SapphireLog.Log("HzTool: toggled " + (_open ? "open" : "closed"));
            EditorToolbar.SyncHzHighlight();
        }

        internal static void Close()
        { if (_open) { _open = false; _layoutSig = NoSig; EditorToolbar.SyncHzHighlight(); } }

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            /* Gated like the shape library (editor + master switch), NOT additionally on
               FeatToolsSapphire: the tool has its own keybind, so gating it on the toolbar's
               category would leave Shift+F silently doing nothing whenever that category is off. */
            bool want = _open && ed != null && !ed.playMode && MainClass.EditorSuiteOn;
            if (!want)
            {
                if (_open) LogGate(ed);
                K.Show(false);
                GhostPreview.Release(GhostOwner); _ghostSig = long.MinValue;
                return;
            }
            _gateLogged = null;

            // The BASE BPM tracks the selection; it is a reading of the chart, so nothing in the
            // pitch maths re-solves around it — only the run's length and the SetSpeed ratio.
            if (_baseAuto)
            {
                double b = SelectionBpm(ed);
                if (b > 0.0 && Math.Abs(b - _baseBpm) > 1e-6) { _baseBpm = b; _layoutSig = NoSig; }
            }
            // Keep the length pair consistent before the signature reads it, or the panel shows
            // a tile count for the previous angle until something else forces a rebuild.
            SolveLength();
            long sig = 17;
            sig = sig * 31 + _octave;
            sig = sig * 31 + (_baseAuto ? 1 : 0);
            sig = sig * 31 + (long)Math.Round(_baseBpm * 1000.0);
            sig = sig * 31 + _selOct * 128 + _selStep;
            sig = sig * 31 + _edo;
            sig = sig * 31 + _refOct * 128 + _refStep;
            sig = sig * 31 + (long)Math.Round(_refHz * 100.0);
            sig = sig * 31 + (_tuningOpen ? 1 : 0);
            sig = sig * 31 + (long)Math.Round(_hz * 1000.0);
            sig = sig * 31 + (long)Math.Round(_bpm * 1000.0);
            sig = sig * 31 + (long)Math.Round(_angle * 1000.0);
            sig = sig * 31 + (long)Math.Round(_beats * 1000.0);
            sig = sig * 31 + _tiles;
            sig = sig * 31 + (int)_lock;
            sig = sig * 31 + (_lockTiles ? 1 : 0);
            sig = sig * 31 + (_circle ? 1 : 0);
            sig = sig * 31 + _sides * 4096 + _laps;
            sig = sig * 31 + (_perfectCircle ? 1 : 0);
            sig = sig * 31 + (_pauseFill ? 1 : 0);
            sig = sig * 31 + (_writeSpeed ? 1 : 0);
            if (K.SyncWidth()) _layoutSig = NoSig;
            if (!K.Built || sig != _layoutSig) { _layoutSig = sig; Build(); }
            if (K.DockSide == 0 && Input.GetKeyDown(KeyCode.Escape) && !Typing(ed)) { Close(); return; }
            K.Show(true);
            K.TickScroll();
            TickPreview(ed);
        }

        /* Ghost tiles for the run this panel would place — same drawing as the angle pad's, so
           the two share GhostPreview. The Hz run is uniform: `_tiles` taps at `_angle`, no
           twirls. Re-walked only when one of those changes or the selection moves. */
        private const string GhostOwner = "hztool";
        private const int MaxGhosts = 400;
        private static long _ghostSig = long.MinValue;

        private static void TickPreview(scnEditor ed)
        {
            int anchorSeq = GhostPreview.AnchorSeq(ed);
            long sig = 17;
            sig = sig * 31 + anchorSeq;
            sig = sig * 31 + _tiles;
            sig = sig * 31 + (long)Math.Round(_angle * 1000.0);
            if (sig == _ghostSig && GhostPreview.OwnedBy(GhostOwner)) return;
            _ghostSig = sig;
            int n = Mathf.Clamp(_tiles, 0, MaxGhosts);
            if (anchorSeq < 0 || n <= 0 || _angle <= 0.0 || _tiles > MaxGhosts)
            { GhostPreview.Release(GhostOwner); return; }
            var walk = new GhostStep[n];
            for (int i = 0; i < n; i++) walk[i] = new GhostStep(_angle, false);
            GhostPreview.Show(GhostOwner, ed, anchorSeq, walk);
        }

        internal static void Dispose()
        {
            GhostPreview.Release(GhostOwner); _ghostSig = long.MinValue;
            K.Dispose();
            _statusTmp = null; _layoutSig = NoSig; _open = false;
        }

        private static bool Typing(scnEditor ed)
        { try { return ed.userIsEditingAnInputField; } catch { return false; } }

        /* "I clicked it and nothing happened" can't be diagnosed from a screenshot. When the
           panel is flagged open but a gate is hiding it, name the gate in the log — once per
           change of reason, never per frame. */
        private static string _gateLogged;

        private static void LogGate(scnEditor ed)
        {
            string why = ed == null ? "no editor instance"
                       : ed.playMode ? "play mode"
                       : !MainClass.EditorSuiteOn ? "master switch off"
                       : null;
            if (why == null || why == _gateLogged) return;
            _gateLogged = why;
            SapphireLog.Log("HzTool: open but hidden — " + why);
        }

        // ── the maths ────────────────────────────────────────────────────────

        private static double Hpm => _hz * 60.0;

        // The SetSpeed the run needs: how much faster than the section the shape has to run.
        private static double SpeedMult => _baseBpm > 0.0 ? _bpm / _baseBpm : 1.0;

        /* An edit to one of the pitch trio. The angle and the target Hz are inputs; the shape
           BPM is the answer. SolveLength then re-quantises the tile count (duration stays exact)
           and SolveShapeBpm recomputes the tempo from the frequency that will actually play. */
        private static void Solve(Var edited)
        {
            if (Absorber(edited) == Var.Angle && _hz > 0.0 && !_circle) _angle = 3.0 * _bpm / _hz;
            SolveLength();
        }

        /* Three variables, one equation: hold the lock, move the remaining one. Editing the very
           value you locked is taken as "I changed my mind" — the lock yields for that edit.

           With NOTHING locked the shape BPM is what moves, because it is the answer the tool
           exists to give: choose a note, choose a shape, read off the tempo. Only an edit to the
           BPM itself pushes the change into the angle instead. */
        private static Var Absorber(Var edited)
        {
            if (_lock != Var.None && _lock != edited)
            {
                if (edited != Var.Bpm && _lock != Var.Bpm) return Var.Bpm;
                if (edited != Var.Hz && _lock != Var.Hz) return Var.Hz;
                return Var.Angle;
            }
            return edited == Var.Bpm ? Var.Angle : Var.Bpm;
        }

        /* DURATION IS EXACT, PITCH BENDS.

           A run of N tiles at F Hz lasts N/F seconds whatever the geometry, so filling exactly
           `beats` of the section means `N / F = beats · 60 / baseBpm`. N is a whole number, so
           for a fixed duration only a QUANTISED ladder of frequencies is reachable:

               actual Hz = tiles · baseBpm / (60 · beats)

           `_hz` therefore stays the frequency you ASKED for; ActualHz is what the run will play,
           and the gap between them is reported in cents. Duration is never adjusted to make the
           pitch come out — that is the priority, and it is why the shape BPM below is computed
           from ActualHz rather than from the target. */
        private static double ActualHz => _pauseFill
            ? _hz                                        // exact by construction — see PauseBeats
            : (_beats > 0.0 && _baseBpm > 0.0 ? _tiles * _baseBpm / (60.0 * _beats) : _hz);

        /* PAUSE FILL — the third degree of freedom. Duration exact and pitch exact are only in
           conflict because the tile count is a whole number; a Pause event absorbs the remainder,
           so both can be exact at once (and, in circle mode, a closed loop as well).

           The run is deliberately made SHORTER than asked — tiles are floored, never rounded — so
           the leftover is positive and a pause can swallow it. It plays at the target frequency
           exactly, then holds for what is left of the requested duration. */
        private static double RunBeats => _pauseFill && _hz > 0.0
            ? _tiles * _baseBpm / (60.0 * _hz)
            : _beats;

        private static double PauseBeats => _pauseFill ? _beats - RunBeats : 0.0;

        // How far the reachable frequency sits from the one asked for. ±1 cent is inaudible;
        // a semitone is 100.
        private static double Cents => _hz > 0.0 && ActualHz > 0.0
            ? 1200.0 * Math.Log(ActualHz / _hz, 2.0)
            : 0.0;

        // The tile count that would hit the target frequency exactly — generally fractional,
        // which is the whole reason the pitch has to give.
        private static double IdealTiles => _baseBpm > 0.0 ? _beats * 60.0 * _hz / _baseBpm : 0.0;

        /* Length. `_beats` is a pure INPUT and is never written back to — that is what "duration
           always matches exactly" means. Only the tile count moves, and the pitch follows it. */
        private static void SolveLength()
        {
            if (_circle) { SolveCircle(); return; }
            if (_hz <= 0.0 || _baseBpm <= 0.0) return;
            // Floor in pause mode so the leftover is never negative; round otherwise, since
            // there the tile count IS the pitch and the nearest one wins.
            if (!_lockTiles)
                _tiles = Mathf.Clamp((int)(_pauseFill ? Math.Floor(IdealTiles) : Math.Round(IdealTiles)),
                                     1, MaxTiles);
            SolveShapeBpm();
        }

        /* The tempo the run has to play at, from the frequency it will ACTUALLY have. Locking the
           shape BPM inverts it: the angle gives way instead, for "this is all the speed I have".
           In circle mode the angle is pinned by the side count, so that lock cannot be honoured
           and the panel greys it. */
        private static void SolveShapeBpm()
        {
            double f = ActualHz;
            if (f <= 0.0) return;
            if (_lock == Var.Bpm && !_circle) { if (_bpm > 0.0) _angle = 3.0 * _bpm / f; }
            else _bpm = _angle * f / 3.0;
        }

        /* FULL CIRCLE. A tile of charter A turns the heading by 180−A, so N of them close a loop
           exactly when N·(180−A) = 360 — the run is a regular N-gon, and the angle is pinned at
           180 − 360/N. Laps re-trace the same polygon: 10 laps of a 16-gon is 160 tiles over ten
           circles, NOT one 160-sided circle (that would be a much wider angle). */
        private static void SolveCircle()
        {
            _sides = Mathf.Clamp(_sides, 3, MaxTiles);   // 2 sides is a line, not a circle
            _laps = Mathf.Clamp(_laps, 1, MaxTiles / 3);
            _angle = 180.0 - 360.0 / _sides;
            /* PERFECT CIRCLES: the loop has to CLOSE, so the tile count must be a whole number of
               laps — tiles = sides × laps. That is a coarse ladder, and the pitch pays for it.

               Switched off, the run keeps the same curvature but may stop partway round: the tile
               count is free to be the one nearest the ideal, so the pitch is as close as an
               integer count allows and the shape ends as an arc. Duration is exact either way —
               this option only chooses which of the other two gives. */
            if (_perfectCircle)
            {
                // With a pause to absorb the remainder, a whole number of laps that FITS is what
                // is wanted — so the lap count is floored into the duration rather than rounded
                // past it. All three (closed loop, exact pitch, exact duration) then hold.
                if (_pauseFill && _sides > 0)
                    _laps = Mathf.Clamp((int)Math.Floor(IdealTiles / _sides), 1, MaxTiles / 3);
                _tiles = Mathf.Clamp(_sides * _laps, 1, MaxTiles);
            }
            else
            {
                _tiles = Mathf.Clamp((int)(_pauseFill ? Math.Floor(IdealTiles) : Math.Round(IdealTiles)),
                                     1, MaxTiles);
            }
            SolveShapeBpm();
        }

        // How far round the polygon the run actually gets — whole laps when it closes.
        private static double ActualLaps => _sides > 0 ? _tiles / (double)_sides : 0.0;

        /* SUGGEST. Duration is fixed and the pitch is quantised by the tile count, so the pitch
           error depends ONLY on the product sides×laps — every factorisation of the same product
           sounds identical. So: take the product nearest the ideal tile count (that is the best
           pitch any closed loop can do for this duration), then split it into the factor pair
           whose side count is closest to the circle you already asked for, which keeps the shape
           as near your intent as the arithmetic allows. */
        private static void SuggestCircle()
        {
            int p = Mathf.Clamp((int)Math.Round(IdealTiles), 3, MaxTiles);
            int want = Mathf.Max(3, _sides);
            int bestSides = p, bestGap = Math.Abs(p - want);
            for (int d = 3; d <= p; d++)
            {
                if (p % d != 0) continue;
                int gap = Math.Abs(d - want);
                if (gap >= bestGap) continue;
                bestGap = gap; bestSides = d;
            }
            _sides = bestSides;
            _laps = Mathf.Max(1, p / bestSides);
            SolveCircle();
            SetStatus(Loc.T("suggested") + " " + _sides + "×" + _laps + "  ·  "
                      + ActualHz.ToString("0.###") + " Hz  ·  " + CentsLabel());
        }

        private static string CentsLabel()
        {
            double c = Cents;
            return (c >= 0 ? "+" : "") + c.ToString("0.#") + Loc.T(" cents");
        }

        private const int MaxTiles = 2000;

        // Effective BPM where the run will be built: the level's BPM scaled by the anchor floor's
        // speed multiplier, which is what SetSpeed events actually do to a tile.
        private static double SelectionBpm(scnEditor ed)
        {
            try
            {
                var ld = ed.levelData;
                if (ld == null || ld.bpm <= 0f) return 0.0;
                double b = ld.bpm;
                var sel = ed.selectedFloors;
                if (sel != null && sel.Count > 0 && sel[sel.Count - 1] != null)
                {
                    float sp = sel[sel.Count - 1].speed;
                    if (sp > 0f) b *= sp;
                }
                return b;
            }
            catch { return 0.0; }
        }

        /* Pitch of step `step` in octave `oct`, relative to the reference. Octaves are whole
           doublings whatever the EDO is, so the octave term stays outside the division:
           f = ref · 2^( (oct − refOct) + (step − refStep)/EDO ). At the defaults this is exactly
           440 · 2^((midi − 69)/12). */
        private static double Freq(int oct, int step)
            => _refHz * Math.Pow(2.0, (oct - _refOct) + (double)(step - _refStep) / _edo);

        private static readonly string[] NoteNames =
            { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        // Letter names only mean something in 12-EDO; every other division gets the standard
        // "steps\edo" degree notation instead of a letter that would be a lie.
        private static string NoteLabel(int oct, int step)
            => _edo == 12 && step >= 0 && step < 12
                ? NoteNames[step] + oct
                : step + "\\" + _edo + " o" + oct;

        // ── actions ──────────────────────────────────────────────────────────

        /* Place the run. PseudoBuild's anchor mode walks with the anchor's own spin, so the run
           follows the path's direction and an incoming twirl for free — that is what keeps a 30°
           tap reading back as 30 instead of 330.

           The SetSpeed matters because the angle was solved for _bpm: if the anchor is not
           already playing at _bpm the run is the wrong frequency however right the geometry is.
           One multiplier on the first new tile makes it true, and its inverse on the LAST tile
           hands the chart back at its original speed (the circular-path tool's KeepBPM
           convention). */
        private static void Place()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null || _angle <= 0.0 || _hz <= 0.0 || _bpm <= 0.0)
            { SetStatus(Loc.T("Set a BPM and a frequency first")); return; }
            if (!PseudoBuild.HasAnchor(ed)) { SetStatus(Loc.T("select a tile first")); return; }

            int n = Mathf.Clamp(_tiles, 1, MaxTiles);
            // Re-read the anchor's tempo at the moment of placing: the panel may have been open
            // since before the selection moved to a tile in a different-speed section.
            double b = SelectionBpm(ed);
            if (b > 0.0) _baseBpm = b;
            double mult = SpeedMult;
            int firstSeq = -1;
            try { firstSeq = ed.selectedFloors[ed.selectedFloors.Count - 1].seqID + 1; } catch { }

            var unit = new PseudoStep[n];
            for (int i = 0; i < n; i++) unit[i] = new PseudoStep(_angle, StepKind.Tap, false);

            int placed;
            bool wroteSpeed = false;
            using (new SaveStateScope(ed))
            {
                placed = PseudoBuild.Build(ed, unit, new PseudoContext { Fixed = false, RepeatN = 1 });
                if (placed > 0 && _writeSpeed && firstSeq >= 1 && Math.Abs(mult - 1.0) > 1e-4)
                {
                    /* A floor's speed governs the traversal FROM that floor to the next, not the
                       floor's own arrival — so the tempo for the run's FIRST hit is carried by
                       the ANCHOR, one before the first new tile. Writing it on the first new tile
                       instead lands every event a tile late. (This is exactly why the circular
                       path tool writes its KeepBPM pair on the selected tile and the run's last
                       tile; it was right and this copied it wrong.)

                       By the same rule the restore belongs on the run's LAST tile: that floor's
                       speed covers the exit into whatever follows, while all n hits of the run
                       stay on the fast side of it. Skipped when the run ends the track. */
                    EditorToolbar.AddSetSpeed(ed, firstSeq - 1, mult);
                    int lastSeq = firstSeq + placed - 1;
                    bool hasAfter = false;
                    try { hasAfter = ed.floors != null && lastSeq + 1 < ed.floors.Count; } catch { }
                    if (hasAfter) EditorToolbar.AddSetSpeed(ed, lastSeq, 1.0 / mult);
                    wroteSpeed = true;
                }
                /* The pause that makes the duration exact. It rides the run's LAST tile, and the
                   restore SetSpeed is forced onto that same tile even at the end of the track —
                   otherwise the hold would be counted in the run's fast beats instead of the
                   section's, and the run would finish early by exactly the speed multiplier. */
                if (placed > 0 && _pauseFill && PauseBeats > 1e-4)
                {
                    int lastSeq = firstSeq + placed - 1;
                    if (wroteSpeed && !RestoredAt(ed, lastSeq)) EditorToolbar.AddSetSpeed(ed, lastSeq, 1.0 / mult);
                    EditorQuickChart.AddPause(ed, lastSeq, PauseBeats);
                }
                if (placed > 0) { try { ed.ApplyEventsToFloors(); ed.RemakePath(true, true); } catch { } }
            }
            GhostPreview.Release(GhostOwner); _ghostSig = long.MinValue;   // the run is real now
            if (placed <= 0) { SetStatus(Loc.T("select a tile / open a level")); return; }
            string msg = placed + Loc.T(" tile") + (placed == 1 ? "" : "s") + Loc.T(" placed");
            if (_writeSpeed)
                msg += Math.Abs(mult - 1.0) > 1e-4
                    ? "  ·  ×" + mult.ToString("0.###") + Loc.T(" speed")
                    : "  ·  " + Loc.T("already at this BPM");
            if (_pauseFill && PauseBeats > 1e-4)
                msg += "  ·  " + Loc.T("pause") + " " + PauseBeats.ToString("0.###");
            SetStatus(msg);
        }



        // Hand the run to an angle pad instead of placing it — the pad is where a user tweaks a
        // run (twirls, a tail, a group repeat) before committing it.
        private static void ToPad()
        {
            if (_angle <= 0.0) { SetStatus(Loc.T("Set a BPM and a frequency first")); return; }
            int n = Mathf.Clamp(_tiles, 1, MaxTiles);
            EditorQuickChart.OpenPadWith("(" + _angle.ToString("0.####") + ")*" + n);
            SetStatus(Loc.T("sent to the angle pad"));
        }

        // Did the restore SetSpeed already land on this floor? (It does whenever a tile follows
        // the run; the pause path needs it there even when one doesn't.)
        private static bool RestoredAt(scnEditor ed, int seq)
        {
            try
            {
                foreach (var ev in ed.events)
                    if (ev != null && ev.floor == seq && ev.eventType == ADOFAI.LevelEventType.SetSpeed) return true;
            }
            catch { }
            return false;
        }

        private static void SetStatus(string s)
        { _status = s ?? ""; if (_statusTmp != null) _statusTmp.text = _status; }

        // ── UI ───────────────────────────────────────────────────────────────

        // DefaultH is only the height of the FIRST frame; FitHeight sizes it to the real content
        // as soon as the rows are laid out.
        private const float PanelW = 306f, MinW = 260f, MinH = 200f, DefaultH = 430f;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;
        private const float WhiteW = 20f, WhiteH = 62f, BlackH = 38f;

        /* One runnable check for the solver, run once when the panel first opens (the shape
           library does the same with PseudoBuild.SelfCheck). It drives the REAL statics and
           restores them, rather than re-deriving the formulas — a test that repeats the
           expression it is testing proves nothing. */
        private static bool _selfChecked;

        private static bool SelfCheck()
        {
            double bpm = _bpm, hz = _hz, ang = _angle, beats = _beats, bb = _baseBpm;
            int tiles = _tiles, sides = _sides, laps = _laps;
            bool lt = _lockTiles, circ = _circle, auto = _baseAuto, perf = _perfectCircle;
            bool pf = _pauseFill;
            Var lk = _lock;
            string st = _status;

            _baseBpm = 175.0; _hz = 20.0; _beats = 1.0; _lock = Var.None; _lockTiles = false;
            _circle = true; _laps = 1; _sides = 16;
            SolveCircle();
            // Geometry closes, and the tempo is derived from the frequency that will PLAY.
            bool ok = Mathf.Abs((float)(_sides * (180.0 - _angle)) - 360f) < 0.01f
                   && Mathf.Abs((float)(_angle * ActualHz / 3.0 - _bpm)) < 0.001f;
            // DURATION IS EXACT: N tiles at the frequency that plays occupy the beats asked for.
            ok &= Mathf.Abs((float)(_tiles / ActualHz - _beats * 60.0 / _baseBpm)) < 1e-4;
            // …and it stays exact after Suggest, which may only move sides/laps and the pitch.
            SuggestCircle();
            ok &= Mathf.Abs((float)_beats - 1f) < 1e-6
               && Mathf.Abs((float)(_tiles / ActualHz - _beats * 60.0 / _baseBpm)) < 1e-4
               && _tiles == _sides * _laps
               && _sides >= 3;
            // Suggest must beat the 16×1 it started from: 6.86 ideal tiles is nowhere near 16.
            ok &= Math.Abs(Cents) < 60.0;
            // A longer run buys accuracy, never duration error.
            _beats = 4.0; _sides = 16; _laps = 1;
            SuggestCircle();
            ok &= Mathf.Abs((float)_beats - 4f) < 1e-6
               && Math.Abs(Cents) < 40.0
               && Mathf.Abs((float)(_tiles / ActualHz - _beats * 60.0 / _baseBpm)) < 1e-4;

            /* Dropping the closed-loop constraint must buy pitch, keep the duration, and keep
               the curvature. 16 sides × 1 lap over 4 beats is 16 tiles and nearly an octave flat;
               letting the arc stop at the ideal 27 lands within 30 cents of the target. */
            _beats = 4.0; _sides = 16; _laps = 1; _perfectCircle = true;
            SolveCircle();
            double closedCents = Math.Abs(Cents);
            double closedAngle = _angle;
            _perfectCircle = false;
            SolveCircle();
            ok &= Math.Abs(Cents) < closedCents                                  // more accurate
               && Math.Abs(Cents) < 30.0
               && Mathf.Abs((float)(_angle - closedAngle)) < 1e-6                // same curvature
               && Mathf.Abs((float)_beats - 4f) < 1e-6                           // duration held
               && Mathf.Abs((float)(_tiles / ActualHz - _beats * 60.0 / _baseBpm)) < 1e-4;
            _perfectCircle = true;

            /* PAUSE FILL makes all three exact at once: a closed loop, the target pitch to the
               cent, and the requested duration — the pause swallows what the whole tile count
               cannot express. 175 BPM, 20 Hz, 4 beats, 16 sides: one closed lap is 16 tiles,
               which at a true 20 Hz runs 2.333 beats, and the pause is the remaining 1.667. */
            _beats = 4.0; _sides = 16; _laps = 1; _perfectCircle = true; _pauseFill = true;
            SolveCircle();
            ok &= Math.Abs(Cents) < 1e-6                                    // pitch EXACT
               && _tiles % _sides == 0                                      // loop still closes
               && PauseBeats > -1e-6                                        // never negative
               && Mathf.Abs((float)(RunBeats + PauseBeats - _beats)) < 1e-4 // duration EXACT
               && Mathf.Abs((float)(_tiles / ActualHz - RunBeats * 60.0 / _baseBpm)) < 1e-4;
            _pauseFill = false;

            _bpm = bpm; _hz = hz; _angle = ang; _beats = beats; _baseBpm = bb; _baseAuto = auto;
            _tiles = tiles; _sides = sides; _laps = laps; _lockTiles = lt; _circle = circ;
            _perfectCircle = perf; _pauseFill = pf; _lock = lk; _status = st;
            SapphireLog.Log("EditorHzTool.SelfCheck: " + (ok ? "PASS" : "FAIL"));
            return ok;
        }

        private static void Build()
        {
            if (!_selfChecked) { _selfChecked = true; SelfCheck(); }
            K.LblW = 104f;
            K.Scrollable = true;
            K.DefaultH = DefaultH;
            // PanelKit panels anchor TOP-LEFT with a top-left pivot, so x is measured rightward
            // from the screen's left edge — a negative x parks the window off-screen. (Shape
            // library uses 700, Magic Shape 340; this sits between them.)
            bool first = !K.Built;
            K.Rebuild(Loc.T("Hz tool"), Close, new Vector2(420f, -150f));
            if (first) SapphireLog.Log("HzTool: panel built at "
                + ((RectTransform)K.PanelGo.transform).anchoredPosition + " size "
                + ((RectTransform)K.PanelGo.transform).sizeDelta);
            ResizeHandle.AttachAll((RectTransform)K.PanelGo.transform, true, MinW, MinH);

            float y = -34f;
            float fullW = K.W - Pad * 2f;

            // Pitch triangle. Each row carries a padlock: the locked value is held while the
            // others move, so "keep this Hz, tell me the angle" and "keep this angle, tell me
            // the Hz" are the same three rows rather than two modes.
            // What you WANT: the note, and the shape's angle.
            y = LockRow(y, Loc.T("Frequency (Hz)"), (float)_hz, Var.Hz,
                v => { if (v > 0f) { _hz = v; Solve(Var.Hz); ClearNote(); } });
            y = K.FloatRow(y, Loc.T("Hits / minute"), (float)Hpm,
                v => { if (v > 0f) { _hz = v / 60.0; Solve(Var.Hz); ClearNote(); } });
            // What you'll ACTUALLY get: the tile count is a whole number, so for an exact
            // duration only a ladder of frequencies is reachable. Shown next to the target with
            // the error in cents, muted while it is inaudible.
            K.Label(Loc.T("Actual Hz"), Pad, y, K.LblW, RowH, Theme.TextMuted);
            K.Label(ActualHz.ToString("0.###") + "   " + CentsLabel(),
                    Pad + K.LblW + 4f, y, fullW - K.LblW - 4f, RowH,
                    Math.Abs(Cents) > 5.0 ? Theme.DangerText : Theme.TextMuted, 11.5f);
            y -= RowH + Gap;

            y = LockRow(y, Loc.T("Tile angle"), (float)_angle, Var.Angle,
                v => { if (v > 0f) { _angle = v; Solve(Var.Angle); ClearNote(); } });

            // What that COSTS: the tempo the run has to play at. Editable (with a padlock) for
            // the "I only have this much speed" case, but normally it is the tool's answer.
            y = LockRow(y, Loc.T("Shape BPM"), (float)_bpm, Var.Bpm,
                v => { if (v > 0f) { _bpm = v; Solve(Var.Bpm); ClearNote(); } });

            // What the chart is at now, and therefore the SetSpeed between the two.
            // Padlock = pinned: stop mirroring the selection. Same control as the pitch rows so
            // "which of these is held" reads the same way everywhere; typing a value pins it too,
            // since a typed base BPM that the next selection change wiped would be a trap.
            y = PinnedRow(y, Loc.T("Base BPM"), (float)_baseBpm, !_baseAuto,
                pin => { _baseAuto = !pin; _layoutSig = NoSig; },
                v => { if (v > 0f) { _baseBpm = v; _baseAuto = false; SolveLength(); _layoutSig = NoSig; } });
            K.Label(Loc.T("SetSpeed"), Pad, y, K.LblW, RowH, Theme.TextMuted);
            K.Label("×" + SpeedMult.ToString("0.####"), Pad + K.LblW + 4f, y, fullW - K.LblW - 4f, RowH,
                    Math.Abs(SpeedMult - 1.0) > 1e-4 ? Theme.Text : Theme.TextMuted);
            y -= RowH + Gap;

            K.Footer(Loc.T("duration is exact; the tile count is whole, so the pitch snaps"), y);
            y -= 14f + Gap;

            // ── note picker ──
            y -= 4f;
            K.Label(Loc.T("Note"), Pad, y, 46f, RowH, Theme.TextMuted);
            K.Cell("−", Pad + 48f, y, 22f, RowH, () => { _octave = Mathf.Max(-1, _octave - 1); _layoutSig = NoSig; }, true);
            K.Label("o" + _octave + (Octaves > 1 ? "–" + (_octave + 1) : ""), Pad + 73f, y, 52f, RowH, Theme.Text, 11.5f);
            K.Cell("+", Pad + 127f, y, 22f, RowH, () => { _octave = Mathf.Min(9, _octave + 1); _layoutSig = NoSig; }, true);
            // Tuning is a submenu, not five more rows: almost nobody changes it, and the picker
            // has to stay the thing you see when the panel opens.
            K.Cell(Loc.T("Tuning") + (_tuningOpen ? " −" : " +"), Pad + 153f, y, fullW - 153f, RowH,
                   () => { _tuningOpen = !_tuningOpen; _layoutSig = NoSig; }, true, accent: _tuningOpen);
            y -= RowH + Gap;

            if (_tuningOpen)
            {
                y = K.IntRow(y, Loc.T("EDO (steps / octave)"), _edo,
                    v => { _edo = Mathf.Clamp(v, 1, 72); ClampTuning(); ClearNote(); });
                y = K.FloatRow(y, Loc.T("Reference pitch (Hz)"), (float)_refHz,
                    v => { if (v > 0f) { _refHz = v; ClearNote(); } });
                y = K.IntRow(y, Loc.T("Reference octave"), _refOct,
                    v => { _refOct = Mathf.Clamp(v, -1, 9); ClearNote(); });
                y = K.IntRow(y, Loc.T("Reference step"), _refStep,
                    v => { _refStep = Mathf.Clamp(v, 0, _edo - 1); ClearNote(); });
                K.Cell(Loc.T("Reset tuning"), Pad, y, fullW, RowH,
                    () => { _edo = 12; _refHz = 440.0; _refOct = 4; _refStep = 9; ClearNote(); }, true);
                y -= RowH + Gap;
                K.Footer(Loc.T("12-EDO · step 9 · octave 4 · 440 Hz = concert A"), y);
                y -= 14f + Gap;
            }

            if (_selStep >= 0)
            {
                K.Label(NoteLabel(_selOct, _selStep) + "   " + Freq(_selOct, _selStep).ToString("0.##") + " Hz",
                        Pad, y, fullW, RowH, Theme.Text, 11.5f);
                y -= RowH + Gap;
            }
            y = Keyboard(y, fullW);

            y -= Gap;
            /* Length. Ask for the run in BEATS — that is how a chart is written — and derive the
               tile count from the angle. Locking Tiles instead makes it the input and beats the
               readout, for when a count is what you actually have. */
            /* Switching it on drives from SIDES, not from the current angle. Deriving N from a
               typical charting frequency lands on 3 or 4 — a fast buzz means SHORT tiles, and
               short tiles that each turn a lot close the loop almost immediately. Asking "how
               many sides" and reporting the BPM/Hz that needs is the useful direction, and the
               SetSpeed option can then actually write that BPM. */
            y = K.ToggleRow(y, Loc.T("Full circle"), _circle,
                v =>
                {
                    _circle = v;
                    if (v && _sides < 3) _sides = 16;
                    SolveLength();
                    _layoutSig = NoSig;
                });
            y = K.ToggleRow(y, Loc.T("Exact pitch (pad with a pause)"), _pauseFill,
                v => { _pauseFill = v; SolveLength(); _layoutSig = NoSig; });
            if (_pauseFill)
            {
                K.Label(Loc.T("Pause"), Pad, y, K.LblW, RowH, Theme.TextMuted);
                K.Label(PauseBeats.ToString("0.###") + " " + Loc.T("beats") + "   ("
                        + Loc.T("run") + " " + RunBeats.ToString("0.###") + ")",
                        Pad + K.LblW + 4f, y, fullW - K.LblW - 4f, RowH,
                        PauseBeats < -1e-4 ? Theme.DangerText : Theme.Text, 11.5f);
                y -= RowH + Gap;
            }

            /* Duration is always editable, in beats of the BASE BPM. In circle mode it cannot
               change the tile count freely — the polygon's side count is fixed — so it picks the
               nearest whole number of LAPS that fills the time asked for.

               The padlock only appears OUTSIDE circle mode: there, beats and tiles are two views
               of one number and something has to say which is the input. In circle mode sides and
               laps are always the inputs and the rest is arithmetic, so a lock would be a control
               that does nothing. */
            if (_circle)
            {
                y = K.FloatRow(y, Loc.T("Duration (beats)"), (float)_beats, v =>
                {
                    if (v <= 0f) return;
                    _beats = v; SolveLength(); _layoutSig = NoSig;
                });
                y = K.ToggleRow(y, Loc.T("Perfect circles"), _perfectCircle,
                    v => { _perfectCircle = v; SolveLength(); _layoutSig = NoSig; });
                y = K.IntRow(y, Loc.T("Sides"), _sides,
                    v => { _sides = Mathf.Clamp(v, 3, MaxTiles); SolveLength(); _layoutSig = NoSig; });
                if (_perfectCircle)
                {
                    y = K.IntRow(y, Loc.T("Laps"), _laps,
                        v => { _laps = Mathf.Clamp(v, 1, MaxTiles / 3); SolveLength(); _layoutSig = NoSig; });
                }
                else
                {
                    // Fractional: the run stops wherever the tile count lands.
                    K.Label(Loc.T("Laps"), Pad, y, K.LblW, RowH, Theme.TextMuted);
                    K.Label(ActualLaps.ToString("0.###"), Pad + K.LblW + 4f, y,
                            fullW - K.LblW - 4f, RowH, Theme.Text, 11.5f);
                    y -= RowH + Gap;
                }
                K.Label(Loc.T("Tiles"), Pad, y, K.LblW, RowH, Theme.TextMuted);
                K.Label(_tiles + "   (" + Loc.T("ideal") + " " + IdealTiles.ToString("0.##") + ")",
                        Pad + K.LblW + 4f, y, fullW - K.LblW - 4f, RowH, Theme.Text, 11.5f);
                y -= RowH + Gap;
                // Only a closed loop has anything to choose: with the constraint off the tile
                // count is already the pitch-optimal one and there is nothing left to suggest.
                if (_perfectCircle)
                {
                    K.Cell(Loc.T("Suggest sides & laps"), Pad, y, fullW, RowH, SuggestCircle, true);
                    y -= RowH + Gap;
                }
                K.Footer(Loc.T(_perfectCircle
                    ? "a closed loop needs angle = 180 − 360 ÷ sides, so the Hz snaps"
                    : "the arc stops where the pitch is closest; only the curvature is kept"), y);
                y -= 14f + Gap;
            }
            else
            {
                y = K.FloatRow(y, Loc.T("Duration (beats)"), (float)_beats,
                    v => { if (v > 0f) { _beats = v; SolveLength(); _layoutSig = NoSig; } });
                y = LenRow(y, Loc.T("Tiles"), _tiles, true,
                    v => { _tiles = Mathf.Clamp((int)Math.Round(v), 1, MaxTiles); _lockTiles = true; SolveShapeBpm(); _layoutSig = NoSig; });
            }

            y = K.ToggleRow(y, Loc.T("Write SetSpeed for this BPM"), _writeSpeed,
                v => { _writeSpeed = v; _layoutSig = NoSig; });

            float bw = (fullW - Gap) * 0.5f;
            K.Cell(Loc.T("Place"), Pad, y, bw, RowH + 4f, Place, true, accent: true);
            K.Cell(Loc.T("To angle pad"), Pad + bw + Gap, y, bw, RowH + 4f, ToPad, true);
            y -= RowH + 4f + Gap;

            _statusTmp = K.Status(_status, y);
            y -= 30f;
            K.SetHeight(y);
            FitHeight(-y + Pad);
        }

        /* Grow the window to its content so nothing is hidden behind a scroll on first open —
           the panel has enough rows now that a fixed DefaultH always cut something off. Only
           while the height is still the one WE set: the moment a user drags the panel to their
           own size, that becomes theirs and refitting would fight the drag. Capped to the canvas
           so a tall content (tuning open) can still scroll rather than run off-screen. */
        private static float _autoH;

        private static void FitHeight(float needed)
        {
            if (K.PanelGo == null) return;
            var rt = (RectTransform)K.PanelGo.transform;
            if (_autoH > 0f && Mathf.Abs(rt.sizeDelta.y - _autoH) > 1f) return;   // user resized
            float cap = 900f;
            try { var c = K.CanvasGo != null ? K.CanvasGo.GetComponent<RectTransform>() : null;
                  if (c != null && c.rect.height > 200f) cap = c.rect.height - 90f; } catch { }
            float h = Mathf.Clamp(needed, MinH, cap);
            if (Mathf.Abs(rt.sizeDelta.y - h) < 0.5f) { _autoH = h; return; }
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
            _autoH = h;
        }

        /* A value row with a padlock cell on its right. The lock is exclusive across the three
           pitch values — clicking a lit one clears it — and a locked row is tinted so the held
           value is visible at a glance rather than remembered. */
        private const float LockW = 26f;

        private static float LockRow(float y, string label, float value, Var id, Action<float> set)
        {
            bool on = _lock == id;
            K.Label(label, Pad, y, K.LblW, RowH, on ? Theme.Text : Theme.TextMuted);
            float fw = K.W - Pad * 2f - K.LblW - 4f - LockW - Gap;
            K.InputField(Pad + K.LblW + 4f, y, fw, value.ToString("0.###"),
                         v => { float f; if (ExprEval.TryParseFloat(v, out f)) set(f); });
            K.Cell(on ? "L" : "·", Pad + K.LblW + 4f + fw + Gap, y, LockW, RowH,
                   () => { _lock = on ? Var.None : id; _layoutSig = NoSig; }, true, accent: on);
            return y - (RowH + Gap);
        }

        // A value row whose padlock is a plain bool rather than a member of the pitch trio.
        private static float PinnedRow(float y, string label, float value, bool pinned,
                                       Action<bool> setPin, Action<float> set)
        {
            K.Label(label, Pad, y, K.LblW, RowH, pinned ? Theme.Text : Theme.TextMuted);
            float fw = K.W - Pad * 2f - K.LblW - 4f - LockW - Gap;
            K.InputField(Pad + K.LblW + 4f, y, fw, value.ToString("0.###"),
                         v => { float f; if (ExprEval.TryParseFloat(v, out f)) set(f); });
            K.Cell(pinned ? "L" : "·", Pad + K.LblW + 4f + fw + Gap, y, LockW, RowH,
                   () => setPin(!pinned), true, accent: pinned);
            return y - (RowH + Gap);
        }

        // Same shape for the beats/tiles pair, whose lock is a single bool.
        private static float LenRow(float y, string label, float value, bool isTiles, Action<float> set)
        {
            bool on = _lockTiles == isTiles;
            K.Label(label, Pad, y, K.LblW, RowH, on ? Theme.Text : Theme.TextMuted);
            float fw = K.W - Pad * 2f - K.LblW - 4f - LockW - Gap;
            K.InputField(Pad + K.LblW + 4f, y, fw, isTiles ? value.ToString("0") : value.ToString("0.###"),
                         v => { float f; if (ExprEval.TryParseFloat(v, out f)) set(f); });
            K.Cell(on ? "L" : "·", Pad + K.LblW + 4f + fw + Gap, y, LockW, RowH,
                   () => { _lockTiles = isTiles; SolveLength(); _layoutSig = NoSig; }, true, accent: on);
            return y - (RowH + Gap);
        }

        /* The picker. In 12-EDO it is a real piano: white keys laid out left to right, the black
           keys created AFTERWARDS so uGUI's sibling order draws them on top, each centred on the
           seam between its two whites. In any other EDO those shapes mean nothing, so it becomes
           a flat strip of EDO equal keys with the octave's first step tinted as the landmark.
           Wide divisions get one octave instead of two so the keys stay clickable. */
        private static readonly int[] WhiteSemis = { 0, 2, 4, 5, 7, 9, 11 };
        private static readonly int[] BlackSemis = { 1, 3, 6, 8, 10 };
        // white-key index each black key sits after (C#→after C, D#→after D, F#→after F, …)
        private static readonly int[] BlackAfterWhite = { 0, 1, 3, 4, 5 };

        private static int Octaves => _edo <= 24 ? 2 : 1;

        private static float Keyboard(float y, float fullW)
            => _edo == 12 ? PianoKeys(y, fullW) : DegreeKeys(y, fullW);

        private static float PianoKeys(float y, float fullW)
        {
            float w = Mathf.Min(WhiteW, (fullW - 2f) / 14f);   // 14 whites over two octaves
            float x0 = Pad;

            for (int oct = 0; oct < 2; oct++)
                for (int i = 0; i < WhiteSemis.Length; i++)
                {
                    int o = _octave + oct, st = WhiteSemis[i];
                    float x = x0 + (oct * 7 + i) * w;
                    bool on = _selStep == st && _selOct == o;
                    var bg = K.Cell("", x, y, w - 1f, WhiteH, () => PickNote(o, st), true, accent: on);
                    if (!on) bg.color = new Color(0.86f, 0.86f, 0.89f, 0.92f);
                    // Label only the Cs, so the octave reads without crowding the keys.
                    if (st == 0)
                        K.Label("C" + o, x + 2f, y - WhiteH + 16f, w, 14f,
                                new Color(0.15f, 0.15f, 0.18f, 1f), 9f);
                }

            for (int oct = 0; oct < 2; oct++)
                for (int i = 0; i < BlackSemis.Length; i++)
                {
                    int o = _octave + oct, st = BlackSemis[i];
                    float bw = w * 0.62f;
                    float x = x0 + (oct * 7 + BlackAfterWhite[i] + 1) * w - bw * 0.5f;
                    bool on = _selStep == st && _selOct == o;
                    var bg = K.Cell("", x, y, bw, BlackH, () => PickNote(o, st), true, accent: on);
                    if (!on) bg.color = new Color(0.08f, 0.08f, 0.10f, 0.98f);
                }

            return y - WhiteH - Gap;
        }

        private static float DegreeKeys(float y, float fullW)
        {
            int octs = Octaves;
            int total = _edo * octs;
            float w = (fullW - 1f) / Mathf.Max(1, total);
            bool label = w >= 13f;                            // below that a number is unreadable
            for (int i = 0; i < total; i++)
            {
                int o = _octave + i / _edo, st = i % _edo;
                bool on = _selStep == st && _selOct == o;
                var bg = K.Cell(label ? st.ToString() : "", Pad + i * w, y, w - 1f, WhiteH,
                                () => PickNote(o, st), true, accent: on);
                if (!on)
                    bg.color = st == 0
                        ? new Color(0.86f, 0.86f, 0.89f, 0.92f)   // octave landmark
                        : new Color(0.30f, 0.30f, 0.35f, 0.92f);
            }
            return y - WhiteH - Gap;
        }

        private static void PickNote(int oct, int step)
        {
            _selOct = oct; _selStep = step;
            _hz = Freq(oct, step);
            Solve(Var.Hz);
            _layoutSig = NoSig;
            SetStatus(NoteLabel(oct, step) + " — " + _angle.ToString("0.###") + "\u00b0");
        }

        private static void ClearNote() { _selStep = -1; _layoutSig = NoSig; }

        // A shrunken EDO can strand the reference on a step that no longer exists.
        private static void ClampTuning() { _refStep = Mathf.Clamp(_refStep, 0, _edo - 1); }

    }
}
