using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using Sapphire.UI;

namespace Sapphire
{
    /* Patterns panel (experimental) — front end for PatternEngine. Tabs:
       TRAIN  the captured events every N musical beats across a tile range
       RAMP   copies of one captured event with chosen fields interpolated start → end
       CAMERA zoom punch / rotation sway pulses (set + tween pairs) every N beats
       RETIME one SetSpeed per tile so a free-angle run keeps an even rhythm

       Templates come from the selected tile ("Capture"), so events are built with the game's
       own inspector first — there is no second event editor here. Lives on the panel rail
       only while Settings.FeatExperimental is on. */
    internal static class EditorPatterns
    {
        private static readonly PanelKit K = new PanelKit("SapphirePatterns", 944, PanelW, focusable: true);
        private static bool _open, _selfChecked;
        private static string _status = "";
        private static TextMeshProUGUI _statusTmp;
        private static long _layoutSig = NoSig;
        private const long NoSig = long.MinValue;

        private static int _tab;                 // 0 train, 1 ramp, 2 camera, 3 retime
        private static int _first, _last = -1;   // -1 = end of level
        private static readonly List<ADOFAI.LevelEvent> _captured = new List<ADOFAI.LevelEvent>();
        private static int _capturedFrom = -1;

        private static float _every = 1f;
        private static int _rampIdx, _rampPlace, _rampCount = 8;   // place 0 one per tile, 1 all on first
        private static DG.Tweening.Ease _rampEase = DG.Tweening.Ease.Linear;
        private static readonly Dictionary<string, string> _rampEnd = new Dictionary<string, string>();
        private static float _camZoom = 100f, _camPunch = 10f, _camSway = 0f, _camTween = 0.5f;
        private static DG.Tweening.Ease _camEase = DG.Tweening.Ease.OutCubic;
        private static int _retimeMode, _retimeN = 4;               // 0 uniform 1/N, 1 fit

        internal static bool IsOpen => _open;
        internal static PanelKit Kit => K;
        internal static void SetOpen(bool v) { if (v != _open) Toggle(); }

        internal static bool TabAvailable()
        {
            var s = MainClass.Settings;
            return s != null && s.FeatExperimental && MainClass.EditorSuiteOn;
        }

        internal static void Toggle()
        {
            _open = !_open;
            _status = "";
            if (_open && PanelKit.SelectionRange(out int min, out int max)) { _first = min; _last = max; }
            _layoutSig = NoSig;
        }

        internal static void OpenTab(int tab)
        {
            if (!_open) Toggle();
            _tab = tab;
            if (PanelKit.SelectionRange(out int min, out int max)) { _first = min; _last = max; }
            _layoutSig = NoSig;
        }

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (!_open || ed == null || ed.playMode || !TabAvailable()) { K.Show(false); return; }
            if (!_selfChecked) { _selfChecked = true; PatternEngine.SelfCheck(); }
            long sig = 17;
            sig = sig * 31 + _tab;
            sig = sig * 31 + _rampIdx;
            sig = sig * 31 + _rampPlace;
            sig = sig * 31 + _retimeMode;
            sig = sig * 31 + _captured.Count;
            if (K.SyncWidth()) _layoutSig = NoSig;
            if (!K.Built || sig != _layoutSig) { _layoutSig = sig; Build(); }
            K.Show(true);
            K.TickScroll();
        }

        internal static void Dispose()
        {
            K.Dispose();
            _statusTmp = null; _layoutSig = NoSig;
            _captured.Clear(); _capturedFrom = -1;
        }

        private static void Refresh() { _layoutSig = NoSig; }

        private static void SetStatus(string s)
        {
            _status = s ?? "";
            if (_statusTmp != null) _statusTmp.text = _status;
        }

        // Inclusive tile range; -1 resolves to the last tile.
        private static bool Range(out int a, out int b)
        {
            int len = 0;
            try { len = ADOBase.lm.listFloors.Count; } catch { }
            a = Mathf.Clamp(_first, 0, Math.Max(0, len - 1));
            b = _last < 0 ? len - 1 : Mathf.Clamp(_last, 0, len - 1);
            return len > 0 && a <= b;
        }

        // ── capture ─────────────────────────────────────────────────────────

        // Everything on the first selected tile except twirls and speed — those are path
        // structure, and a train of them would rewrite the chart's geometry and timing.
        private static void Capture()
        {
            var ed = scnEditor.instance;
            if (ed == null || !PanelKit.SelectionRange(out int seq, out _)) { SetStatus(Loc.T("Nothing selected")); return; }
            _captured.Clear(); _rampEnd.Clear(); _rampIdx = 0;
            foreach (var e in ed.events)
                if (e != null && e.floor == seq && e.eventType != ADOFAI.LevelEventType.Twirl
                    && e.eventType != ADOFAI.LevelEventType.SetSpeed)
                    _captured.Add(e.Copy());
            _capturedFrom = seq;
            SetStatus(_captured.Count == 0 ? Loc.T("That tile has no events to capture") : "");
            Refresh();
        }

