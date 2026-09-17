using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Audio window: the song's waveform, a scrub you can drag, the position in milliseconds, and
       the offset suggestion that used to sit beside the level-settings field.

       Preview playback runs on Sapphire's OWN AudioSource, never the conductor's. The conductor
       is the editor's clock — every floor time, the playhead and the hitsound schedule read from
       it — so seeking it to audition a moment would desync the thing you are editing. A private
       source means scrubbing is free and stops mattering the moment the window closes. */
    internal static class EditorVisualizer
    {
        private static readonly PanelKit K = new PanelKit("SapphireVisualizer", 907, PanelW, focusable: true);
        private const float PanelW = 580f, WaveH = 84f, CurveH = 54f, HeaderH = 28f + Gap;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private static bool _open;
        private static Vector2 _size = new Vector2(PanelW, 396f);

        /* Stretch. The panel is resizable, but every row is laid out at an absolute y against a
           constant width, so growing it used to buy nothing but empty space. Rebuilding the
           shell per drag frame is not an option (thirty objects destroyed and recreated every
           frame is the same stall the timeline had), so the shell records what has to move and a
           per-frame pass moves it: the two lanes take the spare height, everything under them
           shifts by what they took, and the full-width rows take the spare width. */
        private static readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<RectTransform, float>> _below =
            new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<RectTransform, float>>();
        private static readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<RectTransform, float>> _wide =
            new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<RectTransform, float>>();
        // Rects pinned to the panel's RIGHT edge: they keep their distance from it when the
        // panel is stretched, instead of stranding in the middle.
        private static readonly System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<RectTransform, float>> _rightPinned =
            new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<RectTransform, float>>();
        /* Wrapped text has to be re-MEASURED when the panel width changes, not merely widened:
           a narrower panel wraps the same sentence onto more lines, and the rows beneath it have
           to move by the difference or the text runs straight through them. */
        private struct WrapBox { public TMPro.TextMeshProUGUI T; public float BaseY, BaseH; }
        private static readonly System.Collections.Generic.List<WrapBox> _wrapped =
            new System.Collections.Generic.List<WrapBox>();
        private static float _waveY0, _wrapExtra;
        private static float _naturalH, _curveY0;
        private static Vector2 _laidOut = new Vector2(-1f, -1f);
        private static RectTransform _waveRect, _scrubRect, _onsetRect, _curveRect, _curveScrub;
        private static RawImage _waveImg, _curveImg;
        private static Texture2D _tex, _curveTex;
        private static int _curvePoints;      // what the curve texture was drawn from
        private static int _curveVer = -1;    // ... and which version of its values
        private static string _texClip;
        private static TMPro.TextMeshProUGUI _timeLbl, _noteLbl, _bpmLbl;
        private static bool _noteIsStatus;   // the note is a progress message, not a result
        private static RoundedRectGraphic _playBg;
        private static TMPro.TextMeshProUGUI _playLbl;
        private static float _scrub;          // seconds into the song
        private static float _len = 1f;
        // Visible window, in seconds. The texture is the whole song, so zoom and pan are a
        // uvRect — nothing is re-rendered and the markers just re-map onto the new window.
        private static float _viewT0;
        private static float _viewSpan = 1f;
        private static bool _dragging, _panning;
        private static float _panFrom, _panT0;
        private static AudioSource _preview;
        /* Metronome. Clicks are SCHEDULED against the audio clock, not fired from Tick: a frame
           boundary is up to 16ms away from where the beat actually falls, which is the same
           order as the offset error you would be listening for. PlayScheduled with dspTime is
           sample-accurate, so what you hear is the grid, not the frame rate. */
        private static bool _metro;
        private static AudioClip _clickHi, _clickLo;
        private static AudioSource[] _clickSrc;
        private static int _clickNext;
        private static double _playStartDsp;      // dspTime when the preview started
        private static float _playStartTime;      // clip position it started from
        private static int _beatScheduled = int.MinValue;
        private static float _beatT;              // audio time of the next unscheduled beat
        private static int _beatIdx;              // its index, for bar accents
        private static RoundedRectGraphic _metroBg;
        private static float _volMusic = 0.8f, _volClick = 0.55f;
        private static RectTransform _volMusicFill, _volClickFill;
        private static RectTransform _volMusicTrack, _volClickTrack;
        private static int _volDrag;              // 0 none · 1 music · 2 click
        // Curve axis + hover readout. The lane is useless as a measurement without a scale.
        private static TMPro.TextMeshProUGUI _axHi, _axMid, _axLo, _hoverLbl;
        private static RectTransform _hoverLine;
        private static float _axRangeLo, _axRangeHi, _axCentre;
        /* Everything below the Analyze button is hidden until there is something to show. An
           interface full of dead controls over an unread song is worse than no interface. */
        private static bool _advOpen;
        private static readonly System.Collections.Generic.List<GameObject> _analysed =
            new System.Collections.Generic.List<GameObject>();
        private static RoundedRectGraphic _analyzeBg;
        private static TMPro.TextMeshProUGUI _analyzeLbl;
        private static bool _shellAdv;            // which variant the shell was built for
        // Pooled grid lines. Capped: past ~64 the window is zoomed out far enough that a line
        // per beat is a solid block, which says nothing.
        private static readonly System.Collections.Generic.List<RectTransform> _grid =
            new System.Collections.Generic.List<RectTransform>();
        private const int GridMax = 64;

        internal static bool IsOpen => _open;
        internal static PanelKit Kit => K;
        internal static void SetOpen(bool v) { if (v) Open(); else Close(); }
        internal static void Open() { _open = true; }
        internal static void Close() { _open = false; StopPreview(); }
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
            if (!_open || !inEditor)
            {
                if (_preview != null && _preview.isPlaying) StopPreview();
                K.Show(false);
                return;
            }
            if (!K.Built || _shellAdv != _advOpen) BuildShell();
            bool ready = EnsureTex();
            SetAnalysedVisible(ready);
            if (_analyzeLbl != null)
            {
                string a = AudioAnalysis.Status == AudioAnalysis.State.Loading ? Loc.T("Analysing…")
                         : ready ? Loc.T("Re-analyse") : Loc.T("Analyse");
                if (_analyzeLbl.text != a) _analyzeLbl.text = a;
            }
            if (_waveImg != null && _waveImg.enabled != ready) _waveImg.enabled = ready;
            if (!ready && _scrubRect != null && _scrubRect.gameObject.activeSelf) _scrubRect.gameObject.SetActive(false);
            if (_noteLbl != null)
            {
                // The status note is OURS to clear — nothing else writes this label until a
                // suggestion is made, so leaving it behind stranded "Reading the song…" under a
                // finished analysis for the rest of the session.
                if (!ready)
                {
                    string note = AudioAnalysis.StatusNote ?? Loc.T("Reading the song…");
                    if (_noteLbl.text != note) _noteLbl.text = note;
                    _noteIsStatus = true;
                }
                else if (_noteIsStatus) { _noteLbl.text = ""; _noteIsStatus = false; }
            }
            K.Show(true);
            if (K.DockSide == 0 && Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            TickScrub();
            TickReadout();
            TickTempo();
            TickMetronome(ed);
            TickVolume();
            TickResize();
        }

        internal static void Dispose()
        {
            StopPreview();
            if (_preview != null) UnityEngine.Object.Destroy(_preview.gameObject);
            _preview = null;
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            if (_curveTex != null) UnityEngine.Object.Destroy(_curveTex);
            _tex = null; _texClip = null; _curveTex = null; _curvePoints = 0; _curveVer = -1;
            K.Dispose();
            _waveRect = _scrubRect = _onsetRect = _curveRect = _curveScrub = null; _curveImg = null;
            _waveImg = null; _timeLbl = _noteLbl = _bpmLbl = null; _playBg = null; _playLbl = null;
            _grid.Clear();
            StopClicks();
            if (_clickSrc != null && _clickSrc.Length > 0 && _clickSrc[0] != null)
                UnityEngine.Object.Destroy(_clickSrc[0].gameObject);
            _clickSrc = null; _clickHi = _clickLo = null; _metroBg = null; _metro = false;
            _volMusicFill = _volClickFill = _volMusicTrack = _volClickTrack = null; _volDrag = 0;
            _axHi = _axMid = _axLo = _hoverLbl = null; _hoverLine = null;
            _open = false;
        }

        // ── shell ────────────────────────────────────────────────────────────

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Audio"), Close, new Vector2(420f, -140f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 320f, 220f);
            K.OnDragEnd = () => K.SnapDockOnDragEnd();

            /* K.Rebuild destroyed every child, so ALL of these now hold dead Unity objects.
               _grid especially: TickGrid walks it from index 0 and touching a destroyed
               RectTransform throws, which killed the rest of TickScrub — and with it the curve,
               the hover and the readout — every frame after the first rebuild. The game swallows
               the exception, so it presented as "the visualizer breaks after re-analyse". */
            _analysed.Clear();
            _grid.Clear();
            _below.Clear(); _wide.Clear(); _rightPinned.Clear(); _wrapped.Clear();
            _laidOut = new Vector2(-1f, -1f);
            _texClip = null;                 // rebind the textures to the NEW images below
            _curvePoints = -1; _curveVer = -1;
            _shellAdv = _advOpen;

            float y = -HeaderH;   // clear the title bar; content used to start under it
            // The one control that always works, and the only one before a song has been read.
            float aw = PanelW - Pad * 2f - 96f - Gap;
            _analyzeBg = K.Cell(Loc.T("Analyse"), Pad, y, aw, RowH, () => AudioAnalysis.Request(), true, accent: true);
            _analyzeLbl = _analyzeBg.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            K.Cell(Loc.T("Advanced") + (_advOpen ? " −" : " +"), Pad + aw + Gap, y, 96f, RowH,
                   () => { _advOpen = !_advOpen; }, true, accent: _advOpen);
            y -= RowH + Gap;

            if (_advOpen)
            {
                y = AdvRow(y, Loc.T("Window (s)"), AudioAnalysis.CurveWindowSec, 1f, 30f,
                           v => AudioAnalysis.CurveWindowSec = v,
                           Loc.T("How much audio each reading spans. The graph cannot resolve a change shorter than this. Longer reads steadier and more precisely.   ·   1-30, default 8"));
                y = AdvRow(y, Loc.T("Step (s)"), AudioAnalysis.CurveStepSec, 0.5f, 10f,
                           v => AudioAnalysis.CurveStepSec = v,
                           Loc.T("How often a reading is taken. Denser graph, slower analysis — but it does not sharpen a change; the window does.   ·   0.5-10, default 3"));
                // Auto picks the smoothness by model selection each analysis; the field below
                // then shows what it chose, and typing in it turns Auto off.
                K.Cell(Loc.T("Auto") + (AudioAnalysis.AutoTune ? " ✓" : ""), Pad, y, 96f, RowH,
                       () => { AudioAnalysis.AutoTune = !AudioAnalysis.AutoTune; AudioAnalysis.Recurve(); },
                       true, accent: AudioAnalysis.AutoTune);
                y -= RowH + 2f;
                var autoHelp = Loc.T("Fits Smoothness to the song by asking which value explains the audio with the fewest invented changes. Off by default: on songs that hop between half and double it prefers the hop, because hopping scores well.");
                {
                    float hw2 = PanelW - Pad * 2f - 10f;
                    var th = K.Label(autoHelp, Pad + 10f, y, hw2, 20f, new Color(1f, 1f, 1f, 0.52f), 10.5f);
                    th.alignment = TMPro.TextAlignmentOptions.TopLeft;
                    th.textWrappingMode = TMPro.TextWrappingModes.Normal;
                    float hh2 = Mathf.Ceil(th.GetPreferredValues(autoHelp, hw2, 0f).y) + 2f;
                    th.rectTransform.sizeDelta = new Vector2(hw2, hh2);
                    _wrapped.Add(new WrapBox { T = th, BaseY = y, BaseH = hh2 });
                    y -= hh2 + Gap + 4f;
                }
                y = AdvRow(y, Loc.T("Smoothness (λ)"), AudioAnalysis.CurveLambda, 0f, 200f,
                           v => { AudioAnalysis.CurveLambda = v; AudioAnalysis.AutoTune = false; },
                           Loc.T("What a small tempo change costs the graph. High holds one line through noise but commits to a real shift late; low reacts promptly and also follows noise. Typing here turns Auto off.   ·   0-200, default 12"));
                y = AdvRow(y, Loc.T("Octave cost"), AudioAnalysis.CurveOctCost, 0f, 200f,
                           v => AudioAnalysis.CurveOctCost = v,
                           Loc.T("Extra cost for a jump bigger than 1.35x — an octave flip rather than a tempo change. This is what lets Smoothness stay low enough to catch a shift promptly without the line hopping between half and double.   ·   0-200, default 36"));
                y = AdvRow(y, Loc.T("Tempo bins"), AudioAnalysis.CurveBins, 24f, 1024f,
                           v => AudioAnalysis.CurveBins = Mathf.RoundToInt(v),
                           Loc.T("How finely 60-480 BPM is split. A reading is a bin centre, so this is the precision floor: 224 steps 0.94%, 448 steps 0.47%, 896 steps 0.23%. 448 is the knee — doubling it costs double for about two points.   ·   24-1024, default 448"));
                K.Cell(Loc.T("Reset"), Pad, y, 96f, RowH,
                       () => { AudioAnalysis.ResetTunables(); AudioAnalysis.Recurve(); }, true);
                K.Label(Loc.T("Changes apply straight away."), Pad + 96f + Gap * 2f, y,
                        PanelW - Pad * 2f - 96f - Gap * 2f, RowH, new Color(1f, 1f, 1f, 0.52f), 10.5f);
                y -= RowH + Gap + 8f;
            }

            _timeLbl = K.Label("0 ms", Pad, y, PanelW - Pad * 2f, 18f, Theme.Text, 14f);
            Mark(_timeLbl.gameObject); Wide(_timeLbl.rectTransform);
            y -= 22f;

            var waveGo = new GameObject("Wave", typeof(RectTransform));
            waveGo.transform.SetParent(K.RowParent, false);
            _waveRect = (RectTransform)waveGo.transform;
            _waveRect.anchorMin = new Vector2(0f, 1f); _waveRect.anchorMax = new Vector2(1f, 1f);
            _waveRect.pivot = new Vector2(0f, 1f);
            _waveRect.offsetMin = new Vector2(Pad, 0f); _waveRect.offsetMax = new Vector2(-Pad, 0f);
            _waveRect.anchoredPosition = new Vector2(Pad, y);
            _waveRect.sizeDelta = new Vector2(-Pad * 2f, WaveH);
            _waveY0 = y;
            var back = waveGo.AddComponent<RoundedRectGraphic>();
            back.Radius = 4f;
            back.color = new Color(1f, 1f, 1f, 0.05f);
            back.raycastTarget = true;

            var imgGo = new GameObject("Img", typeof(RectTransform));
            imgGo.transform.SetParent(waveGo.transform, false);
            var ir = (RectTransform)imgGo.transform;
            ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
            ir.offsetMin = new Vector2(2f, 2f); ir.offsetMax = new Vector2(-2f, -2f);
            _waveImg = imgGo.AddComponent<RawImage>();
            _waveImg.raycastTarget = false;
            _waveImg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.65f);

            /* Beat grid first, so every marker draws over it. It comes from the level's own BPM
               and offset — no detection involved — and it is the fastest way to judge an offset:
               if the lines sit on the transients, the offset is right. */
            for (int i = 0; i < GridMax; i++)
            {
                var g = Marker(waveGo.transform, new Color(1f, 1f, 1f, 0.18f), 1f);
                g.gameObject.SetActive(false);
                _grid.Add(g);
            }
            _onsetRect = Marker(waveGo.transform, new Color(1f, 0.85f, 0.3f, 0.9f), 1f);
            _scrubRect = Marker(waveGo.transform, Color.white, 2f);
            Mark(waveGo);
            y -= WaveH + Gap;

            /* Tempo over the whole song, on the same time axis and the same uvRect as the
               waveform above it, so zoom and pan move both together. */
            var curveGo = new GameObject("Curve", typeof(RectTransform));
            curveGo.transform.SetParent(K.RowParent, false);
            _curveRect = (RectTransform)curveGo.transform;
            _curveRect.anchorMin = new Vector2(0f, 1f); _curveRect.anchorMax = new Vector2(1f, 1f);
            _curveRect.pivot = new Vector2(0f, 1f);
            _curveRect.offsetMin = new Vector2(Pad, 0f); _curveRect.offsetMax = new Vector2(-Pad, 0f);
            _curveRect.anchoredPosition = new Vector2(Pad, y);
            _curveRect.sizeDelta = new Vector2(-Pad * 2f, CurveH);
            _curveY0 = y;
            var cback = curveGo.AddComponent<RoundedRectGraphic>();
            cback.Radius = 4f;
            cback.color = new Color(1f, 1f, 1f, 0.05f);
            cback.raycastTarget = false;
            var cimgGo = new GameObject("Img", typeof(RectTransform));
            cimgGo.transform.SetParent(curveGo.transform, false);
            var cir = (RectTransform)cimgGo.transform;
            cir.anchorMin = Vector2.zero; cir.anchorMax = Vector2.one;
            cir.offsetMin = new Vector2(2f, 2f); cir.offsetMax = new Vector2(-2f, -2f);
            _curveImg = cimgGo.AddComponent<RawImage>();
            _curveImg.raycastTarget = false;
            _curveImg.color = new Color(1f, 0.85f, 0.35f, 0.9f);
            _curveImg.enabled = false;

            // Axis: the two ends of the drawn range and the anchor through the middle.
            _axHi = AxisLabel(curveGo.transform, 1f);
            _axMid = AxisLabel(curveGo.transform, 0.5f);
            _axLo = AxisLabel(curveGo.transform, 0f);

            Mark(curveGo);
            // The scrub reads across BOTH lanes — judging a tempo reading against the audio under
            // it is the whole point, and that needs one line through both.
            _curveScrub = Marker(curveGo.transform, new Color(1f, 1f, 1f, 0.85f), 2f);
            _hoverLine = Marker(curveGo.transform, new Color(1f, 1f, 1f, 0.35f), 1f);
            _hoverLine.gameObject.SetActive(false);
            var hGo = new GameObject("HoverLbl", typeof(RectTransform));
            hGo.transform.SetParent(curveGo.transform, false);
            var hr = (RectTransform)hGo.transform;
            hr.anchorMin = hr.anchorMax = new Vector2(0f, 1f);
            hr.pivot = new Vector2(0f, 1f);
            hr.sizeDelta = new Vector2(120f, 14f);
            _hoverLbl = UIBuilder.Tmp(hGo, "", 10.5f, TextAnchor.UpperLeft, Color.white);
            _hoverLbl.raycastTarget = false;
            hGo.SetActive(false);
            y -= CurveH + Gap;

            /* The wheel zooms and a right-drag pans, but a control nobody can see is a control
               nobody uses — these say so out loud and give a way back to the whole song. */
            float x = Pad;
            _playBg = K.Cell("▶", x, y, 44f, RowH, TogglePreview, true); Mark(_playBg.gameObject); Below(_playBg); x += 44f + Gap;
            _playLbl = _playBg.GetComponentInChildren<TMPro.TextMeshProUGUI>();
            // Clicks the level's OWN grid, so playing it over the song is the direct test of
            // whether the BPM and offset are right.
            _metroBg = K.Cell(Loc.T("Click"), x, y, 46f, RowH,
                              () => { _metro = !_metro; _beatScheduled = int.MinValue; }, true);
            Mark(_metroBg.gameObject); Below(_metroBg);
            x += 46f + Gap;
            Row(K.Cell("−", x, y, 28f, RowH, () => ZoomBy(1f / 1.6f), true)); x += 28f + Gap;
            Row(K.Cell("+", x, y, 28f, RowH, () => ZoomBy(1.6f), true)); x += 28f + Gap;
            Row(K.Cell(Loc.T("Fit"), x, y, 38f, RowH,
                       () => { _viewT0 = 0f; _viewSpan = _len; }, true));
            x += 38f + Gap;
            var suggest = K.Cell(Loc.T("Suggest offset"), x, y, Mathf.Max(80f, PanelW - Pad - x), RowH,
                ApplySuggestion, true);
            Row(suggest); Wide(suggest.rectTransform);
            y -= RowH + Gap;

            /* The tempo readout gets a ROW OF ITS OWN. Sharing the ladder's line meant a 150px
               box holding "Tempo 95.23 (unsure) · here 301.2 · 249.7-301.2 (±27.1%)", which
               wrapped to three lines and printed straight through the buttons above and the
               volume sliders below. Full width, one line, elided if it still will not fit. */
            _bpmLbl = K.Label("", Pad, y, PanelW - Pad * 2f, RowH, Theme.TextMuted, 11.5f);
            _bpmLbl.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            _bpmLbl.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            Mark(_bpmLbl.gameObject); Below(_bpmLbl.rectTransform); Wide(_bpmLbl.rectTransform);
            y -= RowH + Gap;

            /* Detected tempo. It writes nothing on its own: the estimate is only trustworthy on
               about a quarter of songs, and autocorrelation cannot tell a beat from a half-beat,
               so the multiples are offered rather than guessed at. */
            /* A power-of-two ladder, because that is what charters actually use: of 31,220
               speed multipliers across 429 published charts, 78.5% are exact powers of two.
               Almost all of the rest are magic-shape compensation — x3 and x1/3 (180/60), x1.5
               and x2/3 (180/120), x4/3, x6/5, x9/8 — which hold the INPUT tempo steady while the
               angles change, so they are not tempo changes either. A ratio that is neither is
               the real signal that the base BPM moved. */
            float bx = Pad;
            Row(K.Cell("÷4", bx, y, 34f, RowH, () => ApplyBpm(0.25f), true)); bx += 34f + Gap;
            Row(K.Cell("÷2", bx, y, 34f, RowH, () => ApplyBpm(0.5f), true)); bx += 34f + Gap;
            Row(K.Cell(Loc.T("Use"), bx, y, 44f, RowH, () => ApplyBpm(1f), true)); bx += 44f + Gap;
            Row(K.Cell("×2", bx, y, 34f, RowH, () => ApplyBpm(2f), true)); bx += 34f + Gap;
            Row(K.Cell("×4", bx, y, 34f, RowH, () => ApplyBpm(4f), true)); bx += 34f + Gap;
            /* 3/2 is a magic-shape ratio in a CHART, but in AUDIO it is a real metrical
               ambiguity — a compound or dotted feel. 初音ミクの激唱 is charted at 200 and reads
               133.7, which is exactly that, and no power-of-two button can reach it. */
            Row(K.Cell("×1.5", bx, y, 40f, RowH, () => ApplyBpm(1.5f), true)); bx += 40f + Gap;
            Row(K.Cell("÷1.5", bx, y, 40f, RowH, () => ApplyBpm(1f / 1.5f), true)); bx += 40f + Gap;
            // Both at once, from the audio alone — the way to start a level from nothing.
            Row(K.Cell(Loc.T("Seed"), bx, y, 44f, RowH, ApplyBoth, true)); bx += 44f + Gap;
            // The chart-side counterpart: what the level's own speed changes mean against this.
            Row(K.Cell(Loc.T("Chart"), bx, y, 52f, RowH,
                () => { EditorChartAnalysis.Open(); EditorChartAnalysis.Kit.BringToFront(); }, true));
            y -= RowH + Gap;

            // Levels for the two things playing at once: the song, and the click over it. Judging
            // whether a click sits on a transient is a balance problem before it is a timing one.
            float vx = Pad;
            var mus = K.Label(Loc.T("Music"), vx, y, 44f, RowH, Theme.TextMuted, 11.5f);
            Mark(mus.gameObject); Below(mus.rectTransform); vx += 46f;
            _volMusicTrack = VolBar(vx, y, 120f, out _volMusicFill);
            Mark(_volMusicTrack.gameObject); Below(_volMusicTrack); vx += 126f;
            var clk = K.Label(Loc.T("Click"), vx, y, 52f, RowH, Theme.TextMuted, 11.5f);
            Mark(clk.gameObject); Below(clk.rectTransform); vx += 54f;
            _volClickTrack = VolBar(vx, y, 120f, out _volClickFill);
            Mark(_volClickTrack.gameObject); Below(_volClickTrack);
            y -= RowH + Gap;

            _noteLbl = K.Label("", Pad, y, PanelW - Pad * 2f, 30f, Theme.TextMuted, 11.5f);
            _noteLbl.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            _noteLbl.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            Mark(_noteLbl.gameObject); Below(_noteLbl.rectTransform); Wide(_noteLbl.rectTransform);
            y -= 30f;

            _naturalH = -y + Pad;              // what the shell needs at its minimum
            if (_size.y < _naturalH) _size.y = _naturalH;
            panel.sizeDelta = _size;
            ApplyStretch(true);
        }

        // Rows under the two lanes: they slide down by whatever height the lanes take.
        private static void Below(RectTransform r)
        { if (r != null) _below.Add(new System.Collections.Generic.KeyValuePair<RectTransform, float>(r, r.anchoredPosition.y)); }
        private static void Below(RoundedRectGraphic g) { if (g != null) Below(g.rectTransform); }
        private static void Row(RoundedRectGraphic g) { if (g != null) { Mark(g.gameObject); Below(g); } }
        // Rows that should span the panel: they take the spare width.
        private static void Wide(RectTransform r)
        { if (r != null) _wide.Add(new System.Collections.Generic.KeyValuePair<RectTransform, float>(r, r.sizeDelta.x)); }
        private static void RightAligned(RectTransform r)
        { if (r != null) _rightPinned.Add(new System.Collections.Generic.KeyValuePair<RectTransform, float>(r, r.anchoredPosition.x)); }

        /* Spare height goes to the two lanes — the waveform takes the lion's share, since that
           is the thing being read — and everything below them moves by exactly what they took.
           Spare width goes to the rows that span. */
        private static void ApplyStretch(bool force = false)
        {
            if (!K.Built || _waveRect == null || _curveRect == null) return;
            if (!force && (_laidOut - _size).sqrMagnitude < 0.5f) return;
            _laidOut = _size;
            float dw = _size.x - PanelW;

            /* Wrapped help text is re-MEASURED, not merely widened: a narrower panel puts the
               same sentence on more lines. Each box moves by the growth of the boxes above it,
               and everything below the whole block moves by the total — computed from the
               BUILD-TIME positions every frame, never accumulated, or a resize would walk the
               layout down the panel one frame at a time. */
            float wrap = 0f;
            for (int i = 0; i < _wrapped.Count; i++)
            {
                var w = _wrapped[i];
                if (w.T == null) continue;
                var rt = w.T.rectTransform;
                float hw = Mathf.Max(80f, PanelW - Pad * 2f - 10f + dw);
                float hh = Mathf.Ceil(w.T.GetPreferredValues(w.T.text, hw, 0f).y) + 2f;
                rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, w.BaseY - wrap);
                if (Mathf.Abs(rt.sizeDelta.x - hw) > 0.5f || Mathf.Abs(rt.sizeDelta.y - hh) > 0.5f)
                    rt.sizeDelta = new Vector2(hw, hh);
                wrap += hh - w.BaseH;
            }
            _wrapExtra = wrap;

            // Spare height goes to the two lanes, the waveform taking the larger share since it
            // is the thing being read.
            float extra = Mathf.Max(0f, _size.y - (_naturalH + wrap));
            float wh = WaveH + extra * 0.66f, ch = CurveH + extra * 0.34f;
            _waveRect.anchoredPosition = new Vector2(Pad, _waveY0 - wrap);
            _waveRect.sizeDelta = new Vector2(-Pad * 2f, wh);
            _curveRect.anchoredPosition = new Vector2(Pad, _curveY0 - wrap - (wh - WaveH));
            _curveRect.sizeDelta = new Vector2(-Pad * 2f, ch);

            float shift = wrap + (wh - WaveH) + (ch - CurveH);
            for (int i = 0; i < _below.Count; i++)
            {
                var r = _below[i].Key;
                if (r == null) continue;
                r.anchoredPosition = new Vector2(r.anchoredPosition.x, _below[i].Value - shift);
            }
            for (int i = 0; i < _rightPinned.Count; i++)
            {
                var r = _rightPinned[i].Key;
                if (r == null) continue;
                r.anchoredPosition = new Vector2(_rightPinned[i].Value + dw, r.anchoredPosition.y);
            }
            for (int i = 0; i < _wide.Count; i++)
            {
                var r = _wide[i].Key;
                if (r == null) continue;
                r.sizeDelta = new Vector2(Mathf.Max(60f, _wide[i].Value + dw), r.sizeDelta.y);
            }
            // The lanes changed size, so their textures describe the wrong window now.
            _wT0 = float.NaN; _cT0 = float.NaN;
        }

        private static RectTransform VolBar(float x, float y, float w, out RectTransform fill)
        {
            var go = new GameObject("Vol", typeof(RectTransform));
            go.transform.SetParent(K.RowParent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y - 5f);
            r.sizeDelta = new Vector2(w, RowH - 10f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 3f; bg.color = new Color(1f, 1f, 1f, 0.08f); bg.raycastTarget = true;

            var fGo = new GameObject("F", typeof(RectTransform));
            fGo.transform.SetParent(go.transform, false);
            fill = (RectTransform)fGo.transform;
            fill.anchorMin = new Vector2(0f, 0f); fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = new Vector2(0f, 0f); fill.offsetMax = new Vector2(0f, 0f);
            var fg = fGo.AddComponent<RoundedRectGraphic>();
            fg.Radius = 3f;
            fg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.55f);
            fg.raycastTarget = false;
            return r;
        }

        // Polled like every other drag surface here, so it behaves the same as the scrub.
        private static void TickVolume()
        {
            var m = (Vector2)Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                if (_volMusicTrack != null && RectTransformUtility.RectangleContainsScreenPoint(_volMusicTrack, m, null)) _volDrag = 1;
                else if (_volClickTrack != null && RectTransformUtility.RectangleContainsScreenPoint(_volClickTrack, m, null)) _volDrag = 2;
            }
            if (!Input.GetMouseButton(0)) _volDrag = 0;
            if (_volDrag != 0)
            {
                var track = _volDrag == 1 ? _volMusicTrack : _volClickTrack;
                Vector2 lp;
                if (track != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(track, m, null, out lp))
                {
                    var rect = track.rect;
                    float v = Mathf.Clamp01((lp.x - rect.xMin) / Mathf.Max(1f, rect.width));
                    if (_volDrag == 1) _volMusic = v; else _volClick = v;
                }
            }
            if (_volMusicFill != null && _volMusicTrack != null)
                _volMusicFill.sizeDelta = new Vector2(_volMusic * _volMusicTrack.rect.width, 0f);
            if (_volClickFill != null && _volClickTrack != null)
                _volClickFill.sizeDelta = new Vector2(_volClick * _volClickTrack.rect.width, 0f);
            if (_preview != null && !Mathf.Approximately(_preview.volume, _volMusic)) _preview.volume = _volMusic;
            if (_clickSrc != null)
                foreach (var a in _clickSrc)
                    if (a != null && !Mathf.Approximately(a.volume, _volClick)) a.volume = _volClick;
        }

        // Pinned to a fraction of the lane's height; the text is set when the range is known.
        private static TMPro.TextMeshProUGUI AxisLabel(Transform parent, float frac)
        {
            var go = new GameObject("Ax", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, frac);
            r.pivot = new Vector2(0f, frac);
            r.anchoredPosition = new Vector2(3f, 0f);
            r.sizeDelta = new Vector2(46f, 12f);
            var t = UIBuilder.Tmp(go, "", 9.5f, TextAnchor.UpperLeft, new Color(0.62f, 0.62f, 0.68f, 1f));
            t.raycastTarget = false;
            return t;
        }

        // Everything built between the Analyze row and the end of the shell belongs to the
        // analysed state; one list, hidden wholesale, rather than a flag on each control.
        private static void Mark(GameObject go) { if (go != null) _analysed.Add(go); }

        private static void SetAnalysedVisible(bool on)
        {
            for (int i = 0; i < _analysed.Count; i++)
            {
                var g = _analysed[i];
                if (g != null && g.activeSelf != on) g.SetActive(on);
            }
        }

        /* Each knob carries what it actually does and what the default costs, because the
           point of exposing them is that a song which defeats the defaults is exactly when a
           charter wants to turn one — and a number with no explanation is not a control, it is
           a dare. The range is stated too: every field clamps, silently, to lo..hi. */
        private const float AdvLblW = 132f, AdvFieldW = 74f;

        /* Label and field on one line, the explanation under it in its own measured box.

           MEASURED, not a fixed 26px: the longest of these wraps to four lines and a constant
           height let it print straight through the Reset button below. TMP can tell us how tall
           the text will be at this width before it is laid out, so ask. */
        private static float AdvRow(float y, string label, float value, float lo, float hi,
                                    System.Action<float> set, string help = null)
        {
            // Value column flush RIGHT, numerals right-aligned in it: four settings read as a
            // column of numbers to compare, not four boxes at four different offsets.
            var lbl = K.Label(label, Pad, y, PanelW - Pad * 2f - AdvFieldW - Gap, RowH, Theme.Text, 11.5f);
            Wide(lbl.rectTransform);
            var fr = K.InputField(PanelW - Pad - AdvFieldW, y, AdvFieldW, value.ToString("0.###"), sv =>
            {
                float v;
                if (!float.TryParse(sv, out v)) return;
                set(Mathf.Clamp(v, lo, hi));
                /* Apply immediately. The song is already decoded and its envelope already
                   measured, so a tunable change costs at most a rescore — and for the two
                   decode parameters, not even that. Making the charter press Re-analyse for it
                   meant re-reading the file, which is the slow part and had nothing to do with
                   the knob they turned. */
                AudioAnalysis.Recurve();
            }, TextAnchor.MiddleRight);
            RightAligned(fr);
            y -= RowH + 2f;
            if (!string.IsNullOrEmpty(help))
            {
                float hw = PanelW - Pad * 2f - 10f;
                var t = K.Label(help, Pad + 10f, y, hw, 20f, new Color(1f, 1f, 1f, 0.52f), 10.5f);
                t.alignment = TMPro.TextAlignmentOptions.TopLeft;
                t.textWrappingMode = TMPro.TextWrappingModes.Normal;
                float hh = Mathf.Ceil(t.GetPreferredValues(help, hw, 0f).y) + 2f;
                t.rectTransform.sizeDelta = new Vector2(hw, hh);
                _wrapped.Add(new WrapBox { T = t, BaseY = y, BaseH = hh });
                y -= hh;
            }
            return y - Gap - 4f;
        }

        private static RectTransform Marker(Transform parent, Color col, float w)
        {
            var go = new GameObject("Mark", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.sizeDelta = new Vector2(w, 0f);
            var g = go.AddComponent<RoundedRectGraphic>();
            g.Radius = 0f; g.color = col; g.raycastTarget = false;
            return r;
        }

        // ── waveform texture (same envelope the timeline draws) ──────────────

        /* The two lanes render the VISIBLE WINDOW at display resolution, not the whole song into
           one texture that is then point-sampled down to the panel width.

           The old way is why the waveform showed as a solid block with noise on it: a 2048-column
           texture squeezed into ~780 pixels with point filtering keeps roughly one column in
           three and throws the rest away, so what you saw was an arbitrary sample of the song
           rather than its shape. The curve had the opposite problem — 140 points stretched across
           780 pixels, so every segment was five pixels of staircase.

           Drawing the window instead means one texture column per screen column at every zoom:
           the waveform aggregates however many envelope frames fall in a column, and the curve
           interpolates between its points. Redrawn when the view moves, which is the same cadence
           the timeline strip already uses. */
        private const int ViewTexW = 1024;
        private static float _wT0 = float.NaN, _wSpan;
        private static float _cT0 = float.NaN, _cSpan;
        private static float _waveNorm = 1f;

        // true once there is a texture to show; false while the song is still being read.
        private static bool EnsureTex()
        {
            var res = AudioAnalysis.Get();
            if (res == null || res.Env == null || res.Env.Length < 2) return false;
            if (_tex != null && _texClip == res.ClipName)
            {
                if (_waveImg != null && _waveImg.texture != _tex) _waveImg.texture = _tex;
                return true;
            }
            _texClip = res.ClipName;
            _len = Mathf.Max(0.001f, res.Length);
            _viewT0 = 0f; _viewSpan = _len;
            _wT0 = float.NaN; _cT0 = float.NaN;

            /* Normalise to a high PERCENTILE, not the peak. A modern master is limited to within
               a hair of full scale for most of its length, so scaling by the maximum drew every
               loud song as a solid block. The 98th percentile of the envelope leaves the loud
               body around three quarters height and lets the quiet parts be visible as quiet. */
            var env = res.Env;
            int n = env.Length;
            var sample = new float[Mathf.Min(4096, n)];
            for (int i = 0; i < sample.Length; i++) sample[i] = env[(int)((long)i * n / sample.Length)];
            System.Array.Sort(sample);
            _waveNorm = Mathf.Max(1e-4f, sample[Mathf.Clamp((int)(sample.Length * 0.98f), 0, sample.Length - 1)]);

            const int H = 64;
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _tex = new Texture2D(ViewTexW, H, TextureFormat.RGBA32, false)
            { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
            if (_waveImg != null)
            {
                _waveImg.texture = _tex;
                _waveImg.uvRect = new Rect(0f, 0f, 1f, 1f);   // the texture IS the window now
            }
            if (_onsetRect != null)
            {
                bool has = res.Onset >= 0f;
                if (_onsetRect.gameObject.activeSelf != has) _onsetRect.gameObject.SetActive(has);
                if (has) PlaceMarker(_onsetRect, res.Onset);
            }
            RenderWaveView(res);
            return true;
        }

        /* One texture column per screen column. Each column takes the PEAK of the envelope
           frames inside it for the outline and their MEAN for the solid body, which is what
           makes a waveform readable: the outline shows transients, the body shows level. When a
           column covers less than one frame (deep zoom) the envelope is interpolated instead, so
           the shape stays smooth rather than turning into steps. */
        private static void RenderWaveView(AudioAnalysis.Result res)
        {
            if (_tex == null || res == null || res.Env == null) return;
            _wT0 = _viewT0; _wSpan = _viewSpan;
            var env = res.Env; float hop = res.Hop;
            int n = env.Length, H = _tex.height;
            var px = new Color32[ViewTexW * H];
            var body = new Color32(255, 255, 255, 235);
            var edge = new Color32(255, 255, 255, 90);
            var clear = new Color32(255, 255, 255, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            float dt = _viewSpan / ViewTexW;
            for (int x = 0; x < ViewTexW; x++)
            {
                float ta = _viewT0 + x * dt, tb = ta + dt;
                int ia = Mathf.Clamp(Mathf.FloorToInt(ta / hop), 0, n - 1);
                int ib = Mathf.Clamp(Mathf.CeilToInt(tb / hop), ia + 1, n);
                float pk = 0f, mean = 0f;
                for (int i = ia; i < ib; i++) { float v = env[i]; if (v > pk) pk = v; mean += v; }
                mean /= Mathf.Max(1, ib - ia);
                int hp = Mathf.Clamp(Mathf.RoundToInt(pk / _waveNorm * (H * 0.5f)), 1, H / 2);
                int hb = Mathf.Clamp(Mathf.RoundToInt(mean / _waveNorm * (H * 0.5f)), 1, hp);
                for (int d = 0; d <= hp; d++)
                {
                    var c = d <= hb ? body : edge;
                    int y0 = H / 2 - d, y1 = H / 2 + d;
                    if (y0 >= 0) px[y0 * ViewTexW + x] = c;
                    if (y1 < H) px[y1 * ViewTexW + x] = c;
                }
            }
            _tex.SetPixels32(px);
            _tex.Apply(false);
        }

        // ── view · scrub · pan ───────────────────────────────────────────────

        private const float MinSpan = 0.05f;   // 50ms across the window is as far in as it goes

        // Buttons zoom about the SCRUB, the wheel about the cursor: in both cases the thing you
        // are looking at is the thing that stays put.
        private static void ZoomBy(float factor)
        {
            float anchor = Mathf.Clamp(_scrub, _viewT0, _viewT0 + _viewSpan);
            float frac = _viewSpan > 1e-6f ? (anchor - _viewT0) / _viewSpan : 0.5f;
            _viewSpan = Mathf.Clamp(_viewSpan / factor, MinSpan, _len);
            _viewT0 = anchor - frac * _viewSpan;
            ClampView();
        }

        private static void ClampView()
        {
            _viewSpan = Mathf.Clamp(_viewSpan, MinSpan, _len);
            _viewT0 = Mathf.Clamp(_viewT0, 0f, Mathf.Max(0f, _len - _viewSpan));
        }

        // seconds ↔ local x within the waveform rect
        private static float TimeAt(float localX)
        {
            var rect = _waveRect.rect;
            return _viewT0 + Mathf.Clamp01((localX - rect.xMin) / Mathf.Max(1f, rect.width)) * _viewSpan;
        }

        // Same mapping without the clamp, so a drag past the edge keeps moving the scrub while
        // the view pans under it instead of the two fighting each other.
        private static float TimeAtUnclamped(float localX)
        {
            var rect = _waveRect.rect;
            return _viewT0 + (localX - rect.xMin) / Mathf.Max(1f, rect.width) * _viewSpan;
        }

        private static void TickScrub()
        {
            if (_waveRect == null) return;
            var m = (Vector2)Input.mousePosition;
            bool over = RectTransformUtility.RectangleContainsScreenPoint(_waveRect, m, null);
            Vector2 lp;
            bool haveLocal = RectTransformUtility.ScreenPointToLocalPointInRectangle(_waveRect, m, null, out lp);

            /* Zoom about the cursor: the time under the pointer stays put, which is the only
               zoom that lets you keep a transient in view while closing in on it. */
            float wheel = MainClass.WheelY;
            float wheelX = Input.mouseScrollDelta.x;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (over && haveLocal && Mathf.Abs(wheel) > 0.01f && !shift)
            {
                /* A trackpad reports fractional deltas — 0.1 per flick is common — and raising
                   0.85 to that power is a 1.6% change nobody can see. Floor the step at half a
                   notch so a nudge is a nudge either way. */
                float step = Mathf.Sign(wheel) * Mathf.Max(0.5f, Mathf.Abs(wheel));
                float anchor = TimeAt(lp.x);
                float rectW = Mathf.Max(1f, _waveRect.rect.width);
                float frac = Mathf.Clamp01((lp.x - _waveRect.rect.xMin) / rectW);
                _viewSpan = Mathf.Clamp(_viewSpan * Mathf.Pow(0.8f, step), MinSpan, _len);
                _viewT0 = anchor - frac * _viewSpan;
                ClampView();
            }
            // Shift+wheel, and a trackpad's own horizontal wheel, pan.
            if (over && (Mathf.Abs(wheelX) > 0.01f || (shift && Mathf.Abs(wheel) > 0.01f)))
            {
                float d = Mathf.Abs(wheelX) > 0.01f ? -wheelX : wheel;
                _viewT0 += d * _viewSpan * 0.12f;
                ClampView();
            }

            // Pan with the right or middle button; left stays the scrub.
            if (over && (Input.GetMouseButtonDown(1) || Input.GetMouseButtonDown(2)) && haveLocal)
            { _panning = true; _panFrom = lp.x; _panT0 = _viewT0; }
            if (_panning && (Input.GetMouseButton(1) || Input.GetMouseButton(2)))
            {
                if (haveLocal)
                {
                    float perPx = _viewSpan / Mathf.Max(1f, _waveRect.rect.width);
                    _viewT0 = _panT0 - (lp.x - _panFrom) * perPx;
                    ClampView();
                }
            }
            else _panning = false;

            if (Input.GetMouseButtonDown(0) && over) _dragging = true;
            if (_dragging && Input.GetMouseButton(0) && haveLocal)
            {
                /* Dragging to the edge PANS rather than stopping. Zoomed in, a scrub that clamps
                   at the window edge cannot reach the rest of the song without letting go,
                   zooming out, and starting again — which is most of the work of scrubbing. The
                   rate grows with how far past the edge the pointer is, so a small overshoot
                   creeps and a large one travels. */
                var rr = _waveRect.rect;
                float outL = rr.xMin - lp.x, outR = lp.x - rr.xMax;
                float past = Mathf.Max(outL, outR);
                if (past > 0f)
                {
                    float dir = outR > outL ? 1f : -1f;
                    float rate = Mathf.Min(past / Mathf.Max(1f, rr.width), 1.5f);
                    _viewT0 += dir * rate * _viewSpan * Time.unscaledDeltaTime * 2.5f;
                    ClampView();
                }
            }
            if (_dragging && Input.GetMouseButton(0) && haveLocal)
            {
                _scrub = Mathf.Clamp(TimeAtUnclamped(lp.x), 0f, _len);
                if (_preview != null && _preview.isPlaying)
                {
                    _preview.time = Mathf.Min(_scrub, _len - 0.01f);
                    // The clip jumped, so the clip-time/dsp-time mapping has to be re-taken.
                    _playStartTime = _preview.time;
                    _playStartDsp = AudioSettings.dspTime;
                    _beatScheduled = int.MinValue;
                    StopClicks();
                }
            }
            if (!Input.GetMouseButton(0)) _dragging = false;

            // Playing: the scrub IS the playhead, so it tracks the preview source — and the view
            // follows it out of the window rather than leaving you staring at stale audio.
            if (!_dragging && _preview != null && _preview.isPlaying)
            {
                _scrub = _preview.time;
                if (_scrub < _viewT0 || _scrub > _viewT0 + _viewSpan)
                { _viewT0 = _scrub - _viewSpan * 0.1f; ClampView(); }
            }

            ApplyView();
            TickGrid();
            TickCurve();
            TickCurveHover();
            PlaceMarker(_scrubRect, _scrub);
            PlaceMarkerIn(_curveScrub, _curveRect, _scrub);
        }

        /* Bar lines brighter than beats, both drawn from chart time zero — which IS the level's
           offset, so the grid moves when the offset does and you can watch a correction land. */
        private static void TickGrid()
        {
            if (_grid.Count == 0 || _waveRect == null) return;
            if (_grid[0] == null) { _grid.Clear(); return; }   // destroyed by a rebuild
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            float bpm = OffsetSuggest.Bpm(ed);
            float zero = 0f;
            try { if (ed != null && ed.levelData != null) zero = ed.levelData.offset / 1000f; } catch { }
            float cr = bpm > 0f ? 60f / bpm : 0f;

            int used = 0;
            if (cr > 0f)
            {
                // Thin out to bars, then to whole bar groups, rather than drawing a wall of lines.
                int stride = 1;
                while (_viewSpan / (cr * stride) > GridMax) stride *= stride < 4 ? 4 : 2;
                float step = cr * stride;
                float first = zero + Mathf.Ceil((_viewT0 - zero) / step) * step;
                var rect = _waveRect.rect;
                for (float t = first; t <= _viewT0 + _viewSpan && used < _grid.Count; t += step)
                {
                    var g = _grid[used++];
                    if (!g.gameObject.activeSelf) g.gameObject.SetActive(true);
                    float f = (t - _viewT0) / Mathf.Max(1e-6f, _viewSpan);
                    g.anchoredPosition = new Vector2(rect.xMin + f * rect.width, 0f);
                    // Downbeat of a 4/4 bar reads stronger; with a stride the lines already are bars.
                    int beat = Mathf.RoundToInt((t - zero) / cr);
                    bool bar = stride > 1 || (beat % 4 == 0);
                    var col = bar ? new Color(1f, 1f, 1f, 0.32f) : new Color(1f, 1f, 1f, 0.14f);
                    var gg = g.GetComponent<RoundedRectGraphic>();
                    if (gg != null && gg.color != col) gg.color = col;
                }
            }
            for (int i = used; i < _grid.Count; i++)
                if (_grid[i].gameObject.activeSelf) _grid[i].gameObject.SetActive(false);
        }

        /* The curve is drawn as a column-per-pixel texture spanning the whole song, so it shares
           the waveform's uvRect and costs nothing to pan. Redrawn only when the point count
           changes — which is once, when the coroutine finishes. */
        private static void TickCurve()
        {
            if (_curveImg == null) return;
            var res = AudioAnalysis.Get();
            if (res == null) { _curveImg.enabled = false; return; }
            if (res.Curve == null)
            {
                AudioAnalysis.BeginCurve(res);
                _curveImg.enabled = false;
                return;
            }
            if (_curveTex == null)
            {
                _curveTex = new Texture2D(ViewTexW, 48, TextureFormat.RGBA32, false)
                { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
                _cT0 = float.NaN;
            }
            /* Re-assert the binding every frame, not only on creation. A shell rebuild makes a
               NEW RawImage while this texture survives, and a RawImage with no texture draws as
               a solid rectangle — which is the lane going entirely yellow. */
            if (_curveImg.texture != _curveTex)
            {
                _curveImg.texture = _curveTex;
                _curveImg.uvRect = new Rect(0f, 0f, 1f, 1f);
                _cT0 = float.NaN;
            }
            _curveImg.enabled = true;
            // Values changed (a rescale, a re-decode) or the window moved: redraw the window.
            float tol = _viewSpan / ViewTexW * 0.5f;
            bool moved = float.IsNaN(_cT0) || Mathf.Abs(_cT0 - _viewT0) >= tol
                      || Mathf.Abs(_cSpan - _viewSpan) >= tol;
            if (moved || _curveVer != res.CurveVersion || _curvePoints != res.Curve.Length)
                RenderCurveView(res);
        }

        /* The curve drawn as a POLYLINE across the window, one texture column per screen column,
           joined between samples. Previously the texture had one column per curve point — 140 of
           them for a seven-minute song — stretched over the lane, so every segment was a
           five-pixel staircase. Sampling the curve per column instead (AudioAnalysis.TempoAt,
           which interpolates and corrects for the window centre) means the line is as smooth as
           the lane is wide, at any zoom. */
        private static void RenderCurveView(AudioAnalysis.Result res)
        {
            if (_curveTex == null || res.Curve == null || res.Curve.Length < 2) return;
            _cT0 = _viewT0; _cSpan = _viewSpan;
            _curveVer = res.CurveVersion; _curvePoints = res.Curve.Length;
            int H = _curveTex.height;
            SetCurveAxis(res);
            float lo = _axRangeLo, hi = _axRangeHi;
            if (hi <= lo) return;

            var px = new Color32[ViewTexW * H];
            var clear = new Color32(255, 255, 255, 0);
            for (int i = 0; i < px.Length; i++) px[i] = clear;
            // The anchor line, so a steady curve is visibly steady rather than merely centred.
            int midY = Mathf.Clamp(Mathf.RoundToInt((_axCentre - lo) / (hi - lo) * (H - 1)), 0, H - 1);
            var guide = new Color32(255, 255, 255, 28);
            for (int x = 0; x < ViewTexW; x++) px[midY * ViewTexW + x] = guide;

            float dt = _viewSpan / ViewTexW;
            int prevY = -1;
            for (int x = 0; x < ViewTexW; x++)
            {
                float t = _viewT0 + (x + 0.5f) * dt;
                float b = AudioAnalysis.TempoAt(res, t);
                if (b <= 0f) { prevY = -1; continue; }
                float cf = ConfAt(res, t);
                byte a = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(110f, 255f, Mathf.Clamp01(cf / 0.30f))), 110, 255);
                var line = new Color32(255, 255, 255, a);
                int y = Mathf.Clamp(Mathf.RoundToInt((b - lo) / (hi - lo) * (H - 1)), 0, H - 1);
                // Join to the previous column, so a step reads as a vertical edge rather than
                // two disconnected dots.
                int y0 = prevY < 0 ? y : Mathf.Min(y, prevY);
                int y1 = prevY < 0 ? y : Mathf.Max(y, prevY);
                for (int yy = y0; yy <= y1; yy++) px[yy * ViewTexW + x] = line;
                prevY = y;
            }
            _curveTex.SetPixels32(px);
            _curveTex.Apply(false);
        }

        private static float ConfAt(AudioAnalysis.Result res, float t)
        {
            if (res.CurveConf == null || res.CurveConf.Length == 0) return 0f;
            int i = Mathf.Clamp(Mathf.RoundToInt((t - AudioAnalysis.CurveWindowSec * 0.5f)
                                                 / Mathf.Max(1e-6f, AudioAnalysis.CurveStepSec)),
                                0, res.CurveConf.Length - 1);
            return res.CurveConf[i];
        }

        private static void SetCurveAxis(AudioAnalysis.Result res)
        {
            if (res.CurveHi <= 0f) return;
            /* Fixed minimum scale, centred on the global tempo. Auto-fitting to the curve's own
               min and max was actively misleading: a song that holds 400 BPM to within half a
               percent had that half percent stretched across the whole lane and read as violent
               fluctuation. The window is now at least +/-12%, so flat looks flat, while a real
               journey — Parallel Universe Shifter runs 70 to 131 — still grows to fit. */
            /* Anchor on the CURVE, not the global estimate, whenever the two disagree. The
               global is a 30s autocorrelation and on a song it cannot read — Parallel Universe
               Shifter comes back 95 where the chart says 300 — centring the lane on it pins the
               whole curve against the top edge and wastes half the lane. When they do agree the
               global is the better-measured number, so it keeps the anchor and a steady line
               sits exactly on the guide. */
            float centre = res.CurveMedian > 0f ? res.CurveMedian
                         : res.Tempo > 0f ? res.Tempo : (res.CurveLo + res.CurveHi) * 0.5f;
            if (res.Tempo > 0f && res.CurveMedian > 0f
                && Mathf.Abs(Mathf.Log(res.Tempo / res.CurveMedian)) < 0.10f) centre = res.Tempo;
            if (centre <= 0f) return;
            float need = Mathf.Max(Mathf.Abs(res.CurveHi - centre), Mathf.Abs(centre - res.CurveLo));
            float half = Mathf.Max(centre * 0.12f, need * 1.15f);
            float lo = centre - half, hi = centre + half;
            _axRangeLo = lo; _axRangeHi = hi; _axCentre = centre;
            if (_axHi != null) _axHi.text = hi.ToString("0.#");
            if (_axMid != null) _axMid.text = centre.ToString("0.#");
            if (_axLo != null) _axLo.text = lo.ToString("0.#");
        }

        private static void TickCurveHover()
        {
            if (_curveRect == null || _hoverLine == null || _hoverLbl == null) return;
            var res = AudioAnalysis.Get();
            var m = (Vector2)Input.mousePosition;
            Vector2 lp;
            bool over = res != null && res.Curve != null
                && RectTransformUtility.RectangleContainsScreenPoint(_curveRect, m, null)
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(_curveRect, m, null, out lp);
            if (!over)
            {
                if (_hoverLine.gameObject.activeSelf) _hoverLine.gameObject.SetActive(false);
                if (_hoverLbl.gameObject.activeSelf) _hoverLbl.gameObject.SetActive(false);
                return;
            }
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_curveRect, m, null, out lp);
            var rect = _curveRect.rect;
            float frac = Mathf.Clamp01((lp.x - rect.xMin) / Mathf.Max(1f, rect.width));
            float t = _viewT0 + frac * _viewSpan;
            float bpm = AudioAnalysis.TempoAt(res, t);
            if (bpm <= 0f)
            {
                if (_hoverLine.gameObject.activeSelf) _hoverLine.gameObject.SetActive(false);
                if (_hoverLbl.gameObject.activeSelf) _hoverLbl.gameObject.SetActive(false);
                return;
            }
            if (!_hoverLine.gameObject.activeSelf) _hoverLine.gameObject.SetActive(true);
            if (!_hoverLbl.gameObject.activeSelf) _hoverLbl.gameObject.SetActive(true);
            _hoverLine.anchoredPosition = new Vector2(rect.xMin + frac * rect.width, 0f);
            // Flip the label to the other side near the right edge so it never runs off.
            float lx = rect.xMin + frac * rect.width + 6f;
            if (lx > rect.xMax - 120f) lx = rect.xMin + frac * rect.width - 124f;
            var lr = _hoverLbl.rectTransform;
            lr.anchoredPosition = new Vector2(lx, -2f);
            string txt = string.Format("{0:0.0} BPM   {1}", bpm, Clock(t));
            if (_hoverLbl.text != txt) _hoverLbl.text = txt;
        }

        private static string Clock(float t)
        {
            if (t < 0f) t = 0f;
            int mm = (int)(t / 60f);
            return string.Format("{0}:{1:00.0}", mm, t - mm * 60f);
        }

        // Redraw when the window has moved by as much as one texture column; below that the
        // picture would not change.
        private static void ApplyView()
        {
            if (_waveImg == null || _len <= 0f || _tex == null) return;
            float tol = _viewSpan / ViewTexW * 0.5f;
            if (!float.IsNaN(_wT0) && Mathf.Abs(_wT0 - _viewT0) < tol
                && Mathf.Abs(_wSpan - _viewSpan) < tol) return;
            var res = AudioAnalysis.Get();
            if (res != null) RenderWaveView(res);
        }

        // Off-window markers park outside the rect rather than pinning to an edge, where they
        // would read as "the onset is right here".
        private static void PlaceMarker(RectTransform r, float t) { PlaceMarkerIn(r, _waveRect, t); }

        private static void PlaceMarkerIn(RectTransform r, RectTransform host, float t)
        {
            if (r == null || host == null) return;
            var rect = host.rect;
            float f = (t - _viewT0) / Mathf.Max(1e-6f, _viewSpan);
            bool inView = f >= -0.01f && f <= 1.01f;
            if (r.gameObject.activeSelf != inView) r.gameObject.SetActive(inView);
            if (!inView) return;
            float x = rect.xMin + f * rect.width;
            if (!Mathf.Approximately(r.anchoredPosition.x, x)) r.anchoredPosition = new Vector2(x, 0f);
        }

        private static void TickReadout()
        {
            if (_timeLbl == null) return;
            int ms = Mathf.RoundToInt(_scrub * 1000f);
            string t = ms + " ms   (" + (_scrub).ToString("0.000") + " s)"
                     + (_viewSpan < _len - 1e-4f ? "   ·  " + _viewSpan.ToString("0.00") + " s " + Loc.T("shown") : "");
            if (_timeLbl.text != t) _timeLbl.text = t;
            if (_playLbl != null)
            {
                string g = _preview != null && _preview.isPlaying ? "❚❚" : "▶";
                if (_playLbl.text != g) _playLbl.text = g;
            }
        }

        // ── preview playback ─────────────────────────────────────────────────

        private static void TogglePreview()
        {
            if (_preview != null && _preview.isPlaying) { StopPreview(); return; }
            var clip = AudioAnalysis.PlayableClip();
            if (clip == null)
            {
                // Lost the clip (a scene load eats it). Ask for it back rather than sitting dead.
                if (AudioAnalysis.Requested && AudioAnalysis.Status != AudioAnalysis.State.Loading)
                    AudioAnalysis.Request();
                Note(AudioAnalysis.StatusNote ?? Loc.T("Reading the song…"));
                return;
            }
            // The source itself can be destroyed with the scene; Unity's null is the tell.
            if (_preview == null)
            {
                var go = new GameObject("SapphireAudioPreview");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _preview = go.AddComponent<AudioSource>();
                _preview.playOnAwake = false;
                _preview.loop = false;
            }
            if (_preview == null) { Note(Loc.T("Audio preview unavailable.")); return; }
            _preview.clip = clip;
            _preview.volume = _volMusic;
            /* Rewind when the scrub is sitting at the end. Playback drags the scrub along with
               it, so after a track finishes — or after scrubbing into the last moment — pressing
               play started at the end and stopped again immediately, which reads as a dead
               button. Start of the visible window, or the song, whichever is nearer the scrub. */
            float from = _scrub;
            if (from >= clip.length - 0.25f) from = _viewSpan < _len - 0.01f ? _viewT0 : 0f;
            _scrub = Mathf.Clamp(from, 0f, Mathf.Max(0f, clip.length - 0.01f));
            _preview.time = _scrub;
            _preview.Play();
            _playStartTime = _preview.time;
            _playStartDsp = AudioSettings.dspTime;
            _beatScheduled = int.MinValue;
        }

        private static void StopPreview()
        {
            try { if (_preview != null && _preview.isPlaying) _preview.Stop(); } catch { }
            StopClicks();
            _beatScheduled = int.MinValue;
        }

        // ── metronome ────────────────────────────────────────────────────────

        private static void TickMetronome(scnEditor ed)
        {
            if (_metroBg != null)
            {
                var ac = Theme.Accent;
                var want = _metro ? new Color(ac.r, ac.g, ac.b, 0.45f) : new Color(1f, 1f, 1f, 0.08f);
                if (_metroBg.color != want) _metroBg.color = want;
            }
            if (!_metro || _preview == null || !_preview.isPlaying) { _beatScheduled = int.MinValue; return; }

            float bpm = OffsetSuggest.Bpm(ed);
            float off = 0f;
            try { if (ed != null && ed.levelData != null) off = ed.levelData.offset / 1000f; } catch { }
            if (bpm <= 0f) return;
            _metroBpm = bpm;
            float cr = 60f / bpm;
            if (cr <= 0.02f) return;              // 3000 BPM of clicks is not a verification aid

            EnsureClicks();
            if (_clickSrc == null) return;

            /* Map clip time to the audio clock. The preview reports where it is, but scheduling
               has to happen in dspTime, and the two only line up through the moment playback
               began — reading _preview.time per frame would reintroduce the frame jitter this
               exists to avoid. */
            double now = AudioSettings.dspTime;
            double horizon = now + 0.35;          // schedule this far ahead
            float tNow = _playStartTime + (float)(now - _playStartDsp);

            if (_beatScheduled == int.MinValue) SeekBeat(off, cr, tNow);

            for (int guard = 0; guard < 64; guard++)
            {
                double dsp = _playStartDsp + (_beatT - _playStartTime);
                if (dsp > horizon) break;
                if (dsp > now)
                {
                    var src = _clickSrc[_clickNext % _clickSrc.Length];
                    _clickNext++;
                    // Bar downbeats accented — a click track with no bar is hard to keep place in.
                    src.clip = (_beatIdx % 4 == 0) ? _clickHi : _clickLo;
                    src.PlayScheduled(dsp);
                }
                StepBeat(cr);
            }
        }

        /* The beat grid FOLLOWS the detected tempo rather than repeating one interval. A song
           that accelerates leaves a fixed grid behind within a few bars, which makes the click
           useless on exactly the levels whose timing is hardest to check. So each beat's length
           is read from the tempo curve where that beat falls, and the grid is integrated forward
           from the offset rather than multiplied out from it.

           The curve is folded into the LEVEL's octave first: the detection may legitimately sit
           at half or double what the charter wrote, and a click track at half speed is not
           comparable to the grid being verified. Rate changes carry over; the density does not. */
        private static float BeatLen(float fallbackCrotchet, float t, float levelBpm)
        {
            var res = AudioAnalysis.Get();
            /* Only follow the curve where following it can WIN. The grid is integrated forward
               beat by beat, so every per-beat error compounds: on a song whose tempo does not
               actually move, a curve wobbling half a percent puts the click a beat out by the
               second chorus, which is exactly the "off-beat at the right BPM" this was reported
               as. A steady song gets one fixed interval from the level's own BPM — nothing to
               drift — and the curve is read only when it is both trustworthy and genuinely
               moving, which is the case the integration exists for. */
            if (!AudioAnalysis.CurveMoves(res)) return fallbackCrotchet;
            float bpm = AudioAnalysis.TempoAt(res, t);
            if (bpm <= 0f) return fallbackCrotchet;
            if (levelBpm > 0f) bpm = AudioAnalysis.FoldOctave(bpm, levelBpm);
            if (bpm < 20f || bpm > 1200f) return fallbackCrotchet;
            return 60f / bpm;
        }

        private static float _metroBpm;           // level BPM the grid is being folded against

        private static void StepBeat(float cr)
        {
            _beatT += BeatLen(cr, _beatT, _metroBpm);
            _beatIdx++;
        }

        // Integrate forward from the offset to the first beat at or after `target`. A few hundred
        // steps for a position deep in a song, which is nothing, and it keeps the bar count
        // honest — counting bars from a variable grid is the whole point of integrating.
        private static void SeekBeat(float off, float cr, float target)
        {
            _beatT = off; _beatIdx = 0;
            int guard = 0;
            while (_beatT < target && guard++ < 100000) StepBeat(cr);
            _beatScheduled = 0;
        }

        private static void EnsureClicks()
        {
            if (_clickSrc != null) return;
            _clickHi = MakeClick(2000f);
            _clickLo = MakeClick(1300f);
            var go = new GameObject("SapphireMetronome");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _clickSrc = new AudioSource[8];
            for (int i = 0; i < _clickSrc.Length; i++)
            {
                var a = go.AddComponent<AudioSource>();
                a.playOnAwake = false; a.loop = false; a.volume = _volClick;
                _clickSrc[i] = a;
            }
        }

        // A short sine with an exponential decay: enough pitch to place it against the music,
        // short enough that the attack is the thing you hear.
        private static AudioClip MakeClick(float hz)
        {
            const int rate = 44100;
            int n = rate / 40;                     // 25 ms
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * 90f);
                data[i] = Mathf.Sin(2f * Mathf.PI * hz * t) * env * 0.8f;
            }
            var c = AudioClip.Create("click" + (int)hz, n, 1, rate, false);
            /* Same trap as GetData: SetData has a ReadOnlySpan<float> overload that Mono binds
               here, against a Span this mscorlib does not define. Pin the array one. */
            try
            {
                var mi = typeof(AudioClip).GetMethod("SetData", new[] { typeof(float[]), typeof(int) });
                if (mi != null) mi.Invoke(c, new object[] { data, 0 });
            }
            catch (System.Exception ex) { SapphireLog.Log("Visualizer: click build failed: " + ex.Message); }
            return c;
        }

        private static void StopClicks()
        {
            if (_clickSrc == null) return;
            foreach (var a in _clickSrc) { try { if (a != null) a.Stop(); } catch { } }
        }

        // ── tempo ────────────────────────────────────────────────────────────

        private static void TickTempo()
        {
            if (_bpmLbl == null) return;
            var res = AudioAnalysis.Get();
            string t;
            if (res == null) t = "";
            else
            {
                float conf;
                float bpm = AudioAnalysis.Tempo(res, out conf);
                if (bpm <= 0f) t = Loc.T("Tempo: unknown");
                else
                {
                    bool steady;
                    bool ok = AudioAnalysis.TempoTrusted(res, conf, out steady);
                    t = string.Format("{0} {1:0.00}   {2}", Loc.T("Tempo"), bpm,
                        steady ? Loc.T("(steady)") : ok ? Loc.T("(confident)") : Loc.T("(unsure)"));
                    // With a curve, the number under the scrub is the one that matters.
                    if (res.Curve != null && res.Curve.Length > 0)
                    {
                        float at = AudioAnalysis.TempoAt(res, _scrub);
                        if (at > 0f)
                            t += string.Format("   ·  {0} {1:0.0}", Loc.T("here"), at);
                        if (res.CurveHi > res.CurveLo * 1.005f)
                            t += string.Format("   ·  {0:0.0}–{1:0.0}  (±{2:0.#}%)", res.CurveLo, res.CurveHi,
                                100f * (res.CurveHi - res.CurveLo) * 0.5f / Mathf.Max(1f, bpm));
                    }
                    else if (res.CurveRunning) t += "   ·  " + Loc.T("mapping tempo…");
                }
            }
            if (_bpmLbl.text != t) _bpmLbl.text = t;
        }

        /* Chart-free start: BPM and offset from the audio alone, for a level that has neither.
           The tempo estimate never looks at the chart, and feeding it back into the phase fold
           gives a grid too — so a brand-new level can be seeded entirely from the song. */
        private static void ApplyBoth()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            var res = AudioAnalysis.Get();
            if (ed == null || res == null) { Note(AudioAnalysis.StatusNote ?? Loc.T("Reading the song…")); return; }
            float conf;
            float bpm = AudioAnalysis.Tempo(res, out conf);
            bool steadySeed;
            if (bpm <= 0f || !AudioAnalysis.TempoTrusted(res, conf, out steadySeed))
            { Note(Loc.T("Tempo is too unclear to apply.")); return; }

            // Offset from the DETECTED tempo's grid, anchored on the first onset so the count-in
            // lands on the same beat a charter would pick.
            float phase = AudioAnalysis.BeatPhase(res, bpm);
            float t = res.Onset >= 0f ? res.Onset : 0f;
            if (phase >= 0f) t = AudioAnalysis.SnapToBeat(t, phase, bpm);
            if (t < 0f) t = 0f;
            int ms = Mathf.RoundToInt(t * 1000f);
            try
            {
                var ev = ed.levelData != null ? ed.levelData.songSettings : null;
                if (ev == null) return;
                using (new SaveStateScope(ed)) { ev["bpm"] = bpm; ev["offset"] = ms; }
                ed.UpdateSongAndLevelSettings();
                Note(string.Format("{0} {1:0.00}  ·  {2} {3} ms", Loc.T("BPM set to"), bpm, Loc.T("offset"), ms));
            }
            catch (System.Exception ex) { SapphireLog.Log("Visualizer: seed failed: " + ex.Message); }
        }

        private static void ApplyBpm(float mult)
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            var res = AudioAnalysis.Get();
            if (ed == null || res == null) return;
            float conf;
            float bpm = AudioAnalysis.Tempo(res, out conf);
            if (bpm <= 0f) { Note(Loc.T("Tempo: unknown")); return; }
            bool steady;
            if (!AudioAnalysis.TempoTrusted(res, conf, out steady))
            { Note(Loc.T("Tempo is too unclear to apply.")); return; }
            float v = bpm * mult;
            if (v < 10f || v > 1000f) { Note(Loc.T("Tempo is too unclear to apply.")); return; }
            try
            {
                var ev = ed.levelData != null ? ed.levelData.songSettings : null;
                if (ev == null) return;
                using (new SaveStateScope(ed)) ev["bpm"] = v;
                ed.UpdateSongAndLevelSettings();
                Note(string.Format("{0} {1:0.00}", Loc.T("BPM set to"), v));
            }
            catch (System.Exception ex) { SapphireLog.Log("Visualizer: bpm write failed: " + ex.Message); }
        }

        private static void Note(string s) { if (_noteLbl != null) _noteLbl.text = s; _noteIsStatus = false; }

        // ── offset suggestion ────────────────────────────────────────────────

        private static void ApplySuggestion()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            int? ms = OffsetSuggest.Compute(ed);
            Note(OffsetSuggest.LastNote ?? "");
            if (!ms.HasValue) return;
            try
            {
                var ev = ed.levelData != null ? ed.levelData.songSettings : null;
                if (ev == null) return;
                using (new SaveStateScope(ed)) ev["offset"] = ms.Value;
                ed.UpdateSongAndLevelSettings();
            }
            catch (System.Exception ex) { SapphireLog.Log("Visualizer: offset write failed: " + ex.Message); }
            EnsureTex();
        }

        // ── misc ─────────────────────────────────────────────────────────────

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
            if ((r.sizeDelta - _size).sqrMagnitude > 1f) _size = r.sizeDelta;
            // The resize handle's floor is a constant; the real floor is the shell's own content,
            // which grows when Advanced is open. Push back rather than letting rows fall out.
            if (_naturalH > 0f && _size.y < _naturalH)
            { _size.y = _naturalH; r.sizeDelta = _size; }
            // A narrow panel wraps the help onto more lines, so the floor rises with it.
            ApplyStretch();
            if (_wrapExtra > 0f && _size.y < _naturalH + _wrapExtra)
            { _size.y = _naturalH + _wrapExtra; r.sizeDelta = _size; }
        }
    }
}
