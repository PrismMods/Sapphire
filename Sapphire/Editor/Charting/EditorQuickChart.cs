using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Quick-chart mode: fast in-place charting without arming a tool. Defaults (all rebindable
       from the settings panel — Editor ▸ Keybinds):
         • I        toggle a swirl (Twirl) on the selected tile(s)
         • O        prompt for a speed on the selected tile; [ / ] halve / double it
         • Shift+P  prompt for beats → place a Pause event on the selected tile
         • Shift+L  prompt for X/Y  → place a PositionTrack event on the selected tile
         • Shift+G  open an "angle pad": a floating field that appends a whole run of tiles from
                    space-separated angle values (math allowed: 180-30, 360/8). A trailing 't'
                    twirls that tile (30t 30t 180); a parenthesised group repeats with '*'
                    ((30 30 180)*3). Duplicable, each holding its own value as a temp preset.

       The defaults dodge the editor's tile-placement keys (which are registered under BOTH
       KeyModifier.None and Shift, matched by exact modifier equality) — see Keybinds for the
       verified key list. Everything lives on one own canvas, torn down with the suite. */
    internal static class EditorQuickChart
    {
        // Every hotkey below is rebindable — see Keybinds (which also carries the tile-key
        // collision rule these defaults were picked against).

        private static GameObject _canvasGo;
        private static RectTransform _root;
        private static readonly List<Pad> _pads = new List<Pad>();
        private static GameObject _promptGo;
        private static TMP_InputField[] _promptFields; // for Tab cycling
        private static bool _speedIsBpm;               // remembered Set-speed mode
        private static int _spawnSeq;   // cascades duplicate windows so they don't stack exactly

        // ── tick ─────────────────────────────────────────────────────────────
        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            bool on = false;
            try
            {
                ed = scnEditor.instance;
                on = ed != null && !ed.playMode && s != null && MainClass.EditorSuiteOn && s.FeatQuickChart;
            }
            catch { }

            if (!on)
            {
                if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
                ClearGhosts();
                return;
            }
            if (_canvasGo != null && !_canvasGo.activeSelf) _canvasGo.SetActive(true);

            /* Build the canvas BEFORE the auto-spawn so its first layout pass has happened by
               the time a pad asks where the screen edges are; on the frame a canvas is created
               its rect is still 0, and the pad would be placed against nothing. Costs one frame
               of delay the first time the mode is switched on. */
            EnsureCanvas();
            ClampPads();
            TickSteppers();
            TickPreview(ed);
            /* The pad is the mode's main surface, so the mode is never on with nothing to type
               into. Spawned WITHOUT focus — an auto-appearing field that steals the keyboard
               would eat the editor's own tile keys the moment quick chart turns on. */
            // No longer waits for a laid-out canvas: the default position is corner-relative, so
            // there is no rect to read and nothing to get wrong on the first frame.
            if (_pads.Count == 0) SpawnPad(null, "", focus: false);

            if (!Input.anyKeyDown) return; // hotkeys below are all GetKeyDown
            // Prompt-local keys (handled BEFORE the typing gate, since a prompt field is focused).
            if (_picking)
            {
                if (Input.GetKeyDown(KeyCode.Escape)) { EndPick(); return; }
                for (int i = 0; i < _pads.Count && i < 9; i++)
                    if (Input.GetKeyDown(KeyCode.Alpha1 + i) || Input.GetKeyDown(KeyCode.Keypad1 + i))
                    { var target = _pads[i]; EndPick(); DoPlace(target); return; }
                return;   // swallow everything else while the pick is armed
            }
            if (_promptGo != null && Input.GetKeyDown(KeyCode.Escape)) { ClosePrompt(); return; }
            if (_promptGo != null && Input.GetKeyDown(KeyCode.Tab)) { CyclePromptFocus(); return; }
            if (Typing(ed)) return;

            /* Enter places from the pads without one being focused — DoPlace hands focus back to
               the tile, so "nothing focused" is the normal state between runs. A focused field
               never reaches here (Typing above), so its own onSubmit still handles Enter. */
            if (_pads.Count > 0 && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)))
            {
                if (_pads.Count == 1) DoPlace(_pads[0]);
                else BeginPick();
                return;
            }

            // Ctrl/Cmd/Alt belong to game chords — never quick-chart.
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;
            // Keybinds.Down matches the Shift state as part of the bind, so bare keys and Shift
            // combos live in ONE chain — a user who moves the angle pad off Shift+G onto a bare
            // key needs no branch of its own.
            if (Keybinds.Down(Bind.QcSwirl)) QuickSwirl(ed);
            else if (Keybinds.Down(Bind.QcSetSpeed)) OpenSpeedPrompt(ed);
            // [ / ] halve/double the selected tile's SetSpeed (only when it has one — else the
            // game's event-page nav still works; a Harmony guard suppresses nav when we act).
            else if (Keybinds.Down(Bind.QcSpeedDown)) AdjustSelectedSpeed(ed, 0.5);
            else if (Keybinds.Down(Bind.QcSpeedUp)) AdjustSelectedSpeed(ed, 2.0);
            else if (Keybinds.Down(Bind.QcPause)) OpenPausePrompt(ed);
            else if (Keybinds.Down(Bind.QcLocate)) OpenLocatePrompt(ed);
            else if (Keybinds.Down(Bind.QcAnglePad)) SpawnPad(null, "");
        }

        // A game OR Sapphire input field is focused (the pad/prompt fields don't set the game's
        // flag, so also check the EventSystem selection). Same guard the toolbar uses.
        private static bool Typing(scnEditor ed)
        {
            try
            {
                if (ed.userIsEditingAnInputField) return true;
                var es = EventSystem.current;
                var sel = es != null ? es.currentSelectedGameObject : null;
                return sel != null && (sel.GetComponent<TMP_InputField>() != null
                                    || sel.GetComponent<InputField>() != null);
            }
            catch { return false; }
        }

        // ── quick swirl ──────────────────────────────────────────────────────
        // Toggle a Twirl on the selected tile(s). Direction is set by the last selected tile:
        // if it has a swirl, clear the swirl from all selected; else add one to each that lacks it.
        private static void QuickSwirl(scnEditor ed)
        {
            List<scrFloor> sel = null;
            try { sel = ed.selectedFloors; } catch { }
            if (sel == null || sel.Count == 0) { Notify(ed, "Quick swirl: select a tile"); return; }

            scrFloor primary = sel[sel.Count - 1];
            bool primaryHas = FindEvent(ed, primary.seqID, ADOFAI.LevelEventType.Twirl) != null;
            int changed = 0;
            using (new SaveStateScope(ed))
            {
                foreach (var fl in sel)
                {
                    if (fl == null) continue;
                    var existing = FindEvent(ed, fl.seqID, ADOFAI.LevelEventType.Twirl);
                    if (primaryHas) { if (existing != null) { ed.events.Remove(existing); changed++; } }
                    else if (existing == null) { ed.events.Add(new ADOFAI.LevelEvent(fl.seqID, ADOFAI.LevelEventType.Twirl)); changed++; }
                }
                if (changed > 0)
                {
                    try { ed.ApplyEventsToFloors(); } catch { }
                    try { ed.RemakePath(true, true); } catch { }
                }
            }
            if (changed > 0) Notify(ed, primaryHas ? "Swirl removed" : "Swirl added");
        }

        private static ADOFAI.LevelEvent FindEvent(scnEditor ed, int seq, ADOFAI.LevelEventType type)
        {
            try
            {
                var evs = ed.events;
                for (int i = 0; i < evs.Count; i++)
                {
                    var e = evs[i];
                    if (e != null && e.floor == seq && e.eventType == type) return e;
                }
            }
            catch { }
            return null;
        }

        // ── pause / position prompts ───────────────────────────────────────────
        private static void OpenPausePrompt(scnEditor ed)
        {
            int seq = SelectedSeq(ed);
            if (seq < 0) { Notify(ed, "Pause: select a tile"); return; }
            ShowPrompt("Pause", new[] { "Beats" }, new[] { "1" }, vals =>
            {
                var edit = SafeEditor();
                if (edit == null) return;
                using (new SaveStateScope(edit))
                {
                    var ev = new ADOFAI.LevelEvent(seq, ADOFAI.LevelEventType.Pause);
                    if (!SetNum(ev, "duration", vals[0])) LogKeys("Pause", ev);
                    Enable(ev, "duration");
                    edit.events.Add(ev);
                    ApplyAndRemake(edit);
                }
                Notify(edit, "Pause · " + Trim(vals[0]) + " beat" + (Math.Abs(vals[0]) == 1.0 ? "" : "s"));
            });
        }

        private static void OpenLocatePrompt(scnEditor ed)
        {
            int seq = SelectedSeq(ed);
            if (seq < 0) { Notify(ed, "Tile location: select a tile"); return; }
            ShowPrompt("Tile location", new[] { "X", "Y" }, new[] { "0", "0" }, vals =>
            {
                var edit = SafeEditor();
                if (edit == null) return;
                using (new SaveStateScope(edit))
                {
                    var ev = new ADOFAI.LevelEvent(seq, ADOFAI.LevelEventType.PositionTrack);
                    if (!SetXY(ev, "positionOffset", vals[0], vals[1])) LogKeys("PositionTrack", ev);
                    Enable(ev, "positionOffset");
                    edit.events.Add(ev);
                    ApplyAndRemake(edit);
                }
                Notify(edit, "Tile location · (" + Trim(vals[0]) + ", " + Trim(vals[1]) + ")");
            });
        }

        // ── set speed (bare O) + halve/double ([ / ]) ──────────────────────────
        private static void OpenSpeedPrompt(scnEditor ed)
        {
            int seq = SelectedSeq(ed);
            if (seq < 0) { Notify(ed, "Set speed: select a tile"); return; }
            EnsureCanvas();
            ClosePrompt();
            const float w = 260f, pad = 12f, headH = 30f, modeH = 30f, rowH = 28f, btnH = 32f;
            float modeY = headH + 6f;
            float fieldY = modeY + modeH + 8f;
            float statusY = fieldY + rowH + 4f;
            float h = statusY + 16f + 8f + btnH + pad;

            _promptGo = new GameObject("SpeedPrompt", typeof(RectTransform));
            _promptGo.transform.SetParent(_root, false);
            var rr = (RectTransform)_promptGo.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.sizeDelta = new Vector2(w, h);
            rr.anchoredPosition = Vector2.zero;
            var bg = _promptGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.10f, 0.10f, 0.12f, 0.99f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f);
            bg.raycastTarget = true;

            var tGo = new GameObject("Title", typeof(RectTransform));
            tGo.transform.SetParent(_promptGo.transform, false);
            var tr = (RectTransform)tGo.transform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.offsetMin = new Vector2(pad, -headH); tr.offsetMax = new Vector2(-pad, -6f);
            UIBuilder.Tmp(tGo, Loc.T("Set speed"), 14f, TextAnchor.MiddleLeft, Theme.Text).raycastTarget = false;

            // value field
            var fGo = new GameObject("F", typeof(RectTransform));
            fGo.transform.SetParent(_promptGo.transform, false);
            var frt = (RectTransform)fGo.transform;
            frt.anchorMin = frt.anchorMax = new Vector2(0f, 1f); // top-left, fixed size (not stretched)
            frt.pivot = new Vector2(0f, 1f);
            frt.anchoredPosition = new Vector2(pad, -fieldY);
            frt.sizeDelta = new Vector2(w - pad * 2f, rowH);
            var fbg = fGo.AddComponent<RoundedRectGraphic>();
            fbg.Radius = 5f; fbg.color = new Color(1f, 1f, 1f, 0.07f); fbg.BorderWidth = 1f;
            fbg.BorderColor = new Color(1f, 1f, 1f, 0.14f); fbg.raycastTarget = true;
            var ftGo = new GameObject("Text", typeof(RectTransform));
            ftGo.transform.SetParent(fGo.transform, false);
            var ftr = (RectTransform)ftGo.transform;
            ftr.anchorMin = Vector2.zero; ftr.anchorMax = Vector2.one;
            ftr.offsetMin = new Vector2(8f, 0f); ftr.offsetMax = new Vector2(-8f, 0f);
            string def0 = _speedIsBpm ? "100" : "2";
            var ftxt = UIBuilder.Tmp(ftGo, def0, 14f, TextAnchor.MiddleLeft, Theme.Text);
            ftxt.richText = false;
            var field = UIBuilder.BuildInputField(fGo, ftxt);
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = def0;
            _promptFields = new[] { field };

            // mode segmented: BPM | Multiplier
            float halfW = (w - pad * 2f - 6f) * 0.5f;
            RoundedRectGraphic bpmBg = null, mulBg = null;
            Action syncMode = () =>
            {
                var on = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.34f);
                var off = new Color(1f, 1f, 1f, 0.06f);
                if (bpmBg != null) bpmBg.color = _speedIsBpm ? on : off;
                if (mulBg != null) mulBg.color = _speedIsBpm ? off : on;
            };
            bpmBg = MakeModeBtn(_promptGo, Loc.T("BPM"), pad, -modeY, halfW, modeH, () =>
            { if (!_speedIsBpm && (field.text == "2" || string.IsNullOrEmpty(field.text))) field.text = "100"; _speedIsBpm = true; syncMode(); });
            mulBg = MakeModeBtn(_promptGo, Loc.T("Multiplier"), pad + halfW + 6f, -modeY, halfW, modeH, () =>
            { if (_speedIsBpm && (field.text == "100" || string.IsNullOrEmpty(field.text))) field.text = "2"; _speedIsBpm = false; syncMode(); });
            syncMode();

            var sGo = new GameObject("Status", typeof(RectTransform));
            sGo.transform.SetParent(_promptGo.transform, false);
            var sr = (RectTransform)sGo.transform;
            sr.anchorMin = new Vector2(0f, 1f); sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.offsetMin = new Vector2(pad, -(statusY + 16f)); sr.offsetMax = new Vector2(-pad, -statusY);
            var status = UIBuilder.Tmp(sGo, "", 10.5f, TextAnchor.MiddleLeft, Theme.DangerText);
            status.raycastTarget = false;

            Action tryOk = () =>
            {
                if (!ExprEval.TryEval(field.text, out double v)) { status.text = Loc.T("check the value"); return; }
                bool isBpm = _speedIsBpm;
                ClosePrompt();
                var edit = SafeEditor();
                if (edit == null) return;
                using (new SaveStateScope(edit))
                {
                    var ev = new ADOFAI.LevelEvent(seq, ADOFAI.LevelEventType.SetSpeed);
                    if (isBpm) { ev["speedType"] = SpeedType.Bpm; if (!SetNum(ev, "beatsPerMinute", v)) LogKeys("SetSpeed", ev); Enable(ev, "beatsPerMinute"); }
                    else { ev["speedType"] = SpeedType.Multiplier; if (!SetNum(ev, "bpmMultiplier", v)) LogKeys("SetSpeed", ev); Enable(ev, "bpmMultiplier"); }
                    edit.events.Add(ev);
                    ApplyAndRemake(edit);
                }
                Notify(edit, isBpm ? ("Speed · " + Trim(v) + " BPM") : ("Speed · ×" + Trim(v)));
            };
            field.onSubmit.AddListener(_ => tryOk());

            MakeTextBtn(_promptGo, Loc.T("Place"), w - pad - 84f, pad, 84f, btnH,
                new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.34f), () => { Deselect(); tryOk(); });
            MakeTextBtn(_promptGo, Loc.T("Cancel"), pad, pad, 78f, btnH,
                new Color(1f, 1f, 1f, 0.07f), () => { Deselect(); ClosePrompt(); });

            try { field.ActivateInputField(); } catch { }
        }

        // Halve/double the SELECTED tile's existing SetSpeed value (BPM or Multiplier, whichever
        // it uses). No-op if the tile has no SetSpeed — then [ / ] keep the game's event-page nav.
        private static void AdjustSelectedSpeed(scnEditor ed, double factor)
        {
            int seq = SelectedSeq(ed);
            if (seq < 0) return;
            var ev = FindEvent(ed, seq, ADOFAI.LevelEventType.SetSpeed);
            if (ev == null) return;
            bool isBpm = false;
            try { isBpm = (SpeedType)ev["speedType"] == SpeedType.Bpm; } catch { }
            string key = isBpm ? "beatsPerMinute" : "bpmMultiplier";
            double cur = 0;
            try { cur = System.Convert.ToDouble(ev[key]); } catch { }
            if (cur <= 0) cur = isBpm ? 100.0 : 1.0;
            double nv = cur * factor;
            using (new SaveStateScope(ed))
            {
                SetNum(ev, key, nv);
                Enable(ev, key);
                ApplyAndRemake(ed);
            }
            Notify(ed, isBpm ? ("Speed · " + Trim(nv) + " BPM") : ("Speed · ×" + Trim(nv)));
        }

        // For the Harmony guard that suppresses the game's [ / ] event-page nav while we own them.
        internal static bool BracketSpeedActive()
        {
            try
            {
                var s = MainClass.Settings;
                if (s == null || !MainClass.EditorSuiteOn || !s.FeatQuickChart) return false;
                // Rebinding the speed keys hands the brackets back to the game — suppressing its
                // page nav for a key we no longer listen to would just break event navigation.
                if (!OwnsBrackets()) return false;
                var ed = scnEditor.instance;
                if (ed == null || ed.playMode || Typing(ed)) return false;
                int seq = SelectedSeq(ed);
                return seq >= 0 && FindEvent(ed, seq, ADOFAI.LevelEventType.SetSpeed) != null;
            }
            catch { return false; }
        }

        private static bool OwnsBrackets() =>
               (Keybinds.Key(Bind.QcSpeedDown) == KeyCode.LeftBracket && !Keybinds.Shift(Bind.QcSpeedDown))
            || (Keybinds.Key(Bind.QcSpeedUp) == KeyCode.RightBracket && !Keybinds.Shift(Bind.QcSpeedUp));

        // ── event-data helpers (coerce to the default's runtime type) ──────────
        /* Shared with the Hz tool: a Pause of `beats` on one floor. Lives here because the
           type-coercion + Enable pair below is the landmine (a fresh event's optional properties
           come DISABLED, so setting the value alone does nothing) and it should have exactly one
           implementation. */
        internal static void AddPause(scnEditor ed, int floorSeq, double beats)
        {
            try
            {
                var ev = new ADOFAI.LevelEvent(floorSeq, ADOFAI.LevelEventType.Pause);
                if (!SetNum(ev, "duration", beats)) LogKeys("Pause", ev);
                Enable(ev, "duration");
                ed.events.Add(ev);
            }
            catch (Exception ex) { SapphireLog.Log("QuickChart: add Pause failed: " + ex.Message); }
        }

        // Shared with the Hz tool: a PositionTrack offset on one floor. Same reason as AddPause —
        // the SetXY + Enable pair is the landmine and wants exactly one implementation.
        internal static void AddPositionTrack(scnEditor ed, int floorSeq, double x, double y)
        {
            try
            {
                var ev = new ADOFAI.LevelEvent(floorSeq, ADOFAI.LevelEventType.PositionTrack);
                if (!SetXY(ev, "positionOffset", x, y)) LogKeys("PositionTrack", ev);
                Enable(ev, "positionOffset");
                ed.events.Add(ev);
            }
            catch (Exception ex) { SapphireLog.Log("QuickChart: add PositionTrack failed: " + ex.Message); }
        }

        private static bool SetNum(ADOFAI.LevelEvent ev, string key, double val)
        {
            try
            {
                if (!ev.ContainsKey(key)) return false;
                object cur = ev[key];
                if (cur is int) ev[key] = (int)Math.Round(val);
                else if (cur is long) ev[key] = (long)Math.Round(val);
                else if (cur is double) ev[key] = val;
                else ev[key] = (float)val;
                return true;
            }
            catch { return false; }
        }

        private static bool SetXY(ADOFAI.LevelEvent ev, string key, double x, double y)
        {
            var v = new Vector2((float)x, (float)y);
            try
            {
                if (!ev.ContainsKey(key)) { ev[key] = v; return false; }
                object cur = ev[key];
                var t = cur != null ? cur.GetType() : null;
                if (t != null && t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Tuple<,>))
                {
                    var ga = t.GetGenericArguments();
                    ev[key] = Activator.CreateInstance(t,
                        Convert.ChangeType(x, ga[0]), Convert.ChangeType(y, ga[1]));
                    return true;
                }
                // Positions are boxed Vector2 in this game (verified) — the safe default reader.
                ev[key] = v;
                return true;
            }
            catch { ev[key] = v; return false; }
        }

        // A freshly-built event has disabled[key] = !startEnabled, so OPTIONAL properties (like
        // PositionTrack.positionOffset) come DISABLED and their value doesn't apply until enabled.
        // Clear the disable so the value we just set actually takes effect (matches the inspector
        // radio being on). Idempotent for always-on properties.
        private static void Enable(ADOFAI.LevelEvent ev, string key)
        {
            try { if (ev.disabled != null) ev.disabled[key] = false; } catch { }
        }

        // The event data keys come from a JSON resource, not the IL — if a rename ever breaks a
        // key, dump what the freshly-built event actually has so we can retarget quickly.
        private static void LogKeys(string name, ADOFAI.LevelEvent ev)
        {
            try
            {
                var d = ev.GetData();
                SapphireLog.Log("QuickChart: " + name + " missing key; has [" +
                    (d != null ? string.Join(", ", d.Keys) : "null") + "]");
            }
            catch { }
        }

        private static void ApplyAndRemake(scnEditor ed)
        {
            try { ed.ApplyEventsToFloors(); } catch { }
            try { ed.RemakePath(true, true); } catch { }
        }

        // ── angle pad placement ────────────────────────────────────────────────
        // Append a run of tiles at RELATIVE (charter) angles, chained off the last selected tile
        // (or the track's end): each value is the tile's angle relative to the current heading —
        // 180 = straight, 90 = quarter turn, 0 = U-turn (the same convention the tile-angle readout
        // shows). Routed through PseudoBuild's anchor mode (Fixed=false): spin tracked from the
        // anchor, flip before a twirled tap — reproduces this pad's original build exactly. The
        // whole expanded run (ParseAngles already flattens (...)*n) is one unit, so RepeatN=1.
        /* Takes the EXPANDED run (ExpandRun already applied the repeats and the first-tile flip),
           so RepeatN stays 1 — one description of the run, no chance of the repeat rule being
           applied differently here than in the preview. */
        private static int PlaceAngles(scnEditor ed, List<AngleStep> steps)
        {
            if (ed == null || steps == null || steps.Count == 0) return 0;
            if (steps.Count > MaxRunTiles) return 0;
            var unit = new PseudoStep[steps.Count];
            for (int i = 0; i < steps.Count; i++) unit[i] = new PseudoStep(steps[i].Angle, StepKind.Tap, steps[i].Twirl);
            return PseudoBuild.Build(ed, unit, new PseudoContext { Fixed = false, RepeatN = 1 });
        }

        private struct AngleStep
        {
            public double Angle; public bool Twirl;
            public AngleStep(double a, bool t) { Angle = a; Twirl = t; }
        }

        private const int MaxRunTiles = 1000; // guard against (…)*N blowups

        // Parse the pad field into a run of tiles. Grammar:
        //   • whitespace-separated tokens, each an ExprEval angle expression (180-30, 360/8, 2*45)
        //   • a trailing 't' marks a Twirl on that tile:            30t 30t 180
        //   • a parenthesised group repeats with '*':               (30 30 180)*3   /   (30 30) * 2
        //   groups nest. Returns null on any malformed input so a typo never places a partial run.
        private static List<AngleStep> ParseAngles(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int i = 0;
            var outv = ParseSeq(text, ref i, false);
            if (outv == null || outv.Count == 0 || outv.Count > MaxRunTiles) return null;
            SkipSep(text, ref i);
            if (i < text.Length) return null; // trailing junk
            return outv;
        }

        private static void SkipSep(string s, ref int i)
        { while (i < s.Length && (char.IsWhiteSpace(s[i]) || s[i] == ',')) i++; }

        // Parse tokens until end (inGroup=false) or a matching ')' (inGroup=true, which it consumes).
        private static List<AngleStep> ParseSeq(string s, ref int i, bool inGroup)
        {
            var res = new List<AngleStep>();
            for (;;)
            {
                SkipSep(s, ref i);
                if (i >= s.Length) return inGroup ? null : res;   // unclosed group = malformed
                char c = s[i];
                if (c == ')') { if (!inGroup) return null; i++; return res; }
                if (c == '(')
                {
                    i++;
                    var grp = ParseSeq(s, ref i, true); // consumes the ')'
                    if (grp == null) return null;
                    int rep = 1;
                    SkipSep(s, ref i);
                    if (i < s.Length && s[i] == '*')
                    {
                        i++; SkipSep(s, ref i);
                        if (!ReadCount(s, ref i, out rep)) return null;
                    }
                    for (int r = 0; r < rep; r++) res.AddRange(grp);
                    if (res.Count > MaxRunTiles) return null;
                }
                else
                {
                    // bare token: up to whitespace / paren / comma. Keeps math operators, so
                    // 180-30 and 2*45 stay single ExprEval expressions.
                    int st = i;
                    while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '(' && s[i] != ')' && s[i] != ',') i++;
                    string tok = s.Substring(st, i - st);
                    bool twirl = tok.Length > 0 && (tok[tok.Length - 1] == 't' || tok[tok.Length - 1] == 'T');
                    if (twirl) tok = tok.Substring(0, tok.Length - 1);
                    if (!ExprEval.TryEval(tok, out double v)) return null;
                    res.Add(new AngleStep(v, twirl));
                }
            }
        }

        // Read a repeat count after a group's '*': the next bare token, a positive integer 1..999.
        private static bool ReadCount(string s, ref int i, out int rep)
        {
            rep = 0;
            int st = i;
            while (i < s.Length && !char.IsWhiteSpace(s[i]) && s[i] != '(' && s[i] != ')' && s[i] != ',') i++;
            if (!ExprEval.TryEval(s.Substring(st, i - st), out double v)) return false;
            int n = (int)Math.Round(v);
            if (n < 1 || n > 999) return false;
            rep = n;
            return true;
        }

        // ── shared helpers ─────────────────────────────────────────────────────
        private static scnEditor SafeEditor() { try { return scnEditor.instance; } catch { return null; } }

        private static int SelectedSeq(scnEditor ed)
        {
            try
            {
                var sel = ed.selectedFloors;
                if (sel != null && sel.Count > 0 && sel[sel.Count - 1] != null) return sel[sel.Count - 1].seqID;
            }
            catch { }
            return -1;
        }

        private static void Notify(scnEditor ed, string msg)
        {
            try { if (ed != null) ed.ShowNotification(msg, null, 0f); } catch { }
            SapphireLog.Log("QuickChart: " + msg);
        }

        private static string Trim(double v) =>
            v.ToString(Math.Abs(v - Math.Round(v)) < 1e-9 ? "0" : "0.####", CultureInfo.InvariantCulture);

        private static void Deselect()
        {
            try { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); } catch { }
        }

        // ── canvas ─────────────────────────────────────────────────────────────
        private static void EnsureCanvas()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SapphireQuickChart", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 946; // above panels, below the toolbar/popups/dropdown/master
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();

            var rootGo = new GameObject("Root", typeof(RectTransform));
            rootGo.transform.SetParent(_canvasGo.transform, false);
            _root = (RectTransform)rootGo.transform;
            _root.anchorMin = Vector2.zero; _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero; _root.offsetMax = Vector2.zero;
            _root.pivot = new Vector2(0.5f, 0.5f);
        }

        // Keep every open window on screen through a resize (windows are center-anchored).
        private static void ClampPads()
        {
            if (!CanvasReady) return;                            // wait for a real layout size
            for (int i = _pads.Count - 1; i >= 0; i--)
            {
                if (_pads[i].Root == null) { _pads.RemoveAt(i); continue; }
                ClampPad((RectTransform)_pads[i].Root.transform);
            }
            if (_promptGo != null) Clamp((RectTransform)_promptGo.transform);
        }

        // The canvas has been through a layout pass, so its rect is the real screen size. A
        // freshly created canvas reports 0 until Unity lays it out, and anything positioned
        // against that lands nowhere near where it was asked to go.
        private static bool CanvasReady => _root != null && _root.rect.width >= 100f;

        // Pads are corner-anchored; the prompt card is still centred, so they clamp differently.
        private static void ClampPad(RectTransform rt) => rt.anchoredPosition =
            ClampPad(rt.anchoredPosition, rt.rect.width, rt.rect.height);

        private static Vector2 ClampPad(Vector2 p, float w, float h)
        {
            if (_root == null || _root.rect.width < 100f) return p;
            float xMin = -Mathf.Max(PadMargin, _root.rect.width - w - PadMargin);
            float yMin = -Mathf.Max(PadMargin, _root.rect.height - h - PadMargin);
            return new Vector2(Mathf.Clamp(p.x, xMin, -PadMargin), Mathf.Clamp(p.y, yMin, -PadMargin));
        }

        private static void Clamp(RectTransform rt) => rt.anchoredPosition =
            ClampToCanvas(rt.anchoredPosition, rt.rect.width, rt.rect.height);

        /* Keep a WHOLE window inside the canvas. The old rule clamped only the window's CENTRE
           to within 20px of the edge, which let a 300-wide pad sit with 130px of itself off the
           right of the screen and called that fine — the "spawns outside the screen" bug. A
           window wider than the canvas gets pinned centred rather than fighting the clamp. */
        private static Vector2 ClampToCanvas(Vector2 p, float w, float h)
        {
            if (_root == null) return p;
            float mx = Mathf.Max(0f, (_root.rect.width - w) * 0.5f - PadMargin);
            float my = Mathf.Max(0f, (_root.rect.height - h) * 0.5f - PadMargin);
            return new Vector2(Mathf.Clamp(p.x, -mx, mx), Mathf.Clamp(p.y, -my, my));
        }

        // ── angle pad window ───────────────────────────────────────────────────
        private sealed class Pad
        {
            internal GameObject Root;
            internal TMP_InputField Field;
            internal TMP_InputField Reps;       // how many times to lay the expression down
            internal bool FlipFirst;            // invert the very first tile's twirl at build time
            internal RoundedRectGraphic TwirlBtn;  // lit while FlipFirst is on
            internal GameObject Stepper;        // ▲▼ over the repeat field, shown on hover
            internal RectTransform RepsRect;    // hover target for the stepper
            internal TextMeshProUGUI Hint;
            internal GameObject Badge;          // "1".."9" overlay, shown only while picking
        }

        private const int MaxReps = 999;

        /* Nudge arrows over the repeat field, revealed on hover so the resting card stays clean.
           Drawn triangles, not glyphs: the arrow characters are exactly the sort the user's fonts
           drop, and this button is 10px tall — a tofu box here would be the whole control. */
        private static GameObject BuildStepper(GameObject fieldGo, Pad pw)
        {
            var host = new GameObject("Stepper", typeof(RectTransform));
            host.transform.SetParent(fieldGo.transform, false);
            var hr = (RectTransform)host.transform;
            hr.anchorMin = new Vector2(1f, 0f); hr.anchorMax = new Vector2(1f, 1f);
            hr.pivot = new Vector2(1f, 0.5f);
            hr.anchoredPosition = new Vector2(-2f, 0f);
            hr.sizeDelta = new Vector2(13f, -2f);
            StepBtn(host, true, () => StepReps(pw, +1));
            StepBtn(host, false, () => StepReps(pw, -1));
            return host;
        }

        private static void StepBtn(GameObject host, bool up, Action onClick)
        {
            var go = new GameObject(up ? "Up" : "Down", typeof(RectTransform));
            go.transform.SetParent(host.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, up ? 0.5f : 0f);
            r.anchorMax = new Vector2(1f, up ? 1f : 0.5f);
            r.offsetMin = Vector2.zero; r.offsetMax = Vector2.zero;
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 2f;
            bg.color = new Color(1f, 1f, 1f, 0.10f);
            bg.raycastTarget = true;
            var triGo = new GameObject("T", typeof(RectTransform));
            triGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)triGo.transform;
            tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
            tr.pivot = new Vector2(0.5f, 0.5f);
            tr.sizeDelta = Vector2.zero;
            var poly = triGo.AddComponent<PolyGraphic>();
            poly.color = Theme.Text;
            poly.raycastTarget = false;
            float d = up ? 1f : -1f;
            poly.SetPolygon(new[]
            {
                new Vector2(-3.2f, -1.6f * d), new Vector2(3.2f, -1.6f * d), new Vector2(0f, 2.0f * d),
            });
            go.AddComponent<Hover>().Init(bg, bg.color, new Color(1f, 1f, 1f, 0.24f));
            ClickHandler.Attach(go, onClick);
        }

        private static void StepReps(Pad pw, int delta)
        {
            if (pw == null || pw.Reps == null) return;
            pw.Reps.text = Mathf.Clamp(RepsOf(pw) + delta, 1, MaxReps).ToString();
        }

        // Per-frame rather than pointer events on two overlapping rects: the stepper sits INSIDE
        // the field, so enter/exit pairs fight each other as the cursor crosses the boundary.
        private static void TickSteppers()
        {
            Vector2 m = Input.mousePosition;
            for (int i = 0; i < _pads.Count; i++)
            {
                var pw = _pads[i];
                if (pw == null || pw.Stepper == null || pw.RepsRect == null) continue;
                bool over = false;
                try { over = RectTransformUtility.RectangleContainsScreenPoint(pw.RepsRect, m, null); } catch { }
                if (pw.Stepper.activeSelf != over) pw.Stepper.SetActive(over);
            }
        }

        private static int RepsOf(Pad pw)
        {
            if (pw == null || pw.Reps == null) return 1;
            int n;
            return ExprEval.TryParseInt(pw.Reps.text, out n) ? Mathf.Clamp(n, 1, MaxReps) : 1;
        }

        /* Enter places. With one pad open that's unambiguous; with several it can't be, so Enter
           ARMS a pick instead — every pad gets a number badge and the matching digit places that
           one. Esc cancels. Beats making the user click into the right pad first, which is the
           whole point of a keyboard flow. */
        private static bool _picking;

        // The group syntax lives in the help topic now — the strip under the field is a reminder,
        // not a manual, and it has to share its row with the repeat field.
        private const string HintText = "append t for twirl · math supported";

        /* TOP-RIGHT corner. The toolbar is centred on the top edge and its submenus drop
           straight down from it, so the shelf position this used to sit in was covered by the
           bar's own menus; the right corner is clear of both, and of the key hints + mode chips
           at the bottom. Dropped below PadTopInset so it clears the master switch and its
           "Sapphire" label, which own the very top-right. Pads are centre-anchored, so this is
           an offset from the canvas centre, and it goes through the same containment clamp as a
           dragged window — a default position must never be one the clamp would reject. */
        private const float PadMargin = 16f;

        /* Pads anchor to the canvas TOP-RIGHT, not to its centre. Twice now a centre-anchored
           default computed from `_root.rect` still landed on the master switch, and the rect is
           the one term here that cannot be checked from the source. Anchoring at the corner makes
           the placement true BY CONSTRUCTION — "16 in from the right, ChromeBottom+12 down from
           the top" — with no canvas arithmetic to be wrong about and nothing for the clamp to
           fight. With pivot (1,1) an anchoredPosition of (x, y) means the pad's right edge sits
           -x left of the canvas right edge and its top edge -y below the canvas top, so both are
           negative and read directly as insets. */
        private static Vector2 DefaultPadPos()
            => new Vector2(-PadMargin, -(EditorMasterSwitch.ChromeBottom + 12f));

        /* One runnable check for the rule that decides what actually gets built. The subtle part
           is that the flip is scoped to ONE tile of ONE repetition — everything else must come
           through byte-identical, or a repeated shape stops closing. */
        private static bool _selfChecked;

        private static bool SelfCheck()
        {
            var unit = new List<AngleStep> { new AngleStep(30, true), new AngleStep(30, true), new AngleStep(120, true) };
            var plain = ExpandRun(unit, 3, false);
            var flipped = ExpandRun(unit, 3, true);
            bool ok = plain != null && plain.Count == 9 && flipped != null && flipped.Count == 9;
            if (ok)
            {
                ok &= !flipped[0].Twirl && plain[0].Twirl;            // first tile inverted…
                for (int i = 1; i < 9 && ok; i++)                      // …and nothing else touched
                    ok &= flipped[i].Twirl == plain[i].Twirl
                       && Math.Abs(flipped[i].Angle - plain[i].Angle) < 1e-9;
                for (int r = 1; r < 3 && ok; r++)                      // later reps are the unit
                    for (int i = 0; i < 3 && ok; i++)
                        ok &= flipped[r * 3 + i].Twirl == unit[i].Twirl;
            }
            ok &= ExpandRun(null, 2, true) == null && ExpandRun(new List<AngleStep>(), 2, true) == null;
            SapphireLog.Log("EditorQuickChart.SelfCheck: " + (ok ? "PASS" : "FAIL"));
            return ok;
        }

        private static void SpawnPad(Vector2? at, string initial, bool focus = true, int reps = 1)
        {
            if (!_selfChecked) { _selfChecked = true; SelfCheck(); }
            EnsureCanvas();
            if (_canvasGo != null && !_canvasGo.activeSelf) _canvasGo.SetActive(true);
            // Taller than the content strictly needs: the button row is lifted clear of the
            // bottom-right resize grip (22px hit zone) so Place stays clickable to its own edge.
            // Height is the sum of the rows, not a guess: header 28 + field to -66 + hint row to
            // -90, then a 8px breather, the 30px button row and 26px of bottom margin that lifts
            // it clear of the resize grip's 22px corner zone.
            const float w = 300f, h = 156f, pad = 10f;

            var pw = new Pad();
            var root = new GameObject("AnglePad", typeof(RectTransform));
            root.transform.SetParent(_root, false);
            var rr = (RectTransform)root.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(1f, 1f);
            rr.pivot = new Vector2(1f, 1f);
            rr.sizeDelta = new Vector2(w, h);
            Vector2 pos = at ?? DefaultPadPos();
            // Duplicates cascade DOWN-LEFT, back into the screen: the default spot is the
            // top-right corner, so any other direction walks them off it.
            if (at == null) { int k = _spawnSeq++ % 6; pos += new Vector2(-k * 26f, -k * 26f); }
            pos = ClampPad(pos, w, h);
            rr.anchoredPosition = pos;
            var bg = root.AddComponent<RoundedRectGraphic>();
            bg.Radius = 9f;
            bg.color = new Color(0.10f, 0.10f, 0.12f, 0.98f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true; // swallow clicks so tiles aren't placed under the pad
            pw.Root = root;

            // header: title (drag) + duplicate + close
            var header = new GameObject("Header", typeof(RectTransform));
            header.transform.SetParent(root.transform, false);
            var hr = (RectTransform)header.transform;
            hr.anchorMin = new Vector2(0f, 1f); hr.anchorMax = new Vector2(1f, 1f);
            hr.pivot = new Vector2(0.5f, 1f);
            hr.offsetMin = new Vector2(0f, -28f); hr.offsetMax = new Vector2(0f, 0f);
            var hbg = header.AddComponent<RoundedRectGraphic>();
            hbg.Radius = 9f;
            hbg.color = new Color(1f, 1f, 1f, 0.05f);
            hbg.raycastTarget = true;
            header.AddComponent<DragHandle>();

            var titleGo = new GameObject("T", typeof(RectTransform));
            titleGo.transform.SetParent(header.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(pad, 0f); tr.offsetMax = new Vector2(-142f, 0f);
            var title = UIBuilder.Tmp(titleGo, Loc.T("Angle pad"), 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            title.raycastTarget = false;

            /* Swirl: flip the twirl on the FIRST tile. A twirl reverses the turn direction, so
               the leading one decides which way the whole run bends — the rest are relative to
               it. Dropping it reuses the same angles when the path already progresses the way
               you want, which is the common case when a run is pasted more than once. The icon
               is drawn, not typed: the user's fonts have no spiral glyph. */
            // Drawn, not a glyph: "×" already means close on this very header, so clear needs a
            // shape of its own — a bin reads as "empty this" at 24px where a letter would not.
            MakeIconBtn(header, -118f, 24f, Loc.T("Clear"), DrawClearIcon, () =>
            {
                if (pw.Field != null) pw.Field.text = "";
                if (pw.Hint != null) { pw.Hint.color = Theme.TextMuted; pw.Hint.text = Loc.T(HintText); }
            });
            pw.TwirlBtn = MakeIconBtn(header, -90f, 24f, Loc.T("Flip the first tile's twirl"),
                DrawSwirlIcon, () => FlipFirstTwirl(pw));
            MakeGlyphBtn(header, "?", -62f, 24f, Loc.T("Angle pad help"),
                () => { Deselect(); EditorHelp.OpenTopic("AnglePad"); });
            MakeGlyphBtn(header, "+", -34f, 24f, Loc.T("Duplicate"),
                () => SpawnPad(rr.anchoredPosition + new Vector2(26f, -26f),
                               pw.Field != null ? pw.Field.text : "", true, RepsOf(pw)));
            MakeGlyphBtn(header, "×", -6f, 24f, Loc.T("Close"), () => ClosePad(pw));

            // input field
            var fieldGo = new GameObject("Field", typeof(RectTransform));
            fieldGo.transform.SetParent(root.transform, false);
            var fr = (RectTransform)fieldGo.transform;
            fr.anchorMin = new Vector2(0f, 1f); fr.anchorMax = new Vector2(1f, 1f);
            fr.pivot = new Vector2(0.5f, 1f);
            fr.offsetMin = new Vector2(pad, -66f); fr.offsetMax = new Vector2(-pad, -34f);
            var fbg = fieldGo.AddComponent<RoundedRectGraphic>();
            fbg.Radius = 5f;
            fbg.color = new Color(1f, 1f, 1f, 0.07f);
            fbg.BorderWidth = 1f;
            fbg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            fbg.raycastTarget = true;
            var ftGo = new GameObject("Text", typeof(RectTransform));
            ftGo.transform.SetParent(fieldGo.transform, false);
            var ftr = (RectTransform)ftGo.transform;
            ftr.anchorMin = Vector2.zero; ftr.anchorMax = Vector2.one;
            ftr.offsetMin = new Vector2(8f, 0f); ftr.offsetMax = new Vector2(-8f, 0f);
            var ftxt = UIBuilder.Tmp(ftGo, initial ?? "", 14f, TextAnchor.MiddleLeft, Theme.Text);
            ftxt.richText = false;
            var field = UIBuilder.BuildInputField(fieldGo, ftxt);
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = initial ?? "";
            pw.Field = field;

            // hint line
            var hintGo = new GameObject("Hint", typeof(RectTransform));
            hintGo.transform.SetParent(root.transform, false);
            var hir = (RectTransform)hintGo.transform;
            hir.anchorMin = new Vector2(0f, 1f); hir.anchorMax = new Vector2(1f, 1f);
            hir.pivot = new Vector2(0.5f, 1f);
            hir.offsetMin = new Vector2(pad, -90f); hir.offsetMax = new Vector2(-pad - 68f, -70f);
            pw.Hint = UIBuilder.Tmp(hintGo, Loc.T(HintText), 10.5f, TextAnchor.MiddleLeft, Theme.TextMuted);
            pw.Hint.raycastTarget = false;

            /* Repeat count. Kept OUT of the expression on purpose: `(30t 150)*4` bakes the
               repetition into the text, so the "t" button above would flip the first tile of
               every copy. As a separate count the expression stays one UNIT — flip its leading
               twirl and only the first pass through loses it, which is the point when the path
               already enters turning the right way. PseudoBuild carries the spin across
               repetitions, so the remaining passes continue correctly from there. */
            var repsGo = new GameObject("Reps", typeof(RectTransform));
            repsGo.transform.SetParent(root.transform, false);
            var rpr = (RectTransform)repsGo.transform;
            rpr.anchorMin = rpr.anchorMax = new Vector2(1f, 1f);
            rpr.pivot = new Vector2(1f, 1f);
            rpr.anchoredPosition = new Vector2(-pad, -70f);
            rpr.sizeDelta = new Vector2(52f, 20f);
            var rpbg = repsGo.AddComponent<RoundedRectGraphic>();
            rpbg.Radius = 5f;
            rpbg.color = new Color(1f, 1f, 1f, 0.07f);
            rpbg.BorderWidth = 1f;
            rpbg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            rpbg.raycastTarget = true;
            var rptGo = new GameObject("Text", typeof(RectTransform));
            rptGo.transform.SetParent(repsGo.transform, false);
            var rptr = (RectTransform)rptGo.transform;
            rptr.anchorMin = Vector2.zero; rptr.anchorMax = Vector2.one;
            rptr.offsetMin = new Vector2(6f, 0f); rptr.offsetMax = new Vector2(-16f, 0f);   // stepper column
            var rptxt = UIBuilder.Tmp(rptGo, "1", 11.5f, TextAnchor.MiddleCenter, Theme.Text);
            rptxt.richText = false;
            var repsField = UIBuilder.BuildInputField(repsGo, rptxt);
            repsField.lineType = TMP_InputField.LineType.SingleLine;
            repsField.text = Mathf.Clamp(reps, 1, MaxReps).ToString();
            UIBuilder.MakeNumericField(repsField);
            pw.Reps = repsField;
            pw.RepsRect = rpr;
            pw.Stepper = BuildStepper(repsGo, pw);
            pw.Stepper.SetActive(false);
            var rlGo = new GameObject("x", typeof(RectTransform));
            rlGo.transform.SetParent(root.transform, false);
            var rlr = (RectTransform)rlGo.transform;
            rlr.anchorMin = rlr.anchorMax = new Vector2(1f, 1f);
            rlr.pivot = new Vector2(1f, 1f);
            rlr.anchoredPosition = new Vector2(-pad - 56f, -70f);
            rlr.sizeDelta = new Vector2(14f, 20f);
            UIBuilder.Tmp(rlGo, "×", 11.5f, TextAnchor.MiddleRight, Theme.TextMuted).raycastTarget = false;

            /* Button row. Both edges line up with the expression field above (pad / -pad) rather
               than being inset for the resize grip — the row is lifted above the grip's 22px
               corner zone instead, so nothing steals a click and the card reads as one column. */
            const float btnH = 30f, btnY = 26f, btnGap = 8f;
            float placeW = 96f;
            var placeGo = new GameObject("Place", typeof(RectTransform));
            placeGo.transform.SetParent(root.transform, false);
            var pr = (RectTransform)placeGo.transform;
            pr.anchorMin = new Vector2(1f, 0f); pr.anchorMax = new Vector2(1f, 0f);
            pr.pivot = new Vector2(1f, 0f);
            pr.anchoredPosition = new Vector2(-pad, btnY);
            pr.sizeDelta = new Vector2(placeW, btnH);
            var pbg = placeGo.AddComponent<RoundedRectGraphic>();
            pbg.Radius = 6f;
            pbg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.34f);
            pbg.BorderWidth = 1f;
            pbg.BorderColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.5f);
            pbg.raycastTarget = true;
            var plGo = new GameObject("L", typeof(RectTransform));
            plGo.transform.SetParent(placeGo.transform, false);
            var plr = (RectTransform)plGo.transform;
            plr.anchorMin = Vector2.zero; plr.anchorMax = Vector2.one;
            plr.offsetMin = Vector2.zero; plr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(plGo, Loc.T("Place"), 13f, TextAnchor.MiddleCenter, Theme.Text).raycastTarget = false;
            placeGo.AddComponent<Hover>().Init(pbg, pbg.color, new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.5f));
            ClickHandler.Attach(placeGo, () => { Deselect(); DoPlace(pw); });

            // Store-as-shape: same row as Place but GREY, because it's the secondary action —
            // Place is what the pad is for, this just files the run away for later.
            MakeFlatBtn(root, Loc.T("Add to Shape Library"), -pad - placeW - btnGap, btnY,
                        w - pad * 2f - placeW - btnGap, btnH,
                        () => { Deselect(); StoreAsShape(pw); });

            // Enter in the field places too (keeps hands on the keyboard).
            field.onSubmit.AddListener(_ => DoPlace(pw));
            SyncTwirlBtn(pw);

            // Resizable like the MSM/MH popups — grip at the bottom-right; content is anchored
            // (header/field/hint stretch, buttons ride the edges) so it re-fits width automatically.
            // Floors: both bottom buttons + margins across, and every row stacked down.
            ResizeHandle.AttachAll(rr, true, 262f, 150f);

            _pads.Add(pw);
            if (focus) try { field.ActivateInputField(); } catch { }
        }

        /* Open (or reuse) a pad carrying `expr` — the Hz tool's "To angle pad" entry point. An
           EMPTY pad is reused rather than stacking another window on top of the one the mode
           always keeps open; anything the user has typed is left alone. */
        internal static void OpenPadWith(string expr)
        {
            EnsureCanvas();
            foreach (var p in _pads)
                if (p != null && p.Field != null && p.Field.text.Trim().Length == 0)
                {
                    p.Field.text = expr;
                    try { p.Field.ActivateInputField(); } catch { }
                    return;
                }
            SpawnPad(null, expr);
        }

        private static void DoPlace(Pad pw)
        {
            if (pw == null || pw.Field == null) return;
            var angles = ParseAngles(pw.Field.text);
            if (angles == null || angles.Count == 0)
            {
                if (pw.Hint != null) { pw.Hint.text = Loc.T("check the expression"); pw.Hint.color = Theme.DangerText; }
                return;
            }
            int n = PlaceAngles(SafeEditor(), ExpandRun(angles, RepsOf(pw), pw.FlipFirst));
            // The run is real now; the ghosts of it would sit on top of the tiles just placed.
            ClearGhosts(); _ghostSig = long.MinValue;
            if (pw.Hint != null)
            {
                pw.Hint.color = Theme.TextMuted;
                pw.Hint.text = n > 0 ? (n + Loc.T(" tile") + (n == 1 ? "" : "s") + Loc.T(" placed"))
                                     : Loc.T("select a tile / open a level");
            }
            // Focus goes back to the TILE, not the pad: after placing you almost always want the
            // editor's own keys (and the next Enter re-places from wherever the caret isn't).
            Deselect();
        }

        // ── pad pick (Enter with more than one pad open) ──────────────────────

        private static void BeginPick()
        {
            _picking = true;
            for (int i = 0; i < _pads.Count && i < 9; i++) ShowBadge(_pads[i], i + 1);
        }

        private static void EndPick()
        {
            _picking = false;
            foreach (var p in _pads)
                if (p.Badge != null) { UnityEngine.Object.Destroy(p.Badge); p.Badge = null; }
        }

        // Accent square on the pad's top-left corner carrying its digit.
        private static void ShowBadge(Pad pw, int n)
        {
            if (pw == null || pw.Root == null) return;
            if (pw.Badge != null) UnityEngine.Object.Destroy(pw.Badge);
            var go = new GameObject("PickBadge", typeof(RectTransform));
            go.transform.SetParent(pw.Root.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(6f, -6f);
            r.sizeDelta = new Vector2(22f, 22f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.9f);
            bg.raycastTarget = false;
            var lGo = new GameObject("N", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(lGo, n.ToString(), 13f, TextAnchor.MiddleCenter, Color.black).raycastTarget = false;
            pw.Badge = go;
        }

        private static void ClosePad(Pad pw)
        {
            if (pw == null) return;
            if (_picking) EndPick();   // indices shift; a stale badge would point at the wrong pad
            // The mode always keeps one pad, so × on the last one CLEARS it instead of leaving a
            // bare screen for Tick to refill (which would teleport it back to the default corner).
            if (_pads.Count == 1 && _pads[0] == pw)
            {
                if (pw.Field != null) pw.Field.text = "";
                if (pw.Hint != null) { pw.Hint.color = Theme.TextMuted; pw.Hint.text = Loc.T(HintText); }
                return;
            }
            _pads.Remove(pw);
            if (pw.Root != null) UnityEngine.Object.Destroy(pw.Root);
            pw.Root = null;
        }

        private static void FlipFirstTwirl(Pad pw)
        {
            if (pw == null) return;
            pw.FlipFirst = !pw.FlipFirst;
            SyncTwirlBtn(pw);
            _ghostSig = long.MinValue;                  // preview has to re-walk
            if (pw.Hint != null) { pw.Hint.color = Theme.TextMuted; pw.Hint.text = Loc.T(HintText); }
        }

        private static void SyncTwirlBtn(Pad pw)
        {
            if (pw == null || pw.TwirlBtn == null) return;
            var col = pw.FlipFirst ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
                                   : new Color(1f, 1f, 1f, 0.07f);
            pw.TwirlBtn.color = col;
            var hov = pw.TwirlBtn.GetComponent<Hover>();
            if (hov != null) hov.Rest = col;
        }

        /* THE RUN AS IT WILL ACTUALLY BE PLACED: `reps` copies of the unit, with the very first
           tile of the FIRST copy's twirl inverted when the pad's swirl toggle is on.

           The flip deliberately does NOT touch the expression. A twirl in the text is part of the
           SHAPE — "30t 30t 120t" is a unit whose three twirls make it close — so rewriting the
           text to "30 30t 120t" changes every repetition and the run stops being that shape. What
           the toggle actually means is narrower: the path already enters turning the right way, so
           the leading twirl is redundant THIS ONCE. That is a property of the entry, not of the
           pattern, and it can only apply to the first pass.

           One function so placement, the ghost preview and Add to Shape Library cannot disagree
           about what the run is. */
        private static List<AngleStep> ExpandRun(List<AngleStep> unit, int reps, bool flipFirst)
        {
            if (unit == null || unit.Count == 0) return null;
            reps = Mathf.Clamp(reps, 1, MaxReps);
            var run = new List<AngleStep>(unit.Count * reps);
            for (int r = 0; r < reps; r++) run.AddRange(unit);
            if (flipFirst) run[0] = new AngleStep(run[0].Angle, !run[0].Twirl);
            return run;
        }

        /* Store the pad's run as a shape. The pad's text is the WHOLE run — ParseAngles already
           flattened every (…)*n group — so it is saved with repeat 1 into its own category; a
           shape-library repeat on top of that would multiply a run the user already spelled out.
           Named by the expression, which is the most useful label a one-click save can produce. */
        private const int MaxStoredShapeTiles = 64;   // the panel renders one angle field per tile

        private static void StoreAsShape(Pad pw)
        {
            if (pw == null || pw.Field == null) return;
            var angles = ParseAngles(pw.Field.text);
            if (angles == null || angles.Count == 0)
            {
                if (pw.Hint != null) { pw.Hint.text = Loc.T("check the expression"); pw.Hint.color = Theme.DangerText; }
                return;
            }
            // Store what the pad would PLACE — repeats and the first-tile flip included, since a
            // saved shape is non-repeating by definition and has to spell the whole run out.
            angles = ExpandRun(angles, RepsOf(pw), pw.FlipFirst);
            if (angles == null) return;
            if (angles.Count > MaxStoredShapeTiles)
            {
                if (pw.Hint != null) { pw.Hint.text = Loc.T("run too long to store"); pw.Hint.color = Theme.DangerText; }
                return;
            }
            var v = new ShapeVariant
            {
                K = angles.Count,
                Angles = new double[angles.Count],
                Twirls = new bool[angles.Count],
                N = 1,
            };
            double sum = 0;
            for (int i = 0; i < angles.Count; i++)
            { v.Angles[i] = angles[i].Angle; v.Twirls[i] = angles[i].Twirl; sum += angles[i].Angle; }

            string name = pw.Field.text.Trim();
            if (name.Length > 28) name = name.Substring(0, 27) + "…";
            var e = ShapeStore.AddShape(name, ShapeStore.NonRepeatCat, sum, v);
            if (pw.Hint != null)
            {
                pw.Hint.color = Theme.TextMuted;
                pw.Hint.text = e != null ? Loc.T("stored in") + " " + Loc.T(ShapeStore.NonRepeatCat)
                                         : Loc.T("check the expression");
            }
            if (e != null) try { EditorShapeLibrary.Refresh(e.Id); } catch { }
            Deselect();
        }

        // ── prompt card (pause beats / tile X-Y) ───────────────────────────────
        private static void ShowPrompt(string title, string[] labels, string[] defaults, Action<double[]> onOk)
        {
            EnsureCanvas();
            ClosePrompt();
            int n = labels.Length;
            const float w = 260f, pad = 12f, rowH = 34f, headH = 30f, btnH = 32f;
            float h = headH + n * (rowH + 6f) + 20f + btnH + pad;

            _promptGo = new GameObject("Prompt", typeof(RectTransform));
            _promptGo.transform.SetParent(_root, false);
            var rr = (RectTransform)_promptGo.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.sizeDelta = new Vector2(w, h);
            rr.anchoredPosition = Vector2.zero;
            var bg = _promptGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.10f, 0.10f, 0.12f, 0.99f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f);
            bg.raycastTarget = true;

            var tGo = new GameObject("Title", typeof(RectTransform));
            tGo.transform.SetParent(_promptGo.transform, false);
            var tr = (RectTransform)tGo.transform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.offsetMin = new Vector2(pad, -headH); tr.offsetMax = new Vector2(-pad, -6f);
            UIBuilder.Tmp(tGo, Loc.T(title), 14f, TextAnchor.MiddleLeft, Theme.Text).raycastTarget = false;

            var fields = new TMP_InputField[n];
            TextMeshProUGUI status = null;
            float y = -headH - 6f;
            for (int i = 0; i < n; i++)
            {
                var lGo = new GameObject("Lbl", typeof(RectTransform));
                lGo.transform.SetParent(_promptGo.transform, false);
                var lr = (RectTransform)lGo.transform;
                lr.anchorMin = new Vector2(0f, 1f); lr.anchorMax = new Vector2(0f, 1f);
                lr.pivot = new Vector2(0f, 1f);
                lr.anchoredPosition = new Vector2(pad, y);
                lr.sizeDelta = new Vector2(60f, rowH);
                UIBuilder.Tmp(lGo, Loc.T(labels[i]), 13f, TextAnchor.MiddleLeft, Theme.TextMuted).raycastTarget = false;

                var fGo = new GameObject("F", typeof(RectTransform));
                fGo.transform.SetParent(_promptGo.transform, false);
                var frt = (RectTransform)fGo.transform;
                frt.anchorMin = new Vector2(1f, 1f); frt.anchorMax = new Vector2(1f, 1f);
                frt.pivot = new Vector2(1f, 1f);
                frt.anchoredPosition = new Vector2(-pad, y - 3f);
                frt.sizeDelta = new Vector2(w - pad * 2f - 66f, rowH - 6f);
                var fbg = fGo.AddComponent<RoundedRectGraphic>();
                fbg.Radius = 5f;
                fbg.color = new Color(1f, 1f, 1f, 0.07f);
                fbg.BorderWidth = 1f;
                fbg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
                fbg.raycastTarget = true;
                var ftGo = new GameObject("Text", typeof(RectTransform));
                ftGo.transform.SetParent(fGo.transform, false);
                var ftr = (RectTransform)ftGo.transform;
                ftr.anchorMin = Vector2.zero; ftr.anchorMax = Vector2.one;
                ftr.offsetMin = new Vector2(8f, 0f); ftr.offsetMax = new Vector2(-8f, 0f);
                var ftxt = UIBuilder.Tmp(ftGo, defaults[i], 14f, TextAnchor.MiddleLeft, Theme.Text);
                ftxt.richText = false;
                var field = UIBuilder.BuildInputField(fGo, ftxt);
                field.lineType = TMP_InputField.LineType.SingleLine;
                field.text = defaults[i];
                fields[i] = field;
                y -= rowH + 6f;
            }

            var sGo = new GameObject("Status", typeof(RectTransform));
            sGo.transform.SetParent(_promptGo.transform, false);
            var sr = (RectTransform)sGo.transform;
            sr.anchorMin = new Vector2(0f, 1f); sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.offsetMin = new Vector2(pad, y - 16f); sr.offsetMax = new Vector2(-pad, y);
            status = UIBuilder.Tmp(sGo, "", 10.5f, TextAnchor.MiddleLeft, Theme.DangerText);
            status.raycastTarget = false;

            Action tryOk = () =>
            {
                var vals = new double[n];
                for (int i = 0; i < n; i++)
                    if (!ExprEval.TryEval(fields[i].text, out vals[i]))
                    { status.text = Loc.T("check ") + Loc.T(labels[i]); return; }
                var cb = onOk; ClosePrompt();
                try { cb(vals); } catch (Exception ex) { SapphireLog.Log("QuickChart prompt: " + ex.Message); }
            };
            for (int i = 0; i < n; i++) fields[i].onSubmit.AddListener(_ => tryOk());
            _promptFields = fields; // Tab cycles between them

            // OK / Cancel
            MakeTextBtn(_promptGo, Loc.T("Place"), w - pad - 84f, pad, 84f, btnH,
                new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.34f), () => { Deselect(); tryOk(); });
            MakeTextBtn(_promptGo, Loc.T("Cancel"), pad, pad, 78f, btnH,
                new Color(1f, 1f, 1f, 0.07f), () => { Deselect(); ClosePrompt(); });

            try { fields[0].ActivateInputField(); } catch { }
        }

        private static void ClosePrompt()
        {
            if (_promptGo != null) UnityEngine.Object.Destroy(_promptGo);
            _promptGo = null;
            _promptFields = null;
        }

        // Tab moves focus to the next prompt field (wraps). TMP has no built-in tab nav in-game.
        private static void CyclePromptFocus()
        {
            var f = _promptFields;
            if (f == null || f.Length == 0) return;
            int cur = -1;
            try
            {
                var es = EventSystem.current;
                var sel = es != null ? es.currentSelectedGameObject : null;
                for (int i = 0; i < f.Length; i++) if (f[i] != null && f[i].gameObject == sel) { cur = i; break; }
            }
            catch { }
            int next = (cur + 1) % f.Length;
            try { if (f[next] != null) { f[next].Select(); f[next].ActivateInputField(); } } catch { }
        }

        // ── small UI factories ─────────────────────────────────────────────────
        /* A muted, bottom-anchored text button (the pad's secondary action). Same geometry as
           the accent Place button next to it, minus the accent — grey reads as "not the thing
           you came here to press". */
        private static void MakeFlatBtn(GameObject parent, string label, float x, float y,
                                        float w, float h, Action onClick)
        {
            var go = new GameObject("FlatBtn", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 0f);
            r.pivot = new Vector2(1f, 0f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(1f, 1f, 1f, 0.08f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(lGo, label, 11.5f, TextAnchor.MiddleCenter, Theme.TextMuted).raycastTarget = false;
            go.AddComponent<Hover>().Init(bg, bg.color, new Color(1f, 1f, 1f, 0.16f));
            ClickHandler.Attach(go, onClick);
        }

        /* Same cell as MakeGlyphBtn but with a DRAWN icon: the user's fonts carry no spiral, and
           a letter standing in for one is what this button already tried. Returns the background
           so the caller can light it to show state. */
        private static RoundedRectGraphic MakeIconBtn(GameObject parent, float x, float size, string tip,
                                                      Action<GameObject> draw, Action onClick)
        {
            var go = new GameObject("B", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(1f, 0.5f); r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(size, size);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.07f);
            bg.raycastTarget = true;
            draw(go);
            go.AddComponent<Hover>().Init(bg, bg.color, new Color(1f, 1f, 1f, 0.16f));
            ClickHandler.Attach(go, () => { Deselect(); onClick(); });
            return bg;
        }

        /* A swirl: an Archimedean spiral drawn as short chords, the same trick the toolbar icons
           use for arcs. Two and a bit turns, tightening inward, which is what makes it read as a
           twirl marker rather than as a circle at 24px. */
        private static void DrawSwirlIcon(GameObject cell)
        {
            /* Starts at rMin, not at the centre: a spiral drawn from r=0 crowds a dozen chords
               into the first two pixels and reads as a filled dot with a tail. Beginning off-centre
               keeps every winding open — pitch is (rMax-rMin)/turns = 3.3px against a 1.0 stroke,
               so 2.3px of daylight between them. */
            const int seg = 32;
            const float turns = 1.75f, rMin = 1.6f, rMax = 7.4f, thick = 1.0f;
            Vector2 prev = Vector2.zero;
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg;
                float a = t * turns * 2f * Mathf.PI;
                float rad = Mathf.Lerp(rMin, rMax, t);
                var p = new Vector2(Mathf.Cos(a) * rad, Mathf.Sin(a) * rad);
                if (i > 0) MakeIconLine(cell, prev, p, thick);
                prev = p;
            }
        }

        // A bin: lid, tapered body, two ribs. Same 1.0 stroke as the swirl beside it.
        private static void DrawClearIcon(GameObject cell)
        {
            const float t = 1.0f;
            MakeIconLine(cell, new Vector2(-5.6f, 4.4f), new Vector2(5.6f, 4.4f), t);    // lid
            MakeIconLine(cell, new Vector2(-1.8f, 6.6f), new Vector2(1.8f, 6.6f), t);    // handle
            MakeIconLine(cell, new Vector2(-1.8f, 6.6f), new Vector2(-1.8f, 4.4f), t);
            MakeIconLine(cell, new Vector2(1.8f, 6.6f), new Vector2(1.8f, 4.4f), t);
            MakeIconLine(cell, new Vector2(-4.4f, 4.4f), new Vector2(-3.4f, -6.4f), t);  // body
            MakeIconLine(cell, new Vector2(4.4f, 4.4f), new Vector2(3.4f, -6.4f), t);
            MakeIconLine(cell, new Vector2(-3.4f, -6.4f), new Vector2(3.4f, -6.4f), t);
            MakeIconLine(cell, new Vector2(-1.2f, 2.6f), new Vector2(-1.0f, -4.4f), t);  // ribs
            MakeIconLine(cell, new Vector2(1.2f, 2.6f), new Vector2(1.0f, -4.4f), t);
        }

        private static void MakeIconLine(GameObject parent, Vector2 a, Vector2 b, float thick)
        {
            var d = b - a;
            var g = new GameObject("S", typeof(RectTransform));
            g.transform.SetParent(parent.transform, false);
            var r = (RectTransform)g.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = (a + b) * 0.5f;
            r.sizeDelta = new Vector2(d.magnitude + thick * 0.5f, thick);
            r.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
            var bar = g.AddComponent<RoundedRectGraphic>();
            bar.Radius = thick * 0.5f;
            bar.color = Theme.Text;
            bar.raycastTarget = false;
        }

        private static void MakeGlyphBtn(GameObject parent, string glyph, float x, float size, string tip, Action onClick)
        {
            var go = new GameObject("B", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var r = (RectTransform)go.transform;
            // right-anchored (x is negative, from the header's right edge) so it rides the edge on resize
            r.anchorMin = new Vector2(1f, 0.5f); r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(1f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(size, size);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            bg.raycastTarget = true;
            var gGo = new GameObject("G", typeof(RectTransform));
            gGo.transform.SetParent(go.transform, false);
            var gr = (RectTransform)gGo.transform;
            gr.anchorMin = Vector2.zero; gr.anchorMax = Vector2.one;
            gr.offsetMin = Vector2.zero; gr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(gGo, glyph, 14f, TextAnchor.MiddleCenter, Theme.Text).raycastTarget = false;
            go.AddComponent<Hover>().Init(bg, bg.color, new Color(1f, 1f, 1f, 0.14f));
            ClickHandler.Attach(go, () => { Deselect(); onClick(); });
        }

        private static void MakeTextBtn(GameObject parent, string label, float x, float yBottom,
            float w, float h, Color baseCol, Action onClick)
        {
            var go = new GameObject("Btn", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 0f);
            r.pivot = new Vector2(0f, 0f);
            r.anchoredPosition = new Vector2(x, yBottom);
            r.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = baseCol;
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(lGo, label, 13f, TextAnchor.MiddleCenter, Theme.Text).raycastTarget = false;
            var hi = baseCol; hi.a = Mathf.Min(1f, baseCol.a + 0.12f);
            go.AddComponent<Hover>().Init(bg, baseCol, hi);
            ClickHandler.Attach(go, onClick);
        }

        // Top-anchored segmented button (Set-speed mode toggle). Colour is driven by the caller's
        // syncMode (accent = selected), so no hover tint (it would fight the selection colour).
        private static RoundedRectGraphic MakeModeBtn(GameObject parent, string label, float x, float yTop,
            float w, float h, Action onClick)
        {
            var go = new GameObject("Mode", typeof(RectTransform));
            go.transform.SetParent(parent.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, yTop);
            r.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
            UIBuilder.Tmp(lGo, label, 13f, TextAnchor.MiddleCenter, Theme.Text).raycastTarget = false;
            ClickHandler.Attach(go, () => { Deselect(); onClick(); });
            return bg;
        }

        // Pointer hover tint for the pad/prompt buttons.
        private sealed class Hover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            private RoundedRectGraphic _bg;
            private Color _rest, _hot;
            internal void Init(RoundedRectGraphic bg, Color rest, Color hot) { _bg = bg; _rest = rest; _hot = hot; }
            // Lit/unlit buttons repaint their resting colour, and the hover must follow or the
            // next mouse-out restores the old one.
            internal Color Rest { set { _rest = value; } }
            public void OnPointerEnter(PointerEventData e) { if (_bg != null) _bg.color = _hot; }
            public void OnPointerExit(PointerEventData e) { if (_bg != null) _bg.color = _rest; }
        }

        /* ── ghost-tile preview ────────────────────────────────────────────────
           Which pad drives it, and the change-signature that decides when to re-walk. The
           drawing itself lives in GhostPreview, shared with the Hz tool. */
        private static long _ghostSig = long.MinValue;
        private const string GhostOwner = "anglepad";
        private const int MaxGhosts = 200;

        internal static void ClearGhosts() { GhostPreview.Release(GhostOwner); _ghostSig = long.MinValue; }

        // The pad being previewed: the one you are typing in, else the only/first one open.
        private static Pad PreviewPad()
        {
            var es = EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel != null)
                foreach (var p in _pads)
                    if (p != null && p.Field != null && sel == p.Field.gameObject) return p;
            return _pads.Count > 0 ? _pads[0] : null;
        }

        private static void TickPreview(scnEditor ed)
        {
            // The Hz tool's own preview is the more specific intent while its panel is up, and
            // two owners fighting for the ghosts every frame would just thrash.
            bool yield = false;
            try { yield = EditorHzTool.IsOpen; } catch { }
            if (yield) { ClearGhosts(); return; }

            var pw = PreviewPad();
            string expr = pw != null && pw.Field != null ? pw.Field.text : null;
            int reps = RepsOf(pw);
            int anchorSeq = GhostPreview.AnchorSeq(ed);

            long sig = 17;
            sig = sig * 31 + (expr != null ? expr.GetHashCode() : 0);
            sig = sig * 31 + reps;
            sig = sig * 31 + anchorSeq;
            sig = sig * 31 + (pw != null && pw.FlipFirst ? 1 : 0);
            if (sig == _ghostSig && GhostPreview.OwnedBy(GhostOwner)) return;
            _ghostSig = sig;
            var steps = ExpandRun(ParseAngles(expr), reps, pw != null && pw.FlipFirst);
            if (anchorSeq < 0 || steps == null || steps.Count == 0 || steps.Count > MaxGhosts)
            { GhostPreview.Release(GhostOwner); return; }
            var walk = new GhostStep[steps.Count];
            for (int i = 0; i < steps.Count; i++) walk[i] = new GhostStep(steps[i].Angle, steps[i].Twirl);
            GhostPreview.Show(GhostOwner, ed, anchorSeq, walk);
        }

        // ── teardown ───────────────────────────────────────────────────────────
        internal static void Dispose()
        {
            ClearGhosts(); _ghostSig = long.MinValue;
            _picking = false;
            _pads.Clear();
            ClosePrompt();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _root = null; _spawnSeq = 0;
        }
    }

}