        private static string CapturedSummary()
        {
            if (_captured.Count == 0) return Loc.T("Nothing captured — select a tile with events, then Capture");
            var names = new List<string>();
            foreach (var e in _captured) names.Add(e.eventType.ToString());
            return string.Format(Loc.T("Tile {0}: {1}"), _capturedFrom, string.Join(", ", names.ToArray()));
        }

        // ── actions ─────────────────────────────────────────────────────────

        private static void DoTrain()
        {
            var ed = scnEditor.instance;
            if (ed == null || _captured.Count == 0 || !Range(out int a, out int b)) { SetStatus(Loc.T("Capture events first")); return; }
            try
            {
                int n = PatternEngine.ApplyTrain(ed, _captured, a, b, _every, out int skipped);
                SetStatus(string.Format(Loc.T("Wrote {0} events"), n)
                    + (skipped > 0 ? " · " + string.Format(Loc.T("{0} skipped (type has no angle offset)"), skipped) : ""));
            }
            catch (Exception ex) { SapphireLog.Log("Patterns: train failed: " + ex); SetStatus(Loc.T("Failed — see log")); }
        }

        private static void DoRamp()
        {
            var ed = scnEditor.instance;
            var start = RampEvent();
            if (ed == null || start == null) { SetStatus(Loc.T("Capture events first")); return; }
            var end = RampEndValues(start);
            if (end.Count == 0) { SetStatus(Loc.T("Set an end value that differs from the start")); return; }
            try
            {
                List<int> floors = null;
                int host = 0, count = Math.Max(1, _rampCount);
                if (_rampPlace == 0)
                {
                    if (!Range(out int a, out int b)) return;
                    floors = new List<int>();
                    for (int i = a; i <= b; i++) floors.Add(i);
                }
                else host = _capturedFrom;
                int n = PatternEngine.ApplyRamp(ed, start, end, count, floors, host, _rampEase);
                SetStatus(string.Format(Loc.T("Wrote {0} events"), n));
            }
            catch (Exception ex) { SapphireLog.Log("Patterns: ramp failed: " + ex); SetStatus(Loc.T("Failed — see log")); }
        }

        private static void DoCamera()
        {
            var ed = scnEditor.instance;
            if (ed == null || !Range(out int a, out int b)) return;
            try
            {
                int n = PatternEngine.ApplyCameraPulse(ed, a, b, _every, _camZoom, _camPunch, _camSway, _camTween, _camEase);
                SetStatus(string.Format(Loc.T("Wrote {0} events"), n));
            }
            catch (Exception ex) { SapphireLog.Log("Patterns: camera failed: " + ex); SetStatus(Loc.T("Failed — see log")); }
        }

        private static void DoRetime()
        {
            var ed = scnEditor.instance;
            if (ed == null || !Range(out int a, out int b)) return;
            double gap = RetimeGap(a, b, out _);
            if (!(gap > 0)) { SetStatus(Loc.T("Select at least two tiles")); return; }
            try
            {
                string err = PatternEngine.ApplyRetime(ed, a, b, gap, out int n);
                SetStatus(err ?? string.Format(Loc.T("Retimed {0} tiles"), n));
            }
            catch (Exception ex) { SapphireLog.Log("Patterns: retime failed: " + ex); SetStatus(Loc.T("Failed — see log")); }
        }

        /* Uniform: 1/N of the musical beat at the range start. Fit: the run's current length
           spread evenly over its swept tiles, so it ends where it ended before. */
        private static double RetimeGap(int a, int b, out string info)
        {
            info = "";
            var f = PatternEngine.ReadFloors();
            if (f == null || b <= a) return 0;
            if (_retimeMode == 0)
            {
                double r = f.Musical[a];
                info = string.Format(Loc.T("{0} BPM · 1/{1} beat per tile"), r.ToString("0.##"), _retimeN);
                return 60.0 / (r * Math.Max(1, _retimeN));
            }
            int swept = 0;
            for (int i = a; i <= b; i++) if (f.Angle[i] > 0.01) swept++;
            if (swept == 0 || b + 1 >= f.Entry.Length) return 0;
            double gap = (f.Entry[b + 1] - f.Entry[a]) / swept;
            info = string.Format(Loc.T("{0} ms per tile, run length kept"), (gap * 1000.0).ToString("0.#"));
            return gap;
        }

        // ── ramp fields ─────────────────────────────────────────────────────

