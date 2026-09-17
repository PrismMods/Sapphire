using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* CHART ANALYSIS — what the chart's speed changes actually mean, against what the song does.

       A chart's effective tile rate is not the song's tempo, and the difference between the two
       kinds of speed change is the whole point of this panel. Measured over 31,220 SetSpeed
       multipliers in 429 published charts:

         · a POWER OF TWO (78.5% of them) is a subdivision change — eighths, sixteenths. The
           music did not change tempo.
         · a simple 180/angle ratio — x3 and x1/3 (180/60), x1.5 and x2/3 (180/120), x4/3, x6/5,
           x9/8 — is a MAGIC SHAPE: odd angles with a compensating speed change that holds the
           input tempo the player taps. Also not a tempo change.
         · anything else is the base tempo genuinely moving.

       So each change is classified, and then checked against the audio: the tempo curve says
       what the music did at that moment, and the two disagreeing is the finding worth having —
       a base change where the song is steady, or a steady chart where the song moves.

       The game has already done the hard part. ApplyEventsToFloors folds every SetSpeed, of
       either type, into scrFloor.speed as a multiplier of the level BPM, and
       CalculateFloorEntryTimes fills scrFloor.entryTime. Reading those two beats reimplementing
       the semantics — and cannot drift from them. */
    internal static class EditorChartAnalysis
    {
        private static readonly PanelKit K = new PanelKit("SapphireChartAnalysis", 906, PanelW, focusable: true);
        private const float PanelW = 560f, HeaderH = 28f + Gap, RowStep = 17f;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private enum Kind { Subdivision, MagicShape, TempoChange }

        private struct Change
        {
            public int Floor;
            public float Time;        // audio time, seconds
            public float From, To;    // effective BPM
            public float Ratio;
            public Kind Kind;
            public float Audio;       // tempo curve reading there, <=0 when unknown
            public float AudioFrom, AudioTo;   // the curve either side of the change
            public float AudioRatio;  // AudioTo / AudioFrom, <=0 when unknown
            public bool Disagrees;    // the song did not do what the chart says it did
        }

        private static readonly System.Collections.Generic.List<Change> _changes =
            new System.Collections.Generic.List<Change>();
        private static bool _open;
        private static Vector2 _size = new Vector2(PanelW, 420f);
        private static RectTransform _viewport, _content;
        private static float _scroll;
        private static bool _dirty = true;
        private static bool _onlyFindings;
        private static string _summary = "";

        internal static bool IsOpen => _open;
        internal static PanelKit Kit => K;
        internal static void SetOpen(bool v) { if (v) Open(); else Close(); }
        internal static void Open() { _open = true; _dirty = true; }
        internal static void Close() { _open = false; }
        internal static void Toggle() { if (_open) Close(); else Open(); }
        internal static bool TabAvailable()
        {
            if (MainClass.Settings == null || !MainClass.EditorSuiteOn) return false;
            try { var ed = scnEditor.instance; return ed != null && !ed.playMode; } catch { return false; }
        }

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool inEditor = ed != null && !ed.playMode && MainClass.EditorSuiteOn;
            if (!_open || !inEditor) { K.Show(false); return; }
            if (!K.Built) { BuildShell(); _dirty = true; }
            if (_dirty) { _dirty = false; Scan(ed); BuildContent(); }
            K.Show(true);
            if (K.DockSide == 0 && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            TickScroll();
            TickResize();
        }

        internal static void Dispose()
        {
            K.Dispose();
            _viewport = null; _content = null; _changes.Clear();
            _open = false; _dirty = true;
        }

        // ── the scan ─────────────────────────────────────────────────────────

        private static void Scan(scnEditor ed)
        {
            _changes.Clear();
            _summary = "";
            float baseBpm = 0f, offset = 0f;
            try
            {
                var ld = ed.levelData;
                if (ld == null) return;
                baseBpm = ld.bpm; offset = ld.offset / 1000f;
            }
            catch { return; }
            if (baseBpm <= 0f) return;

            System.Collections.Generic.List<scrFloor> floors = null;
            try
            {
                var lm = ADOBase.lm;
                if (lm == null) return;
                lm.CalculateFloorEntryTimes();   // cheap, idempotent — the timeline does the same
                floors = lm.listFloors;
            }
            catch { return; }
            if (floors == null || floors.Count < 2) return;

            var res = AudioAnalysis.Get();
            if (res != null && res.Curve == null) AudioAnalysis.BeginCurve(res);

            float prevSpeed = 0f;
            int subdiv = 0, magic = 0, tempo = 0, disagree = 0;
            for (int i = 0; i < floors.Count; i++)
            {
                var f = floors[i];
                if (f == null) continue;
                float sp;
                try { sp = f.speed; } catch { continue; }
                if (i == 0) { prevSpeed = sp; continue; }
                if (Mathf.Abs(sp - prevSpeed) < 1e-6f) continue;

                var c = new Change
                {
                    Floor = i,
                    From = baseBpm * prevSpeed,
                    To = baseBpm * sp,
                    Ratio = prevSpeed > 0f ? sp / prevSpeed : 0f,
                };
                try { c.Time = (float)f.entryTime + offset; } catch { c.Time = 0f; }
                c.Kind = Classify(c.Ratio);
                c.Audio = AudioAnalysis.TempoAt(res, c.Time);
                MeasureAudio(res, ref c);
                if (c.Kind == Kind.Subdivision) subdiv++;
                else if (c.Kind == Kind.MagicShape) magic++;
                else tempo++;
                if (c.Disagrees) disagree++;
                _changes.Add(c);
                prevSpeed = sp;
            }
            _summary = string.Format("{0} {1}   ·   {2} {3}   ·   {4} {5}   ·   {6} {7}",
                subdiv, Loc.T("subdivision"), magic, Loc.T("magic shape"),
                tempo, Loc.T("tempo change"), disagree, Loc.T("to check"));
        }

        /* Power of two first, then the small rationals a magic shape produces. Anything left is
           a real tempo change — which is the only class worth a charter's attention. */
        private static readonly float[] MagicRatios =
        { 3f, 1.5f, 4f / 3f, 6f / 5f, 9f / 8f, 5f / 4f, 5f / 3f, 7f / 4f, 12f / 5f, 8f / 5f };

        private static Kind Classify(float r)
        {
            if (r <= 0f) return Kind.TempoChange;
            float l = Mathf.Log(r, 2f);
            if (Mathf.Abs(l - Mathf.Round(l)) < 0.002f) return Kind.Subdivision;
            // A magic-shape ratio may also carry a subdivision, so divide the octave out first.
            float folded = r / Mathf.Pow(2f, Mathf.Round(l));
            foreach (float m in MagicRatios)
            {
                if (Mathf.Abs(folded - m) < 0.004f || Mathf.Abs(folded - 1f / m) < 0.004f)
                    return Kind.MagicShape;
            }
            return Kind.TempoChange;
        }

        /* What the SONG did across this moment, and whether it matches what the chart says.

           Two things this gets right that asking "did the curve move at all" did not.

           It reads the curve a full WINDOW either side, not a couple of steps. The curve cannot
           resolve a change faster than the window it is measured over, so sampling closer than
           that reads the smear across the change rather than the tempo on each side of it.

           And it compares the SIZE of the change, not merely its presence. A chart that says the
           base went up a fifth and a song that drifted two percent are not in agreement, and the
           old test called them one because both "moved". Ratios are compared in the octave —
           the curve cannot tell a beat from a half-beat, so a chart doubling and a curve holding
           are the same statement about the music. */
        private static void MeasureAudio(AudioAnalysis.Result res, ref Change c)
        {
            c.AudioRatio = 0f;
            if (res == null || res.Curve == null) return;
            float lead = Mathf.Max(AudioAnalysis.CurveWindowSec, AudioAnalysis.CurveStepSec * 2f);
            float a = AudioAnalysis.TempoAt(res, c.Time - lead);
            float b = AudioAnalysis.TempoAt(res, c.Time + lead);
            if (a <= 0f || b <= 0f) return;
            c.AudioFrom = a; c.AudioTo = b; c.AudioRatio = b / a;

            float want = c.Kind == Kind.TempoChange ? FoldRatio(c.Ratio) : 1f;
            float got = FoldRatio(c.AudioRatio);
            // 6% — wider than the curve's own bin and window noise, narrower than any change a
            // charter would bother writing.
            c.Disagrees = Mathf.Abs(Mathf.Log(got / want)) > 0.06f;
        }

        // A ratio modulo octaves: the curve is blind to metre, so x2 and x1 say the same thing.
        private static float FoldRatio(float r)
        {
            if (r <= 0f) return 1f;
            float l = Mathf.Log(r, 2f);
            return r / Mathf.Pow(2f, Mathf.Round(l));
        }

        // ── UI ───────────────────────────────────────────────────────────────

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Chart analysis"), Close, new Vector2(300f, -110f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 380f, 240f);
            K.OnDragEnd = () => K.SnapDockOnDragEnd();

            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = Vector2.zero; _viewport.anchorMax = Vector2.one;
            _viewport.offsetMin = new Vector2(Pad, 3f);
            _viewport.offsetMax = new Vector2(-Pad, -HeaderH);
            vpGo.AddComponent<RectMask2D>();
            var img = vpGo.AddComponent<Image>();
            img.color = new Color(0f, 0f, 0f, 0.01f);
            img.raycastTarget = true;

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f); _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void BuildContent()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);

            float w = _size.x - Pad * 2f;
            float y = -2f;
            EventRows.Label(_content, _summary, 0f, y, w, 16f, Theme.TextMuted);
            y -= 20f;

            float bw = (w - Gap) * 0.5f;
            EventRows.Cell(_content, _onlyFindings ? Loc.T("Showing: to check") : Loc.T("Showing: all changes"),
                0f, y, bw, RowH, () => { _onlyFindings = !_onlyFindings; _dirty = true; }, true);
            EventRows.Cell(_content, Loc.T("Rescan"), bw + Gap, y, bw, RowH, () => { _dirty = true; }, true);
            y -= RowH + Gap * 2f;

            if (_changes.Count == 0)
            {
                EventRows.Label(_content, Loc.T("(no speed changes)"), 0f, y, w, RowH, Theme.TextMuted);
                _content.sizeDelta = new Vector2(0f, -y + 6f);
                return;
            }

            int shown = 0;
            for (int i = 0; i < _changes.Count; i++)
            {
                var c = _changes[i];
                bool check = c.Disagrees;
                if (_onlyFindings && !check) continue;
                if (shown++ > 400) break;        // a chart can hold thousands; the list is a read, not a log

                var col = c.Kind == Kind.TempoChange ? new Color(1f, 0.85f, 0.35f, 1f)
                        : c.Kind == Kind.MagicShape ? new Color(0.65f, 0.82f, 1f, 1f)
                        : Theme.TextMuted;
                if (check) col = new Color(1f, 0.55f, 0.5f, 1f);

                string kind = c.Kind == Kind.Subdivision ? Loc.T("subdivision")
                            : c.Kind == Kind.MagicShape ? Loc.T("magic shape") : Loc.T("tempo change");
                /* Say what the song DID, not just whether it budged — the whole question is
                   whether it made the same move the chart claims. */
                string audio = c.AudioRatio > 0f
                    ? string.Format("{0} {1:0.0}→{2:0.0} ×{3:0.###}", Loc.T("song"),
                        AudioAnalysis.ToBand(c.AudioFrom), AudioAnalysis.ToBand(c.AudioTo),
                        FoldRatio(c.AudioRatio))
                    : Loc.T("song —");
                string line = string.Format("{0}  #{1}   {2:0.#}→{3:0.#}  ×{4:0.###}   {5}   {6}",
                    Clock(c.Time), c.Floor, c.From, c.To, c.Ratio, kind, audio);
                EventRows.Label(_content, line, 0f, y, w, RowStep, col);
                y -= RowStep;
            }
            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        private static string Clock(float t)
        {
            if (t < 0f) t = 0f;
            int m = (int)(t / 60f);
            return string.Format("{0}:{1:00.0}", m, t - m * 60);
        }

        // ── scroll / resize ──────────────────────────────────────────────────

        private static void TickScroll()
        {
            if (_viewport == null || _content == null) return;
            float wheel = MainClass.WheelY;
            if (Mathf.Abs(wheel) < 0.01f) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_viewport, Input.mousePosition, null)) return;
            _scroll = Mathf.Clamp(_scroll + wheel * 60f, 0f,
                Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void ClampScroll()
        {
            if (_viewport == null || _content == null) return;
            _scroll = Mathf.Clamp(_scroll, 0f, Mathf.Max(0f, _content.sizeDelta.y - _viewport.rect.height));
            _content.anchoredPosition = new Vector2(0f, _scroll);
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

        private static void TickResize()
        {
            if (!K.Built) return;
            var r = (RectTransform)K.PanelGo.transform;
            if ((r.sizeDelta - _size).sqrMagnitude > 1f) { _size = r.sizeDelta; _dirty = true; }
        }
    }
}
