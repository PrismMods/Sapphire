using System;
using System.IO;
using DG.Tweening;
using TMPro;
using UnityEngine;
using Sapphire.UI;

namespace Sapphire
{
    /* Deco Tools panel (toolbar cell 10) — Sapphire-native front end for DecoToolsEngine.
       Tabs: FLIPBOOK (image-sequence decoration + per-tile frame swaps) · EXTRACT (video →
       frame folder via Unity VideoPlayer) · 3D STACK (N lerped decoration copies) · LYRICS
       (text parts as built-in AddText or TMP-rendered PNGs, with appear/disappear moves).
       Based on AdofaiMappingHelper by Sprout34, integrated with permission. */
    internal static class EditorDecoTools
    {
        private static readonly PanelKit K = new PanelKit("SapphireDecoTools", 941, PanelW);
        private static bool _open;
        private static string _status = "";
        private static TextMeshProUGUI _statusTmp;
        private static string _layoutSig;
        private static int _tab; // 0 flipbook, 1 extract, 2 3d, 3 lyrics

        internal static bool IsOpen => _open;

        // flipbook state (defaults = MappingHelper's)
        private static int _fbFrom, _fbTo = -1;
        private static string _fbFolder = "", _fbTag = "", _fbEventTag = "";
        private static bool _fbWindowOn; private static int _fbStart = 1, _fbEnd = 1;
        private static float _fbInitAngle, _fbStep;

        // extract state
        private static string _exVideo = "";
        private static int _exFormat;          // 0 png, 1 jpg
        private static bool _exRunning;

        // 3d stack state
        private static int _d3From, _d3To = -1;
        private static string _d3Image = "", _d3Tag = "";
        private static int _d3Count = 10;
        private static Vector2 _d3PosA, _d3PosB, _d3PivA, _d3PivB;
        private static float _d3RotA, _d3RotB;
        private static Vector2 _d3ScaA = new Vector2(100, 100), _d3ScaB = new Vector2(100, 100);
        private static float _d3OpA = 100, _d3OpB = 100;
        private static int _d3DepA = 1, _d3DepB = 1;
        private static Vector2 _d3ParA, _d3ParB;
        private static string _d3ColA = "ffffffff", _d3ColB = "ffffffff";

        // lyrics state
        private static int _lyFrom, _lyTo = -1;
        private static string _lyText = "", _lyTag = "", _lyColor = "ffffffff";
        private static bool _lySplitChar;
        private static bool _lyAllAtOnce = true;
        private static bool _lyAsDeco;
        private static Vector2 _lyInterval = new Vector2(1f, 0f);
        private static float _lyTimeInterval = 45f;
        private static float _lyDuration = 1f;
        private static bool _lyXOn = true; private static float _lyXMin, _lyXMax;
        private static bool _lyYOn = true; private static float _lyYMin, _lyYMax;
        private static bool _lyPxOn = true; private static float _lyPxMin, _lyPxMax;   // pivot X
        private static bool _lyPyOn = true; private static float _lyPyMin, _lyPyMax;   // pivot Y
        private static bool _lyRotOn = true; private static float _lyRotMin, _lyRotMax;
        private static bool _lyScaOn = true; private static float _lyScaMin = 100f, _lyScaMax = 100f;
        private static bool _lyParOn; private static float _lyParMin, _lyParMax;
        private static bool _lyOpOn = true; private static float _lyOpacity = 100f;
        private static float _lyAngle;
        private static Ease _lyEase = Ease.Linear;
        private static bool _lyRevOn = true; private static float _lyRevTo = 100f;
        private static bool _lyPRevOn; private static float _lyPRevTo;
        private static string _lyFont = "";
        private static bool _lyStroke; private static float _lyStrokeSize = 2; private static string _lyStrokeCol = "000000ff";
        private static bool _lyShadow; private static Vector2 _lyShadowOff;
        private static float _lyShadowSpread = 15, _lyShadowDensity = 5;
        private static string _lyShadowCol = "000000ff";
        private static bool _lyDis;
        private static float _lyDisAfter = 1f, _lyDisDur = 1f;
        private static bool _lyDisXOn = true; private static float _lyDisXMin, _lyDisXMax;
        private static bool _lyDisYOn = true; private static float _lyDisYMin, _lyDisYMax;
        private static bool _lyDisRotOn = true; private static float _lyDisRotMin, _lyDisRotMax;
        private static bool _lyDisScaOn = true; private static float _lyDisScaMin = 100f, _lyDisScaMax = 100f;
        private static bool _lyDisOpOn = true; private static float _lyDisOpacity;
        private static Ease _lyDisEase = Ease.Linear;

