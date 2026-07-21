using System;
using System.Collections.Generic;
using HarmonyLib;
using DG.Tweening;
using TMPro;
using UnityEngine;
using Sapphire.UI;

namespace Sapphire
{
    /* Track Tools panel (toolbar cell 9) — Sapphire-native front end for TrackToolsEngine.
       Tabs: FADE IN · FADE OUT · EXPLODE (randomized MoveTrack animations) · SIZE (eased
       PositionTrack/ScaleRadius/ScalePlanets ramps) · MULTI (decoration copies of the track,
       with planets / tagged-copy animation) · GENERATE (append tiles from an angle string,
       ghost preview). Based on AdofaiMappingHelper by Sprout34, integrated with permission. */
    internal static class EditorTrackTools
    {
        private static readonly PanelKit K = new PanelKit("SapphireTrackTools", 942, PanelW, focusable: true);
        private static bool _open;
        private static string _status = "";
        private static TextMeshProUGUI _statusTmp;
        private static long _layoutSig = NoSig;
        private const long NoSig = long.MinValue;   // forces a rebuild; the fold below never produces it
        private static int _tab; // 0 fade-in, 1 fade-out, 2 explode, 3 size, 4 multi, 5 generate

        internal static bool IsOpen => _open;

        // ── per-tab state (defaults = MappingHelper's) ───────────────────────

        private class FadeState
        {
            public int From, To = -1;
            public int WinFrom, WinTo;
            public float Duration = 1f;
            public bool MatchBpm;
            public bool XOn = true; public float XMin, XMax;
            public bool YOn = true; public float YMin, YMax;
            public bool RotOn = true; public float RotMin, RotMax;
            public bool ScaleOn = true; public float ScaleMin = 100f, ScaleMax = 100f;
            public bool OpacityOn = true; public float Opacity;
            public float AngleOffset;
            public Ease Ease = Ease.Linear;
            public bool RevertOn = true; public float RevertTo = 100f;
        }
        private static readonly FadeState _in = new FadeState { WinFrom = 8, WinTo = 8, Opacity = 100f };
        private static readonly FadeState _out = new FadeState { WinFrom = -1, WinTo = -1, Opacity = 0f };
        private static readonly FadeState _ex = new FadeState { WinFrom = -4, WinTo = 4, Opacity = 0f };

        // size tab
        private static int _szFrom, _szTo = -1;
        private static bool _szPosOn = true; private static float _szPosA = 100f, _szPosB = 100f;
        private static bool _szRadOn = true; private static float _szRadA = 100f, _szRadB = 100f;
        private static bool _szPlaOn = true; private static float _szPlaA = 100f, _szPlaB = 100f;
        private static Ease _szEase = Ease.Linear;

        // multi tab
        private static int _muMode;   // 0 copies, 1 animate
        private static int _muFrom, _muTo = -1;
        private static bool _muCentral; private static float _muRotation;
        private static string _muTag = "";
        private static bool _muPlanet; private static bool _muPlanetConc;
        private static bool _muDepthOn; private static int _muDepth0 = 1, _muDepthStep = 1;
        private static bool _muParaOn; private static float _muParaA, _muParaB = 100f;
        private static bool _muParaPlanet;
        private static bool _muAppear = true;
        private static bool _muDistributed = true;
        private static int _muTagOffset;
        private static float _muDuration = 1f;
        private static bool _muXOn = true; private static float _muXMin, _muXMax;
        private static bool _muYOn = true; private static float _muYMin, _muYMax;
        private static bool _muRotOn = true; private static float _muRotMin, _muRotMax;
        private static bool _muScaleOn = true; private static float _muScaleMin = 100f, _muScaleMax = 100f;
        private static bool _muPxOn; private static float _muPxMin, _muPxMax;
        private static bool _muOpOn = true; private static float _muOpacity = 100f;
        private static float _muAngle;
        private static Ease _muEase = Ease.Linear;
        private static bool _muRevOn = true; private static float _muRevTo = 100f;
        private static bool _muPRevOn; private static float _muPRevTo;