        private static ADOFAI.LevelEvent RampEvent()
            => _captured.Count == 0 ? null : _captured[Mathf.Clamp(_rampIdx, 0, _captured.Count - 1)];

        // Only fields a ramp can move are listed; the rest ride along from the start event.
        private static List<KeyValuePair<string, object>> RampFields(ADOFAI.LevelEvent e)
        {
            var outp = new List<KeyValuePair<string, object>>();
            var d = EditorEvents.EventData(e);
            if (d == null) return outp;
            foreach (var kv in d)
            {
                if (kv.Key == "floor" || kv.Key == "eventType" || kv.Value == null) continue;
                bool rampable = kv.Value is float || kv.Value is double || kv.Value is int || kv.Value is long
                    || kv.Value is Vector2 || IsTileTuple(kv.Value)
                    || (kv.Value is string s && (s.Length == 6 || s.Length == 8) && IsHexStr(s));
                if (rampable) outp.Add(kv);
            }
            return outp;
        }

        private static bool IsTileTuple(object v)
        {
            var t = v.GetType();
            return t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Tuple<,>);
        }

        private static bool IsHexStr(string s)
        {
            foreach (char c in s) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        private static string Show(object v)
        {
            if (v is Vector2 p) return Fmt(p.x) + ", " + Fmt(p.y);
            if (v != null && IsTileTuple(v))
            {
                var t = v.GetType();
                return Convert.ToString(t.GetProperty("Item1").GetValue(v, null));
            }
            if (v is float f) return Fmt(f);
            if (v is double d) return d.ToString("0.###");
            return Convert.ToString(v);
        }

        private static string Fmt(float f) => float.IsNaN(f) ? "-" : f.ToString("0.###");

        // Parse the typed end value into the START value's own type; a field whose end text is
        // blank or unchanged is not ramped.
        private static Dictionary<string, object> RampEndValues(ADOFAI.LevelEvent start)
        {
            var outp = new Dictionary<string, object>();
            foreach (var kv in RampFields(start))
            {
                string txt;
                if (!_rampEnd.TryGetValue(kv.Key, out txt) || string.IsNullOrEmpty(txt) || txt == Show(kv.Value)) continue;
                object v = ParseLike(kv.Value, txt);
                if (v != null) outp[kv.Key] = v;
            }
            return outp;
        }

        private static object ParseLike(object like, string txt)
        {
            try
            {
                if (like is Vector2 p)
                {
                    var parts = txt.Split(',');
                    float x = parts.Length > 0 && parts[0].Trim() != "-" ? ParseF(parts[0]) : float.NaN;
                    float y = parts.Length > 1 && parts[1].Trim() != "-" ? ParseF(parts[1]) : float.NaN;
                    return new Vector2(x, y);
                }
                if (IsTileTuple(like))
                {
                    var t = like.GetType();
                    var ga = t.GetGenericArguments();
                    return Activator.CreateInstance(t, Convert.ChangeType(ParseF(txt), ga[0]), t.GetProperty("Item2").GetValue(like, null));
                }
                if (like is string) return IsHexStr(txt.Trim()) ? txt.Trim().ToLowerInvariant() : null;
                return Convert.ChangeType(ParseF(txt), like.GetType());
            }
            catch { return null; }
        }

        private static float ParseF(string s)
        {
            float f;
            if (!ExprEval.TryParseFloat(s.Trim(), out f)) throw new FormatException(s);
            return f;
        }

        // ── UI ──────────────────────────────────────────────────────────────

        private const float PanelW = PanelKit.PaletteW;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private static void Build()
        {
            K.LblW = PanelKit.PaletteLblW;
            K.Scrollable = true;
            K.DefaultH = 460f;
            K.Rebuild(Loc.T("Patterns") + " · " + Loc.T("experimental"), Toggle, new Vector2(360f, -140f));
            ResizeHandle.AttachAll((RectTransform)K.PanelGo.transform, true, PanelKit.PaletteMinW, PanelKit.PaletteMinH);

            float y = -34f;
            y = K.TabRow(y, new[] { Loc.T("Train"), Loc.T("Ramp"), Loc.T("Camera"), Loc.T("Retime") }, _tab,
                         i => { _tab = i; _status = ""; });

            if (_tab == 0 || _tab == 1)
            {
                y = K.PrimaryRow(y, Loc.T("Capture selected tile"), Capture);
                K.Status(CapturedSummary(), y);
                y -= RowH + Gap;
            }
            if (_tab != 1 || _rampPlace == 0)
                y = K.RangeRow(y, () => _first, () => _last, (a, b) => { _first = a; _last = b; }, Refresh, SetStatus);

            if (_tab == 0) y = BuildTrain(y);
            else if (_tab == 1) y = BuildRamp(y);
            else if (_tab == 2) y = BuildCamera(y);
            else y = BuildRetime(y);

            _statusTmp = K.Status(_status, y);
            y -= 32f;
            K.SetHeight(y);
        }

        private static float BuildTrain(float y)
        {
            y = K.FloatRow(y, Loc.T("Every (beats)"), _every, v => { _every = Mathf.Max(1f / 64f, v); Refresh(); });
            y = PreviewLine(y, TrainCount(_captured.Count));
            return K.PrimaryRow(y, Loc.T("Write train"), DoTrain);
        }

        private static float BuildRamp(float y)
        {
            var e = RampEvent();
            if (e == null) return y;
            if (_captured.Count > 1)
            {
                var names = new string[_captured.Count];
                for (int i = 0; i < names.Length; i++) names[i] = _captured[i].eventType.ToString();
                y = K.SegRow(y, Loc.T("Event"), names, () => _rampIdx, v => { _rampIdx = v; _rampEnd.Clear(); });
            }
            y = K.SegRow(y, Loc.T("Place"), new[] { Loc.T("One per tile"), Loc.T("All on its tile") },
                         () => _rampPlace, v => _rampPlace = v);
            if (_rampPlace == 1) y = K.IntRow(y, Loc.T("Copies"), _rampCount, v => { _rampCount = Mathf.Clamp(v, 2, 4096); Refresh(); });
            y = K.EaseRow(y, () => _rampEase, v => _rampEase = v);
            K.Status(Loc.T("End value per field (blank = unchanged)"), y);
            y -= RowH;
            foreach (var kv in RampFields(e))
            {
                string key = kv.Key, cur;
                _rampEnd.TryGetValue(key, out cur);
                y = K.FieldRow(y, key + "  " + Show(kv.Value) + " →", cur ?? "", v => { _rampEnd[key] = v; });
            }
            return K.PrimaryRow(y - 4f, Loc.T("Write ramp"), DoRamp);
        }

        private static float BuildCamera(float y)
        {
            y = K.FloatRow(y, Loc.T("Every (beats)"), _every, v => { _every = Mathf.Max(1f / 64f, v); Refresh(); });
            y = K.FloatRow(y, Loc.T("Base zoom %"), _camZoom, v => _camZoom = Mathf.Max(1f, v));
            y = K.FloatRow(y, Loc.T("Zoom punch %"), _camPunch, v => _camPunch = v);
            y = K.FloatRow(y, Loc.T("Rotation sway °"), _camSway, v => _camSway = v);
            y = K.FloatRow(y, Loc.T("Return (beats)"), _camTween, v => _camTween = Mathf.Max(0f, v));
            y = K.EaseRow(y, () => _camEase, v => _camEase = v);
            y = PreviewLine(y, TrainCount(2));
            return K.PrimaryRow(y, Loc.T("Write pulses"), DoCamera);
        }

        private static float BuildRetime(float y)
        {
            y = K.SegRow(y, Loc.T("Rhythm"), new[] { Loc.T("Uniform 1/N"), Loc.T("Fit run") }, () => _retimeMode, v => _retimeMode = v);
            if (_retimeMode == 0) y = K.IntRow(y, "N", _retimeN, v => { _retimeN = Mathf.Clamp(v, 1, 64); Refresh(); });
            string info = "";
            if (Range(out int a, out int b)) RetimeGap(a, b, out info);
            y = PreviewLine(y, info);
            K.Status(Loc.T("One BPM SetSpeed per tile; the next tile gets its old speed back."), y);
            y -= RowH;
            return K.PrimaryRow(y, Loc.T("Retime range"), DoRetime);
        }

        private static float PreviewLine(float y, string text)
        {
            K.Status(text, y);
            return y - RowH;
        }

        // "writes 54 events on 27 tiles" — computed on rebuild only, never per frame.
        private static string TrainCount(int perSlot)
        {
            if (perSlot <= 0 || !Range(out int a, out int b)) return "";
            var f = PatternEngine.ReadFloors();
            if (f == null) return "";
            var slots = PatternEngine.TrainSlots(f.Entry, f.Speed, f.Musical, f.LevelBpm, a, Math.Min(b, f.Entry.Length - 1), _every);
            var hosts = new HashSet<int>();
            foreach (var s in slots) hosts.Add(s.Host);
            return string.Format(Loc.T("Writes {0} events on {1} tiles"), slots.Count * perSlot, hosts.Count);
        }

        internal static bool Hovered
        {
            get
            {
                try
                {
                    return K.Visible && RectTransformUtility.RectangleContainsScreenPoint(
                        (RectTransform)K.PanelGo.transform, Input.mousePosition, null);
                }
                catch { return false; }
            }
        }
    }
}
