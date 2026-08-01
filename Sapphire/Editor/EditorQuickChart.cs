using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Quick-chart mode: fast in-place charting without arming a tool.
         • Shift+T  toggle a swirl (Twirl) on the selected tile(s)
         • Shift+P  prompt for beats → place a Pause event on the selected tile
         • Shift+L  prompt for X/Y  → place a PositionTrack event on the selected tile
         • Shift+G  open an "angle pad": a floating field that appends a whole run of tiles from
                    space-separated angle values (math allowed: 180-30, 360/8). A trailing 't'
                    twirls that tile (30t 30t 180); a parenthesised group repeats with '*'
                    ((30 30 180)*3). Duplicable, each holding its own value as a temp preset.

       Keybinds are SHIFT combos on purpose: the editor's 34 keyboard tile-placement binds are
       all registered with KeyModifier.None and matched by EXACT modifier equality, so a Shift+
       letter never collides with bare-key placement and needs no suppression (verified in the
       EditorKeybindManager IL). Everything lives on one own canvas, torn down with the suite. */
    internal static class EditorQuickChart
    {
        // Bare keys (non-tile keys): I swirl, O set-speed, [ ] halve/double speed. Shift combos: P L G.
        private const KeyCode KSwirl = KeyCode.I;    // bare — 'I' is not a game tile-placement key
        private const KeyCode KSetSpeed = KeyCode.O; // bare — 'O' is only Ctrl-bound in the editor
        private const KeyCode KPause = KeyCode.P;    // Shift+P — 'P' isn't a tile key either
        private const KeyCode KLocate = KeyCode.L;   // Shift+L
        private const KeyCode KAnglePad = KeyCode.G; // Shift+G

        private const char ArbitraryChar = (char)163; // float-floor sentinel (see EditorToolbar)

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
                return;
            }
            if (_canvasGo != null && !_canvasGo.activeSelf) _canvasGo.SetActive(true);

            ClampPads();

            if (!Input.anyKeyDown) return; // hotkeys below are all GetKeyDown
            // Prompt-local keys (handled BEFORE the typing gate, since a prompt field is focused).
            if (_promptGo != null && Input.GetKeyDown(KeyCode.Escape)) { ClosePrompt(); return; }
            if (_promptGo != null && Input.GetKeyDown(KeyCode.Tab)) { CyclePromptFocus(); return; }
            if (Typing(ed)) return;

            // Ctrl/Cmd/Alt belong to game chords — never quick-chart.
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

            if (!shift)
            {
                if (Input.GetKeyDown(KSwirl)) QuickSwirl(ed);
                else if (Input.GetKeyDown(KSetSpeed)) OpenSpeedPrompt(ed);
                // [ / ] halve/double the selected tile's SetSpeed (only when it has one — else the
                // game's event-page nav still works; a Harmony guard suppresses nav when we act).
                else if (Input.GetKeyDown(KeyCode.LeftBracket)) AdjustSelectedSpeed(ed, 0.5);
                else if (Input.GetKeyDown(KeyCode.RightBracket)) AdjustSelectedSpeed(ed, 2.0);
                return;
            }
            // Shift combos (the game binds every tile key under None AND Shift, so these keys are
            // deliberately NON-tile keys — P/L/G — leaving their Shift variants free).
            if (Input.GetKeyDown(KPause)) OpenPausePrompt(ed);
            else if (Input.GetKeyDown(KLocate)) OpenLocatePrompt(ed);
            else if (Input.GetKeyDown(KAnglePad)) SpawnPad(null, "");
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
            var status = UIBuilder.Tmp(sGo, "", 10.5f, TextAnchor.MiddleLeft, Theme.DangerHover);
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
                var ed = scnEditor.instance;
                if (ed == null || ed.playMode || Typing(ed)) return false;
                int seq = SelectedSeq(ed);
                return seq >= 0 && FindEvent(ed, seq, ADOFAI.LevelEventType.SetSpeed) != null;
            }
            catch { return false; }
        }

        // ── event-data helpers (coerce to the default's runtime type) ──────────
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
        // shows). Sign follows the current tile's spin, matching EditorToolbar.AppendRel. Each
        // CreateFloor advances the selection, so the next value chains onto it. One undo for the run.
        private static int PlaceAngles(scnEditor ed, List<AngleStep> steps)
        {
            if (ed == null || steps == null || steps.Count == 0) return 0;
            scrFloor anchor = null;
            try
            {
                var sel = ed.selectedFloors;
                if (sel != null && sel.Count > 0) anchor = sel[sel.Count - 1];
                if (anchor == null) { var fl = ed.floors; if (fl != null && fl.Count > 0) anchor = fl[fl.Count - 1]; }
            }
            catch { }
            if (anchor == null) return 0;
            int placed = 0;
            using (new SaveStateScope(ed))
            {
                try { ed.DeselectFloors(); } catch { }
                try { ed.SelectFloor(anchor, false); } catch { }
                if (!SelectionIsSingle(ed)) return 0;
                // New tiles land right after the anchor; tile #i gets seqID firstNewSeq+i.
                int firstNewSeq = anchor.seqID + 1;
                double dir = anchor.floatDirection;          // running absolute heading
                int spinSign;                                 // (ccw ? -1 : +1) multiplier on (180-rel)
                try { spinSign = anchor.isCCW ? -1 : 1; } catch { spinSign = 1; }
                for (int i = 0; i < steps.Count; i++)
                {
                    try
                    {
                        // A twirl on THIS tile: the event sits one tile earlier (the ball twirls
                        // leaving the prior tile), added NOW — not after the whole run — and the
                        // spin flips BEFORE this tile is placed so it and everything after chain off
                        // the reversed heading. Placing twirls after the build would fight the
                        // relative chaining and misplace tiles.
                        if (steps[i].Twirl)
                        {
                            int tseq = firstNewSeq + placed - 1;
                            if (tseq >= 0) try { ed.events.Add(new ADOFAI.LevelEvent(tseq, ADOFAI.LevelEventType.Twirl)); } catch { }
                            spinSign = -spinSign;
                        }
                        double a = dir + spinSign * (180.0 - steps[i].Angle);
                        ed.CreateFloorWithCharOrAngle((float)a, ArbitraryChar, false, false);
                        dir = a; placed++;
                    }
                    catch { break; }
                }
                try { ed.RemakePath(true, true); } catch { }
            }
            return placed;
        }

        private static bool SelectionIsSingle(scnEditor ed)
        {
            try { return ed.SelectionIsSingle(); }
            catch { return false; }
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
            if (_root == null || _root.rect.width < 100f) return; // wait for a real layout size
            float hw = _root.rect.width * 0.5f, hh = _root.rect.height * 0.5f;
            for (int i = _pads.Count - 1; i >= 0; i--)
            {
                if (_pads[i].Root == null) { _pads.RemoveAt(i); continue; }
                Clamp((RectTransform)_pads[i].Root.transform, hw, hh);
            }
            if (_promptGo != null) Clamp((RectTransform)_promptGo.transform, hw, hh);
        }

        private static void Clamp(RectTransform rt, float hw, float hh)
        {
            // center-anchored: keep the window's center within the canvas (± a small inset) so a
            // resize can never strand it fully off-screen.
            var p = rt.anchoredPosition;
            p.x = Mathf.Clamp(p.x, -hw + 20f, hw - 20f);
            p.y = Mathf.Clamp(p.y, -hh + 20f, hh - 20f);
            rt.anchoredPosition = p;
        }

        // ── angle pad window ───────────────────────────────────────────────────
        private sealed class Pad
        {
            internal GameObject Root;
            internal TMP_InputField Field;
            internal TextMeshProUGUI Hint;
        }

        private static void SpawnPad(Vector2? at, string initial)
        {
            EnsureCanvas();
            if (_canvasGo != null && !_canvasGo.activeSelf) _canvasGo.SetActive(true);
            const float w = 244f, h = 122f, pad = 10f;

            var pw = new Pad();
            var root = new GameObject("AnglePad", typeof(RectTransform));
            root.transform.SetParent(_root, false);
            var rr = (RectTransform)root.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.sizeDelta = new Vector2(w, h);
            Vector2 pos = at ?? new Vector2(0f, 60f);
            if (at == null) { int k = _spawnSeq++ % 6; pos += new Vector2(k * 26f, -k * 26f); }
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
            tr.offsetMin = new Vector2(pad, 0f); tr.offsetMax = new Vector2(-58f, 0f);
            var title = UIBuilder.Tmp(titleGo, Loc.T("Angle pad"), 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            title.raycastTarget = false;

            MakeGlyphBtn(header, "+", -34f, 24f, Loc.T("Duplicate"),
                () => SpawnPad(rr.anchoredPosition + new Vector2(26f, -26f), pw.Field != null ? pw.Field.text : ""));
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
            hir.offsetMin = new Vector2(pad, -84f); hir.offsetMax = new Vector2(-pad - 60f, -68f);
            pw.Hint = UIBuilder.Tmp(hintGo, Loc.T("180=straight · 30t=twirl · (…)*n"), 10.5f, TextAnchor.MiddleLeft, Theme.TextMuted);
            pw.Hint.raycastTarget = false;

            // place button
            var placeGo = new GameObject("Place", typeof(RectTransform));
            placeGo.transform.SetParent(root.transform, false);
            var pr = (RectTransform)placeGo.transform;
            pr.anchorMin = new Vector2(1f, 0f); pr.anchorMax = new Vector2(1f, 0f);
            pr.pivot = new Vector2(1f, 0f);
            pr.anchoredPosition = new Vector2(-pad - 16f, pad); // clear the bottom-right resize grip
            pr.sizeDelta = new Vector2(78f, 28f);
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

            // Enter in the field places too (keeps hands on the keyboard).
            field.onSubmit.AddListener(_ => DoPlace(pw));

            // Resizable like the MSM/MH popups — grip at the bottom-right; content is anchored
            // (header/field/hint stretch, buttons ride the edges) so it re-fits width automatically.
            ResizeHandle.AttachAll(rr, true, 200f, 108f);

            _pads.Add(pw);
            try { field.ActivateInputField(); } catch { }
        }

        private static void DoPlace(Pad pw)
        {
            if (pw == null || pw.Field == null) return;
            var angles = ParseAngles(pw.Field.text);
            if (angles == null || angles.Count == 0)
            {
                if (pw.Hint != null) { pw.Hint.text = Loc.T("check the expression"); pw.Hint.color = Theme.DangerHover; }
                return;
            }
            int n = PlaceAngles(SafeEditor(), angles);
            if (pw.Hint != null)
            {
                pw.Hint.color = Theme.TextMuted;
                pw.Hint.text = n > 0 ? (n + Loc.T(" tile") + (n == 1 ? "" : "s") + Loc.T(" placed"))
                                     : Loc.T("select a tile / open a level");
            }
            // re-arm the field so the next Enter repeats the run
            try { pw.Field.ActivateInputField(); } catch { }
        }

        private static void ClosePad(Pad pw)
        {
            if (pw == null) return;
            _pads.Remove(pw);
            if (pw.Root != null) UnityEngine.Object.Destroy(pw.Root);
            pw.Root = null;
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
            status = UIBuilder.Tmp(sGo, "", 10.5f, TextAnchor.MiddleLeft, Theme.DangerHover);
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
            public void OnPointerEnter(PointerEventData e) { if (_bg != null) _bg.color = _hot; }
            public void OnPointerExit(PointerEventData e) { if (_bg != null) _bg.color = _rest; }
        }

        // ── teardown ───────────────────────────────────────────────────────────
        internal static void Dispose()
        {
            _pads.Clear();
            ClosePrompt();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _root = null; _spawnSeq = 0;
        }
    }
}
