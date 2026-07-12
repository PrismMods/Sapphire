using System.Collections.Generic;
using System.Reflection;
using System.Text;
using ADOFAI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sapphire
{
    /* Editor event displays (Tweaks tab → Editor), ticked from Overlay.Update like the
       tile-angle readout and hidden outside the editor / during play-testing.

       - Selected-tile chips: one chip per event on the last selected tile, tinted by the
         event's category; hovering a chip shows the event's properties.
       - Timeline: a strip docked at the bottom of the screen, one lane per event category
         present in the level. The x-axis is SONG TIME from the game's own
         scrLevelMaker.CalculateFloorEntryTimes (we call it ourselves — the game only
         runs it at play start), so speed changes, pauses, holds and freeroam all land
         where they sound. Markers are painted into a single texture, not UI objects, so
         10k-event charts stay cheap. The playhead tracks the selected tile / the run
         during play-testing; +/- buttons zoom, the wheel pans, click/drag scrubs.

       The zoom buttons and the strip background are the only raycast targets (the strip
       swallows clicks under it like a toolbar); chips, markers and the tooltip are plain
       rect-math hover, so editor input elsewhere is never blocked. */
    internal static class EditorEvents
    {
        private static GameObject _canvasGo;
        private static RectTransform _canvasRect;
        private static Canvas _canvas;

        // ── chips ──
        private static RectTransform _chipsRow;
        private static readonly List<RectTransform> _chipRects = new List<RectTransform>();
        private static readonly List<LevelEvent> _chipEvents = new List<LevelEvent>();
        private static int _chipFloor = -2, _chipCount = -1, _chipTotal = -1;
        private const int MaxChips = 12;

        // ── timeline ──
        private static RectTransform _stripRect;
        private static RectTransform _markerArea;
        private static RawImage _markerImage;
        private static Texture2D _markerTex;
        private static RectTransform _playhead;
        private static RectTransform _laneLabelHost;
        private const float HeaderW = 96f;   // lane-label column
        private const float LaneH = 20f;     // canvas units per lane (category view)
        private const float CamInspH = 46f;  // inline keyframe-inspector row (cam mode)
        // Cam mode is a WORKSPACE, not a readout — people park here for whole sessions,
        // so lanes and text get room (the strip covers gameplay, which doesn't matter here).
        private static float LaneHEff => KeyframeMode ? 44f : LaneH;
        private static int TexLaneHEff => KeyframeMode ? 44 : TexLaneH;
        private const float TransBandH = 44f; // keyframe modes park the transport up top
        private const float SubRowH = 32f;   // expanded keyframe sub-row (label click)
        internal static int _expandedLane = -1;
        private const float StripPad = 7f;
        private const float ZoomW = 60f;     // zoom-button column on the right
        private const int TexW = 1792;
        private const int TexLaneH = 20;     // texture rows per lane (16px marker + 4 gap)
        private static readonly List<LevelEventCategory> _lanes = new List<LevelEventCategory>();
        private static List<KeyValuePair<float, LevelEvent>>[] _laneEvents; // abs-frac-sorted per lane
        private static int _tlSig = int.MinValue;
        private static int _scanCooldown;
        // Clearance above the game's bottom event palette (until that bar is replaced).
        private const float BottomGap = 74f;

        // ── transport (Sapphire play/rewind + clock, docked at the strip's left) ──
        private const float TransportW = 252f;
        private static GameObject _transportGo;
        private static TextMeshProUGUI _playGlyph;
        private static GameObject _pauseBars;
        private static TextMeshProUGUI _clockText;
        private static TextMeshProUGUI _bpmText;
        private static RoundedRectGraphic _autoBg;
        private static TextMeshProUGUI _autoLabel;
        private static GameObject _diffMenuGo;
        private static bool _autoShown;
        private static bool _transportOn;
        private static CanvasGroup _fadedPlayCluster;

        // ── mode cluster: Editor Mode / difficulty / no-fail chips above the strip ──
        private static RectTransform _modeCluster;
        private static RoundedRectGraphic _emBg;
        private static TextMeshProUGUI _emLabel;
        private static TextMeshProUGUI _diffLabel;
        private static RoundedRectGraphic _nfBg;
        private static TextMeshProUGUI _nfLabel;
        private static bool _emShown, _nfShown;
        private static int _diffShown = -1;

        // View window: [_viewStart, _viewStart + 1/_zoom] in beat-fraction space. Markers
        // re-render on window changes only, mapped into the window, so zoom gains real
        // precision instead of stretching pixels.
        private static float _zoom = 1f;
        private static float _viewStart;
        private static bool _viewDirty;
        private static bool _userZoomed;     // manual zoom sticks until the strip hides
        private static bool _draggingHead;   // scrubbing the playhead with the mouse
        private static bool _cursorForced;   // we re-showed the game-hidden cursor
        private static bool _tlUserHidden;    // fold-arrow-toggled timeline visibility
        private static GameObject _foldCanvasGo;   // own canvas: must outlive the hidden strip
        private static RectTransform _foldRect;
        private static TMPro.TextMeshProUGUI _foldGlyph;
        private static bool _foldGlyphUp;
        private static int _lastSelSeq = -1;
        private const double DefaultViewBeats = 64.0;
        private const double MinViewBeats = 4.0; // zoom-in limit

        private static double[] _timePrefix; // song-time (s) at floor i's start, from the game
        private static double _totalTime = 1.0;
        private static double _timeOffset;   // time of the first INPUT tile — the musical beat 0
        private static double _secPerBeat = 0.5; // 60 / base BPM, for the measure grid

        /* Tempo map: the game's (entryBeat → entryTime) pairs form a piecewise-linear
           beat↔time map, so the measure grid lives in BEAT domain — genuine BPM shifts
           change its spacing automatically. Detection splits it into segments where the
           grid must RE-ANCHOR: a sustained seconds-per-beat change (base-BPM shift,
           different-tempo intro) or a persistent onbeat phase shift (odd pause, intro on
           a different beat lattice). Magicshapes keep constant effective TBPM and pauses
           grow beat and time together, so neither trips the detector. A 4/4-signature
           intro over a 3/4 song with the SAME tempo and lattice leaves no geometric
           trace — undetectable from chart data. */
        private struct TempoSeg
        {
            public double StartBeat; // where the segment begins (game-beat domain)
            public double Anchor;    // grid phase origin — a downbeat, game-beat domain
            public double Spb;       // representative seconds per beat (grid density)
        }
        private static readonly List<TempoSeg> _tempo = new List<TempoSeg>();
        private static double[] _beatPrefix; // scrFloor.entryBeat per floor
        private static double[] _bpmPrefix;  // angle-adjusted effective BPM per floor (eff display)
        private static double[] _tileBpmPrefix; // tile BPM (speed × base bpm) — base-detection signal
        private static double[] _baseBpmPrefix; // detected musical BASE bpm per floor
        private static int _timeSigNum = 4;     // detected beats per measure
        private static int _timeSigPhase = 0;   // downbeat offset (beats) for measure lines
        private static double _levelBpm = 100.0;
        private static double _beatOffset;   // entryBeat of the first input tile = beat 0
        private static double _lastBeat;
        private const double PhaseTol = 0.1;     // "on the beat lattice" tolerance (beats)
        private const double PhaseMinBeats = 3.0;
        private const int PhaseMinTiles = 4;
        private const int MaxTempoSegs = 48;     // beyond this = speed-animated chart; bail

        // ── tooltip ──
        private static GameObject _tooltipGo;
        private static RectTransform _tooltipRect;
        private static TextMeshProUGUI _tooltipText;
        private const float TooltipMaxW = 440f;

        private static FieldInfo _dataField; // LevelEvent.data is protected

        /* Camera keyframe mode (the "filtered view"): the strip shows only MoveCamera
           events, split into one LAYER per animated property. Duration renders as a bar
           (start → start + duration beats at the tile's pulse); duration-0 "set" keyframes
           render as thin ticks — the AWC set+tween pair pattern reads as tick-then-bar. */
        internal static int TlMode;                       // 0 normal · 1 cam · 2 deco · 3 filter
        internal static bool CamMode => TlMode == 1;
        internal static bool KeyframeMode => TlMode >= 1; // bar rendering + span hit + tall lanes
        private static readonly string[] TlModeNames = { "NORMAL", "CAM", "DECO", "FILTER" };
        private static GameObject _modeMenuGo;
        private static TextMeshProUGUI _modeBtnLabel;
        // string-keyed lanes for deco (tags) / filter (filter names)
        private static readonly List<string> _strLanes = new List<string>();
        // Shared with EditorGraph (same view window + tempo mapping).
        internal static float GraphViewStart => _viewStart;
        internal static float GraphViewZoom => _zoom;
        internal static float GraphFracOfFloor(int floor) => FracOfFloor(floor);
        internal static int GraphFloorAtFrac(float frac) => FloorAtFrac(frac);
        internal static float GraphFracEnd(LevelEvent e, float startFrac) => CamDurationFracEnd(e, startFrac);
        internal static float GraphStripTop => BottomStripTop;
        internal static double GraphBeatAtFrac(float frac) => BeatAtTime(frac * _totalTime) - _beatOffset;
        private static RoundedRectGraphic _camBtnBg;
        private static GameObject _graphBtnGo;
        private static RectTransform _camInspHost;
        private const int CamLanePos = -2, CamLaneRot = -3, CamLaneZoom = -4;

        // Fixed lane order; "Other" (grey) catches events with no usable category.
        private static readonly LevelEventCategory[] LaneOrder =
        {
            LevelEventCategory.Gameplay, LevelEventCategory.TrackFx,
            LevelEventCategory.DecorationFx, LevelEventCategory.VisualFx,
            LevelEventCategory.FxModifiers, LevelEventCategory.Conveniences,
            LevelEventCategory.Jank,
        };

        internal static Color CategoryColor(LevelEventCategory c)
        {
            switch (c)
            {
                case LevelEventCategory.Gameplay:     return new Color(1f, 0.37f, 0.37f);
                case LevelEventCategory.TrackFx:      return new Color(1f, 0.71f, 0.28f);
                case LevelEventCategory.DecorationFx: return new Color(0.69f, 0.49f, 1f);
                case LevelEventCategory.VisualFx:     return new Color(0.35f, 0.76f, 1f);
                case LevelEventCategory.FxModifiers:  return new Color(0.39f, 0.89f, 0.65f);
                case LevelEventCategory.Conveniences: return new Color(0.97f, 0.92f, 0.35f);
                case LevelEventCategory.Jank:         return new Color(1f, 0.48f, 0.85f);
                case (LevelEventCategory)CamLanePos:  return new Color(0.35f, 0.76f, 1f);
                case (LevelEventCategory)CamLaneRot:  return new Color(1f, 0.71f, 0.28f);
                case (LevelEventCategory)CamLaneZoom: return new Color(0.39f, 0.89f, 0.65f);
                default:                              return new Color(0.62f, 0.62f, 0.66f);
            }
        }

        private static string CategoryLabel(LevelEventCategory c)
        {
            switch (c)
            {
                case LevelEventCategory.Gameplay:     return "Gameplay";
                case LevelEventCategory.TrackFx:      return "Track FX";
                case LevelEventCategory.DecorationFx: return "Deco FX";
                case LevelEventCategory.VisualFx:     return "Visual FX";
                case LevelEventCategory.FxModifiers:  return "Modifiers";
                case LevelEventCategory.Conveniences: return "Convenience";
                case LevelEventCategory.Jank:         return "DLC";
                case (LevelEventCategory)CamLanePos:  return Loc.T("Position");
                case (LevelEventCategory)CamLaneRot:  return Loc.T("Rotation");
                case (LevelEventCategory)CamLaneZoom: return Loc.T("Zoom");
                default:                              return "Other";
            }
        }

        // Favorites is a UI shelf, not a real placement — skip it when picking a lane.
        private static LevelEventCategory PrimaryCategory(LevelEvent e)
        {
            var cats = e.info != null ? e.info.categories : null;
            if (cats != null)
                for (int i = 0; i < cats.Count; i++)
                    if (cats[i] != LevelEventCategory.Favorites) return cats[i];
            return (LevelEventCategory)(-1); // → "Other"
        }

        private static string EventName(LevelEvent e) =>
            e.info != null && !string.IsNullOrEmpty(e.info.name) ? e.info.name : e.eventType.ToString();

        // Top edge of the bottom-docked strip in canvas units (0 when hidden) — the event
        // dock stacks itself just above it.
        // Fold/expand chevron, bottom-centre: rides the strip's top edge when open, hugs the
        // screen edge when folded. Lives on its own canvas so it survives the strip hiding.
        private static void TickFoldButton(bool show)
        {
            if (!show)
            {
                if (_foldCanvasGo != null && _foldCanvasGo.activeSelf) _foldCanvasGo.SetActive(false);
                return;
            }
            if (_foldCanvasGo == null) BuildFoldButton();
            if (!_foldCanvasGo.activeSelf) _foldCanvasGo.SetActive(true);

            float y = _tlUserHidden ? 6f : BottomStripTop + 4f;
            if (!Mathf.Approximately(_foldRect.anchoredPosition.y, y))
                _foldRect.anchoredPosition = new Vector2(0f, y);
            bool up = _tlUserHidden; // folded → arrow points up (expand)
            if (up != _foldGlyphUp && _foldGlyph != null)
            {
                _foldGlyphUp = up;
                _foldGlyph.rectTransform.localRotation = Quaternion.Euler(0f, 0f, up ? 90f : -90f);
            }
        }

        private static void BuildFoldButton()
        {
            _foldCanvasGo = new GameObject("SapphireTimelineFold", typeof(RectTransform));
            Object.DontDestroyOnLoad(_foldCanvasGo);
            var canvas = _foldCanvasGo.AddComponent<UnityEngine.Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 903; // just under the strip's canvas
            var scaler = _foldCanvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _foldCanvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            var go = new GameObject("Fold", typeof(RectTransform));
            go.transform.SetParent(_foldCanvasGo.transform, false);
            _foldRect = (RectTransform)go.transform;
            _foldRect.anchorMin = _foldRect.anchorMax = new Vector2(0.5f, 0f);
            _foldRect.pivot = new Vector2(0.5f, 0f);
            _foldRect.anchoredPosition = new Vector2(0f, 6f);
            _foldRect.sizeDelta = new Vector2(44f, 18f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 9f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.9f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            var glyphGo = new GameObject("G", typeof(RectTransform));
            glyphGo.transform.SetParent(go.transform, false);
            var gr = (RectTransform)glyphGo.transform;
            gr.anchorMin = gr.anchorMax = new Vector2(0.5f, 0.5f);
            gr.pivot = new Vector2(0.5f, 0.5f);
            gr.sizeDelta = new Vector2(20f, 20f);
            _foldGlyph = glyphGo.AddComponent<TMPro.TextMeshProUGUI>();
            _foldGlyph.font = UI.Theme.TmpFont;
            _foldGlyph.fontSize = 14f;
            _foldGlyph.color = new Color(0.8f, 0.8f, 0.84f, 1f);
            _foldGlyph.alignment = TMPro.TextAlignmentOptions.Center;
            _foldGlyph.raycastTarget = false;
            _foldGlyph.text = "\u203a"; // "›" rotated ±90° = up/down chevron (proven glyph)
            _foldGlyph.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -90f);
            _foldGlyphUp = false;

            UI.ClickHandler.Attach(go, () => { _tlUserHidden = !_tlUserHidden; });
        }

        internal static float BottomStripTop =>
            _stripRect != null && _stripRect.gameObject.activeInHierarchy
                ? _stripRect.anchoredPosition.y + _stripRect.sizeDelta.y : 0f;

        // Read by the scnEditor.ZoomCamera prefix: the editor zooms on any wheel input, so
        // it must stand down over ANY timeline surface — the strip, the mode cluster and
        // tooltip above it, or an open difficulty dropdown (wheel over those leaked into
        // camera zoom).
        internal static bool TimelineHovered
        {
            get
            {
                try
                {
                    if (_diffMenuGo != null) return true;
                    var mouse = Input.mousePosition;
                    return HoverRect(_stripRect, mouse) || HoverRect(_modeCluster, mouse)
                        || HoverRect(_tooltipRect, mouse);
                }
                catch { return false; }
            }
        }

        private static bool HoverRect(RectTransform r, Vector3 mouse) =>
            r != null && r.gameObject.activeInHierarchy
            && RectTransformUtility.RectangleContainsScreenPoint(r, mouse, null);

        private static List<LevelEvent> LevelEventList()
        {
            try
            {
                var g = scnGame.instance;
                return g != null && g.levelData != null ? (List<LevelEvent>)g.levelData.levelEvents : null;
            }
            catch { return null; }
        }

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            bool wantChips = false, wantTl = false;
            try
            {
                if (s != null && MainClass.EditorSuiteOn && (s.EditorShowEvents || s.EditorTimeline))
                {
                    ed = scnEditor.instance;
                    bool editing = ed != null && !ed.playMode && !EditorPanelOpen(ed);
                    bool inEditor = ed != null && !ed.playMode;
                    bool playing = ed != null && ed.playMode;
                    wantChips = editing && s.EditorShowEvents
                        && ed.selectedFloors != null && ed.selectedFloors.Count > 0;
                    // Always up while editing (open game panels included) and play-testing
                    // (the playhead follows the run); only the fold arrow hides it (ESC kept
                    // colliding with the game's own ESC behaviors — dropped July 11).
                    wantTl = (inEditor || playing) && s.EditorTimeline && !_tlUserHidden;
                    TickFoldButton((inEditor || playing) && s.EditorTimeline);
                }
            }
            catch { }

            if (!wantChips && !wantTl)
            {
                if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
                _chipFloor = -2; // force chip rebuild on return
                // Manual zoom survives transient hides (file menu, prefs panel) but resets
                // once the user actually leaves the editor.
                if (ed == null) _userZoomed = false;
                return;
            }
            if (_canvasGo == null) BuildCanvas();
            if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

            string tip = null;
            Vector2 tipAt = default;
            try { TickChips(ed, wantChips, ref tip, ref tipAt); } catch { }
            try { TickTimeline(ed, wantTl, ref tip, ref tipAt); } catch { }
            ShowTooltip(tip, tipAt);
        }

        private static FieldInfo _showingFileActions; // private on scnEditor

        // The file menu, editor prefs and particle editor all drop into the top band the
        // strip occupies (and the strip draws above them) — get out of their way entirely.
        // The file menu's panel object stays active once it has been opened (the game
        // animates it and tracks visibility in a private bool), so read that bool; the
        // other two really are (de)activated, which is how the game itself checks them.
        private static bool EditorPanelOpen(scnEditor ed)
        {
            try
            {
                if (_showingFileActions == null)
                    _showingFileActions = typeof(scnEditor).GetField("showingFileActions",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                if (_showingFileActions != null && (bool)_showingFileActions.GetValue(ed)) return true;
                return (ed.prefsContainer != null && ed.prefsContainer.gameObject.activeInHierarchy)
                    || (ed.particleEditorContainer != null && ed.particleEditorContainer.gameObject.activeInHierarchy);
            }
            catch { return false; }
        }

        internal static void Dispose()
        {
            if (_canvasGo != null) Object.Destroy(_canvasGo);
            if (_foldCanvasGo != null) Object.Destroy(_foldCanvasGo);
            _foldCanvasGo = null; _foldRect = null; _foldGlyph = null;
            if (_markerTex != null) Object.Destroy(_markerTex);
            _canvasGo = null; _canvasRect = null; _canvas = null; _chipsRow = null;
            _stripRect = null; _markerArea = null; _markerImage = null; _markerTex = null; _graphBtnGo = null; _dragGhostGo = null; _modeMenuGo = null; _modeBtnLabel = null;
            _playhead = null; _laneLabelHost = null;
            _tooltipGo = null; _tooltipRect = null; _tooltipText = null;
            _chipRects.Clear(); _chipEvents.Clear(); _lanes.Clear(); _laneEvents = null;
            _timePrefix = null; _beatPrefix = null; _bpmPrefix = null; _tileBpmPrefix = null; _baseBpmPrefix = null; _tempo.Clear(); _chipFloor = -2; _tlSig = int.MinValue;
            _zoom = 1f; _viewStart = 0f; _lastSelSeq = -1; _userZoomed = false; _draggingHead = false;
            _cursorForced = false;
            FadePlayCluster(null, false);
            _transportGo = null; _playGlyph = null; _pauseBars = null; _clockText = null; _bpmText = null;
            _autoBg = null; _autoLabel = null; _autoShown = false; _diffMenuGo = null;
            _transportOn = false;
            _modeCluster = null; _emBg = null; _emLabel = null; _diffLabel = null;
            _nfBg = null; _nfLabel = null; _emShown = false; _nfShown = false; _diffShown = -1;
        }

        // ── selected-tile chips ─────────────────────────────────────────────

        private static void TickChips(scnEditor ed, bool want, ref string tip, ref Vector2 tipAt)
        {
            if (!want)
            {
                if (_chipsRow != null && _chipsRow.gameObject.activeSelf) _chipsRow.gameObject.SetActive(false);
                _chipFloor = -2;
                return;
            }
            var events = LevelEventList();
            var fl = ed.selectedFloors[ed.selectedFloors.Count - 1];
            if (events == null || fl == null)
            {
                if (_chipsRow != null && _chipsRow.gameObject.activeSelf) _chipsRow.gameObject.SetActive(false);
                return;
            }
            if (!_chipsRow.gameObject.activeSelf) _chipsRow.gameObject.SetActive(true);

            int seq = fl.seqID, n = 0;
            for (int i = 0; i < events.Count; i++)
                if (events[i] != null && events[i].floor == seq) n++;
            if (seq != _chipFloor || n != _chipCount || events.Count != _chipTotal)
            {
                _chipFloor = seq; _chipCount = n; _chipTotal = events.Count;
                RebuildChips(seq, events);
            }

            if (tip != null) return;
            Vector2 mouse = Input.mousePosition;
            for (int i = 0; i < _chipRects.Count; i++)
            {
                var r = _chipRects[i];
                if (r == null || !RectTransformUtility.RectangleContainsScreenPoint(r, mouse, null)) continue;
                tip = BuildTooltip(_chipEvents[i], false);
                var corners = new Vector3[4];
                r.GetWorldCorners(corners); // 0=BL 3=BR
                tipAt = new Vector2((corners[0].x + corners[3].x) * 0.5f, corners[0].y - 4f);
                break;
            }
        }

        private static void RebuildChips(int seq, List<LevelEvent> events)
        {
            for (int i = _chipsRow.childCount - 1; i >= 0; i--)
                Object.Destroy(_chipsRow.GetChild(i).gameObject);
            _chipRects.Clear();
            _chipEvents.Clear();

            int made = 0, skipped = 0;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e == null || e.floor != seq) continue;
                if (made >= MaxChips) { skipped++; continue; }
                MakeChip(EventName(e), CategoryColor(PrimaryCategory(e)), e);
                made++;
            }
            if (skipped > 0) MakeChip("+" + skipped + " more", new Color(0.62f, 0.62f, 0.66f), null);
            if (made == 0) MakeChip("No events", new Color(0.55f, 0.55f, 0.6f), null);
        }

        private static void MakeChip(string label, Color cat, LevelEvent evt)
        {
            var go = new GameObject("Chip", typeof(RectTransform));
            go.transform.SetParent(_chipsRow, false);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 8f;
            bg.color = new Color(0.08f, 0.08f, 0.1f, 0.78f);
            bg.BorderWidth = 1f;
            bg.BorderColor = evt != null && !evt.active
                ? new Color(cat.r, cat.g, cat.b, 0.35f)
                : new Color(cat.r, cat.g, cat.b, 0.9f);
            bg.raycastTarget = false;

            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(9f, 0f); tr.offsetMax = new Vector2(-9f, 0f);
            var t = txtGo.AddComponent<TextMeshProUGUI>();
            t.font = UI.Theme.TmpFont;
            t.fontSize = 14;
            t.color = evt != null && !evt.active ? new Color(0.75f, 0.75f, 0.78f, 0.6f) : Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            t.text = label;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = Mathf.Ceil(t.GetPreferredValues(label).x) + 20f;
            le.preferredHeight = 24f;

            _chipRects.Add((RectTransform)go.transform);
            _chipEvents.Add(evt);
        }

        // ── timeline ────────────────────────────────────────────────────────

        private static void TickTimeline(scnEditor ed, bool want, ref string tip, ref Vector2 tipAt)
        {
            if (!want)
            {
                if (_stripRect != null && _stripRect.gameObject.activeSelf) _stripRect.gameObject.SetActive(false);
                if (_modeCluster != null && _modeCluster.gameObject.activeSelf) _modeCluster.gameObject.SetActive(false);
                _cursorForced = false; // the game manages the cursor once we're out
                FadePlayCluster(null, false);
                return;
            }
            var events = LevelEventList();
            var floors = ADOBase.lm != null ? ADOBase.lm.listFloors : null;
            if (events == null || floors == null || floors.Count == 0)
            {
                if (_stripRect != null && _stripRect.gameObject.activeSelf) _stripRect.gameObject.SetActive(false);
                return;
            }
            if (!_stripRect.gameObject.activeSelf) _stripRect.gameObject.SetActive(true);
            if (_modeCluster != null && !_modeCluster.gameObject.activeSelf) _modeCluster.gameObject.SetActive(true);
            UpdateModeCluster();

            bool wantTransport = false, dockOn = false;
            try
            {
                var ms = MainClass.Settings;
                wantTransport = ms != null && ms.EditorTransport;
                dockOn = ms != null && ms.EditorEventDock;
            }
            catch { }
            if (wantTransport != _transportOn) ApplyTransportLayout(wantTransport);
            FadePlayCluster(ed, wantTransport);
            // With the Sapphire event dock replacing the game's bottom palette, the strip
            // can sit flush against the screen edge.
            float gap = dockOn ? 0f : BottomGap;
            if (!Mathf.Approximately(_stripRect.anchoredPosition.y, gap))
                _stripRect.anchoredPosition = new Vector2(0f, gap);

            // Cheap structural signature (count + strided floor/active/angle sample) polled
            // a few times a second — catches add/delete/move/toggle and tile-angle edits
            // (which shift every beat position) without hashing everything per frame.
            if (--_scanCooldown <= 0)
            {
                _scanCooldown = 15;
                int sig = events.Count * 397 ^ floors.Count;
                int stride = Mathf.Max(1, events.Count / 64);
                for (int i = 0; i < events.Count; i += stride)
                {
                    var e = events[i];
                    if (e != null) sig = sig * 31 + e.floor * 2 + (e.active ? 1 : 0);
                }
                int fStride = Mathf.Max(1, floors.Count / 64);
                for (int i = 0; i < floors.Count; i += fStride)
                    if (floors[i] != null)
                        sig = sig * 31 + (int)(floors[i].angleLength * 1000.0) * 17
                            + (int)(floors[i].speed * 1000f) * 7
                            + (int)(floors[i].extraBeats * 100f);
                if (TlMode >= 2)
                    sig = sig * 31 + (int)(_viewStart * _zoom * 4f) * 13 + (int)(Mathf.Log(Mathf.Max(1f, _zoom), 1.5f));
                if (sig != _tlSig)
                {
                    _tlSig = sig;
                    RebuildStructure(events, floors);
                    _viewDirty = true;
                }
            }

            // Playhead: the current tile while play-testing, else the selected tile. The
            // view follows it — centered on selection jumps, paged forward during playback.
            bool playing = ed.playMode;
            float selFrac = -1f;
            int selSeq = -1;
            if (playing)
            {
                try
                {
                    var c = scrController.instance;
                    if (c != null) selSeq = c.currentSeqID;
                    // Constant-speed playhead: the axis IS song time, so the conductor
                    // clock maps straight onto it. Tile-stepping would park on long holds
                    // and then lurch.
                    var cond = scrConductor.instance;
                    if (cond != null && _totalTime > 0.001)
                        selFrac = Mathf.Clamp01((float)(cond.songposition_minusi / _totalTime));
                    else if (selSeq >= 0)
                        selFrac = FracOfFloor(selSeq);
                }
                catch { }
                _lastSelSeq = selSeq;
                float w = 1f / _zoom;
                if (selFrac >= 0f && (selFrac < _viewStart || selFrac > _viewStart + w * 0.95f))
                {
                    _viewStart = Mathf.Clamp(selFrac - w * 0.1f, 0f, 1f - w);
                    _viewDirty = true;
                }
            }
            else
            {
                var sel = ed.selectedFloors;
                if (sel != null && sel.Count > 0 && sel[sel.Count - 1] != null)
                {
                    selSeq = sel[sel.Count - 1].seqID;
                    selFrac = FracOfFloor(selSeq);
                }
                if (selSeq != _lastSelSeq)
                {
                    _lastSelSeq = selSeq;
                    float w = 1f / _zoom;
                    if (selFrac >= 0f && (selFrac < _viewStart + w * 0.05f || selFrac > _viewStart + w * 0.95f))
                    {
                        _viewStart = Mathf.Clamp(selFrac - w * 0.5f, 0f, 1f - w);
                        _viewDirty = true;
                    }
                }
            }

            if (_transportOn) UpdateTransport(ed, playing, selSeq);
            TickCamInspector(ed);

            // While play-testing with the game's hide-cursor-while-playing option on,
            // hovering the strip re-shows the cursor so it stays usable; leaving the
            // strip re-hides it (only if we were the ones who showed it).
            Vector2 mouse = Input.mousePosition;
            bool hoverStrip = RectTransformUtility.RectangleContainsScreenPoint(_stripRect, mouse, null);
            if (playing && hoverStrip)
            {
                if (!Cursor.visible) { Cursor.visible = true; _cursorForced = true; }
            }
            else if (_cursorForced)
            {
                _cursorForced = false;
                try { if (playing && Persistence.GetHideCursorWhilePlaying()) Cursor.visible = false; }
                catch { }
            }

            // Drag the playhead to scrub — selects the tile nearest the mouse, riding the
            // editor's own camera jump. Grab zone is ±8px around the line, edit mode only.
            if (playing) _draggingHead = false;
            else if (_draggingHead)
            {
                if (!Input.GetMouseButton(0)) _draggingHead = false;
                else
                {
                    Vector2 lp;
                    if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_markerArea, mouse, null, out lp))
                    {
                        var rct = _markerArea.rect;
                        float f = _viewStart + Mathf.Clamp01((lp.x - rct.xMin) / Mathf.Max(1f, rct.width)) / _zoom;
                        int fi = FloorAtFrac(f);
                        if (fi >= 0 && fi < floors.Count && fi != selSeq)
                        {
                            try { ed.SelectFloor(floors[fi], true); } catch { }
                            selSeq = fi;
                            selFrac = FracOfFloor(fi);
                            _lastSelSeq = fi; // keep the auto-scroll from recentering mid-drag
                        }
                    }
                }
            }
            else if (Input.GetMouseButtonDown(0) && selFrac >= 0f
                && RectTransformUtility.RectangleContainsScreenPoint(_stripRect, mouse, null))
            {
                Vector2 lp;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_markerArea, mouse, null, out lp))
                {
                    var rct = _markerArea.rect;
                    float headX = rct.xMin + (selFrac - _viewStart) * _zoom * rct.width;
                    if (Mathf.Abs(lp.x - headX) <= 8f) _draggingHead = true;
                }
            }

            // Wheel pans while hovering the strip (only meaningful when zoomed in).
            if (hoverStrip && _zoom > 1f)
            {
                float wheel = Input.mouseScrollDelta.y;
                if (wheel != 0f)
                {
                    _viewStart = Mathf.Clamp(_viewStart - wheel * 0.08f / _zoom, 0f, 1f - 1f / _zoom);
                    _viewDirty = true;
                }
            }

            if (_viewDirty)
            {
                RenderMarkers();
                _viewDirty = false;
            }

            // Playhead placement (hidden when the selection sits outside the view).
            float areaW = _markerArea.rect.width;
            float px = (selFrac - _viewStart) * _zoom * areaW;
            bool showHead = selFrac >= 0f && px >= -1f && px <= areaW + 1f;
            if (_playhead.gameObject.activeSelf != showHead) _playhead.gameObject.SetActive(showHead);
            if (showHead) _playhead.anchoredPosition = new Vector2(px, 0f);

            TickCamDrag(ed, floors, mouse);

            // Hover: nearest marker in the lane under the mouse (not while scrubbing).
            if (tip != null || _laneEvents == null || _draggingHead) return;
            if (_dragEvt != null && _dragMoved) return; // dragging a keyframe, not hovering
            if (!RectTransformUtility.RectangleContainsScreenPoint(_markerArea, mouse, null)) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_markerArea, mouse, null, out local)) return;
            var rect = _markerArea.rect;
            float frac = _viewStart + Mathf.Clamp01((local.x - rect.xMin) / Mathf.Max(1f, rect.width)) / _zoom;
            float yFromTop = rect.yMax - local.y;
            bool inSubRow = false;
            int lane;
            if (CamMode && _expandedLane >= 0)
            {
                float subTop = (_expandedLane + 1) * LaneHEff;
                if (yFromTop < subTop) lane = Mathf.FloorToInt(yFromTop / LaneHEff);
                else if (yFromTop < subTop + SubRowH) { lane = _expandedLane; inSubRow = true; }
                else lane = Mathf.FloorToInt((yFromTop - SubRowH) / LaneHEff);
            }
            else lane = Mathf.FloorToInt(yFromTop / LaneHEff);
            LevelEvent hit = null;
            if (inSubRow)
            {
                // diamonds live at fanned texture positions — hit-test in that space
                float texX = Mathf.Clamp01((local.x - rect.xMin) / Mathf.Max(1f, rect.width)) * TexW;
                float best = 10f;
                foreach (var kf in _subKf)
                {
                    float d = Mathf.Abs(kf.Key - texX);
                    if (d < best) { best = d; hit = kf.Value; }
                }
            }
            else if (lane >= 0 && lane < _laneEvents.Length)
            {
                float tol = 6f / (Mathf.Max(1f, rect.width) * _zoom);
                hit = KeyframeMode
                    ? CamSpanHit(_laneEvents[lane], frac, tol)   // whole bar body is clickable
                    : NearestInLane(_laneEvents[lane], frac, tol);
            }
            if (hit != null)
            {
                tip = BuildTooltip(hit, true);
                tipAt = new Vector2(mouse.x, mouse.y + 14f); // strip is at the bottom → open upward
            }
            // Click a marker to select its tile; click empty strip to move the playhead
            // to that spot (and keep scrubbing while held). Both ride the camera jump.
            // Keyframe modes: right-click a keyframe → visual ease picker for that event.
            if (!playing && KeyframeMode && hit != null && Input.GetMouseButtonDown(1))
            {
                EditorEasePicker.Open(hit, mouse);
                return;
            }
            // Cam mode: right-click EMPTY lane space → new set keyframe for that lane's
            // property at that tile, carrying the current value (camera doesn't jump).
            if (!playing && CamMode && hit == null && !inSubRow && Input.GetMouseButtonDown(1)
                && lane >= 0 && lane < _laneEvents.Length)
            {
                CreateCamKeyframe(ed, floors, lane, frac);
                return;
            }
            if (!playing && Input.GetMouseButtonDown(0))
            {
                if (CamMode && hit != null)
                {
                    if (inSubRow)
                    {
                        // retiming lives HERE: hold-and-move drags the diamond to a new
                        // tile; a plain click just selects.
                        _dragEvt = hit; _dragStartX = local.x; _dragMoved = false; _dragLane = lane;
                    }
                    else
                    {
                        // main view: selection + inline inspector only, never dragging
                        if (hit.floor >= 0 && hit.floor < floors.Count)
                            try { ed.SelectFloor(floors[hit.floor], true); } catch { }
                        _camSel = hit; _camSelDirty = true; _camSelLane = lane;
                        _tlSig = 0; _scanCooldown = 0; _viewDirty = true;
                    }
                }
                else
                {
                    int target = hit != null ? hit.floor : FloorAtFrac(frac);
                    if (target >= 0 && target < floors.Count)
                        try { ed.SelectFloor(floors[target], true); } catch { }
                    if (hit == null)
                    {
                        _draggingHead = true;
                        if (CamMode && _camSel != null) { _camSel = null; _camSelDirty = true; }
                    }
                    else { _showEvt = hit; _showDelay = 3; } // then open THAT event (deferred: the
                                                              // selection change rebuilds the inspector)
                }
            }

            if (_showEvt != null && --_showDelay <= 0)
            {
                var evt = _showEvt; _showEvt = null;
                try
                {
                    // index of this instance among same-type events on its floor
                    int idx = 0;
                    foreach (var e in ed.events)
                    {
                        if (e == null || e.floor != evt.floor || e.eventType != evt.eventType) continue;
                        if (ReferenceEquals(e, evt)) break;
                        idx++;
                    }
                    ed.levelEventsPanel.ShowPanel(evt.eventType, idx);
                }
                catch { }
            }
        }

        private static LevelEvent _showEvt;   // marker-click → open this exact event
        private static int _showDelay;

        // ── cam keyframe dragging + inline selection ────────────────────────
        private static LevelEvent _dragEvt;
        private static float _dragStartX;
        private static bool _dragMoved;
        private static int _dragLane;
        internal static int _camSelLane;
        internal static LevelEvent _camSel;   // keyframe shown in the inline inspector
        internal static bool _camSelDirty;

        /* Inline keyframe inspector (the "expanded track"): a row of editable fields at
           the top of the strip for the clicked keyframe — duration, position, rotation,
           zoom, ease. Typing a value ENABLES the property; clearing a field disables it
           (the panel's on/off toggle equivalent). Every commit goes through SaveStateScope. */
        private static void TickCamInspector(scnEditor ed)
        {
            bool want = CamMode && _camSel != null && ed != null && !ed.playMode;
            if (!want)
            {
                if (_camSel != null && !CamMode) { _camSel = null; _camSelDirty = true; }
                if (_camInspHost != null && _camInspHost.gameObject.activeSelf)
                { _camInspHost.gameObject.SetActive(false); _tlSig = 0; _scanCooldown = 0; }
                return;
            }
            // ESC closes the row (before other ESC consumers is fine — it's passive UI)
            if (Input.GetKeyDown(KeyCode.Escape)) { _camSel = null; _camSelDirty = true; _tlSig = 0; _scanCooldown = 0; return; }
            if (!ed.events.Contains(_camSel)) { _camSel = null; _camSelDirty = true; _tlSig = 0; return; }
            if (!_camSelDirty) return;
            _camSelDirty = false;
            BuildCamInspector(ed, _camSel);
        }

        private static void BuildCamInspector(scnEditor ed, LevelEvent evt)
        {
            if (_camInspHost == null)
            {
                var go = new GameObject("CamInspector", typeof(RectTransform));
                go.transform.SetParent(_stripRect, false);
                _camInspHost = (RectTransform)go.transform;
                _camInspHost.anchorMin = new Vector2(0f, 1f);
                _camInspHost.anchorMax = new Vector2(1f, 1f);
                _camInspHost.pivot = new Vector2(0.5f, 1f);
                _camInspHost.offsetMin = new Vector2(12f, -CamInspH); // transport lives in its own band now
                _camInspHost.offsetMax = new Vector2(-ZoomW - 8f, 0f);
            }
            _camInspHost.gameObject.SetActive(true);
            for (int i = _camInspHost.childCount - 1; i >= 0; i--)
                Object.Destroy(_camInspHost.GetChild(i).gameObject);

            float x = 0f;
            x = CamInspLabel(x, evt.eventType + " · #" + evt.floor, 170f);
            x = CamInspField(x, Loc.T("Beats"), 64f, CamNum(evt, "duration"), s => CamCommitNum(evt, "duration", s, false));
            Vector2 pos = Vector2.zero;
            bool hasPos = false;
            try { var d = EventData(evt); if (d != null && d.TryGetValue("position", out var v) && v is Vector2 p) { pos = p; hasPos = true; } }
            catch { }
            if (hasPos)
            {
                x = CamInspField(x, "X", 60f, CamDisabled(evt, "position") || float.IsNaN(pos.x) ? "" : Trim(pos.x),
                    s => CamCommitPos(evt, s, true));
                x = CamInspField(x, "Y", 60f, CamDisabled(evt, "position") || float.IsNaN(pos.y) ? "" : Trim(pos.y),
                    s => CamCommitPos(evt, s, false));
            }
            x = CamInspField(x, Loc.T("Rotation"), 66f, CamTouchesNumber(evt, "rotation") ? CamNum(evt, "rotation") : "",
                s => CamCommitNum(evt, "rotation", s, true));
            x = CamInspField(x, Loc.T("Zoom"), 72f, CamTouchesNumber(evt, "zoom") ? CamNum(evt, "zoom") : "",
                s => CamCommitNum(evt, "zoom", s, true));
            x = CamInspButtonAt(x, Loc.T("Ease"), 76f, () => EditorEasePicker.Open(evt, Input.mousePosition));
            CamInspButtonAt(x, Loc.T("Graph"), 86f, () => EditorGraph.Open(_camSelLane));
        }

        private static string Trim(float v) => v.ToString("0.###");

        private static string CamNum(LevelEvent evt, string key)
        {
            try
            {
                var d = EventData(evt);
                if (d == null || !d.TryGetValue(key, out var v) || v == null) return "";
                float f = System.Convert.ToSingle(v);
                return float.IsNaN(f) ? "" : Trim(f);
            }
            catch { return ""; }
        }

        // Commit a numeric field; empty string disables the property (togglable = true).
        private static void CamCommitNum(LevelEvent evt, string key, string s, bool togglable)
        {
            var ed = scnEditor.instance;
            if (ed == null) return;
            try
            {
                using (new SaveStateScope(ed))
                {
                    if (string.IsNullOrWhiteSpace(s))
                    {
                        if (togglable && evt.disabled != null && evt.disabled.ContainsKey(key)) evt.disabled[key] = true;
                    }
                    else if (float.TryParse(s, out float f))
                    {
                        evt[key] = f;
                        if (evt.disabled != null && evt.disabled.ContainsKey(key)) evt.disabled[key] = false;
                    }
                }
            }
            catch (System.Exception ex) { SapphireLog.Log("Timeline: edit failed: " + ex.Message); }
            _tlSig = 0; _scanCooldown = 0; _viewDirty = true; _camSelDirty = true;
        }

        private static void CamCommitPos(LevelEvent evt, string s, bool isX)
        {
            var ed = scnEditor.instance;
            if (ed == null) return;
            try
            {
                Vector2 p = Vector2.zero;
                var d = EventData(evt);
                if (d != null && d.TryGetValue("position", out var v) && v is Vector2 cur) p = cur;
                using (new SaveStateScope(ed))
                {
                    float f = string.IsNullOrWhiteSpace(s) ? float.NaN
                        : (float.TryParse(s, out float pf) ? pf : (isX ? p.x : p.y));
                    var np = isX ? new Vector2(f, p.y) : new Vector2(p.x, f);
                    evt["position"] = np;
                    bool any = !float.IsNaN(np.x) || !float.IsNaN(np.y);
                    if (evt.disabled != null && evt.disabled.ContainsKey("position")) evt.disabled["position"] = !any;
                }
            }
            catch (System.Exception ex) { SapphireLog.Log("Timeline: edit failed: " + ex.Message); }
            _tlSig = 0; _scanCooldown = 0; _viewDirty = true; _camSelDirty = true;
        }

        private static float CamInspLabel(float x, string text, float w)
        {
            var go = new GameObject("L", typeof(RectTransform));
            go.transform.SetParent(_camInspHost, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0.5f); r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(w, 22f);
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = UI.Theme.TmpFont; t.fontSize = 14f;
            t.color = UI.Theme.TextMuted; t.alignment = TextAlignmentOptions.Left;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = false;
            t.text = text;
            return x + w + 8f;
        }

        private static float CamInspField(float x, string label, float w, string value, System.Action<string> commit)
        {
            var lgo = new GameObject("FL", typeof(RectTransform));
            lgo.transform.SetParent(_camInspHost, false);
            var lr = (RectTransform)lgo.transform;
            lr.anchorMin = new Vector2(0f, 0.5f); lr.anchorMax = new Vector2(0f, 0.5f);
            lr.pivot = new Vector2(0f, 0.5f);
            lr.anchoredPosition = new Vector2(x, 0f);
            lr.sizeDelta = new Vector2(200f, 22f);
            var lt = lgo.AddComponent<TextMeshProUGUI>();
            lt.font = UI.Theme.TmpFont; lt.fontSize = 13f;
            lt.color = UI.Theme.TextMuted; lt.alignment = TextAlignmentOptions.Left;
            lt.textWrappingMode = TextWrappingModes.NoWrap; lt.overflowMode = TextOverflowModes.Overflow;
            lt.raycastTarget = false;
            lt.text = label;
            float lw = Mathf.Min(72f, label.Length * 8f + 6f);
            lr.sizeDelta = new Vector2(lw, 26f);
            x += lw + 4f;

            var go = new GameObject("F", typeof(RectTransform));
            go.transform.SetParent(_camInspHost, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0.5f); r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(w, 30f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(1f, 1f, 1f, 0.08f);
            bg.raycastTarget = true;
            var txtGo = new GameObject("T", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(7f, 0f); tr.offsetMax = new Vector2(-7f, 0f);
            var txt = UI.UIBuilder.Tmp(txtGo, value, 14f, TextAnchor.MiddleLeft, UI.Theme.Text);
            txt.richText = false;
            var field = UI.UIBuilder.BuildInputField(go, txt);
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = value;
            field.onEndEdit.AddListener(sv => commit(sv));
            return x + w + 10f;
        }

        private static float CamInspButtonAt(float x, string label, float w, System.Action onClick)
        {
            CamInspButton(x, label, w, onClick);
            return x + w + 8f;
        }

        private static void CamInspButton(float x, string label, float w, System.Action onClick)
        {
            var go = new GameObject("B", typeof(RectTransform));
            go.transform.SetParent(_camInspHost, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0.5f); r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(w, 30f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.3f);
            bg.raycastTarget = true;
            var txtGo = new GameObject("T", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            var txt = UI.UIBuilder.Tmp(txtGo, label, 14f, TextAnchor.MiddleCenter, UI.Theme.Text);
            txt.raycastTarget = false;
            UI.ClickHandler.Attach(go, onClick);
        }

        // ── deco/filter string lanes ─────────────────────────────────────────
        private static bool IsStrModeEvent(LevelEvent e)
        {
            if (TlMode == 2)
                return e.eventType == LevelEventType.MoveDecorations
                    || e.eventType == LevelEventType.EmitParticle
                    || e.eventType == LevelEventType.SetParticle
                    || e.eventType == LevelEventType.SetText;
            if (TlMode == 3)
                return e.eventType == LevelEventType.SetFilter
                    || e.eventType == LevelEventType.SetFilterAdvanced;
            return false;
        }

        private static IEnumerable<string> KeysOf(LevelEvent e)
        {
            var d = EventData(e);
            if (d == null) yield break;
            if (TlMode == 2)
            {
                if (!d.TryGetValue("tag", out var tv) || !(tv is string tags)) yield break;
                foreach (var t in tags.Split(' '))
                    if (!string.IsNullOrEmpty(t)) yield return t;
            }
            else if (TlMode == 3)
            {
                if (d.TryGetValue("filter", out var fv) && fv != null)
                {
                    var n = fv.ToString();
                    // CameraFilterPack_Blur_BlurHole → Blur_BlurHole (lane labels stay short)
                    if (n.StartsWith("CameraFilterPack_")) n = n.Substring(17);
                    if (n.Length > 0) yield return n;
                }
            }
        }

        /* Lanes = the tags/filters VISIBLE in the current view window, capped at 8 by
           frequency — a level's hundreds of deco tags would be unusable as rows. The view
           window is part of the structure signature, so panning re-derives the lanes. */
        private static void BuildStringLanes(List<LevelEvent> events)
        {
            _strLanes.Clear();
            float vEnd = _viewStart + 1f / Mathf.Max(1f, _zoom);
            float m = (vEnd - _viewStart) * 0.05f;
            var counts = new Dictionary<string, int>();
            foreach (var e in events)
            {
                if (e == null || !IsStrModeEvent(e)) continue;
                float frac = FracOfFloor(e.floor);
                if (frac < _viewStart - m || frac > vEnd + m) continue;
                foreach (var key in KeysOf(e))
                    counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
            }
            var sorted = new List<KeyValuePair<string, int>>(counts);
            sorted.Sort((a, b) =>
            {
                int c = b.Value.CompareTo(a.Value);
                return c != 0 ? c : string.CompareOrdinal(a.Key, b.Key);
            });
            for (int i = 0; i < sorted.Count && i < 8; i++) _strLanes.Add(sorted[i].Key);
        }

        private static Color StrLaneColor(int i) =>
            Color.HSVToRGB((0.11f + i * 0.618034f) % 1f, 0.55f, 0.95f);

        /* Per-property keyframe independence: retiming ONE property of an event that
           carries several must SPLIT it — a clone carrying just that property moves to the
           new tile, the original keeps the rest. comp: "position" | "posx" | "posy" |
           "rotation" | "zoom". Returns the event that carries comp afterwards. Caller
           provides the SaveStateScope. */
        internal static LevelEvent SplitForRetime(scnEditor ed, LevelEvent evt, string comp, int newFloor)
        {
            bool posWhole = comp == "position";
            bool posX = comp == "posx", posY = comp == "posy";
            bool isPos = posWhole || posX || posY;
            bool othersTouched =
                (!isPos && (CamTouchesPosition(evt) || CamTouchesNumber(evt, comp == "rotation" ? "zoom" : "rotation")))
                || (isPos && (CamTouchesNumber(evt, "rotation") || CamTouchesNumber(evt, "zoom")))
                || (posX && PosCompTouched(evt, false)) || (posY && PosCompTouched(evt, true));
            if (!othersTouched)
            {
                evt.floor = newFloor;
                ed.ApplyEventsToFloors();
                return evt;
            }
            var clone = evt.Copy();
            clone.floor = newFloor;
            // clone keeps ONLY comp; original loses comp
            if (isPos)
            {
                SetPropOff(clone, "rotation"); SetPropOff(clone, "zoom");
                if (posX || posY)
                {
                    var p = PosOf(clone);
                    clone["position"] = posX ? new Vector2(p.x, float.NaN) : new Vector2(float.NaN, p.y);
                    var q = PosOf(evt);
                    evt["position"] = posX ? new Vector2(float.NaN, q.y) : new Vector2(q.x, float.NaN);
                    if (!PosCompTouched(evt, true) && !PosCompTouched(evt, false)) SetPropOff(evt, "position");
                }
                else SetPropOff(evt, "position");
            }
            else
            {
                SetPropOff(clone, "position");
                SetPropOff(clone, comp == "rotation" ? "zoom" : "rotation");
                SetPropOff(evt, comp);
            }
            ed.events.Add(clone);
            ed.ApplyEventsToFloors();
            return clone;
        }

        // The property's value AT a tile = the target of the last earlier keyframe
        // touching it, else the level-settings camera default. comp: 0=x 1=y 2=rot 3=zoom.
        private static float CamValueAt(scnEditor ed, int floor, int comp)
        {
            LevelEvent best = null;
            foreach (var e in ed.events)
            {
                if (e == null || !e.active || e.eventType != LevelEventType.MoveCamera) continue;
                if (e.floor > floor) continue;
                bool touches = comp <= 1 ? PosCompTouched(e, comp == 0)
                    : CamTouchesNumber(e, comp == 2 ? "rotation" : "zoom");
                if (!touches) continue;
                if (best == null || e.floor > best.floor) best = e;
            }
            if (best != null)
            {
                var d = EventData(best);
                if (d != null)
                {
                    if (comp <= 1 && d.TryGetValue("position", out var pv) && pv is Vector2 p)
                        return comp == 0 ? p.x : p.y;
                    if (comp >= 2 && d.TryGetValue(comp == 2 ? "rotation" : "zoom", out var nv) && nv != null)
                        try { return System.Convert.ToSingle(nv); } catch { }
                }
            }
            try
            {
                var ld = scnGame.instance != null ? scnGame.instance.levelData : null;
                if (ld != null)
                    return comp == 3 ? ld.camZoom : comp == 2 ? ld.camRotation
                        : comp == 0 ? ld.camPosition.x : ld.camPosition.y;
            }
            catch { }
            return comp == 3 ? 100f : 0f;
        }

        private static void CreateCamKeyframe(scnEditor ed, List<scrFloor> floors, int lane, float frac)
        {
            int floor = FloorAtFrac(frac);
            if (floor <= 0 || floor >= floors.Count) return;
            try
            {
                var evt = new LevelEvent(floor, LevelEventType.MoveCamera);
                evt["duration"] = 0f;
                evt["ease"] = DG.Tweening.Ease.Linear;
                if (lane == 0)
                {
                    evt["position"] = new Vector2(CamValueAt(ed, floor, 0), CamValueAt(ed, floor, 1));
                    SetPropOff(evt, "rotation"); SetPropOff(evt, "zoom");
                }
                else if (lane == 1)
                {
                    evt["rotation"] = CamValueAt(ed, floor, 2);
                    SetPropOff(evt, "position"); SetPropOff(evt, "zoom");
                }
                else
                {
                    evt["zoom"] = CamValueAt(ed, floor, 3);
                    SetPropOff(evt, "position"); SetPropOff(evt, "rotation");
                }
                using (new SaveStateScope(ed))
                {
                    ed.events.Add(evt);
                    ed.ApplyEventsToFloors();
                }
                try { ed.SelectFloor(floors[floor], true); } catch { }
                _camSel = evt; _camSelDirty = true; _camSelLane = lane;
                _tlSig = 0; _scanCooldown = 0; _viewDirty = true;
            }
            catch (System.Exception ex) { SapphireLog.Log("Timeline: create keyframe failed: " + ex.Message); }
        }

        private static Vector2 PosOf(LevelEvent e)
        {
            var d = EventData(e);
            return d != null && d.TryGetValue("position", out var v) && v is Vector2 p ? p : new Vector2(float.NaN, float.NaN);
        }

        internal static bool PosCompTouched(LevelEvent e, bool isX)
        {
            if (CamDisabled(e, "position")) return false;
            var p = PosOf(e);
            return !float.IsNaN(isX ? p.x : p.y);
        }

        private static void SetPropOff(LevelEvent e, string key)
        {
            try { if (e.disabled != null) e.disabled[key] = true; } catch { }
        }

        private static void TickCamDrag(scnEditor ed, List<scrFloor> floors, Vector2 mouse)
        {
            if (_dragEvt == null) return;
            if (!CamMode) { _dragEvt = null; return; }
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_markerArea, mouse, null, out local))
            { if (Input.GetMouseButtonUp(0)) { _dragEvt = null; SyncDragGhost(false, mouse); } return; }
            var rect = _markerArea.rect;
            float frac = _viewStart + Mathf.Clamp01((local.x - rect.xMin) / Mathf.Max(1f, rect.width)) / _zoom;
            if (!_dragMoved && Mathf.Abs(local.x - _dragStartX) > 6f) _dragMoved = true;
            SyncDragGhost(_dragMoved, mouse);
            if (!Input.GetMouseButtonUp(0)) return;
            SyncDragGhost(false, mouse);
            var evt = _dragEvt; _dragEvt = null;
            if (_dragMoved)
            {
                int nf = FloorAtFrac(frac);
                if (nf > 0 && nf < floors.Count && nf != evt.floor)
                {
                    // per-property retime: splits the event when it carries other props
                    string comp = _dragLane == 1 ? "rotation" : _dragLane == 2 ? "zoom" : "position";
                    try
                    {
                        using (new SaveStateScope(ed))
                            SplitForRetime(ed, evt, comp, nf);
                    }
                    catch (System.Exception ex) { SapphireLog.Log("Timeline: keyframe move failed: " + ex.Message); }
                    _tlSig = 0; _scanCooldown = 0; _viewDirty = true;
                }
                return;
            }
            // plain click: select the tile and open the inline inspector row
            if (evt.floor >= 0 && evt.floor < floors.Count)
                try { ed.SelectFloor(floors[evt.floor], true); } catch { }
            _camSel = evt; _camSelDirty = true; _camSelLane = _dragLane;
            _tlSig = 0; _scanCooldown = 0; _viewDirty = true; // strip grows for the inspector row
        }

        private static float FracOfFloor(int floor)
        {
            if (_timePrefix == null || floor < 0 || floor >= _timePrefix.Length) return -1f;
            return Mathf.Clamp01((float)(_timePrefix[floor] / _totalTime));
        }

        // Inverse of FracOfFloor: the floor whose beat is nearest the given fraction.
        private static int FloorAtFrac(float frac)
        {
            if (_timePrefix == null || _timePrefix.Length == 0) return -1;
            double beat = frac * _totalTime;
            int lo = 0, hi = _timePrefix.Length - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (_timePrefix[mid] < beat) lo = mid + 1; else hi = mid;
            }
            if (lo > 0 && beat - _timePrefix[lo - 1] < _timePrefix[lo] - beat) lo--;
            return lo;
        }

        private static void Zoom(float factor)
        {
            _userZoomed = true;
            float old = _zoom;
            // Zoom 1 = whole level; the far end always allows down to a 4-beat window.
            float max = Mathf.Max(1f, (float)(_totalTime / (MinViewBeats * _secPerBeat)));
            _zoom = Mathf.Clamp(_zoom * factor, 1f, max);
            if (Mathf.Approximately(old, _zoom)) return;
            float center = _viewStart + 0.5f / old;
            _viewStart = Mathf.Clamp(center - 0.5f / _zoom, 0f, 1f - 1f / _zoom);
            _viewDirty = true;
        }

        // Cam mode: a keyframe owns its whole [start, start+duration] bar, not just the
        // head — overlapping bars resolve to the latest-starting one (drawn on top).
        private static LevelEvent CamSpanHit(List<KeyValuePair<float, LevelEvent>> lane, float frac, float tol)
        {
            LevelEvent best = null;
            float bestStart = float.NegativeInfinity;
            for (int i = 0; i < lane.Count; i++)
            {
                float s = lane[i].Key;
                if (s - tol > frac) break; // sorted by start
                float e = CamDurationFracEnd(lane[i].Value, s);
                if (frac <= e + tol && s >= bestStart) { best = lane[i].Value; bestStart = s; }
            }
            return best;
        }

        private static LevelEvent NearestInLane(List<KeyValuePair<float, LevelEvent>> lane, float frac, float tol)
        {
            if (lane == null || lane.Count == 0) return null;
            int lo = 0, hi = lane.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (lane[mid].Key < frac) lo = mid + 1; else hi = mid;
            }
            LevelEvent best = null;
            float bestD = tol;
            for (int i = Mathf.Max(0, lo - 1); i <= Mathf.Min(lane.Count - 1, lo + 1); i++)
            {
                float d = Mathf.Abs(lane[i].Key - frac);
                if (d <= bestD) { bestD = d; best = lane[i].Value; }
            }
            return best;
        }

        // Lanes, labels, beat table and texture allocation — everything that only changes
        // when the level's structure does. Marker pixels render separately (RenderMarkers)
        // so zoom/pan don't pay for this.
        private static void RebuildStructure(List<LevelEvent> events, List<scrFloor> floors)
        {
            // The axis is real song time, straight from the game: CalculateFloorEntryTimes
            // fills scrFloor.entryTime with the full semantics — speed/magicshapes, pauses
            // (including their angle corrections), holds, freeroam. The game only runs it
            // at play start, so refresh it ourselves; it's a cheap single pass over the
            // floors and idempotent. Hand-rolling this from angleLength drifted (pause
            // angle corrections, notably).
            try { if (ADOBase.lm != null) ADOBase.lm.CalculateFloorEntryTimes(); } catch { }
            int len = floors.Count;
            _timePrefix = new double[len];
            _beatPrefix = new double[len];
            _bpmPrefix = new double[len];
            _tileBpmPrefix = new double[len];
            try
            {
                var ldb = scnGame.instance != null ? scnGame.instance.levelData : null;
                _levelBpm = ldb != null && ldb.bpm > 1f ? ldb.bpm : 100.0;
            }
            catch { _levelBpm = 100.0; }
            for (int i = 0; i < len; i++)
            {
                var fl = floors[i];
                _timePrefix[i] = fl != null ? fl.entryTime : (i > 0 ? _timePrefix[i - 1] : 0.0);
                _beatPrefix[i] = fl != null ? fl.entryBeat : (i > 0 ? _beatPrefix[i - 1] : 0.0);
                /* Tile-BPM = speed × base bpm = the game's PULSE, angle-INDEPENDENT: a
                   pseudo (30°) and its completion (150°) both read the same pulse, since
                   angle-beats already split the 180° beat between them. This is the base-
                   detection signal — pseudos, subdivisions and dotted notes never move it.
                   Effective (perceived) BPM = tileBpm ÷ (angle/180) = tileBpm × π/angleLength
                   (270°@1000 → 666.7, 90° → 2000) — a per-tile tap rate, shown as `eff`
                   only; it's wrong as a base because a pseudo's completion tile inflates it. */
                double al = fl != null ? fl.angleLength : System.Math.PI;
                double tileBpm = fl != null ? fl.speed * _levelBpm : _levelBpm;
                _tileBpmPrefix[i] = tileBpm > 1.0 ? tileBpm : (i > 0 ? _tileBpmPrefix[i - 1] : _levelBpm);
                _bpmPrefix[i] = al > 0.05
                    ? tileBpm * System.Math.PI / al
                    : (i > 0 ? _bpmPrefix[i - 1] : tileBpm);
            }
            /* The game's entryBeat loop stops one floor short: the LAST tile keeps a stale
               0 (and tile 0 is set to -1), which broke the beat↔time binary searches —
               every beat extrapolated off the chart's end and the grid vanished. Repair
               the tail with the game's own accumulation (prev tile's angle-beats +
               extraBeats) and enforce monotonicity for safety. */
            if (len >= 2 && _beatPrefix[len - 1] <= _beatPrefix[len - 2])
            {
                double db = 0.0;
                try
                {
                    var prev = floors[len - 2];
                    if (prev != null) db = prev.angleLength / System.Math.PI + prev.extraBeats;
                }
                catch { }
                if (db <= 1e-6)
                {
                    double dtl = _timePrefix[len - 1] - _timePrefix[len - 2];
                    db = _secPerBeat > 1e-9 ? dtl / _secPerBeat : 1.0;
                }
                _beatPrefix[len - 1] = _beatPrefix[len - 2] + db;
            }
            for (int i = 1; i < len; i++)
                if (_beatPrefix[i] < _beatPrefix[i - 1]) _beatPrefix[i] = _beatPrefix[i - 1];

            // Pad past the last tile's start so end-of-chart markers aren't glued to the edge.
            double lastT = _timePrefix[len - 1];
            _totalTime = lastT > 0.0 ? lastT * 1.02 + 0.05 : 1.0;
            // Tile 0 is the start pad with no input — the musical grid begins at tile 1.
            _timeOffset = len > 1 ? _timePrefix[1] : 0.0;
            try
            {
                var ld = scnGame.instance != null ? scnGame.instance.levelData : null;
                if (ld != null && ld.bpm > 1f) _secPerBeat = 60.0 / ld.bpm;
            }
            catch { }

            // Converts _beatPrefix from raw angle-beats to MUSICAL beats — offsets after.
            RebuildTempoMap(len);
            _beatOffset = len > 1 ? _beatPrefix[1] : 0.0;
            _lastBeat = _beatPrefix[len - 1];
            DetectTimeSignature(len);

            // Default view spans 64 beats (zoom 1 = whole level); a manual zoom sticks
            // until the strip is next hidden.
            if (!_userZoomed)
                _zoom = Mathf.Max(1f, (float)(_totalTime / (DefaultViewBeats * _secPerBeat)));
            _viewStart = Mathf.Clamp(_viewStart, 0f, 1f - 1f / _zoom);

            _lanes.Clear();
            if (CamMode)
            {
                // Property layers are ALWAYS present — this is an editing surface, and an
                // empty lane is where a new keyframe of that property would go.
                _lanes.Add((LevelEventCategory)CamLanePos);
                _lanes.Add((LevelEventCategory)CamLaneRot);
                _lanes.Add((LevelEventCategory)CamLaneZoom);
            }
            else if (TlMode >= 2)
            {
                BuildStringLanes(events);
                for (int i = 0; i < _strLanes.Count; i++)
                    _lanes.Add((LevelEventCategory)(-100 - i)); // sentinels; labels from _strLanes
            }
            else
            {
                bool other = false;
                var present = new HashSet<LevelEventCategory>();
                for (int i = 0; i < events.Count; i++)
                {
                    var ev = events[i];
                    if (ev == null || ev.eventType == LevelEventType.Twirl) continue;
                    var c = PrimaryCategory(ev);
                    if ((int)c < 0) other = true; else present.Add(c);
                }
                for (int i = 0; i < LaneOrder.Length; i++)
                    if (present.Contains(LaneOrder[i])) _lanes.Add(LaneOrder[i]);
                if (other) _lanes.Add((LevelEventCategory)(-1));
            }
            int laneCount = Mathf.Max(1, _lanes.Count);

            // Min height: the 2-line transport cluster clips on 1-lane charts otherwise.
            float inspH = CamMode && _camSel != null ? CamInspH : 0f;
            if (!CamMode) _expandedLane = -1;
            // The transport lives in a TOP band in every mode — as a full-height left
            // column its clock text always collided with the lane labels.
            float transH = _transportGo != null ? TransBandH : 0f;
            if (_transportGo != null)
            {
                var trr = (RectTransform)_transportGo.transform;
                trr.anchorMin = new Vector2(0f, 1f);
                trr.anchorMax = new Vector2(0f, 1f);
                trr.pivot = new Vector2(0f, 1f);
                trr.sizeDelta = new Vector2(TransportW + 160f, TransBandH);
                trr.anchoredPosition = new Vector2(8f, -inspH);
            }
            float subH = CamMode && _expandedLane >= 0 ? SubRowH : 0f;
            _stripRect.sizeDelta = new Vector2(0f,
                Mathf.Max(66f, StripPad * 2f + laneCount * LaneHEff + subH + transH) + inspH);
            if (_markerArea != null) _markerArea.offsetMax = new Vector2(-ZoomW - 8f, -StripPad - inspH - transH);
            if (_laneLabelHost != null) _laneLabelHost.offsetMax = new Vector2(HeaderW - 6f, -StripPad - inspH - transH);

            int texH = laneCount * TexLaneHEff + (CamMode && _expandedLane >= 0 ? (int)SubRowH : 0);
            if (_markerTex == null || _markerTex.height != texH)
            {
                if (_markerTex != null) Object.Destroy(_markerTex);
                _markerTex = new Texture2D(TexW, texH, TextureFormat.RGBA32, false);
                _markerTex.filterMode = FilterMode.Point;
                _markerImage.texture = _markerTex;
            }

            var laneIndex = new Dictionary<LevelEventCategory, int>();
            for (int i = 0; i < _lanes.Count; i++) laneIndex[_lanes[i]] = i;
            _laneEvents = new List<KeyValuePair<float, LevelEvent>>[laneCount];
            for (int i = 0; i < laneCount; i++) _laneEvents[i] = new List<KeyValuePair<float, LevelEvent>>();
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                if (e == null || e.floor < 0 || e.floor >= len) continue;
                if (CamMode)
                {
                    // One event can animate several properties → it appears in each layer.
                    if (e.eventType != LevelEventType.MoveCamera) continue;
                    float frac = FracOfFloor(e.floor);
                    if (CamTouchesPosition(e)) _laneEvents[0].Add(new KeyValuePair<float, LevelEvent>(frac, e));
                    if (CamTouchesNumber(e, "rotation")) _laneEvents[1].Add(new KeyValuePair<float, LevelEvent>(frac, e));
                    if (CamTouchesNumber(e, "zoom")) _laneEvents[2].Add(new KeyValuePair<float, LevelEvent>(frac, e));
                    continue;
                }
                if (TlMode >= 2)
                {
                    // multi-tag events appear in every matching lane
                    if (!IsStrModeEvent(e)) continue;
                    float sfrac = FracOfFloor(e.floor);
                    foreach (var key in KeysOf(e))
                    {
                        int idx = _strLanes.IndexOf(key);
                        if (idx >= 0 && idx < laneCount)
                            _laneEvents[idx].Add(new KeyValuePair<float, LevelEvent>(sfrac, e));
                    }
                    continue;
                }
                // Twirls are on half the tiles of any decent chart — pure noise here.
                if (e.eventType == LevelEventType.Twirl) continue;
                int lane;
                if (!laneIndex.TryGetValue(PrimaryCategory(e), out lane)) continue;
                _laneEvents[lane].Add(new KeyValuePair<float, LevelEvent>(FracOfFloor(e.floor), e));
            }
            for (int i = 0; i < laneCount; i++)
                _laneEvents[i].Sort((a, b) => a.Key.CompareTo(b.Key));

            for (int i = _laneLabelHost.childCount - 1; i >= 0; i--)
                Object.Destroy(_laneLabelHost.GetChild(i).gameObject);
            for (int i = 0; i < _lanes.Count; i++)
            {
                var go = new GameObject("Lane", typeof(RectTransform));
                go.transform.SetParent(_laneLabelHost, false);
                var r = (RectTransform)go.transform;
                r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
                r.pivot = new Vector2(0.5f, 1f);
                r.sizeDelta = new Vector2(0f, LaneHEff);
                float shift = CamMode && _expandedLane >= 0 && i > _expandedLane ? SubRowH : 0f;
                r.anchoredPosition = new Vector2(0f, -i * LaneHEff - shift);
                var t = go.AddComponent<TextMeshProUGUI>();
                t.font = UI.Theme.TmpFont;
                t.fontSize = CamMode ? 16 : 13;
                if (CamMode)
                {
                    // AE-style: clicking the property label expands its keyframe sub-row —
                    // the only place timeline keyframes can be retimed.
                    int laneIdx = i;
                    t.raycastTarget = true;
                    UI.ClickHandler.Attach(go, () =>
                    {
                        _expandedLane = _expandedLane == laneIdx ? -1 : laneIdx;
                        _tlSig = 0; _scanCooldown = 0; _viewDirty = true;
                    });
                }
                t.color = CategoryColor(_lanes[i]);
                t.alignment = TextAlignmentOptions.Right;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Overflow;
                if (!CamMode) t.raycastTarget = false;
                if (TlMode >= 2 && i < _strLanes.Count)
                {
                    var tag = _strLanes[i];
                    t.text = tag.Length > 14 ? tag.Substring(0, 13) + "…" : tag;
                    t.color = StrLaneColor(i);
                    t.fontSize = 13;
                }
                else t.text = CategoryLabel(_lanes[i]) + (CamMode && _expandedLane == i ? " ›" : "");
            }
        }

        /* "Touches" = the event actually animates that property. ADOFAI's null-means-keep
           semantics parse omitted tween fields to NaN, so a real number = a keyframe. */
        internal static Dictionary<string, object> EventData(LevelEvent e)
        {
            if (_dataField == null)
                _dataField = typeof(LevelEvent).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
            return _dataField?.GetValue(e) as Dictionary<string, object>;
        }

        // The editor's per-property on/off toggles live in LevelEvent.disabled (public).
        internal static bool CamDisabled(LevelEvent e, string key)
        {
            try { return e.disabled != null && e.disabled.TryGetValue(key, out bool d) && d; }
            catch { return false; }
        }

        internal static bool CamTouchesNumber(LevelEvent e, string key)
        {
            try
            {
                if (CamDisabled(e, key)) return false;
                var d = EventData(e);
                if (d == null || !d.TryGetValue(key, out var v) || v == null) return false;
                float f = System.Convert.ToSingle(v);
                return !float.IsNaN(f) && !float.IsInfinity(f);
            }
            catch { return false; }
        }

        internal static bool CamTouchesPosition(LevelEvent e)
        {
            try
            {
                if (CamDisabled(e, "position")) return false;
                var d = EventData(e);
                if (d == null || !d.TryGetValue("position", out var v) || !(v is Vector2 p)) return false;
                return !float.IsNaN(p.x) || !float.IsNaN(p.y);
            }
            catch { return false; }
        }

        // Keyframe end = start + duration beats at the tile's pulse (duration is in beats).
        private static float CamDurationFracEnd(LevelEvent e, float startFrac)
        {
            try
            {
                var d = EventData(e);
                if (d == null || !d.TryGetValue("duration", out var v) || v == null) return startFrac;
                float beats = System.Convert.ToSingle(v);
                if (float.IsNaN(beats) || beats <= 0f) return startFrac;
                int fl = Mathf.Clamp(e.floor, 0, _tileBpmPrefix.Length - 1);
                double bpm = _tileBpmPrefix[fl] > 1.0 ? _tileBpmPrefix[fl] : _levelBpm;
                double endT = _timePrefix[fl] + beats * (60.0 / bpm);
                return _totalTime > 0.001 ? (float)(endT / _totalTime) : startFrac;
            }
            catch { return startFrac; }
        }

        // Paint the events inside the current view window (3×9 blocks, category-colored,
        // dim when off). Called on structure change, zoom, pan and selection auto-scroll.
        private static void RenderMarkers()
        {
            if (_markerTex == null || _laneEvents == null) return;
            int laneCount = _laneEvents.Length;
            int tlh = TexLaneHEff;
            int subH = CamMode && _expandedLane >= 0 ? (int)SubRowH : 0;
            int texH = laneCount * tlh + subH;
            var px = new Color32[TexW * texH];
            float viewEnd = _viewStart + 1f / _zoom;

            /* Measure grid: 4/4 measures in BEAT domain, mapped to time through the game's
               own (entryBeat → entryTime) pairs — BPM shifts change the spacing by
               themselves. Each tempo segment re-anchors the lattice (detected base-BPM
               changes, onbeat-shifting pauses, odd intros); the segment boundary itself is
               marked amber. Spacing doubles per segment when a zoomed-out view would smear
               into solid grey. The level format has no time-signature data — measures
               default to 4 beats. */
            var faint = new Color32(255, 255, 255, 22);
            var strong = new Color32(255, 255, 255, 44);
            var boundary = new Color32(255, 190, 80, 70);
            double viewStartT = _viewStart * _totalTime;
            double viewEndT = viewEnd * _totalTime;
            double viewSpan = _totalTime / _zoom;
            double viewStartBeat = BeatAtTime(viewStartT);
            // One line per measure, phased to the detected downbeat so the drop lands on a
            // strong line even if the intro doesn't fill whole measures.
            int measure = System.Math.Max(1, _timeSigNum);
            double gridOrigin = _beatOffset + _timeSigPhase;
            for (int s = 0; s < _tempo.Count; s++)
            {
                var seg = _tempo[s];
                double segEnd = s + 1 < _tempo.Count ? _tempo[s + 1].StartBeat : _lastBeat + 64.0;
                double spb = seg.Spb > 1e-6 ? seg.Spb : _secPerBeat;
                double beatsPerLine = measure;
                while (viewSpan / (beatsPerLine * spb) > TexW / 26.0) beatsPerLine *= 2.0;
                double from = System.Math.Max(seg.StartBeat, viewStartBeat - beatsPerLine);
                long n = (long)System.Math.Ceiling((from - gridOrigin) / beatsPerLine - 1e-9);
                for (int guard = 0; guard < 2048; guard++, n++)
                {
                    double b = gridOrigin + n * beatsPerLine;
                    if (b >= segEnd) break;
                    double t = TimeAtBeat(b);
                    if (t > viewEndT) break;
                    double frac = t / _totalTime;
                    if (frac < _viewStart) continue;
                    int gx = Mathf.Clamp((int)((frac - _viewStart) * _zoom * (TexW - 1)), 0, TexW - 1);
                    var gcol = (n & 3) == 0 ? strong : faint; // every 4th measure = phrase
                    for (int y = 0; y < texH; y++) px[y * TexW + gx] = gcol;
                }
                if (s == 0) continue;
                // Detected re-anchor point: amber tick so shifted sections are visible.
                double bt = TimeAtBeat(seg.StartBeat);
                if (bt >= viewStartT && bt <= viewEndT)
                {
                    double bfrac = bt / _totalTime;
                    int bx = Mathf.Clamp((int)((bfrac - _viewStart) * _zoom * (TexW - 1)), 0, TexW - 1);
                    for (int y = 0; y < texH; y++) px[y * TexW + bx] = boundary;
                }
            }

            for (int lane = 0; lane < laneCount; lane++)
            {
                var cat = lane < _lanes.Count ? _lanes[lane] : (LevelEventCategory)(-1);
                var c = TlMode >= 2 ? StrLaneColor(lane) : CategoryColor(cat);
                var list = _laneEvents[lane];
                // texture row 0 = bottom; lanes at/above the expanded one sit above its sub-row
                int y0 = (laneCount - 1 - lane) * tlh + (subH > 0 && lane <= _expandedLane ? subH : 0) + 1;
                for (int i = 0; i < list.Count; i++)
                {
                    float frac = list[i].Key;
                    var e = list[i].Value;
                    float fracEnd = KeyframeMode ? CamDurationFracEnd(e, frac) : frac;
                    if (fracEnd < _viewStart || frac > viewEnd) continue;
                    var col = e.active
                        ? new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 255)
                        : new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 90);
                    int x0 = Mathf.Clamp((int)((frac - _viewStart) * _zoom * (TexW - 4)), 0, TexW - 4);
                    if (!KeyframeMode)
                    {
                        for (int y = y0; y < y0 + tlh - 3; y++)
                        {
                            int row = y * TexW;
                            px[row + x0] = col;
                            px[row + x0 + 1] = col;
                            px[row + x0 + 2] = col;
                            px[row + x0 + 3] = col;
                        }
                        continue;
                    }
                    // Camera keyframes: duration-0 = thin SET tick; tweens = head + body
                    // bar to the end beat. A set tick followed by a tween at the same spot
                    // is the classic pair — they join visually into tick-then-bar.
                    bool selKf = ReferenceEquals(e, _camSel);
                    if (selKf) col = new Color32(255, 255, 255, 255);
                    int x1 = Mathf.Clamp((int)((fracEnd - _viewStart) * _zoom * (TexW - 4)), x0, TexW - 4);
                    var body = new Color32(col.r, col.g, col.b, (byte)(e.active ? 120 : 45));
                    if (x1 <= x0 + 1)
                    {
                        for (int y = y0; y < y0 + tlh - 3; y++)
                        {
                            int row = y * TexW;
                            px[row + x0] = col;
                            px[row + x0 + 1] = col;
                        }
                        continue;
                    }
                    int midLo = y0 + 4, midHi = y0 + tlh - 7;
                    for (int y = y0; y < y0 + tlh - 3; y++)
                    {
                        int row = y * TexW;
                        px[row + x0] = col;                      // head
                        px[row + x0 + 1] = col;
                        px[row + x0 + 2] = col;
                        if (y >= midLo && y <= midHi)
                            for (int x = x0 + 3; x <= x1; x++) px[row + x] = body;
                        px[row + x1] = col;                      // end cap
                    }
                }
            }
            // Expanded property sub-row: one diamond per keyframe, pairs fanned apart so
            // duration-0 partners stay individually clickable. This row is where retiming
            // happens; the cache feeds its hit-testing.
            _subKf.Clear();
            if (subH > 0 && _expandedLane < laneCount)
            {
                int rowBase = (laneCount - 1 - _expandedLane) * tlh;
                int cy = rowBase + subH / 2;
                var list = _laneEvents[_expandedLane];
                var rowBg = new Color32(255, 255, 255, 12);
                for (int y = rowBase; y < rowBase + subH - 2; y++)
                    for (int x = 0; x < TexW; x++)
                        if (px[y * TexW + x].a == 0) px[y * TexW + x] = rowBg;
                int lastX = -1000; // NOT int.MinValue — cx - lastX must not overflow
                for (int i = 0; i < list.Count; i++)
                {
                    float frac = list[i].Key;
                    if (frac < _viewStart || frac > viewEnd) continue;
                    int cx = Mathf.Clamp((int)((frac - _viewStart) * _zoom * (TexW - 4)), 0, TexW - 4) + 2;
                    if (cx - lastX < 12) cx = lastX + 12; // fan out stacked pairs
                    if (cx > TexW - 6) continue;
                    lastX = cx;
                    var e = list[i].Value;
                    bool selK = ReferenceEquals(e, _camSel);
                    var kc = selK ? new Color32(255, 255, 255, 255)
                        : new Color32(200, 220, 255, e.active ? (byte)230 : (byte)90);
                    int rad = selK ? 7 : 5;
                    for (int dy = -rad; dy <= rad; dy++)
                    {
                        int w2 = rad - Mathf.Abs(dy);
                        int yy = Mathf.Clamp(cy + dy, 0, texH - 1);
                        for (int dx = -w2; dx <= w2; dx++)
                            px[yy * TexW + Mathf.Clamp(cx + dx, 0, TexW - 1)] = kc;
                    }
                    _subKf.Add(new KeyValuePair<int, LevelEvent>(cx, e));
                }
            }

            _markerTex.SetPixels32(px);
            _markerTex.Apply(false);
        }

        // sub-row diamond cache: texture-x → event (for hit-testing)
        private static readonly List<KeyValuePair<int, LevelEvent>> _subKf = new List<KeyValuePair<int, LevelEvent>>();

        // ghost diamond riding the cursor while a keyframe is being dragged (visual only)
        private static GameObject _dragGhostGo;

        private static void SyncDragGhost(bool show, Vector2 mouse)
        {
            if (!show)
            {
                if (_dragGhostGo != null && _dragGhostGo.activeSelf) _dragGhostGo.SetActive(false);
                return;
            }
            if (_dragGhostGo == null)
            {
                _dragGhostGo = new GameObject("DragGhost", typeof(RectTransform));
                _dragGhostGo.transform.SetParent(_canvasGo.transform, false);
                var r = (RectTransform)_dragGhostGo.transform;
                r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
                r.pivot = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(13f, 13f);
                r.localEulerAngles = new Vector3(0f, 0f, 45f); // square → diamond
                var img = _dragGhostGo.AddComponent<Image>();
                img.color = new Color(1f, 1f, 1f, 0.75f);
                img.raycastTarget = false;
            }
            if (!_dragGhostGo.activeSelf) _dragGhostGo.SetActive(true);
            Vector2 local;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, mouse, null, out local))
                ((RectTransform)_dragGhostGo.transform).anchoredPosition = local;
        }

        // ── tooltip ─────────────────────────────────────────────────────────

        /* GLOBAL analysis: the base pulse at each tile = the DURATION-WEIGHTED WINDOWED
           MODE of the angle-adjusted effective BPM (_bpmPrefix). Rationale — look at how
           the whole passage is composed, not one tile at a time: the beats are the notes
           whose perceived tempo recurs most (weighted by how long they sound), so the most
           massive histogram bin over a local window is the pulse. Subdivisions (higher
           eff, shorter → less weight), dotted offbeats and pseudos (rare) can't own the
           bin, so the base is naturally robust — no fragile per-tile re-anchoring. Then
           musical beats advance by dt × base/60 (keeps the grid on downbeats), and _tempo
           segments are cut where the base curve steps (amber ticks + edge extrapolation).
           A phase pass finds off-lattice runs (odd pauses / shifted intros). */
        private static void RebuildTempoMap(int len)
        {
            _tempo.Clear();
            double baseBpm = ClampBase(_levelBpm > 1.0 ? _levelBpm : 100.0);
            var seg0 = new TempoSeg { StartBeat = 0.0, Anchor = 0.0, Spb = 60.0 / baseBpm };
            _tempo.Add(seg0);
            _baseBpmPrefix = new double[len];
            if (len < 3 || _beatPrefix == null || _timePrefix == null || _bpmPrefix == null)
            {
                for (int i = 0; i < len; i++) _baseBpmPrefix[i] = baseBpm;
                return;
            }

            // Per-tile note duration (weight for the sustain vote).
            var dur = new double[len];
            for (int i = 0; i < len - 1; i++) dur[i] = System.Math.Max(1e-4, _timePrefix[i + 1] - _timePrefix[i]);
            dur[len - 1] = len >= 2 ? dur[len - 2] : 1.0;

            /* MODEL: default the whole chart to the HEADER tempo; only introduce a new base
               where the tile BPM holds a new consistent value for a sustained run. Per tile,
               `CandidateBase` returns a VOTE (its own tile BPM) only for genuine beat tiles
               (moderate ½×–2× ratio), and 0 = ABSTAIN for decoration (subdivisions, clean
               multiples, magicshape scalings). The base is the DURATION-WEIGHTED WINDOWED
               MODE of the votes — a new tempo has to persist to win, and where a stretch is
               all decoration (no votes) it falls back to the header (baseBpm). */
            var cand = new double[len];
            for (int i = 0; i < len; i++) cand[i] = CandidateBase(_tileBpmPrefix != null ? _tileBpmPrefix[i] : baseBpm);

            const int W = 16;                 // ± window in tiles for the sustain vote
            const double BinLo = 30.0, BinW = 2.0; const int Bins = 400; // 30..830 bpm
            var hist = new double[Bins];
            var touched = new List<int>();
            for (int i = 0; i < len; i++)
            {
                int lo = System.Math.Max(1, i - W), hi = System.Math.Min(len - 1, i + W);
                touched.Clear();
                for (int j = lo; j <= hi; j++)
                {
                    double e = cand[j];
                    if (e <= 1.0) continue;
                    int bi = (int)System.Math.Round((e - BinLo) / BinW);
                    if (bi < 0 || bi >= Bins) continue;
                    if (hist[bi] == 0.0) touched.Add(bi);
                    hist[bi] += dur[j];
                }
                int peak = -1; double peakW = -1.0;
                foreach (int bi in touched)
                {
                    double w = hist[bi] + (bi > 0 ? hist[bi - 1] : 0.0) + (bi < Bins - 1 ? hist[bi + 1] : 0.0);
                    if (w > peakW) { peakW = w; peak = bi; }
                }
                double num = 0.0, den = 0.0;
                for (int j = lo; j <= hi; j++)
                {
                    double e = cand[j];
                    if (e <= 1.0) continue;
                    int bi = (int)System.Math.Round((e - BinLo) / BinW);
                    if (System.Math.Abs(bi - peak) <= 1) { num += e * dur[j]; den += dur[j]; }
                }
                _baseBpmPrefix[i] = den > 0.0 ? num / den : baseBpm;
                foreach (int bi in touched) hist[bi] = 0.0;
            }

            // Musical beats from the per-tile base.
            var musical = new double[len];
            for (int i = 0; i < len - 1; i++)
            {
                double dt = _timePrefix[i + 1] - _timePrefix[i];
                musical[i + 1] = musical[i] + (dt > 0.0 ? dt * _baseBpmPrefix[i] / 60.0 : 0.0);
            }
            System.Array.Copy(musical, _beatPrefix, len);
            seg0.StartBeat = seg0.Anchor = len > 1 ? musical[1] : 0.0;
            seg0.Spb = 60.0 / _baseBpmPrefix[System.Math.Min(1, len - 1)];
            _tempo[0] = seg0;

            // Cut a segment where the base curve steps >5% (amber ticks / edge extrapolation).
            double curBase = _baseBpmPrefix[System.Math.Min(1, len - 1)];
            for (int i = 2; i < len && _tempo.Count <= MaxTempoSegs; i++)
            {
                if (System.Math.Abs(_baseBpmPrefix[i] - curBase) > 0.05 * curBase)
                {
                    curBase = _baseBpmPrefix[i];
                    _tempo.Add(new TempoSeg { StartBeat = musical[i], Anchor = musical[i], Spb = 60.0 / curBase });
                }
            }
            if (_tempo.Count > MaxTempoSegs)
            {
                _tempo.Clear();
                _tempo.Add(seg0);
                return;
            }

            // Phase pass: walk tiles against the active segment's anchor.
            var outSegs = new List<TempoSeg>();
            int segIdx = 0;
            var active = _tempo[0];
            outSegs.Add(active);
            int runTiles = 0;
            double runStartBeat = 0.0, runOffset = 0.0;
            for (int i = 1; i < len; i++)
            {
                double b = _beatPrefix[i];
                while (segIdx + 1 < _tempo.Count && b >= _tempo[segIdx + 1].StartBeat - 1e-9)
                {
                    segIdx++;
                    active = _tempo[segIdx];
                    outSegs.Add(active);
                    runTiles = 0;
                }
                double f = b - active.Anchor;
                double off = f - System.Math.Round(f); // signed distance to the lattice
                if (System.Math.Abs(off) <= PhaseTol) { runTiles = 0; continue; }
                if (runTiles > 0 && System.Math.Abs(off - runOffset) > PhaseTol) { runTiles = 0; }
                if (runTiles == 0) { runStartBeat = b; runOffset = off; }
                runTiles++;
                if (runTiles >= PhaseMinTiles && b - runStartBeat >= PhaseMinBeats)
                {
                    var shifted = new TempoSeg { StartBeat = runStartBeat, Anchor = runStartBeat, Spb = active.Spb };
                    outSegs.Add(shifted);
                    active = shifted;
                    runTiles = 0;
                    if (outSegs.Count > MaxTempoSegs) break;
                }
            }
            if (outSegs.Count > MaxTempoSegs) return; // keep the base-step segmentation
            outSegs.Sort((a, x) => a.StartBeat.CompareTo(x.StartBeat));
            _tempo.Clear();
            _tempo.AddRange(outSegs);
        }

        /* A tile's VOTE for the local base tempo, or 0 = ABSTAIN (decoration, no vote).
           Only tiles at a genuine moderate tempo (½×–2× the header, non-integer ratio) vote
           their own tile BPM — that's a real beat tile whether at the header or a shifted
           tempo like 150→225. Everything else abstains: a clean ×k / ÷k of the header is a
           subdivision or magicshape (900 = 6×150), and an EXTREME ratio is a magicshape
           scaling — these must NOT vote, or a magicshape's 900 folding to 150 fights the
           real 225 tiles and the base flips between the two. The vote loop takes the mode of
           the actual votes and falls back to the header where a stretch is all decoration. */
        private static double CandidateBase(double tileBpm)
        {
            double h = _levelBpm > 1.0 ? _levelBpm : 120.0;
            if (tileBpm <= 1.0) return 0.0;
            double r = tileBpm / h;
            double k = System.Math.Round(r);
            if (k >= 2.0 && System.Math.Abs(r - k) <= 0.08 * k) return 0.0;          // clean ×k → abstain
            double kd = System.Math.Round(1.0 / r);
            if (kd >= 2.0 && System.Math.Abs(1.0 / r - kd) <= 0.08 * kd) return 0.0; // clean /k → abstain
            if (r >= 0.5 && r <= 2.0) return tileBpm;                                // genuine tempo → vote
            return 0.0;                                                              // extreme → abstain
        }

        /* Fold a tile-level rate into the musical pulse range by OCTAVES. Charters subdivide
           a beat by powers of 2 (a 220 section on 90° tiles runs tile-BPM 880 = 4×), so the
           fold factor is 2^k: 880 → 440 → 220. (An integer-divisor clamp landed on ÷3 →
           293/256, the wrong factor.) The pulse sits ≤ ~300; visual-speed events fold down
           by the same octaves. Which octave is right is disambiguated by accent alignment
           (`OctaveByAccent`) — this just bounds the range. */
        private static double ClampBase(double b)
        {
            if (b <= 0.0) return _levelBpm;
            while (b > 300.0) b *= 0.5;
            while (b < 90.0) b *= 2.0;
            return b;
        }

        // Detected musical BASE bpm at a tile — tracks genuine song-tempo shifts, with ×2
        // density sections and magicshapes folded back to the base.
        private static double BpmAtTile(int seq)
        {
            if (_baseBpmPrefix == null || _baseBpmPrefix.Length == 0) return _levelBpm;
            if (seq < 0) seq = 0;
            if (seq >= _baseBpmPrefix.Length) seq = _baseBpmPrefix.Length - 1;
            return _baseBpmPrefix[seq];
        }

        // The game's raw effective bpm at a tile (speed × base) — for comparing against the
        // detected base while tuning the detection.
        private static double EffBpmAtTile(int seq)
        {
            if (_bpmPrefix == null || _bpmPrefix.Length == 0) return _levelBpm;
            if (seq < 0) seq = 0;
            if (seq >= _bpmPrefix.Length) seq = _bpmPrefix.Length - 1;
            return _bpmPrefix[seq];
        }

        private static string BpmLine(int seq) =>
            "base " + BpmNum(BpmAtTile(seq)) + "  ·  eff " + BpmNum(EffBpmAtTile(seq))
            + " bpm  ·  " + _timeSigNum + "/4";

        private static string BpmNum(double bpm)
        {
            if (bpm <= 0.0) return "—";
            double r = System.Math.Round(bpm);
            return System.Math.Abs(bpm - r) < 0.05 ? r.ToString("0") : bpm.ToString("0.#");
        }

        /* Best-guess measure length (the level format carries no time-signature data).
           Builds an on-beat onset-strength signal in musical beats, then autocorrelates it
           at candidate measure lengths — the period the tile pattern best repeats on is the
           meter. Normalised so a longer lag can't win by overlap alone, and biased toward 4
           (by far the most common) so only a clearly stronger period overrides it. */
        private static void DetectTimeSignature(int len)
        {
            _timeSigNum = 4;
            _timeSigPhase = 0;
            if (_beatPrefix == null || len < 16) return;
            double span = _beatPrefix[len - 1] - _beatPrefix[1];
            if (span < 16.0) return;
            int nb = (int)System.Math.Ceiling(span) + 2;
            if (nb > 40000) return; // pathological; keep it cheap
            var onset = new double[nb];
            double b0 = _beatPrefix[1];
            for (int i = 1; i < len; i++)
            {
                double b = _beatPrefix[i] - b0;
                if (b < 0.0 || b >= nb) continue;
                int bi = (int)System.Math.Round(b);
                // On-beat tiles score highest; off-beat notes contribute little.
                onset[bi] += System.Math.Max(0.0, 1.0 - System.Math.Abs(b - bi) * 2.0);
            }
            double bestScore = 0.0;
            int bestL = 4;
            foreach (int L in new[] { 3, 4, 5, 6, 7 })
            {
                double sum = 0.0, norm = 0.0;
                for (int b = 0; b + L < nb; b++)
                {
                    sum += onset[b] * onset[b + L];
                    norm += onset[b] * onset[b];
                }
                double score = norm > 1e-9 ? sum / norm : 0.0;
                if (L == 4) score *= 1.15; // common-meter prior
                if (score > bestScore) { bestScore = score; bestL = L; }
            }
            _timeSigNum = bestL;

            /* Downbeat PHASE: the measure offset where onsets pile up most. Anchoring the
               measure grid here (not at the first tile) lands the strong lines on the real
               downbeats even when the intro doesn't fill whole measures before the drop. */
            double bestPhaseW = -1.0; int bestPhi = 0;
            for (int p = 0; p < bestL; p++)
            {
                double w = 0.0;
                for (int b = p; b < nb; b += bestL) w += onset[b];
                if (w > bestPhaseW) { bestPhaseW = w; bestPhi = p; }
            }
            _timeSigPhase = bestPhi;
        }

        // Piecewise-linear beat→time through the game's per-tile pairs; extrapolates with
        // the edge tempo beyond the chart.
        private static double TimeAtBeat(double b)
        {
            var bp = _beatPrefix; var tp = _timePrefix;
            if (bp == null || bp.Length < 2) return b * _secPerBeat;
            int hi = bp.Length - 1;
            if (b <= bp[0]) return tp[0] - (bp[0] - b) * _secPerBeat;
            if (b >= bp[hi])
            {
                double spb = _tempo.Count > 0 ? _tempo[_tempo.Count - 1].Spb : _secPerBeat;
                return tp[hi] + (b - bp[hi]) * spb;
            }
            int lo = 0;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (bp[mid] <= b) lo = mid; else hi = mid;
            }
            double db = bp[hi] - bp[lo];
            return db < 1e-9 ? tp[lo] : tp[lo] + (tp[hi] - tp[lo]) * (b - bp[lo]) / db;
        }

        private static double BeatAtTime(double t)
        {
            var bp = _beatPrefix; var tp = _timePrefix;
            if (bp == null || bp.Length < 2) return _secPerBeat > 0.0 ? t / _secPerBeat : 0.0;
            int hi = bp.Length - 1;
            if (t <= tp[0]) return bp[0] - (tp[0] - t) / _secPerBeat;
            if (t >= tp[hi])
            {
                double spb = _tempo.Count > 0 ? _tempo[_tempo.Count - 1].Spb : _secPerBeat;
                return bp[hi] + (t - tp[hi]) / (spb > 1e-9 ? spb : _secPerBeat);
            }
            int lo = 0;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (tp[mid] <= t) lo = mid; else hi = mid;
            }
            double dt = tp[hi] - tp[lo];
            return dt < 1e-9 ? bp[lo] : bp[lo] + (bp[hi] - bp[lo]) * (t - tp[lo]) / dt;
        }

        private static string BuildTooltip(LevelEvent e, bool withFloor)
        {
            if (e == null) return null;
            var sb = new StringBuilder(256);
            sb.Append("<b>").Append(EventName(e)).Append("</b>");
            if (withFloor)
            {
                sb.Append("  <color=#94949E>tile ").Append(e.floor);
                if (_beatPrefix != null && e.floor >= 0 && e.floor < _beatPrefix.Length)
                    sb.Append(" · beat ").Append(
                        (_beatPrefix[e.floor] - _beatOffset).ToString("0.#"));
                sb.Append("</color>");
            }
            if (!e.active) sb.Append("  <color=#C77>(off)</color>");

            if (_dataField == null)
                _dataField = typeof(LevelEvent).GetField("data", BindingFlags.NonPublic | BindingFlags.Instance);
            var data = _dataField != null ? _dataField.GetValue(e) as Dictionary<string, object> : null;
            if (data != null)
            {
                int lines = 0;
                foreach (var kv in data)
                {
                    if (lines >= 16) { sb.Append("\n<color=#94949E>+").Append(data.Count - lines).Append(" more…</color>"); break; }
                    string v = FormatValue(kv.Value);
                    sb.Append("\n<color=#94949E>").Append(kv.Key).Append(":</color> ").Append(v);
                    lines++;
                }
            }
            return sb.ToString();
        }

        private static string FormatValue(object v)
        {
            string s;
            if (v == null) s = "—";
            else if (v is float) s = ((float)v).ToString("0.###");
            else if (v is double) s = ((double)v).ToString("0.###");
            else s = v.ToString();
            if (s.Length > 48) s = s.Substring(0, 47) + "…";
            // Keep stray tag-like values from breaking the tooltip's own rich text.
            return s.Replace("<", "‹");
        }

        private static void ShowTooltip(string text, Vector2 screenAt)
        {
            if (string.IsNullOrEmpty(text))
            {
                if (_tooltipGo != null && _tooltipGo.activeSelf) _tooltipGo.SetActive(false);
                return;
            }
            if (!_tooltipGo.activeSelf) _tooltipGo.SetActive(true);
            if (_tooltipText.text != text)
            {
                _tooltipText.text = text;
                var pref = _tooltipText.GetPreferredValues(text, TooltipMaxW, 0f);
                _tooltipRect.sizeDelta = new Vector2(Mathf.Min(pref.x, TooltipMaxW) + 22f, pref.y + 16f);
            }
            Vector2 local;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenAt, null, out local);
            var half = _canvasRect.rect.size * 0.5f;
            var size = _tooltipRect.sizeDelta;
            // Grow downward from anchors in the upper half (chips), upward near the
            // bottom (the timeline strip), clamped on-screen either way.
            bool below = screenAt.y > Screen.height * 0.5f;
            _tooltipRect.pivot = new Vector2(0.5f, below ? 1f : 0f);
            local.x = Mathf.Clamp(local.x, -half.x + size.x * 0.5f + 6f, half.x - size.x * 0.5f - 6f);
            if (below) local.y = Mathf.Max(local.y, -half.y + size.y + 6f);
            else local.y = Mathf.Min(local.y, half.y - size.y - 6f);
            _tooltipRect.anchoredPosition = local;
        }

        // ── construction ────────────────────────────────────────────────────

        private static void BuildCanvas()
        {
            _canvasGo = new GameObject("SapphireEditorEvents", typeof(RectTransform));
            Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 899; // just under the tile-angle text
            _canvas = canvas;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>(); // zoom buttons only
            _canvasRect = (RectTransform)_canvasGo.transform;

            // Timeline strip docked at the bottom (Premiere-classic layout), lifted above
            // the game's own bottom playback bar until that bar is replaced too.
            var stripGo = new GameObject("Timeline", typeof(RectTransform));
            stripGo.transform.SetParent(_canvasGo.transform, false);
            _stripRect = (RectTransform)stripGo.transform;
            _stripRect.anchorMin = new Vector2(0f, 0f);
            _stripRect.anchorMax = new Vector2(1f, 0f);
            _stripRect.pivot = new Vector2(0.5f, 0f);
            _stripRect.anchoredPosition = new Vector2(0f, BottomGap);
            _stripRect.sizeDelta = new Vector2(0f, Mathf.Max(66f, StripPad * 2f + LaneH));
            var stripBg = stripGo.AddComponent<Image>();
            stripBg.color = new Color(0f, 0f, 0f, 0.55f);
            // The strip acts like a toolbar: it swallows clicks/wheel over itself so the
            // editor scene underneath doesn't react to them.
            stripBg.raycastTarget = true;

            var labelGo = new GameObject("LaneLabels", typeof(RectTransform));
            labelGo.transform.SetParent(stripGo.transform, false);
            _laneLabelHost = (RectTransform)labelGo.transform;
            _laneLabelHost.anchorMin = new Vector2(0f, 0f);
            _laneLabelHost.anchorMax = new Vector2(0f, 1f);
            _laneLabelHost.pivot = new Vector2(0f, 0.5f);
            _laneLabelHost.offsetMin = new Vector2(4f, StripPad);
            _laneLabelHost.offsetMax = new Vector2(HeaderW - 6f, -StripPad);

            var areaGo = new GameObject("Markers", typeof(RectTransform));
            areaGo.transform.SetParent(stripGo.transform, false);
            _markerArea = (RectTransform)areaGo.transform;
            _markerArea.anchorMin = new Vector2(0f, 0f);
            _markerArea.anchorMax = new Vector2(1f, 1f);
            _markerArea.offsetMin = new Vector2(HeaderW, StripPad);
            _markerArea.offsetMax = new Vector2(-ZoomW - 8f, -StripPad);
            _markerImage = areaGo.AddComponent<RawImage>();
            _markerImage.color = Color.white;
            _markerImage.raycastTarget = false;

            // Playhead: line + a small handle at the top, tracking the selected tile.
            var headGo = new GameObject("Playhead", typeof(RectTransform));
            headGo.transform.SetParent(areaGo.transform, false);
            _playhead = (RectTransform)headGo.transform;
            _playhead.anchorMin = new Vector2(0f, 0f);
            _playhead.anchorMax = new Vector2(0f, 1f);
            _playhead.pivot = new Vector2(0.5f, 0.5f);
            _playhead.sizeDelta = new Vector2(2f, 2f);
            var headImg = headGo.AddComponent<Image>();
            headImg.color = new Color(1f, 1f, 1f, 0.9f);
            headImg.raycastTarget = false;
            var knobGo = new GameObject("Knob", typeof(RectTransform));
            knobGo.transform.SetParent(headGo.transform, false);
            var knob = (RectTransform)knobGo.transform;
            knob.anchorMin = new Vector2(0.5f, 1f);
            knob.anchorMax = new Vector2(0.5f, 1f);
            knob.pivot = new Vector2(0.5f, 1f);
            knob.sizeDelta = new Vector2(12f, 8f);
            var knobBg = knobGo.AddComponent<RoundedRectGraphic>();
            knobBg.Radius = 2.5f;
            knobBg.color = Color.white;
            knobBg.raycastTarget = false;

            MakeZoomButton(stripGo.transform, "-", -ZoomW + 2f, () => Zoom(1f / 1.5f));
            MakeZoomButton(stripGo.transform, "+", -ZoomW / 2f + 3f, () => Zoom(1.5f));
            BuildCamButton(stripGo.transform);
            BuildGraphButton(stripGo.transform);
            BuildTransport(stripGo.transform);
            BuildModeCluster();
            _stripRect.gameObject.SetActive(false);

            // Selected-tile chip row, sitting under the tile-angle readout.
            var rowGo = new GameObject("TileEvents", typeof(RectTransform));
            rowGo.transform.SetParent(_canvasGo.transform, false);
            _chipsRow = (RectTransform)rowGo.transform;
            _chipsRow.anchorMin = _chipsRow.anchorMax = new Vector2(0.5f, 0.93f);
            _chipsRow.pivot = new Vector2(0.5f, 1f);
            _chipsRow.anchoredPosition = new Vector2(0f, -36f);
            var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = 6f;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            var fitter = rowGo.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _chipsRow.gameObject.SetActive(false);

            // Shared hover tooltip. Nested canvas with override sorting so it renders
            // above the OTHER Sapphire canvases too (event dock/chrome sit at 905-907
            // and used to cover it).
            var tipGo = new GameObject("Tooltip", typeof(RectTransform));
            tipGo.transform.SetParent(_canvasGo.transform, false);
            _tooltipGo = tipGo;
            _tooltipRect = (RectTransform)tipGo.transform;
            _tooltipRect.anchorMin = _tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
            _tooltipRect.pivot = new Vector2(0.5f, 1f);
            var tipCanvas = tipGo.AddComponent<Canvas>();
            tipCanvas.overrideSorting = true;
            tipCanvas.sortingOrder = 940;
            var tipBg = tipGo.AddComponent<RoundedRectGraphic>();
            tipBg.Radius = 8f;
            tipBg.color = new Color(0.07f, 0.07f, 0.09f, 0.95f);
            tipBg.BorderWidth = 1f;
            tipBg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            tipBg.raycastTarget = false;

            var tipTxtGo = new GameObject("Text", typeof(RectTransform));
            tipTxtGo.transform.SetParent(tipGo.transform, false);
            var ttr = (RectTransform)tipTxtGo.transform;
            ttr.anchorMin = Vector2.zero; ttr.anchorMax = Vector2.one;
            ttr.offsetMin = new Vector2(11f, 8f); ttr.offsetMax = new Vector2(-11f, -8f);
            _tooltipText = tipTxtGo.AddComponent<TextMeshProUGUI>();
            _tooltipText.font = UI.Theme.TmpFont;
            _tooltipText.fontSize = 14;
            _tooltipText.color = Color.white;
            _tooltipText.alignment = TextAlignmentOptions.TopLeft;
            _tooltipText.richText = true;
            _tooltipText.raycastTarget = false;
            _tooltipGo.SetActive(false);
        }

        // Play/rewind proxy the game's own buttons (scnEditor.playPause/rewind) so behavior
        // and shortcuts stay identical; only the chrome is ours. Built once, shown when the
        // "Transport in timeline" toggle is on (ApplyTransportLayout shifts the lanes over).
        private static void BuildTransport(Transform strip)
        {
            _transportGo = new GameObject("Transport", typeof(RectTransform));
            _transportGo.transform.SetParent(strip, false);
            var r = (RectTransform)_transportGo.transform;
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 0.5f);
            r.offsetMin = new Vector2(0f, 0f);
            r.offsetMax = new Vector2(TransportW, 0f);

            MakeTransportButton(r, "Rewind", 8f, 24f, go =>
            {
                var t = MakeGlyph(go.transform, "←", 15);
                t.color = Color.white;
            }, () =>
            {
                try { var ed = scnEditor.instance; if (ed != null && ed.rewind != null) ed.rewind.onClick.Invoke(); }
                catch { }
            });

            MakeTransportButton(r, "Play", 40f, 28f, go =>
            {
                _playGlyph = MakeGlyph(go.transform, "▶", 14);
                _playGlyph.color = Color.white;
                // Pause = two procedural bars (user fonts have no reliable ❚ glyph).
                _pauseBars = new GameObject("Pause", typeof(RectTransform));
                _pauseBars.transform.SetParent(go.transform, false);
                var pb = (RectTransform)_pauseBars.transform;
                pb.anchorMin = Vector2.zero; pb.anchorMax = Vector2.one;
                pb.offsetMin = pb.offsetMax = Vector2.zero;
                for (int i = 0; i < 2; i++)
                {
                    var bar = new GameObject("Bar", typeof(RectTransform));
                    bar.transform.SetParent(pb, false);
                    var br = (RectTransform)bar.transform;
                    br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
                    br.pivot = new Vector2(0.5f, 0.5f);
                    br.anchoredPosition = new Vector2(i == 0 ? -3.5f : 3.5f, 0f);
                    br.sizeDelta = new Vector2(3.5f, 12f);
                    var bg = bar.AddComponent<RoundedRectGraphic>();
                    bg.Radius = 1.5f;
                    bg.color = Color.white;
                    bg.raycastTarget = false;
                }
                _pauseBars.SetActive(false);
            }, () =>
            {
                try
                {
                    var ed = scnEditor.instance;
                    if (ed == null) return;
                    if (ed.playMode && RDC.auto)
                    {
                        // Mid-autoplay the game's play button does nothing useful — mirror
                        // the editor's Space handler instead (pause/resume the run).
                        var c = ADOBase.controller;
                        if (c == null) return;
                        ed.pausedInPlayMode = !c.paused;
                        if (ed.buttonAuto != null) ed.buttonAuto.interactable = !ed.pausedInPlayMode;
                        if (ed.blinkTimer != null) DG.Tweening.TweenExtensions.TogglePause(ed.blinkTimer);
                        c.TogglePauseGame();
                    }
                    else if (ed.playPause != null) ed.playPause.onClick.Invoke();
                }
                catch { }
            });

            // (AUTO moved to the mode cluster, right of NO FAIL.) Clock on the top line,
            // detected base BPM on the line below it — both top-anchored in the strip.
            var clockGo = new GameObject("Clock", typeof(RectTransform));
            clockGo.transform.SetParent(r, false);
            var cr = (RectTransform)clockGo.transform;
            cr.anchorMin = new Vector2(0f, 1f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(0f, 1f);
            cr.offsetMin = new Vector2(76f, 0f);
            cr.offsetMax = new Vector2(-4f, 0f);
            cr.anchoredPosition = new Vector2(76f, -5f);
            cr.sizeDelta = new Vector2(cr.sizeDelta.x, 17f);
            _clockText = clockGo.AddComponent<TextMeshProUGUI>();
            _clockText.font = UI.Theme.TmpFont;
            _clockText.fontSize = 13;
            _clockText.color = new Color(0.85f, 0.85f, 0.88f, 1f);
            _clockText.alignment = TextAlignmentOptions.TopLeft;
            _clockText.textWrappingMode = TextWrappingModes.NoWrap;
            _clockText.overflowMode = TextOverflowModes.Overflow;
            _clockText.raycastTarget = false;

            var bpmGo = new GameObject("Bpm", typeof(RectTransform));
            bpmGo.transform.SetParent(r, false);
            var bpr = (RectTransform)bpmGo.transform;
            bpr.anchorMin = new Vector2(0f, 1f);
            bpr.anchorMax = new Vector2(1f, 1f);
            bpr.pivot = new Vector2(0f, 1f);
            bpr.offsetMin = new Vector2(76f, 0f);
            bpr.offsetMax = new Vector2(-4f, 0f);
            bpr.anchoredPosition = new Vector2(76f, -22f);
            bpr.sizeDelta = new Vector2(bpr.sizeDelta.x, 16f);
            _bpmText = bpmGo.AddComponent<TextMeshProUGUI>();
            _bpmText.font = UI.Theme.TmpFont;
            _bpmText.fontSize = 12;
            _bpmText.color = new Color(0.6f, 0.62f, 0.68f, 1f);
            _bpmText.alignment = TextAlignmentOptions.TopLeft;
            _bpmText.textWrappingMode = TextWrappingModes.NoWrap;
            _bpmText.overflowMode = TextOverflowModes.Overflow;
            _bpmText.raycastTarget = false;
            _transportGo.SetActive(false);
        }

        // Quick-access state chips the charter flips constantly, parked above the strip's
        // right end. Editor Mode toggles the Sapphire setting; difficulty opens a small
        // dropdown (GCS.difficulty); no-fail flips GCS.useNoFail (and the live controller
        // flag mid-run); AUTO flips RDC.auto.
        private static void BuildModeCluster()
        {
            var go = new GameObject("ModeCluster", typeof(RectTransform));
            go.transform.SetParent(_canvasGo.transform, false);
            _modeCluster = (RectTransform)go.transform;
            _modeCluster.anchorMin = _modeCluster.anchorMax = new Vector2(1f, 0f);
            _modeCluster.pivot = new Vector2(1f, 0f);
            _modeCluster.sizeDelta = new Vector2(64f + 78f + 64f + 46f + 18f, 24f);

            _emBg = MakeModeChip(0f, 64f, "EDITOR", out _emLabel, () =>
            {
                try
                {
                    var s = MainClass.Settings;
                    if (s == null) return;
                    s.EditorModeEnabled = !s.EditorModeEnabled;
                    UI.UICore.OnSettingsChanged?.Invoke();
                }
                catch { }
            });
            MakeModeChip(70f, 78f, "NORMAL", out _diffLabel, ToggleDiffMenu);
            _nfBg = MakeModeChip(154f, 64f, "NO FAIL", out _nfLabel, () =>
            {
                try
                {
                    GCS.useNoFail = !GCS.useNoFail;
                    var c = scrController.instance;
                    if (c != null) c.noFail = GCS.useNoFail;
                }
                catch { }
            });
            _autoBg = MakeModeChip(224f, 46f, "AUTO", out _autoLabel,
                () => { try { RDC.auto = !RDC.auto; } catch { } });
            _autoShown = false;
            _modeCluster.gameObject.SetActive(false);
        }

        // Small dropdown above the difficulty chip; picking a row sets GCS.difficulty.
        private static void ToggleDiffMenu()
        {
            if (_diffMenuGo != null) { CloseDiffMenu(); return; }

            _diffMenuGo = new GameObject("DiffMenu", typeof(RectTransform));
            _diffMenuGo.transform.SetParent(_canvasGo.transform, false);
            var blocker = (RectTransform)_diffMenuGo.transform;
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = Vector2.zero; blocker.offsetMax = Vector2.zero;
            var blockImg = _diffMenuGo.AddComponent<Image>();
            blockImg.color = new Color(0f, 0f, 0f, 0.01f);
            blockImg.raycastTarget = true;
            UI.ClickHandler.Attach(_diffMenuGo, CloseDiffMenu);

            const float rowH = 26f, width = 86f, pad = 4f;
            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(_diffMenuGo.transform, false);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(1f, 0f);
            panel.pivot = new Vector2(0f, 0f);
            // Above the difficulty chip (cluster x=70): cluster is right-anchored.
            var cp = _modeCluster.anchoredPosition;
            panel.anchoredPosition = new Vector2(cp.x - _modeCluster.sizeDelta.x + 70f - 4f,
                cp.y + 24f + 6f);
            panel.sizeDelta = new Vector2(width, rowH * 3f + pad * 2f);
            var bg = panelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 9f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.97f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;

            int current = 1;
            try { current = (int)GCS.difficulty; } catch { }
            var accent = UI.Theme.Accent;
            string[] names = { "LENIENT", "NORMAL", "STRICT" };
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var rowGo = new GameObject("Row" + i, typeof(RectTransform));
                rowGo.transform.SetParent(panelGo.transform, false);
                var rr = (RectTransform)rowGo.transform;
                rr.anchorMin = new Vector2(0f, 1f);
                rr.anchorMax = new Vector2(1f, 1f);
                rr.pivot = new Vector2(0.5f, 1f);
                rr.offsetMin = new Vector2(pad, 0f);
                rr.offsetMax = new Vector2(-pad, 0f);
                rr.anchoredPosition = new Vector2(0f, -pad - (2 - i) * rowH); // STRICT on top
                rr.sizeDelta = new Vector2(rr.sizeDelta.x, rowH);
                var rowBg = rowGo.AddComponent<RoundedRectGraphic>();
                rowBg.Radius = 6f;
                rowBg.color = i == current
                    ? new Color(accent.r, accent.g, accent.b, 0.35f)
                    : new Color(1f, 1f, 1f, 0f);
                rowBg.raycastTarget = true;
                var rowBtn = rowGo.AddComponent<Button>();
                rowBtn.targetGraphic = rowBg;
                var rc = rowBtn.colors;
                rc.normalColor = Color.white;
                rc.highlightedColor = new Color(1.5f, 1.5f, 1.5f, 1f);
                rc.pressedColor = new Color(2f, 2f, 2f, 1f);
                rowBtn.colors = rc;
                rowBtn.onClick.AddListener(() =>
                {
                    try { GCS.difficulty = (Difficulty)idx; } catch { }
                    CloseDiffMenu();
                });
                rowBtn.onClick.AddListener(Deselect);
                var t = MakeGlyph(rowGo.transform, names[i], 11);
                t.color = i == current ? Color.white : new Color(0.78f, 0.78f, 0.81f, 1f);
            }
        }

        private static void CloseDiffMenu()
        {
            if (_diffMenuGo != null) Object.Destroy(_diffMenuGo);
            _diffMenuGo = null;
        }

        private static RoundedRectGraphic MakeModeChip(float x, float w, string label,
            out TextMeshProUGUI text, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Chip_" + label, typeof(RectTransform));
            go.transform.SetParent(_modeCluster, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0f);
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
            r.sizeDelta = new Vector2(w, 0f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 7f;
            bg.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            colors.pressedColor = new Color(1.8f, 1.8f, 1.8f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);
            btn.onClick.AddListener(Deselect);
            text = MakeGlyph(go.transform, label, 11);
            text.color = new Color(0.75f, 0.75f, 0.78f, 1f);
            return bg;
        }

        private static void UpdateModeCluster()
        {
            if (_modeCluster == null) return;
            if (_diffMenuGo != null && !_modeCluster.gameObject.activeInHierarchy) CloseDiffMenu();
            // Track the strip's top-right corner (lane count changes its height).
            float y = _stripRect.anchoredPosition.y + _stripRect.sizeDelta.y + 8f;
            if (!Mathf.Approximately(_modeCluster.anchoredPosition.y, y))
                _modeCluster.anchoredPosition = new Vector2(-8f, y);

            var accent = UI.Theme.Accent;
            var s = MainClass.Settings;
            bool em = false;
            try { em = s != null && s.EditorModeEnabled; } catch { }
            if (em != _emShown && _emBg != null)
            {
                _emShown = em;
                _emBg.color = em ? new Color(accent.r, accent.g, accent.b, 0.45f)
                                 : new Color(0.08f, 0.08f, 0.1f, 0.85f);
                if (_emLabel != null) _emLabel.color = em ? Color.white : new Color(0.75f, 0.75f, 0.78f, 1f);
            }

            int diff = -1;
            try { diff = (int)GCS.difficulty; } catch { }
            if (diff != _diffShown && _diffLabel != null)
            {
                _diffShown = diff;
                _diffLabel.text = diff == 0 ? "LENIENT" : diff == 2 ? "STRICT" : "NORMAL";
            }

            bool nf = false;
            // The editor's N shortcut flips scrController.noFail directly (NOT GCS.useNoFail),
            // so read the live controller when it exists.
            try
            {
                nf = GCS.useNoFail;
                var ctl = scrController.instance;
                if (ctl != null) nf = ctl.noFail;
            }
            catch { }
            if (nf != _nfShown && _nfBg != null)
            {
                _nfShown = nf;
                _nfBg.color = nf ? new Color(accent.r, accent.g, accent.b, 0.45f)
                                 : new Color(0.08f, 0.08f, 0.1f, 0.85f);
                if (_nfLabel != null) _nfLabel.color = nf ? Color.white : new Color(0.75f, 0.75f, 0.78f, 1f);
            }

            bool auto = false;
            try { auto = RDC.auto; } catch { }
            if (auto != _autoShown && _autoBg != null)
            {
                _autoShown = auto;
                _autoBg.color = auto ? new Color(accent.r, accent.g, accent.b, 0.45f)
                                     : new Color(0.08f, 0.08f, 0.1f, 0.85f);
                if (_autoLabel != null) _autoLabel.color = auto ? Color.white : new Color(0.75f, 0.75f, 0.78f, 1f);
            }
        }

        private static void MakeTransportButton(Transform parent, string name, float x, float size,
            System.Action<GameObject> buildFace, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); // top-anchored, aligned with the clock line
            r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, -3f);
            r.sizeDelta = new Vector2(size, size);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(1f, 1f, 1f, 0.1f);
            bg.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.7f, 1.7f, 1.7f, 1f);
            colors.pressedColor = new Color(2.4f, 2.4f, 2.4f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);
            btn.onClick.AddListener(Deselect);
            buildFace(go);
        }

        // uGUI keeps the clicked button SELECTED, and Space/Enter re-submits the selection
        // — with Space also being the autoplay-pause key, every chip re-toggled itself.
        private static void Deselect()
        {
            try
            {
                var es = UnityEngine.EventSystems.EventSystem.current;
                if (es != null) es.SetSelectedGameObject(null);
            }
            catch { }
        }

        private static TextMeshProUGUI MakeGlyph(Transform parent, string glyph, int size)
        {
            var go = new GameObject("Glyph", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.font = UI.Theme.TmpFont;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.text = glyph;
            return t;
        }

        private static void ApplyTransportLayout(bool on)
        {
            _transportOn = on;
            if (_transportGo != null) _transportGo.SetActive(on);
            float tw = on ? TransportW : 0f;
            _laneLabelHost.offsetMin = new Vector2(tw + 4f, StripPad);
            _laneLabelHost.offsetMax = new Vector2(tw + HeaderW - 6f, -StripPad);
            _markerArea.offsetMin = new Vector2(tw + HeaderW, StripPad);
        }

        private static void UpdateTransport(scnEditor ed, bool playing, int selSeq)
        {
            // Show pause bars only while the run actually advances; paused autoplay shows
            // ▶ again (click = resume).
            bool showPause = playing;
            if (playing)
            {
                try { var c = scrController.instance; if (c != null && c.paused) showPause = false; }
                catch { }
            }
            if (_playGlyph != null && _playGlyph.gameObject.activeSelf == showPause)
            {
                _playGlyph.gameObject.SetActive(!showPause);
                if (_pauseBars != null) _pauseBars.SetActive(showPause);
            }

            if (_clockText == null) return;
            string txt, bpm = "";
            if (playing)
            {
                double elapsed = 0.0;
                float pitch = 1f;
                try
                {
                    var cond = scrConductor.instance;
                    if (cond != null)
                    {
                        elapsed = cond.songposition_minusi;
                        if (cond.song != null && cond.song.pitch > 0f) pitch = cond.song.pitch;
                    }
                }
                catch { }
                // The conductor keeps advancing after the last tile — freeze the readout
                // at the level's end instead of counting into the outro.
                if (_totalTime > 0.0 && elapsed > _totalTime) elapsed = _totalTime;
                double beat = BeatAtTime(elapsed) - _beatOffset;
                txt = FormatClock(elapsed / pitch) + " / " + FormatClock(_totalTime / pitch)
                    + "  ·  beat " + (beat < 0 ? 0 : (int)beat);
                bpm = BpmLine(selSeq);
            }
            else if (selSeq >= 0 && _beatPrefix != null && selSeq < _beatPrefix.Length)
            {
                double beat = _beatPrefix[selSeq] - _beatOffset;
                txt = "tile " + selSeq + "  ·  beat " + beat.ToString("0.#");
                bpm = BpmLine(selSeq);
            }
            else txt = "";
            if (_clockText.text != txt) _clockText.text = txt;
            if (_bpmText != null && _bpmText.text != bpm) _bpmText.text = bpm;
        }

        // While our transport is up, fade the game's bottom-left play cluster (parent of
        // its play button); restored when the toggle goes off or we leave the editor.
        private static void FadePlayCluster(scnEditor ed, bool fade)
        {
            try
            {
                if (!fade)
                {
                    if (_fadedPlayCluster != null)
                    {
                        _fadedPlayCluster.alpha = 1f;
                        _fadedPlayCluster.blocksRaycasts = true;
                        _fadedPlayCluster.interactable = true;
                        _fadedPlayCluster = null;
                    }
                    return;
                }
                var host = ed != null && ed.playPause != null ? ed.playPause.transform.parent : null;
                if (host == null) return;
                var cg = host.GetComponent<CanvasGroup>();
                if (cg == null) cg = host.gameObject.AddComponent<CanvasGroup>();
                // interactable stays true so proxied clicks through the group keep working.
                if (cg.alpha != 0f) { cg.alpha = 0f; cg.blocksRaycasts = false; }
                _fadedPlayCluster = cg;
            }
            catch { }
        }

        private static string FormatClock(double seconds)
        {
            if (seconds < 0.0) seconds = 0.0;
            int s = (int)seconds;
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        // Mode dropdown: NORMAL (category view) / CAM / DECO / FILTER — the CDF surfaces.
        private static void BuildCamButton(Transform parent)
        {
            var go = new GameObject("CamMode", typeof(RectTransform)); // name kept for help mapping
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(1f, 0.5f);
            r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.sizeDelta = new Vector2(ZoomW - 4f, 22f);
            r.anchoredPosition = new Vector2(-ZoomW + 2f, -26f);
            _camBtnBg = go.AddComponent<RoundedRectGraphic>();
            _camBtnBg.Radius = 5f;
            _camBtnBg.color = new Color(1f, 1f, 1f, 0.12f);
            _camBtnBg.raycastTarget = true;
            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            _modeBtnLabel = txtGo.AddComponent<TextMeshProUGUI>();
            _modeBtnLabel.font = UI.Theme.TmpFont;
            _modeBtnLabel.fontSize = 11;
            _modeBtnLabel.color = Color.white;
            _modeBtnLabel.alignment = TextAlignmentOptions.Center;
            _modeBtnLabel.raycastTarget = false;
            _modeBtnLabel.text = "NORMAL";
            UI.ClickHandler.Attach(go, ToggleModeMenu);
            SyncCamButton();
        }

        private static void ToggleModeMenu()
        {
            if (_modeMenuGo != null) { Object.Destroy(_modeMenuGo); _modeMenuGo = null; return; }
            _modeMenuGo = new GameObject("ModeMenu", typeof(RectTransform));
            _modeMenuGo.transform.SetParent(_stripRect, false);
            var menuCanvas = _modeMenuGo.AddComponent<Canvas>();
            menuCanvas.overrideSorting = true;
            menuCanvas.sortingOrder = 941; // above the strip, cluster and dock
            _modeMenuGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
            var r = (RectTransform)_modeMenuGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(1f, 0f);
            r.anchoredPosition = new Vector2(-6f, -14f);
            r.sizeDelta = new Vector2(96f, 4 * 26f + 8f);
            var bg = _modeMenuGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 8f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.98f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;
            for (int i = 0; i < 4; i++)
            {
                int mode = i;
                var rowGo = new GameObject("M" + i, typeof(RectTransform));
                rowGo.transform.SetParent(_modeMenuGo.transform, false);
                var rr = (RectTransform)rowGo.transform;
                rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f);
                rr.pivot = new Vector2(0.5f, 1f);
                rr.offsetMin = new Vector2(4f, 0f); rr.offsetMax = new Vector2(-4f, 0f);
                rr.anchoredPosition = new Vector2(0f, -4f - i * 26f);
                rr.sizeDelta = new Vector2(rr.sizeDelta.x, 24f);
                var rbg = rowGo.AddComponent<RoundedRectGraphic>();
                rbg.Radius = 5f;
                var a = UI.Theme.Accent;
                rbg.color = TlMode == mode ? new Color(a.r, a.g, a.b, 0.45f) : new Color(1f, 1f, 1f, 0.03f);
                rbg.raycastTarget = true;
                var lGo = new GameObject("L", typeof(RectTransform));
                lGo.transform.SetParent(rowGo.transform, false);
                var lr = (RectTransform)lGo.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = lr.offsetMax = Vector2.zero;
                var lt = lGo.AddComponent<TextMeshProUGUI>();
                lt.font = UI.Theme.TmpFont;
                lt.fontSize = 12;
                lt.color = Color.white;
                lt.alignment = TextAlignmentOptions.Center;
                lt.raycastTarget = false;
                lt.text = TlModeNames[i];
                UI.ClickHandler.Attach(rowGo, () =>
                {
                    TlMode = mode;
                    if (!CamMode) { _camSel = null; _camSelDirty = true; _expandedLane = -1; }
                    Object.Destroy(_modeMenuGo); _modeMenuGo = null;
                    SyncCamButton();
                    _tlSig = 0; _scanCooldown = 0; _viewDirty = true;
                });
            }
        }

        private static void SyncCamButton()
        {
            if (_camBtnBg == null) return;
            var a = UI.Theme.Accent;
            _camBtnBg.color = TlMode > 0 ? new Color(a.r, a.g, a.b, 0.5f) : new Color(1f, 1f, 1f, 0.12f);
            if (_modeBtnLabel != null) _modeBtnLabel.text = TlModeNames[Mathf.Clamp(TlMode, 0, 3)];
            if (_graphBtnGo != null && _graphBtnGo.activeSelf != CamMode) _graphBtnGo.SetActive(CamMode);
        }

        // No selection needed: opens on the timeline's current view (zoom tab by default).
        private static void BuildGraphButton(Transform parent)
        {
            var go = new GameObject("GraphBtn", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            _graphBtnGo = go;
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(1f, 0.5f);
            r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.sizeDelta = new Vector2(ZoomW - 4f, 22f);
            r.anchoredPosition = new Vector2(-ZoomW + 2f, -52f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;
            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            var t = txtGo.AddComponent<TextMeshProUGUI>();
            t.font = UI.Theme.TmpFont;
            t.fontSize = 11;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.text = "GRAPH";
            UI.ClickHandler.Attach(go, () => EditorGraph.Open(_camSel != null ? _camSelLane : 2));
            go.SetActive(false);
        }

        private static void MakeZoomButton(Transform parent, string label, float x, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Zoom" + label, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(1f, 0.5f);
            r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.sizeDelta = new Vector2(24f, 24f);
            r.anchoredPosition = new Vector2(x, 0f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;
            var btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1.6f, 1.6f, 1.6f, 1f);
            colors.pressedColor = new Color(2.2f, 2.2f, 2.2f, 1f);
            btn.colors = colors;
            btn.onClick.AddListener(onClick);
            btn.onClick.AddListener(Deselect);

            var txtGo = new GameObject("Label", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = tr.offsetMax = Vector2.zero;
            var t = txtGo.AddComponent<TextMeshProUGUI>();
            t.font = UI.Theme.TmpFont;
            t.fontSize = 17;
            t.color = Color.white;
            t.alignment = TextAlignmentOptions.Center;
            t.raycastTarget = false;
            t.text = label;
        }
    }
}
