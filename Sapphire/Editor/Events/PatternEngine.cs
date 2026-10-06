using System;
using System.Collections.Generic;
using UnityEngine;

namespace Sapphire
{
    /* Writes repeated events as PATTERNS rather than one at a time (experimental; see
       tools/chart-scan for the counts that motivated it — 18% of non-twirl events in published
       charts are periodic trains, ≥8% stepped cascades, and per-tile SetSpeed retimes are typed
       by hand). Everything it writes is a plain vanilla event, so charts stay portable.

       The math is split from the game so SelfCheck can run it on synthetic floors. */
    internal static class PatternEngine
    {
        // ── pure math ─────────────────────────────────────────────────────

        internal static bool IsPow2(double r)
        {
            if (!(r > 0.0) || double.IsInfinity(r)) return false;
            double k = Math.Log(r, 2.0);
            return Math.Abs(k - Math.Round(k)) < 1e-4;
        }

        /* Musical tempo in effect at each floor. A speed change leaves the MUSIC alone when it is
           a power of two (subdivision) or when the hit gap stays a power-of-two multiple of the
           previous one (magic shape / free-angle compensation: speed tracks the angle so the tap
           rhythm holds). Anything else is a real tempo change. Measured rule, see the
           chart-tempo-semantics notes. */
        internal static double[] MusicalBpm(double[] speed, double[] angleDeg, double levelBpm)
        {
            int n = speed.Length;
            var m = new double[n];
            if (n == 0) return m;
            m[0] = levelBpm * speed[0];
            for (int i = 1; i < n; i++)
            {
                double r = speed[i - 1] > 0 ? speed[i] / speed[i - 1] : 1.0;
                bool same = Math.Abs(r - 1.0) < 1e-6 || IsPow2(r);
                if (!same && angleDeg[i] > 0.01 && angleDeg[i - 1] > 0.01)
                    same = IsPow2((angleDeg[i] / speed[i]) / (angleDeg[i - 1] / speed[i - 1]));
                m[i] = same ? m[i - 1] : m[i - 1] * r;
            }
            return m;
        }

        internal struct Slot { public int Host; public double OffsetDeg; public int Index; }

        /* One slot every `everyBeats` musical beats from floor `first`'s hit to floor `last`'s.
           Hosted on the latest floor already hit, with angleOffset measured at THAT floor's speed
           — ApplyEventsToFloors converts angleOffset with the host's speed only, so a long offset
           across a tempo change would drift; a short one cannot. */
        internal static List<Slot> TrainSlots(double[] entryTime, double[] speed, double[] musical,
                                              double levelBpm, int first, int last, double everyBeats)
        {
            var outp = new List<Slot>();
            if (everyBeats <= 1e-6 || first < 0 || last >= entryTime.Length || first > last) return outp;
            double t = entryTime[first], end = entryTime[last] + 1e-6;
            int h = first;
            for (int k = 0; t <= end && k < 100000; k++)
            {
                while (h + 1 <= last && entryTime[h + 1] <= t + 1e-6) h++;
                double secPerBeatHost = 60.0 / Math.Max(1e-6, speed[h] * levelBpm);
                outp.Add(new Slot { Host = h, OffsetDeg = (t - entryTime[h]) / secPerBeatHost * 180.0, Index = k });
                t += everyBeats * 60.0 / Math.Max(1e-6, musical[h]);
            }
            return outp;
        }

        /* Per-floor BPM so every non-midspin floor in [first,last] lasts `gapSec`:
           duration = angle/180 · 60/bpm  ⇒  bpm = angle/180 · 60/gap. Midspins (no sweep) get NaN —
           they take no time, so they get no SetSpeed. */
        internal static double[] RetimeBpm(double[] angleDeg, int first, int last, double gapSec)
        {
            var outp = new double[last - first + 1];
            for (int i = first; i <= last; i++)
                outp[i - first] = angleDeg[i] > 0.01 ? angleDeg[i] / 180.0 * 60.0 / gapSec : double.NaN;
            return outp;
        }