        internal static void Toggle()
        {
            _open = !_open;
            _status = "";
            if (_open && PanelKit.SelectionRange(out int min, out int max) && min != max)
            {
                _fbFrom = _d3From = _lyFrom = min;
                _fbTo = _d3To = _lyTo = max;
                _layoutSig = null;
            }
            EditorToolbar.SyncDecoToolsHighlight();
        }

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool want = _open && ed != null && !ed.playMode && MainClass.EditorSuiteOn;
            if (!want) { K.Show(false); return; }
            string sig = _tab + "|" + (_lyAsDeco ? 1 : 0) + "|" + (_lyDis ? 1 : 0) + "|" + (_fbWindowOn ? 1 : 0);
            if (!K.Built || sig != _layoutSig)
            {
                _layoutSig = sig;
                Build();
            }
            K.Show(true);
        }

        internal static void Dispose()
        {
            K.Dispose();
            _statusTmp = null; _layoutSig = null;
            TextToPng.Dispose();
        }

        private static void SetStatus(string s)
        {
            _status = s ?? "";
            if (_statusTmp != null) _statusTmp.text = _status;
        }

        private static void Refresh() { _layoutSig = null; }

        private static Tuple<int, int> Range(int from, int to)
        {
            int len = PanelKit.LevelLen();
            int a = Mathf.Clamp(PanelKit.ResolveTile(from), 0, len);
            int b = Mathf.Clamp(PanelKit.ResolveTile(to), 0, len);
            return a <= b ? Tuple.Create(a, b) : Tuple.Create(b, a);
        }

        private static TrackToolsEngine.RandRange RR(bool on, float min, float max)
            => new TrackToolsEngine.RandRange(on, min, max);

        // resolve a user path against the level folder; empty result = invalid
        private static string InLevelDir(string userPath, out string levelDir)
        {
            levelDir = DecoToolsEngine.LevelDir();
            if (levelDir == null || string.IsNullOrEmpty(userPath)) return null;
            return Path.IsPathRooted(userPath) ? userPath : Path.Combine(levelDir, userPath);
        }

        // ── actions ──────────────────────────────────────────────────────────