        // generate tab
        private static int _gtAt = -1;   // -1 = last tile
        private static string _gtAngles = "";
        private static int _gtCount = 4;
        private static bool _gtPreview = true;

        internal static void Toggle()
        {
            _open = !_open;
            _status = "";
            if (_open && SelectionRange(out int min, out int max) && min != max)
            {
                _in.From = _out.From = _ex.From = _szFrom = _muFrom = min;
                _in.To = _out.To = _ex.To = _szTo = _muTo = max;
                _gtAt = max;
                _layoutSig = NoSig;
            }
            EditorToolbar.SyncTrackToolsHighlight();
        }

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool want = _open && ed != null && !ed.playMode && MainClass.EditorSuiteOn
                       && MainClass.Settings != null && MainClass.Settings.FeatToolsMods;
            if (!want)
            {
                K.Show(false);
                SyncPreviewTransition(ed);
                return;
            }
            // Integer fold, not a string: `int + string` boxes each operand and the chain
            // compiles to string.Concat(object[]) — ~5 allocations every frame the panel is
            // open, to detect a layout change that happens on a click. (See EditorCopyPanel.LayoutSig.)
            long sig = 17;
            sig = sig * 31 + _tab;
            sig = sig * 31 + _muMode;
            sig = sig * 31 + (_muCentral ? 1 : 0);
            sig = sig * 31 + (_muAppear ? 1 : 0);
            if (!K.Built || sig != _layoutSig)
            {
                _layoutSig = sig;
                Build();
            }
            K.Show(true);
            SyncPreviewTransition(ed);
        }

        internal static void Dispose()
        {
            K.Dispose();
            _statusTmp = null; _layoutSig = NoSig;
            ClearGhosts();
        }

        // ── range helpers (shared logic in PanelKit) ─────────────────────────

        private static int Len() => PanelKit.LevelLen();
        private static int Resolve(int v) => PanelKit.ResolveTile(v);
        private static bool SelectionRange(out int min, out int max) => PanelKit.SelectionRange(out min, out max);

        private static Tuple<int, int> ResolveRange(int from, int to)
        {
            int len = Len();
            int a = Mathf.Clamp(Resolve(from), 0, len);
            int b = Mathf.Clamp(Resolve(to), 0, len);
            return a <= b ? Tuple.Create(a, b) : Tuple.Create(b, a);
        }

        private static void SetStatus(string s)
        {
            _status = s ?? "";
            if (_statusTmp != null) _statusTmp.text = _status;
        }

        private static void Refresh() { _layoutSig = NoSig; }

        // ── actions ──────────────────────────────────────────────────────────

        private static TrackToolsEngine.RandRange RR(bool on, float min, float max)
            => new TrackToolsEngine.RandRange(on, min, max);