        /* Interpolate one event value. Numbers, Vector2 components, numeric tuple components and
           hex colours ramp; NaN (ADOFAI's "keep") stays NaN; anything else keeps the start value. */
        internal static object Lerp(object a, object b, double t)
        {
            if (a == null || b == null) return a;
            if (a is float fa && IsNum(b)) return float.IsNaN(fa) ? fa : (float)(fa + (ToD(b) - fa) * t);
            if (a is double da && IsNum(b)) return double.IsNaN(da) ? da : da + (ToD(b) - da) * t;
            if (a is int ia && IsNum(b)) return (int)Math.Round(ia + (ToD(b) - ia) * t);
            if (a is long la && IsNum(b)) return (long)Math.Round(la + (ToD(b) - la) * t);
            if (a is Vector2 va && b is Vector2 vb)
                return new Vector2(LerpF(va.x, vb.x, t), LerpF(va.y, vb.y, t));
            var ty = a.GetType();
            if (ty.IsGenericType && ty.GetGenericTypeDefinition() == typeof(Tuple<,>) && b.GetType() == ty)
            {
                var ga = ty.GetGenericArguments();
                object a1 = ty.GetProperty("Item1").GetValue(a, null), a2 = ty.GetProperty("Item2").GetValue(a, null);
                object b1 = ty.GetProperty("Item1").GetValue(b, null), b2 = ty.GetProperty("Item2").GetValue(b, null);
                return Activator.CreateInstance(ty, IsNum(a1) ? Convert.ChangeType(Lerp(a1, b1, t), ga[0]) : a1,
                                                    IsNum(a2) ? Convert.ChangeType(Lerp(a2, b2, t), ga[1]) : a2);
            }
            if (a is string sa && b is string sb && IsHex(sa) && IsHex(sb)) return LerpHex(sa, sb, t);
            return a;
        }

        private static float LerpF(float a, float b, double t)
            => float.IsNaN(a) || float.IsNaN(b) ? a : (float)(a + (b - a) * t);

        private static bool IsNum(object o) => o is float || o is double || o is int || o is long;
        private static double ToD(object o) => Convert.ToDouble(o);