        private static void DoFlipbook()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            if (DecoToolsEngine.LevelDir() == null) { SetStatus(Loc.T("Save the level first")); return; }
            if (string.IsNullOrEmpty(_fbTag)) { SetStatus(Loc.T("Enter a tag first")); return; }
            string dir = Path.Combine(DecoToolsEngine.LevelDir(), Path.GetFileName(_fbFolder.TrimEnd('/', Path.DirectorySeparatorChar)));
            if (string.IsNullOrEmpty(_fbFolder) || !Directory.Exists(dir)) { SetStatus(Loc.T("Frame folder not found in the level folder")); return; }
            try
            {
                var r = Range(_fbFrom, _fbTo);
                int used;
                using (new SaveStateScope(ed))
                    used = DecoToolsEngine.Flipbook(new DecoToolsEngine.FlipbookParams
                    {
                        From = r.Item1, To = r.Item2,
                        Folder = _fbFolder, Tag = _fbTag, EventTag = _fbEventTag,
                        WindowOn = _fbWindowOn, FrameStart = _fbStart, FrameEnd = _fbEnd,
                        InitialAngleOffset = _fbInitAngle, AngleStep = _fbStep,
                    });
                SetStatus(used > 0 ? string.Format(Loc.T("{0} frames per tile"), used) : Loc.T("No frames found"));
            }
            catch (Exception ex)
            {
                SapphireLog.Log("DecoTools flipbook: " + ex);
                SetStatus(string.Format(Loc.T("{0} failed — see log"), Loc.T("Flipbook")));
            }
        }

        private static void DoExtract()
        {
            if (_exRunning) { SetStatus(Loc.T("Already extracting…")); return; }
            string videoAbs = InLevelDir(_exVideo, out string levelDir);
            if (levelDir == null) { SetStatus(Loc.T("Save the level first")); return; }
            if (videoAbs == null || !File.Exists(videoAbs)) { SetStatus(Loc.T("Video file not found")); return; }
            string outDir = Path.Combine(levelDir, Path.GetFileNameWithoutExtension(videoAbs));
            _exRunning = true;
            SetStatus(Loc.T("Extracting frames…"));
            DecoToolsEngine.ExtractFrames(videoAbs, outDir, _exFormat == 1, count =>
            {
                _exRunning = false;
                SetStatus(string.Format(Loc.T("Extracted {0} frames to {1}"), count, Path.GetFileName(outDir)));
            });
        }

        private static void Do3D()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            string imageAbs = InLevelDir(_d3Image, out string levelDir);
            if (levelDir == null) { SetStatus(Loc.T("Save the level first")); return; }
            if (imageAbs == null || !File.Exists(imageAbs)) { SetStatus(Loc.T("Image file not found")); return; }
            try
            {
                var r = Range(_d3From, _d3To);
                using (new SaveStateScope(ed))
                    DecoToolsEngine.Deco3D(new DecoToolsEngine.Deco3DParams
                    {
                        From = r.Item1, To = r.Item2,
                        Image = _d3Image, Tag = _d3Tag, Count = Math.Max(1, _d3Count),
                        PosA = _d3PosA, PosB = _d3PosB, PivotA = _d3PivA, PivotB = _d3PivB,
                        RotA = _d3RotA, RotB = _d3RotB, ScaleA = _d3ScaA, ScaleB = _d3ScaB,
                        OpacityA = _d3OpA, OpacityB = _d3OpB, DepthA = _d3DepA, DepthB = _d3DepB,
                        ParA = _d3ParA, ParB = _d3ParB, ColorA = _d3ColA, ColorB = _d3ColB,
                    });
                SetStatus(Loc.T("Applied"));
            }
            catch (Exception ex)
            {
                SapphireLog.Log("DecoTools 3D: " + ex);
                SetStatus(string.Format(Loc.T("{0} failed — see log"), Loc.T("3D stack")));
            }
        }

        private static void DoLyrics()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;
            if (string.IsNullOrEmpty(_lyTag)) { SetStatus(Loc.T("Enter a tag first")); return; }
            if (string.IsNullOrWhiteSpace(_lyText)) { SetStatus(Loc.T("Enter the lyric text")); return; }
            if (_lyAsDeco)
            {
                if (DecoToolsEngine.LevelDir() == null) { SetStatus(Loc.T("Save the level first")); return; }
                string fontAbs = _lyFont;
                if (string.IsNullOrEmpty(fontAbs) || !File.Exists(fontAbs)) { SetStatus(Loc.T("Font file (.ttf/.otf) not found")); return; }
                string ext = Path.GetExtension(fontAbs).ToLowerInvariant();
                if (ext != ".ttf" && ext != ".otf") { SetStatus(Loc.T("Font file (.ttf/.otf) not found")); return; }
                if (_lyText.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                { SetStatus(Loc.T("Lyric contains characters invalid in file names")); return; }
            }
            try
            {
                var r = Range(_lyFrom, _lyTo);
                int parts;
                using (new SaveStateScope(ed))
                    parts = DecoToolsEngine.Lyric(new DecoToolsEngine.LyricParams
                    {
                        From = r.Item1, To = r.Item2,
                        Text = _lyText, SplitByChar = _lySplitChar, AllAtOnce = _lyAllAtOnce,
                        AsDecoration = _lyAsDeco, Tag = _lyTag, ColorHex = _lyColor,
                        PositionInterval = _lyInterval, TimeInterval = _lyTimeInterval,
                        Duration = _lyDuration,
                        XPos = RR(_lyXOn, _lyXMin, _lyXMax), YPos = RR(_lyYOn, _lyYMin, _lyYMax),
                        XPivot = RR(_lyPxOn, _lyPxMin, _lyPxMax), YPivot = RR(_lyPyOn, _lyPyMin, _lyPyMax),
                        Rot = RR(_lyRotOn, _lyRotMin, _lyRotMax), Scale = RR(_lyScaOn, _lyScaMin, _lyScaMax),
                        Parallax = RR(_lyParOn, _lyParMin, _lyParMax),
                        OpacityOn = _lyOpOn, Opacity = _lyOpacity,
                        AngleOffset = _lyAngle, Ease = _lyEase,
                        ScaleRevertOn = _lyRevOn, ScaleRevertTo = _lyRevTo,
                        ParallaxRevertOn = _lyPRevOn, ParallaxRevertTo = _lyPRevTo,
                        FontPath = _lyFont,
                        Stroke = _lyStroke, StrokeSize = (int)_lyStrokeSize, StrokeColor = _lyStrokeCol,
                        Shadow = _lyShadow, ShadowOffset = _lyShadowOff,
                        ShadowSpread = (int)_lyShadowSpread, ShadowDensity = _lyShadowDensity, ShadowColor = _lyShadowCol,
                        Disappear = _lyDis, DisappearAfter = _lyDisAfter, DisappearDuration = _lyDisDur,
                        DisXPos = RR(_lyDisXOn, _lyDisXMin, _lyDisXMax), DisYPos = RR(_lyDisYOn, _lyDisYMin, _lyDisYMax),
                        DisRot = RR(_lyDisRotOn, _lyDisRotMin, _lyDisRotMax), DisScale = RR(_lyDisScaOn, _lyDisScaMin, _lyDisScaMax),
                        DisOpacityOn = _lyDisOpOn, DisOpacity = _lyDisOpacity, DisappearEase = _lyDisEase,
                    });
                SetStatus(string.Format(Loc.T("Generated {0} lyric parts"), parts));
            }
            catch (Exception ex)
            {
                SapphireLog.Log("DecoTools lyrics: " + ex);
                SetStatus(string.Format(Loc.T("{0} failed — see log"), Loc.T("Lyrics")));
            }
        }

        // ── UI ───────────────────────────────────────────────────────────────

        private const float PanelW = 332f;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;

        private static void Build()
        {
            K.LblW = 104f;
            K.Rebuild(Loc.T("Deco Tools"), Toggle, new Vector2(690f, -120f));

            float y = -34f;
            var tabNames = new[] { Loc.T("Flipbook"), Loc.T("Extract"), Loc.T("3D stack"), Loc.T("Lyrics") };
            float tabW = (PanelW - Pad * 2f - Gap * 3f) / 4f;
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var cellBg = K.Cell(tabNames[i], Pad + i * (tabW + Gap), y, tabW, RowH,
                    () => { _tab = idx; _status = ""; Refresh(); }, false);
                cellBg.color = PanelKit.Tint(_tab == i);
            }
            y -= RowH + 10f;

            switch (_tab)
            {
                case 0: y = BuildFlipbook(y); break;
                case 1: y = BuildExtract(y); break;
                case 2: y = Build3D(y); break;
                case 3: y = BuildLyrics(y); break;
            }

            _statusTmp = K.Status(_status, y);
            y -= 32f;
            K.Footer("MappingHelper · Sprout34", y);
            y -= 16f;
            K.SetHeight(y);
        }

        private static float BuildFlipbook(float y)
        {
            y = K.RangeRow(y, () => _fbFrom, () => _fbTo, (a, b) => { _fbFrom = a; _fbTo = b; }, Refresh, SetStatus);
            y = K.FieldRow(y, Loc.T("Frame folder"), _fbFolder, v => _fbFolder = v.Trim());
            y = K.FieldRow(y, Loc.T("Tag"), _fbTag, v => _fbTag = v.Trim());
            y = K.FieldRow(y, Loc.T("Event tag"), _fbEventTag, v => _fbEventTag = v.Trim());
            y = K.ToggleRow(y, Loc.T("Frame window (off: all frames)"), _fbWindowOn, v => { _fbWindowOn = v; Refresh(); });
            if (_fbWindowOn)
                y = K.PairRow(y, Loc.T("Frames"), () => _fbStart, () => _fbEnd, (a, b) => { _fbStart = Math.Max(1, a); _fbEnd = Math.Max(1, b); });
            y = K.FloatRow(y, Loc.T("Start angle"), _fbInitAngle, v => _fbInitAngle = v);
            y = K.FloatRow(y, Loc.T("Angle per frame"), _fbStep, v => _fbStep = v);
            y -= 4f;
            K.Cell(Loc.T("Create flipbook"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoFlipbook, true, true);
            return y - (RowH + 2f) - 10f;
        }

        private static float BuildExtract(float y)
        {
            K.Label(Loc.T("Video file (in the level folder)"), Pad, y, PanelW - Pad * 2f, 16f, Theme.TextMuted); y -= 18f;
            K.InputField(Pad, y, PanelW - Pad * 2f, _exVideo, v => _exVideo = v.Trim());
            y -= RowH + Gap;
            y = K.SegRow(y, Loc.T("Format"), new[] { "PNG", "JPG" }, () => _exFormat, v => _exFormat = v);
            y -= 4f;
            K.Cell(Loc.T("Extract frames"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoExtract, true, true);
            y -= (RowH + 2f) + Gap;
            K.Label(Loc.T("Frames land in a folder named after the video — use it in Flipbook."),
                Pad, y, PanelW - Pad * 2f, 28f, Theme.TextMuted, 10.5f);
            return y - 30f - 6f;
        }

        private static float Build3D(float y)
        {
            y = K.RangeRow(y, () => _d3From, () => _d3To, (a, b) => { _d3From = a; _d3To = b; }, Refresh, SetStatus);
            y = K.FieldRow(y, Loc.T("Image file"), _d3Image, v => _d3Image = v.Trim());
            y = K.FieldRow(y, Loc.T("Tag"), _d3Tag, v => _d3Tag = v.Trim());
            y = K.IntRow(y, Loc.T("Copies"), _d3Count, v => _d3Count = Math.Max(1, v));
            y = K.FloatPairRow(y, Loc.T("Pos X from/to"), () => _d3PosA.x, () => _d3PosB.x, (a, b) => { _d3PosA.x = a; _d3PosB.x = b; });
            y = K.FloatPairRow(y, Loc.T("Pos Y from/to"), () => _d3PosA.y, () => _d3PosB.y, (a, b) => { _d3PosA.y = a; _d3PosB.y = b; });
            y = K.FloatPairRow(y, Loc.T("Pivot X from/to"), () => _d3PivA.x, () => _d3PivB.x, (a, b) => { _d3PivA.x = a; _d3PivB.x = b; });
            y = K.FloatPairRow(y, Loc.T("Pivot Y from/to"), () => _d3PivA.y, () => _d3PivB.y, (a, b) => { _d3PivA.y = a; _d3PivB.y = b; });
            y = K.FloatPairRow(y, Loc.T("Rotation from/to"), () => _d3RotA, () => _d3RotB, (a, b) => { _d3RotA = a; _d3RotB = b; });
            y = K.FloatPairRow(y, Loc.T("Scale X from/to"), () => _d3ScaA.x, () => _d3ScaB.x, (a, b) => { _d3ScaA.x = a; _d3ScaB.x = b; });
            y = K.FloatPairRow(y, Loc.T("Scale Y from/to"), () => _d3ScaA.y, () => _d3ScaB.y, (a, b) => { _d3ScaA.y = a; _d3ScaB.y = b; });
            y = K.FloatPairRow(y, Loc.T("Opacity from/to"), () => _d3OpA, () => _d3OpB, (a, b) => { _d3OpA = a; _d3OpB = b; });
            y = K.PairRow(y, Loc.T("Depth from/to"), () => _d3DepA, () => _d3DepB, (a, b) => { _d3DepA = a; _d3DepB = b; });
            y = K.FloatPairRow(y, Loc.T("Parallax X f/t"), () => _d3ParA.x, () => _d3ParB.x, (a, b) => { _d3ParA.x = a; _d3ParB.x = b; });
            y = K.FloatPairRow(y, Loc.T("Parallax Y f/t"), () => _d3ParA.y, () => _d3ParB.y, (a, b) => { _d3ParA.y = a; _d3ParB.y = b; });
            y = HexPairRow(y, Loc.T("Color from/to"), () => _d3ColA, () => _d3ColB, (a, b) => { _d3ColA = a; _d3ColB = b; });
            y -= 4f;
            K.Cell(Loc.T("Create stack"), Pad, y, PanelW - Pad * 2f, RowH + 2f, Do3D, true, true);
            return y - (RowH + 2f) - 10f;
        }

        private static float BuildLyrics(float y)
        {
            y = K.RangeRow(y, () => _lyFrom, () => _lyTo, (a, b) => { _lyFrom = a; _lyTo = b; }, Refresh, SetStatus);
            K.Label(Loc.T("Lyric text"), Pad, y, PanelW - Pad * 2f, 16f, Theme.TextMuted); y -= 18f;
            K.InputField(Pad, y, PanelW - Pad * 2f, _lyText, v => _lyText = v);
            y -= RowH + Gap;
            y = K.SegRow(y, Loc.T("Split"), new[] { Loc.T("Words"), Loc.T("Chars") }, () => _lySplitChar ? 1 : 0, v => _lySplitChar = v == 1);
            y = K.SegRow(y, Loc.T("Generate"), new[] { Loc.T("All parts"), Loc.T("First only") }, () => _lyAllAtOnce ? 0 : 1, v => _lyAllAtOnce = v == 0);
            y = K.SegRow(y, Loc.T("As"), new[] { Loc.T("Game text"), Loc.T("PNG (font)") }, () => _lyAsDeco ? 1 : 0, v => { _lyAsDeco = v == 1; Refresh(); });
            y = K.FieldRow(y, Loc.T("Tag"), _lyTag, v => _lyTag = v.Trim());
            y = K.FieldRow(y, Loc.T("Color (hex)"), _lyColor, v => _lyColor = v.Trim().TrimStart('#'));
            y = K.FloatPairRow(y, Loc.T("Spacing X/Y"), () => _lyInterval.x, () => _lyInterval.y, (a, b) => { _lyInterval.x = a; _lyInterval.y = b; });
            y = K.FloatRow(y, Loc.T("Part stagger (°)"), _lyTimeInterval, v => _lyTimeInterval = v);
            y = K.FloatRow(y, Loc.T("Duration"), _lyDuration, v => _lyDuration = Mathf.Max(0f, v));
            y = K.RandRow(y, Loc.T("X offset"), () => _lyXOn, v => _lyXOn = v, () => _lyXMin, () => _lyXMax, (a, b) => { _lyXMin = a; _lyXMax = b; });
            y = K.RandRow(y, Loc.T("Y offset"), () => _lyYOn, v => _lyYOn = v, () => _lyYMin, () => _lyYMax, (a, b) => { _lyYMin = a; _lyYMax = b; });
            y = K.RandRow(y, Loc.T("Pivot X"), () => _lyPxOn, v => _lyPxOn = v, () => _lyPxMin, () => _lyPxMax, (a, b) => { _lyPxMin = a; _lyPxMax = b; });
            y = K.RandRow(y, Loc.T("Pivot Y"), () => _lyPyOn, v => _lyPyOn = v, () => _lyPyMin, () => _lyPyMax, (a, b) => { _lyPyMin = a; _lyPyMax = b; });
            y = K.RandRow(y, Loc.T("Rotation"), () => _lyRotOn, v => _lyRotOn = v, () => _lyRotMin, () => _lyRotMax, (a, b) => { _lyRotMin = a; _lyRotMax = b; });
            y = K.RandRow(y, Loc.T("Scale"), () => _lyScaOn, v => _lyScaOn = v, () => _lyScaMin, () => _lyScaMax, (a, b) => { _lyScaMin = a; _lyScaMax = b; });
            y = K.RandRow(y, Loc.T("Parallax"), () => _lyParOn, v => _lyParOn = v, () => _lyParMin, () => _lyParMax, (a, b) => { _lyParMin = a; _lyParMax = b; });
            y = K.ToggleFieldRow(y, Loc.T("Opacity"), () => _lyOpOn, v => _lyOpOn = v, () => _lyOpacity, v => _lyOpacity = v);
            y = K.ToggleFieldRow(y, Loc.T("Land scale"), () => _lyRevOn, v => _lyRevOn = v, () => _lyRevTo, v => _lyRevTo = v);
            y = K.ToggleFieldRow(y, Loc.T("Land parallax"), () => _lyPRevOn, v => _lyPRevOn = v, () => _lyPRevTo, v => _lyPRevTo = v);
            y = K.FloatRow(y, Loc.T("Angle offset"), _lyAngle, v => _lyAngle = v);
            y = K.EaseRow(y, () => _lyEase, v => _lyEase = v);

            if (_lyAsDeco)
            {
                y -= 4f;
                K.Label(Loc.T("PNG rendering"), Pad, y, PanelW - Pad * 2f, 16f, Theme.TextMuted); y -= 18f;
                y = K.FieldRow(y, Loc.T("Font file"), _lyFont, v => _lyFont = v.Trim());
                y = K.ToggleFieldRow(y, Loc.T("Stroke size"), () => _lyStroke, v => _lyStroke = v, () => _lyStrokeSize, v => _lyStrokeSize = v);
                y = K.FieldRow(y, Loc.T("Stroke color"), _lyStrokeCol, v => _lyStrokeCol = v.Trim().TrimStart('#'));
                y = K.ToggleFieldRow(y, Loc.T("Shadow spread"), () => _lyShadow, v => _lyShadow = v, () => _lyShadowSpread, v => _lyShadowSpread = v);
                y = K.FloatPairRow(y, Loc.T("Shadow offset"), () => _lyShadowOff.x, () => _lyShadowOff.y, (a, b) => { _lyShadowOff.x = a; _lyShadowOff.y = b; });
                y = K.FloatRow(y, Loc.T("Shadow density"), _lyShadowDensity, v => _lyShadowDensity = Mathf.Clamp(v, 0f, 10f));
                y = K.FieldRow(y, Loc.T("Shadow color"), _lyShadowCol, v => _lyShadowCol = v.Trim().TrimStart('#'));
            }

            y -= 4f;
            y = K.ToggleRow(y, Loc.T("Disappear animation"), _lyDis, v => { _lyDis = v; Refresh(); });
            if (_lyDis)
            {
                y = K.FloatRow(y, Loc.T("After (beats)"), _lyDisAfter, v => _lyDisAfter = v);
                y = K.FloatRow(y, Loc.T("Duration"), _lyDisDur, v => _lyDisDur = Mathf.Max(0f, v));
                y = K.RandRow(y, Loc.T("X offset"), () => _lyDisXOn, v => _lyDisXOn = v, () => _lyDisXMin, () => _lyDisXMax, (a, b) => { _lyDisXMin = a; _lyDisXMax = b; });
                y = K.RandRow(y, Loc.T("Y offset"), () => _lyDisYOn, v => _lyDisYOn = v, () => _lyDisYMin, () => _lyDisYMax, (a, b) => { _lyDisYMin = a; _lyDisYMax = b; });
                y = K.RandRow(y, Loc.T("Rotation"), () => _lyDisRotOn, v => _lyDisRotOn = v, () => _lyDisRotMin, () => _lyDisRotMax, (a, b) => { _lyDisRotMin = a; _lyDisRotMax = b; });
                y = K.RandRow(y, Loc.T("Scale"), () => _lyDisScaOn, v => _lyDisScaOn = v, () => _lyDisScaMin, () => _lyDisScaMax, (a, b) => { _lyDisScaMin = a; _lyDisScaMax = b; });
                y = K.ToggleFieldRow(y, Loc.T("Opacity"), () => _lyDisOpOn, v => _lyDisOpOn = v, () => _lyDisOpacity, v => _lyDisOpacity = v);
                y = K.EaseRow(y, () => _lyDisEase, v => _lyDisEase = v);
            }

            y -= 4f;
            K.Cell(Loc.T("Generate lyrics"), Pad, y, PanelW - Pad * 2f, RowH + 2f, DoLyrics, true, true);
            return y - (RowH + 2f) - 10f;
        }

        // [label] [hexA] [hexB]
        private static float HexPairRow(float y, string label, Func<string> getA, Func<string> getB, Action<string, string> set)
        {
            K.Label(label, Pad, y, K.LblW, RowH, Theme.TextMuted);
            float x = Pad + K.LblW + 4f;
            float fw = (PanelW - Pad * 2f - K.LblW - 4f - Gap) * 0.5f;
            K.InputField(x, y, fw, getA(), v => set(v.Trim().TrimStart('#'), getB()));
            K.InputField(x + fw + Gap, y, fw, getB(), v => set(getA(), v.Trim().TrimStart('#')));
            return y - (RowH + Gap);
        }
    }
}