        private static void Run(string label, Action body)
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            if (ADOBase.lm.isOldLevel) { SetStatus(Loc.T("Needs a modern (mesh-floor) level")); return; }
            try
            {
                using (new SaveStateScope(ed)) body();
                SetStatus(Loc.T("Applied"));
            }
            catch (Exception ex)
            {
                SapphireLog.Log("TrackTools " + label + ": " + ex);
                SetStatus(string.Format(Loc.T("{0} failed — see log"), label));
            }
        }

        private static TrackToolsEngine.FadeParams FadeParamsOf(FadeState s)
        {
            var range = ResolveRange(s.From, s.To);
            return new TrackToolsEngine.FadeParams
            {
                From = range.Item1, To = range.Item2,
                WinFrom = s.WinFrom, WinTo = s.WinTo,
                Duration = s.Duration, DurationMatchBpm = s.MatchBpm,
                XPos = RR(s.XOn, s.XMin, s.XMax), YPos = RR(s.YOn, s.YMin, s.YMax),
                Rot = RR(s.RotOn, s.RotMin, s.RotMax), Scale = RR(s.ScaleOn, s.ScaleMin, s.ScaleMax),
                OpacityOn = s.OpacityOn, Opacity = s.Opacity,
                AngleOffset = s.AngleOffset, Ease = s.Ease,
                ScaleRevertOn = s.RevertOn, ScaleRevertTo = s.RevertTo,
            };
        }

        private static void DoFade()
        {
            if (_tab == 0) Run(Loc.T("Fade in"), () => TrackToolsEngine.FadeIn(FadeParamsOf(_in)));
            else if (_tab == 1) Run(Loc.T("Fade out"), () => TrackToolsEngine.FadeOut(FadeParamsOf(_out)));
            else Run(Loc.T("Explode"), () => TrackToolsEngine.Explode(FadeParamsOf(_ex)));
        }

        private static void DoSize()
        {
            var range = ResolveRange(_szFrom, _szTo);
            Run(Loc.T("Size"), () => TrackToolsEngine.SizeChange(new TrackToolsEngine.SizeParams
            {
                From = range.Item1, To = range.Item2,
                PosOn = _szPosOn, PosFrom = _szPosA, PosTo = _szPosB,
                RadiusOn = _szRadOn, RadiusFrom = _szRadA, RadiusTo = _szRadB,
                PlanetsOn = _szPlaOn, PlanetsFrom = _szPlaA, PlanetsTo = _szPlaB,
                Ease = _szEase,
            }));
        }

        private static void DoMulti()
        {
            var range = ResolveRange(_muFrom, _muTo);
            if (_muMode == 0)
            {
                Run(Loc.T("Multi-track"), () => TrackToolsEngine.MultiTrackCreate(new TrackToolsEngine.MultiParams
                {
                    From = range.Item1, To = range.Item2,
                    Centralized = _muCentral, TrackRotation = _muRotation,
                    Tag = _muTag, UsePlanet = _muPlanet, PlanetConcentrated = _muPlanetConc,
                    UseIncreasingDepth = _muDepthOn, InitialDepth = _muDepth0, IncreasingValue = _muDepthStep,
                    ChangeParallax = _muParaOn, ParallaxFrom = _muParaA, ParallaxTo = _muParaB,
                    AffectPlanet = _muParaPlanet,
                }));
            }
            else
            {
                if (string.IsNullOrEmpty(_muTag)) { SetStatus(Loc.T("Enter the copies' tag first")); return; }
                Run(Loc.T("Animate copies"), () => TrackToolsEngine.MultiTrackAnimate(new TrackToolsEngine.MultiAnimParams
                {
                    From = range.Item1, To = range.Item2,
                    Appear = _muAppear, Distributed = _muDistributed,
                    Tag = _muTag, TagOffset = _muTagOffset, Duration = _muDuration,
                    XPos = RR(_muXOn, _muXMin, _muXMax), YPos = RR(_muYOn, _muYMin, _muYMax),
                    Rot = RR(_muRotOn, _muRotMin, _muRotMax), Scale = RR(_muScaleOn, _muScaleMin, _muScaleMax),
                    Parallax = RR(_muPxOn, _muPxMin, _muPxMax),
                    OpacityOn = _muOpOn, Opacity = _muOpacity,
                    AngleOffset = _muAngle, Ease = _muEase,
                    ScaleRevertOn = _muRevOn, ScaleRevertTo = _muRevTo,
                    ParallaxRevertOn = _muPRevOn, ParallaxRevertTo = _muPRevTo,
                }));
            }
        }

        private static void DoGenerate()
        {
            int at = Mathf.Clamp(Resolve(_gtAt), 0, Len());
            int added = 0;
            Run(Loc.T("Generate"), () => { added = TrackToolsEngine.Generate(at, _gtAngles, Math.Max(1, _gtCount)); });
            if (added > 0)
            {
                _gtPreview = false;
                Refresh();
                SetStatus(string.Format(Loc.T("Added {0} tiles"), added));
            }
            else if (added == 0 && _status == Loc.T("Applied"))
                SetStatus(Loc.T("No angles parsed — e.g. 45 90T 135"));
        }

        // ── UI ───────────────────────────────────────────────────────────────

        private const float PanelW = 332f;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;
        private const float LblW = 104f;

        private static void Build()
        {
            K.Rebuild(Loc.T("Track Tools"), Toggle, new Vector2(340f, -120f));

            float y = -34f;
            var tabNames = new[] { Loc.T("Fade in"), Loc.T("Fade out"), Loc.T("Explode"), Loc.T("Size"), Loc.T("Multi"), Loc.T("Generate") };
            float tabW = (PanelW - Pad * 2f - Gap * 2f) / 3f;
            for (int i = 0; i < 6; i++)
            {
                int idx = i;
                var cellBg = K.Cell(tabNames[i], Pad + (i % 3) * (tabW + Gap), y - (i / 3) * (RowH + Gap), tabW, RowH,
                    () => { _tab = idx; _status = ""; Refresh(); }, false);
                cellBg.color = PanelKit.Tint(_tab == i);
            }
            y -= (RowH + Gap) * 2f + 6f;

            switch (_tab)
            {
                case 0: y = BuildFade(y, _in, true); break;
                case 1: y = BuildFade(y, _out, false); break;
                case 2: y = BuildFade(y, _ex, false, true); break;
                case 3: y = BuildSize(y); break;
                case 4: y = BuildMulti(y); break;
                case 5: y = BuildGenerate(y); break;
            }

            _statusTmp = K.Status(_status, y);
            y -= 32f;
            K.Footer("MappingHelper · Sprout34", y);
            y -= 16f;
            K.SetHeight(y);
        }

        private static float BuildFade(float y, FadeState s, bool appear, bool explode = false)
        {
            y = RangeRow(y, () => s.From, () => s.To, (a, b) => { s.From = a; s.To = b; });
            y = PairRow(y, explode ? Loc.T("Window (rel)") : Loc.T("Move window"), () => s.WinFrom, () => s.WinTo,
                (a, b) => { s.WinFrom = a; s.WinTo = b; });
            y = FieldRow(y, Loc.T("Duration"), s.Duration.ToString("0.###"), v =>
            { float f; if (float.TryParse(v, out f)) s.Duration = Mathf.Max(0f, f); });
            if (!explode)
            {
                ToggleRow(y, Loc.T("Duration follows BPM"), s.MatchBpm, v => s.MatchBpm = v);
                y -= RowH + Gap;
            }
            y = RandRow(y, Loc.T("X offset"), () => s.XOn, v => s.XOn = v, () => s.XMin, () => s.XMax, (a, b) => { s.XMin = a; s.XMax = b; });
            y = RandRow(y, Loc.T("Y offset"), () => s.YOn, v => s.YOn = v, () => s.YMin, () => s.YMax, (a, b) => { s.YMin = a; s.YMax = b; });
            y = RandRow(y, Loc.T("Rotation"), () => s.RotOn, v => s.RotOn = v, () => s.RotMin, () => s.RotMax, (a, b) => { s.RotMin = a; s.RotMax = b; });
            y = RandRow(y, Loc.T("Scale"), () => s.ScaleOn, v => s.ScaleOn = v, () => s.ScaleMin, () => s.ScaleMax, (a, b) => { s.ScaleMin = a; s.ScaleMax = b; });
            y = ToggleFieldRow(y, Loc.T("Opacity"), () => s.OpacityOn, v => s.OpacityOn = v,
                () => s.Opacity, v => s.Opacity = v);
            if (appear)
                y = ToggleFieldRow(y, Loc.T("Land scale"), () => s.RevertOn, v => s.RevertOn = v,
                    () => s.RevertTo, v => s.RevertTo = v);
            y = FieldRow(y, explode ? Loc.T("Step angle") : Loc.T("Angle offset"), s.AngleOffset.ToString("0.###"), v =>
            { float f; if (float.TryParse(v, out f)) s.AngleOffset = f; });
            y = EaseRow(y, () => s.Ease, v => s.Ease = v);
            y -= 4f;
            K.Cell(Loc.T("Apply"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoFade, true, true);
            return y - (RowH + 2f) - 10f;
        }

        private static float BuildSize(float y)
        {
            y = RangeRow(y, () => _szFrom, () => _szTo, (a, b) => { _szFrom = a; _szTo = b; });
            y = RandRow(y, Loc.T("Track scale"), () => _szPosOn, v => _szPosOn = v, () => _szPosA, () => _szPosB, (a, b) => { _szPosA = a; _szPosB = b; });
            y = RandRow(y, Loc.T("Radius"), () => _szRadOn, v => _szRadOn = v, () => _szRadA, () => _szRadB, (a, b) => { _szRadA = a; _szRadB = b; });
            y = RandRow(y, Loc.T("Planets"), () => _szPlaOn, v => _szPlaOn = v, () => _szPlaA, () => _szPlaB, (a, b) => { _szPlaA = a; _szPlaB = b; });
            y = EaseRow(y, () => _szEase, v => _szEase = v);
            y -= 4f;
            K.Cell(Loc.T("Apply"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoSize, true, true);
            return y - (RowH + 2f) - 10f;
        }

        private static float BuildMulti(float y)
        {
            float half = (PanelW - Pad * 2f - Gap) * 0.5f;
            K.Cell(Loc.T("Copies"), Pad, y, half, RowH, () => { _muMode = 0; Refresh(); }, false).color = PanelKit.Tint(_muMode == 0);
            K.Cell(Loc.T("Animate"), Pad + half + Gap, y, half, RowH, () => { _muMode = 1; Refresh(); }, false).color = PanelKit.Tint(_muMode == 1);
            y -= RowH + Gap;

            y = RangeRow(y, () => _muFrom, () => _muTo, (a, b) => { _muFrom = a; _muTo = b; });
            y = FieldRow(y, Loc.T("Tag"), _muTag, v => _muTag = v.Trim());

            if (_muMode == 0)
            {
                K.Cell(Loc.T("Per tile"), Pad, y, half, RowH, () => { _muCentral = false; Refresh(); }, false).color = PanelKit.Tint(!_muCentral);
                K.Cell(Loc.T("One tile"), Pad + half + Gap, y, half, RowH, () => { _muCentral = true; Refresh(); }, false).color = PanelKit.Tint(_muCentral);
                y -= RowH + Gap;
                if (_muCentral)
                    y = FieldRow(y, Loc.T("Rotation"), _muRotation.ToString("0.###"), v =>
                    { float f; if (float.TryParse(v, out f)) _muRotation = f; });
                ToggleRow(y, Loc.T("Fake planets"), _muPlanet, v => _muPlanet = v);
                y -= RowH + Gap;
                ToggleRow(y, Loc.T("Planet events on first tile"), _muPlanetConc, v => _muPlanetConc = v);
                y -= RowH + Gap;
                y = RandRow(y, Loc.T("Depth 1st/step"), () => _muDepthOn, v => _muDepthOn = v,
                    () => _muDepth0, () => _muDepthStep, (a, b) => { _muDepth0 = (int)a; _muDepthStep = (int)b; });
                y = RandRow(y, Loc.T("Parallax"), () => _muParaOn, v => _muParaOn = v,
                    () => _muParaA, () => _muParaB, (a, b) => { _muParaA = a; _muParaB = b; });
                ToggleRow(y, Loc.T("Parallax affects planets"), _muParaPlanet, v => _muParaPlanet = v);
                y -= RowH + Gap;
                y -= 4f;
                K.Cell(Loc.T("Create copies"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoMulti, true, true);
            }
            else
            {
                K.Cell(Loc.T("Appear"), Pad, y, half, RowH, () => { _muAppear = true; Refresh(); }, false).color = PanelKit.Tint(_muAppear);
                K.Cell(Loc.T("Disappear"), Pad + half + Gap, y, half, RowH, () => { _muAppear = false; Refresh(); }, false).color = PanelKit.Tint(!_muAppear);
                y -= RowH + Gap;
                ToggleRow(y, Loc.T("Events per tile (off: first tile)"), _muDistributed, v => _muDistributed = v);
                y -= RowH + Gap;
                y = FieldRow(y, Loc.T("Copy # offset"), _muTagOffset.ToString(), v =>
                { int n; if (int.TryParse(v, out n)) _muTagOffset = n; });
                y = FieldRow(y, Loc.T("Duration"), _muDuration.ToString("0.###"), v =>
                { float f; if (float.TryParse(v, out f)) _muDuration = Mathf.Max(0f, f); });
                y = RandRow(y, Loc.T("X offset"), () => _muXOn, v => _muXOn = v, () => _muXMin, () => _muXMax, (a, b) => { _muXMin = a; _muXMax = b; });
                y = RandRow(y, Loc.T("Y offset"), () => _muYOn, v => _muYOn = v, () => _muYMin, () => _muYMax, (a, b) => { _muYMin = a; _muYMax = b; });
                y = RandRow(y, Loc.T("Rotation"), () => _muRotOn, v => _muRotOn = v, () => _muRotMin, () => _muRotMax, (a, b) => { _muRotMin = a; _muRotMax = b; });
                y = RandRow(y, Loc.T("Scale"), () => _muScaleOn, v => _muScaleOn = v, () => _muScaleMin, () => _muScaleMax, (a, b) => { _muScaleMin = a; _muScaleMax = b; });
                y = RandRow(y, Loc.T("Parallax"), () => _muPxOn, v => _muPxOn = v, () => _muPxMin, () => _muPxMax, (a, b) => { _muPxMin = a; _muPxMax = b; });
                y = ToggleFieldRow(y, Loc.T("Opacity"), () => _muOpOn, v => _muOpOn = v, () => _muOpacity, v => _muOpacity = v);
                if (_muAppear)
                {
                    y = ToggleFieldRow(y, Loc.T("Land scale"), () => _muRevOn, v => _muRevOn = v, () => _muRevTo, v => _muRevTo = v);
                    y = ToggleFieldRow(y, Loc.T("Land parallax"), () => _muPRevOn, v => _muPRevOn = v, () => _muPRevTo, v => _muPRevTo = v);
                }
                y = FieldRow(y, Loc.T("Angle offset"), _muAngle.ToString("0.###"), v =>
                { float f; if (float.TryParse(v, out f)) _muAngle = f; });
                y = EaseRow(y, () => _muEase, v => _muEase = v);
                y -= 4f;
                K.Cell(Loc.T("Apply"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoMulti, true, true);
            }
            return y - (RowH + 2f) - 10f;
        }

        private static float BuildGenerate(float y)
        {
            K.Label(Loc.T("After tile"), Pad, y, LblW, RowH, Theme.TextMuted);
            float x = Pad + LblW + 4f;
            int atResolved = Mathf.Clamp(Resolve(_gtAt), 0, Len());
            K.InputField(x, y, 62f, atResolved.ToString(), v =>
            { int n; if (int.TryParse(v, out n)) { _gtAt = Math.Max(0, n); PreviewRefresh(); } });
            K.Cell(Loc.T("Sel"), x + 62f + Gap, y, 42f, RowH, () =>
            { if (SelectionRange(out _, out int max)) { _gtAt = max; Refresh(); PreviewRefresh(); } }, true);
            K.Cell(Loc.T("End"), x + 62f + Gap + 42f + Gap, y, 42f, RowH, () => { _gtAt = -1; Refresh(); PreviewRefresh(); }, true);
            y -= RowH + Gap;

            K.Label(Loc.T("Angles (T = twirl)"), Pad, y, PanelW - Pad * 2f, 16f, Theme.TextMuted); y -= 18f;
            K.InputField(Pad, y, PanelW - Pad * 2f, _gtAngles, v => { _gtAngles = v; PreviewRefresh(); });
            y -= RowH + Gap;
            y = FieldRow(y, Loc.T("Repeat"), _gtCount.ToString(), v =>
            { int n; if (int.TryParse(v, out n)) { _gtCount = Math.Max(1, n); PreviewRefresh(); } });
            ToggleRow(y, Loc.T("Preview (ghost tiles)"), _gtPreview, v => { _gtPreview = v; });
            y -= RowH + Gap + 4f;
            K.Cell(Loc.T("Generate"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoGenerate, true, true);
            return y - (RowH + 2f) - 10f;
        }

        // ── row builders (thin wrappers over PanelKit's shared rows) ─────────

        private static float RangeRow(float y, Func<int> getA, Func<int> getB, Action<int, int> set)
            => K.RangeRow(y, getA, getB, set, Refresh, SetStatus);

        private static float PairRow(float y, string label, Func<int> getA, Func<int> getB, Action<int, int> set)
            => K.PairRow(y, label, getA, getB, set);

        private static float FieldRow(float y, string label, string value, Action<string> commit)
            => K.FieldRow(y, label, value, commit);

        private static float RandRow(float y, string label, Func<bool> getOn, Action<bool> setOn,
            Func<float> getA, Func<float> getB, Action<float, float> set)
            => K.RandRow(y, label, getOn, setOn, getA, getB, set);

        private static float ToggleFieldRow(float y, string label, Func<bool> getOn, Action<bool> setOn,
            Func<float> get, Action<float> set)
            => K.ToggleFieldRow(y, label, getOn, setOn, get, set);

        private static float EaseRow(float y, Func<Ease> get, Action<Ease> set)
            => K.EaseRow(y, get, set);

        private static void ToggleRow(float y, string label, bool value, Action<bool> set)
            => K.ToggleRow(y, label, value, set);

        // ── generate-track ghost preview (MappingHelper's, panel-state driven) ─

        internal static readonly List<scrFloor> Ghosts = new List<scrFloor>();
        private static bool _lastPreview;
        private static bool _moveGhosts; // one-shot: shift later real tiles after a build

        internal static bool PreviewActive
        {
            get
            {
                if (!(_open && _tab == 5 && _gtPreview && K.Visible)) return false;
                int at = Mathf.Clamp(Resolve(_gtAt), 0, Len());
                try { return at + 1 < scrLevelMaker.instance.listFloors.Count; } catch { return false; }
            }
        }

        private static void SyncPreviewTransition(scnEditor ed)
        {
            bool pv = PreviewActive;
            if (pv == _lastPreview) return;
            _lastPreview = pv;
            try { if (ed != null && !ed.playMode) ed.RemakePath(); } catch { }
        }

        private static void PreviewRefresh()
        {
            if (!PreviewActive) return;
            try { if (scnEditor.instance != null && !scnEditor.instance.playMode) scnEditor.instance.RemakePath(); } catch { }
        }

        internal static void ClearGhosts()
        {
            foreach (var f in Ghosts) if (f != null) UnityEngine.Object.DestroyImmediate(f.gameObject);
            Ghosts.Clear();
        }

        private static Vector3 NextPosition(float head)
            => new Vector3(Mathf.Cos(head * Mathf.Deg2Rad), Mathf.Sin(head * Mathf.Deg2Rad), 0) * 1.5f;

        private static float RadToDeg(double rad) => -(float)(rad * Mathf.Rad2Deg - 90);
        private static double DegToRad(double deg) => (90.0 - deg) * Mathf.Deg2Rad;

        internal static void OnMakeLevel()
        {
            ClearGhosts();
            if (scnEditor.instance == null || EditorMagicShape.Playing || !PreviewActive) return;
            if (ADOBase.lm.isOldLevel) return;
            var listFloors = scrLevelMaker.instance.listFloors;
            int at = Mathf.Clamp(Resolve(_gtAt), 0, Len());

            TrackToolsEngine.ParseAngleData(_gtAngles, out var parsed, out var twirl);
            if (parsed.Count == 0) return;
            int count = Math.Max(1, _gtCount);

            var angles = TrackToolsEngine.GetAnglesData();
            int order = 100 + listFloors.Count + parsed.Count * count;
            for (int i = 0; i <= at; i++)
            {
                listFloors[i].SetSortingOrder(order * 5);
                order--;
            }

            Vector3 pos = listFloors[at].transform.position + NextPosition(angles[at].head);
            float prevHead = angles[at].head;
            float tail = (angles[at].head + 180) % 360;
            bool isCCW = listFloors[at].isCCW;
            bool isCCWFirst = isCCW;

            var track = new List<float>();
            var trackTwirl = new List<bool>();
            for (int i = 0; i < count; i++) { track.AddRange(parsed); trackTwirl.AddRange(twirl); }

            scrFloor prev = null;
            for (int i = 0; i < track.Count; i++)
            {
                if (trackTwirl[i]) { isCCW = !isCCW; isCCWFirst = !isCCWFirst; }
                track[i] = TrackToolsEngine.CorrectDirection(tail + (isCCW ? track[i] : -track[i]));
                tail = (track[i] + 180) % 360;

                float direction = track[i];
                GameObject obj = UnityEngine.Object.Instantiate(ADOBase.lm.meshFloor, pos, Quaternion.identity);
                obj.name = "SapphireGhost_" + i;
                scrFloor floor = obj.GetComponent<scrFloor>();
                floor.exitangle = DegToRad(direction);
                floor.entryangle = DegToRad((prevHead + 180) % 360);
                floor.floorRenderer.color = new Color(1, 1, 1, 0.5f);
                floor.editorNumText.letterText.gameObject.SetActive(false);
                floor.SetSortingOrder(order * 5);
                if (trackTwirl[i])
                {
                    floor.floorIcon = FloorIcon.Swirl;
                    floor.isSwirl = true;
                    floor.isCCW = isCCWFirst;
                }
                order--;
                Ghosts.Add(floor);
                if (i != 0)
                {
                    prev.nextfloor = floor;
                    prev.UpdateAngle();
                }
                prev = floor;
                prevHead = direction;
                pos += NextPosition(direction);
            }

            for (int i = at + 1; i < angles.Length; i++)
            {
                if (i < listFloors.Count) listFloors[i].SetSortingOrder(order * 5);
                order--;
            }

            if (listFloors.Count > at + 1)
            {
                prev.exitangle = DegToRad(prevHead);
                prev.nextfloor = listFloors[at + 1];
                prev.UpdateAngle();
                listFloors[at + 1].entryangle = DegToRad((prevHead + 180) % 360);
            }
            _moveGhosts = true;
        }

        internal static void OnApplyEventsToFloors()
        {
            if (!_moveGhosts) return;
            _moveGhosts = false;
            if (scnEditor.instance == null || EditorMagicShape.Playing || !PreviewActive || Ghosts.Count == 0) return;
            if (ADOBase.lm.isOldLevel) return;
            var listFloors = scrLevelMaker.instance.listFloors;
            int at = Mathf.Clamp(Resolve(_gtAt), 0, Len());
            if (listFloors.Count <= at + 1) return;
            var last = Ghosts[Ghosts.Count - 1];
            Vector3 vector = last.transform.position - listFloors[at + 1].transform.position + NextPosition(RadToDeg(last.exitangle));
            for (int i = at + 1; i < listFloors.Count; i++)
                listFloors[i].transform.position += vector;
        }
    }

    [HarmonyPatch(typeof(scrLevelMaker), "MakeLevel")]
    internal static class TrackToolsMakeLevelPatch
    {
        private static void Postfix()
        {
            try { EditorTrackTools.OnMakeLevel(); }
            catch (Exception ex) { SapphireLog.Log("TrackTools preview: " + ex.Message); }
        }
    }

    [HarmonyPatch(typeof(scnGame), "ApplyEventsToFloors",
        typeof(List<scrFloor>), typeof(ADOFAI.LevelData), typeof(scrLevelMaker), typeof(List<ADOFAI.LevelEvent>))]
    internal static class TrackToolsApplyEventsPatch
    {
        private static void Postfix()
        {
            try { EditorTrackTools.OnApplyEventsToFloors(); }
            catch (Exception ex) { SapphireLog.Log("TrackTools preview shift: " + ex.Message); }
        }
    }
}