        private static bool IsHex(string s)
        {
            if (s == null || (s.Length != 6 && s.Length != 8)) return false;
            foreach (char c in s) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        // Output keeps the START string's shape (6 or 8 digits); a 6-digit side reads as alpha ff.
        private static string LerpHex(string a, string b, double t)
        {
            var sb = new System.Text.StringBuilder();
            int n = a.Length / 2;
            for (int i = 0; i < n; i++)
            {
                int ca = Convert.ToInt32(a.Substring(i * 2, 2), 16);
                int cb = i * 2 + 2 <= b.Length ? Convert.ToInt32(b.Substring(i * 2, 2), 16) : 255;
                sb.Append(((int)Math.Round(ca + (cb - ca) * t)).ToString("x2"));
            }
            return sb.ToString();
        }

        internal static double Ease(DG.Tweening.Ease ease, double t)
        {
            try { return DG.Tweening.Core.Easing.EaseManager.Evaluate(ease, null, (float)t, 1f, 1.70158f, 0f); }
            catch { return t; }
        }

        // ── game access ──────────────────────────────────────────────────

        internal sealed class Floors
        {
            public double[] Entry, Speed, Angle, Musical;
            public double LevelBpm;
            public List<scrFloor> List;
        }

        // CalculateFloorEntryTimes is the game's own timing pass (speed, magic shapes, pauses,
        // holds); it only runs at play start, so refresh it — cheap and idempotent.
        internal static Floors ReadFloors()
        {
            var lm = ADOBase.lm;
            if (lm == null || lm.listFloors == null) return null;
            try { lm.CalculateFloorEntryTimes(); } catch { }
            var fl = lm.listFloors;
            int n = fl.Count;
            var f = new Floors { Entry = new double[n], Speed = new double[n], Angle = new double[n], List = fl };
            try { var ld = scnGame.instance != null ? scnGame.instance.levelData : null; f.LevelBpm = ld != null && ld.bpm > 1f ? ld.bpm : 100.0; }
            catch { f.LevelBpm = 100.0; }
            for (int i = 0; i < n; i++)
            {
                var x = fl[i];
                f.Entry[i] = x != null ? x.entryTime : (i > 0 ? f.Entry[i - 1] : 0.0);
                f.Speed[i] = x != null && x.speed > 0f ? x.speed : (i > 0 ? f.Speed[i - 1] : 1.0);
                f.Angle[i] = x != null ? x.angleLength * Mathf.Rad2Deg : 180.0;
            }
            f.Musical = MusicalBpm(f.Speed, f.Angle, f.LevelBpm);
            return f;
        }

        private static void Rebuild(scnEditor ed)
        {
            try { ed.ApplyEventsToFloors(); } catch { }
            try { ed.RemakePath(true, true); } catch { }
        }

        /* Train: every template copied onto each slot. A type with no angleOffset can only sit
           ON a tile, so its off-tile slots are skipped and counted rather than snapped. */
        internal static int ApplyTrain(scnEditor ed, IList<ADOFAI.LevelEvent> templates, int first, int last,
                                       double everyBeats, out int skipped)
        {
            skipped = 0;
            var f = ReadFloors();
            if (f == null || templates == null || templates.Count == 0) return 0;
            last = Math.Min(last, f.Entry.Length - 1);
            var slots = TrainSlots(f.Entry, f.Speed, f.Musical, f.LevelBpm, first, last, everyBeats);
            int n = 0;
            using (new SaveStateScope(ed))
            {
                foreach (var s in slots)
                    foreach (var tpl in templates)
                    {
                        if (Place(ed, tpl.Copy(), s.Host, s.OffsetDeg)) n++; else skipped++;
                    }
                Rebuild(ed);
            }
            return n;
        }

        private static bool Place(scnEditor ed, ADOFAI.LevelEvent e, int host, double offsetDeg)
        {
            e.floor = host;
            if (e.ContainsKey("angleOffset"))
            {
                double baseOff = 0.0;
                try { baseOff = Convert.ToDouble(e["angleOffset"]); } catch { }
                EditorQuickChart.SetNum(e, "angleOffset", baseOff + offsetDeg);
            }
            else if (offsetDeg > 1e-3) return false;
            ed.events.Add(e);
            return true;
        }

        /* Ramp: `count` copies of `start`; each key in `end` interpolated through `ease`.
           onFloors != null puts copy i on onFloors[i] (one per tile); otherwise every copy sits on
           `host` — the cascade shape (Once Forgotten's 54 MoveTracks with angleOffset +25). */
        internal static int ApplyRamp(scnEditor ed, ADOFAI.LevelEvent start, IDictionary<string, object> end,
                                      int count, IList<int> onFloors, int host, DG.Tweening.Ease ease)
        {
            if (start == null || count < 1) return 0;
            if (onFloors != null) count = onFloors.Count;
            using (new SaveStateScope(ed))
            {
                for (int i = 0; i < count; i++)
                {
                    double t = Ease(ease, count == 1 ? 0.0 : (double)i / (count - 1));
                    var c = start.Copy();
                    c.floor = onFloors != null ? onFloors[i] : host;
                    foreach (var kv in end)
                    {
                        try
                        {
                            c[kv.Key] = Lerp(start[kv.Key], kv.Value, t);
                            EditorQuickChart.Enable(c, kv.Key);
                        }
                        catch { }
                    }
                    ed.events.Add(c);
                }
                Rebuild(ed);
            }
            return count;
        }

        /* Camera pulse: a set (duration 0) and a tween back, the shape behind 29k floors in the
           corpus. Rotation alternates sign per pulse so a sway reads as one. */
        internal static int ApplyCameraPulse(scnEditor ed, int first, int last, double everyBeats,
                                             double baseZoom, double punchPct, double swayDeg,
                                             double tweenBeats, DG.Tweening.Ease ease)
        {
            var f = ReadFloors();
            if (f == null) return 0;
            last = Math.Min(last, f.Entry.Length - 1);
            var slots = TrainSlots(f.Entry, f.Speed, f.Musical, f.LevelBpm, first, last, everyBeats);
            int n = 0;
            using (new SaveStateScope(ed))
            {
                foreach (var s in slots)
                {
                    double sign = (s.Index & 1) == 0 ? 1.0 : -1.0;
                    var set = CamEvent(0.0, baseZoom * (1.0 + punchPct / 100.0), swayDeg * sign, DG.Tweening.Ease.Linear);
                    var back = CamEvent(tweenBeats, baseZoom, 0.0, ease);
                    if (Place(ed, set, s.Host, s.OffsetDeg)) n++;
                    if (Place(ed, back, s.Host, s.OffsetDeg)) n++;
                }
                Rebuild(ed);
            }
            return n;
        }

        private static ADOFAI.LevelEvent CamEvent(double duration, double zoom, double rot, DG.Tweening.Ease ease)
        {
            var e = new ADOFAI.LevelEvent(0, ADOFAI.LevelEventType.MoveCamera);
            EditorQuickChart.SetNum(e, "duration", duration);
            EditorQuickChart.SetNum(e, "zoom", zoom); EditorQuickChart.Enable(e, "zoom");
            EditorQuickChart.SetNum(e, "rotation", rot); EditorQuickChart.Enable(e, "rotation");
            try { if (e.disabled != null) e.disabled["position"] = true; } catch { }
            SetEnum(e, "ease", ease.ToString());
            return e;
        }

        internal static void SetEnum(ADOFAI.LevelEvent e, string key, string name)
        {
            try
            {
                object cur = e.ContainsKey(key) ? e[key] : null;
                e[key] = cur is Enum ? Enum.Parse(cur.GetType(), name) : (object)name;
            }
            catch { }
        }

        /* Retime [first,last] so every swept floor lasts gapSec. Existing SetSpeeds in the range
           are replaced; the first floor after it gets a Bpm event restoring the speed it had, so
           nothing downstream moves — even when its own SetSpeed was a Multiplier, which would
           otherwise compound on our new value. Holds, pauses and free-roam abort: their timing
           is not angle/speed alone. Returns null on success, else the reason. */
        internal static string ApplyRetime(scnEditor ed, int first, int last, double gapSec, out int written)
        {
            written = 0;
            var f = ReadFloors();
            if (f == null) return "no level";
            int n = f.Entry.Length;
            last = Math.Min(last, n - 1);
            if (first < 0 || first > last) return Loc.T("Select at least two tiles");
            int bad = 0;
            for (int i = first; i <= last; i++)
            {
                var x = f.List[i];
                if (x != null && (x.holdLength > -1 || x.extraBeats > 1e-4f || x.freeroam)) bad++;
            }
            if (bad > 0) return string.Format(Loc.T("{0} tile(s) hold, pause or free-roam — not retimed"), bad);

            var bpm = RetimeBpm(f.Angle, first, last, gapSec);
            double restore = last + 1 < n ? f.Speed[last + 1] * f.LevelBpm : double.NaN;
            using (new SaveStateScope(ed))
            {
                ed.events.RemoveAll(e => e != null && e.eventType == ADOFAI.LevelEventType.SetSpeed
                                         && e.floor >= first && e.floor <= last);
                for (int i = first; i <= last; i++)
                {
                    if (double.IsNaN(bpm[i - first])) continue;
                    ed.events.Add(BpmEvent(i, bpm[i - first]));
                    written++;
                }
                if (!double.IsNaN(restore))
                {
                    bool had = false;
                    // Only the one AT the tile's start sets its entry speed; a mid-tile SetSpeed
                    // already acted on that speed in the original chart too.
                    foreach (var e in ed.events)
                        if (e != null && e.eventType == ADOFAI.LevelEventType.SetSpeed && e.floor == last + 1
                            && Math.Abs(Off(e)) < 1e-6)
                        {
                            had = true;
                            SetEnum(e, "speedType", "Bpm");
                            EditorQuickChart.SetNum(e, "beatsPerMinute", restore);
                        }
                    if (!had) ed.events.Add(BpmEvent(last + 1, restore));
                }
                Rebuild(ed);
            }
            return null;
        }

        private static double Off(ADOFAI.LevelEvent e)
        {
            try { return e.ContainsKey("angleOffset") ? Convert.ToDouble(e["angleOffset"]) : 0.0; } catch { return 0.0; }
        }

        private static ADOFAI.LevelEvent BpmEvent(int floor, double bpm)
        {
            var e = new ADOFAI.LevelEvent(floor, ADOFAI.LevelEventType.SetSpeed);
            SetEnum(e, "speedType", "Bpm");
            EditorQuickChart.SetNum(e, "beatsPerMinute", bpm);
            EditorQuickChart.Enable(e, "beatsPerMinute");
            return e;
        }

        // ── self-check ───────────────────────────────────────────────────

        internal static bool SelfCheck()
        {
            var fails = new List<string>();
            Action<bool, string> ok = (c, m) => { if (!c) fails.Add(m); };

            // 1. Retime reproduces Once Forgotten floors 4584–4599: 1/6 beat at 117 → 3.9 × angle.
            double[] ofnr = { 30, 45, 232.5, 75, 127.5, 45, 30, 180, 270, 300, 195, 330, 165, 300, 90, 180 };
            double[] want = { 117, 175.5, 906.75, 292.5, 497.25, 175.5, 117, 702, 1053, 1170, 760.5, 1287, 643.5, 1170, 351, 702 };
            var got = RetimeBpm(ofnr, 0, ofnr.Length - 1, 60.0 / (117.0 * 6.0));
            for (int i = 0; i < want.Length; i++) ok(Math.Abs(got[i] - want[i]) < 1e-6, "retime[" + i + "]");

            // 2. Fit keeps the run's total duration.
            double[] ang = { 180, 90, 270, 0, 45 };   // 0 = midspin
            double total = 0; for (int i = 0; i < 4; i++) total += ang[i] / 180.0 * 60.0 / 120.0;   // at 120
            var fit = RetimeBpm(ang, 0, 3, total / 3.0);
            double after = 0; for (int i = 0; i < 4; i++) if (!double.IsNaN(fit[i])) after += ang[i] / 180.0 * 60.0 / fit[i];
            ok(Math.Abs(after - total) < 1e-6 && double.IsNaN(fit[3]), "fit");

            // 3. Musical tempo through a ×⅓ magic shape (60° tiles, still one beat each), its ×3
            //    exit, a ×2 subdivision, then a real ×7/6 change.
            double[] sp = { 1, 1, 1 / 3.0, 1 / 3.0, 1, 2, 2, 2 * 7.0 / 6.0 };
            double[] an = { 180, 180, 60, 60, 180, 180, 180, 180 };
            var mus = MusicalBpm(sp, an, 120);
            ok(Math.Abs(mus[3] - 120) < 1e-6, "magic shape kept tempo");
            ok(Math.Abs(mus[6] - 120) < 1e-6, "subdivision kept tempo");
            ok(Math.Abs(mus[7] - 140) < 1e-6, "real change moved tempo");

            // 4. Train: hosts never run backwards, offsets are within their host tile.
            double[] en = new double[sp.Length];
            for (int i = 1; i < en.Length; i++) en[i] = en[i - 1] + an[i - 1] / 180.0 * 60.0 / (sp[i - 1] * 120);
            var slots = TrainSlots(en, sp, mus, 120, 0, en.Length - 1, 0.5);
            int prevHost = -1;
            foreach (var s in slots)
            {
                ok(s.Host >= prevHost && s.OffsetDeg >= -1e-6, "train order");
                if (s.Host + 1 < en.Length) ok(s.OffsetDeg <= an[s.Host] + 1e-6, "train offset in tile " + s.Host);
                prevHost = s.Host;
            }
            ok(slots.Count > 0, "train empty");

            // 5. Ramp values.
            ok(Math.Abs((float)Lerp(0f, 650f, 0.5) - 325f) < 1e-4, "lerp float");
            var v = (Vector2)Lerp(new Vector2(float.NaN, 0f), new Vector2(5f, 10f), 0.5);
            ok(float.IsNaN(v.x) && Math.Abs(v.y - 5f) < 1e-4, "lerp vector keep-null");
            ok((string)Lerp("00000000", "ffffffff", 0.5) == "80808080", "lerp hex alpha");
            var tup = Lerp(Tuple.Create(8, "ThisTile"), Tuple.Create(34, "ThisTile"), 0.5) as Tuple<int, string>;
            ok(tup != null && tup.Item1 == 21 && tup.Item2 == "ThisTile", "lerp tile ref");

            SapphireLog.Log("PatternEngine.SelfCheck: " + (fails.Count == 0 ? "PASS" : "FAIL " + string.Join(", ", fails.ToArray())));
            return fails.Count == 0;
        }
    }
}
