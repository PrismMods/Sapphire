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
        private const float LaneH = 20f;     // canvas units per lane
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
        private static RoundedRectGraphic _autoBg;
        private static TextMeshProUGUI _autoLabel;
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
        private static int _lastSelSeq = -1;
        private const double DefaultViewBeats = 64.0;
        private const double MinViewBeats = 4.0; // zoom-in limit

        private static double[] _timePrefix; // song-time (s) at floor i's start, from the game
        private static double _totalTime = 1.0;
        private static double _timeOffset;   // time of the first INPUT tile — the musical beat 0
        private static double _secPerBeat = 0.5; // 60 / base BPM, for the measure grid

        // ── tooltip ──
        private static GameObject _tooltipGo;
        private static RectTransform _tooltipRect;
        private static TextMeshProUGUI _tooltipText;
        private const float TooltipMaxW = 440f;

        private static FieldInfo _dataField; // LevelEvent.data is protected

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
                case LevelEventCategory.Jank:         return "Jank";
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
        internal static float BottomStripTop =>
            _stripRect != null && _stripRect.gameObject.activeInHierarchy
                ? _stripRect.anchoredPosition.y + _stripRect.sizeDelta.y : 0f;

        // Read by the scnEditor.ZoomCamera prefix: the editor zooms on any wheel input, so
        // it must stand down while the wheel is panning the strip.
        internal static bool TimelineHovered
        {
            get
            {
                try
                {
                    return _stripRect != null && _stripRect.gameObject.activeInHierarchy
                        && RectTransformUtility.RectangleContainsScreenPoint(_stripRect, Input.mousePosition, null);
                }
                catch { return false; }
            }
        }

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
                if (s != null && (s.EditorShowEvents || s.EditorTimeline))
                {
                    ed = scnEditor.instance;
                    bool editing = ed != null && !ed.playMode && !EditorPanelOpen(ed);
                    bool playing = ed != null && ed.playMode;
                    wantChips = editing && s.EditorShowEvents
                        && ed.selectedFloors != null && ed.selectedFloors.Count > 0;
                    // The timeline stays up during play-testing (the playhead follows the run).
                    wantTl = (editing || playing) && s.EditorTimeline;
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
            if (_markerTex != null) Object.Destroy(_markerTex);
            _canvasGo = null; _canvasRect = null; _canvas = null; _chipsRow = null;
            _stripRect = null; _markerArea = null; _markerImage = null; _markerTex = null;
            _playhead = null; _laneLabelHost = null;
            _tooltipGo = null; _tooltipRect = null; _tooltipText = null;
            _chipRects.Clear(); _chipEvents.Clear(); _lanes.Clear(); _laneEvents = null;
            _timePrefix = null; _chipFloor = -2; _tlSig = int.MinValue;
            _zoom = 1f; _viewStart = 0f; _lastSelSeq = -1; _userZoomed = false; _draggingHead = false;
            _cursorForced = false;
            FadePlayCluster(null, false);
            _transportGo = null; _playGlyph = null; _pauseBars = null; _clockText = null;
            _autoBg = null; _autoLabel = null; _autoShown = false;
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

            // Hover: nearest marker in the lane under the mouse (not while scrubbing).
            if (tip != null || _laneEvents == null || _draggingHead) return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(_markerArea, mouse, null)) return;
            Vector2 local;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_markerArea, mouse, null, out local)) return;
            var rect = _markerArea.rect;
            float frac = _viewStart + Mathf.Clamp01((local.x - rect.xMin) / Mathf.Max(1f, rect.width)) / _zoom;
            int lane = Mathf.FloorToInt((rect.yMax - local.y) / LaneH);
            LevelEvent hit = null;
            if (lane >= 0 && lane < _laneEvents.Length)
                hit = NearestInLane(_laneEvents[lane], frac, 6f / (Mathf.Max(1f, rect.width) * _zoom));
            if (hit != null)
            {
                tip = BuildTooltip(hit, true);
                tipAt = new Vector2(mouse.x, mouse.y + 14f); // strip is at the bottom → open upward
            }
            // Click a marker to select its tile; click empty strip to move the playhead
            // to that spot (and keep scrubbing while held). Both ride the camera jump.
            if (!playing && Input.GetMouseButtonDown(0))
            {
                int target = hit != null ? hit.floor : FloorAtFrac(frac);
                if (target >= 0 && target < floors.Count)
                    try { ed.SelectFloor(floors[target], true); } catch { }
                if (hit == null) _draggingHead = true;
            }
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
            for (int i = 0; i < len; i++)
            {
                var fl = floors[i];
                _timePrefix[i] = fl != null ? fl.entryTime : (i > 0 ? _timePrefix[i - 1] : 0.0);
            }
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

            // Default view spans 64 beats (zoom 1 = whole level); a manual zoom sticks
            // until the strip is next hidden.
            if (!_userZoomed)
                _zoom = Mathf.Max(1f, (float)(_totalTime / (DefaultViewBeats * _secPerBeat)));
            _viewStart = Mathf.Clamp(_viewStart, 0f, 1f - 1f / _zoom);

            _lanes.Clear();
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
            int laneCount = Mathf.Max(1, _lanes.Count);

            _stripRect.sizeDelta = new Vector2(0f, StripPad * 2f + laneCount * LaneH);

            int texH = laneCount * TexLaneH;
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
                r.sizeDelta = new Vector2(0f, LaneH);
                r.anchoredPosition = new Vector2(0f, -i * LaneH);
                var t = go.AddComponent<TextMeshProUGUI>();
                t.font = UI.Theme.TmpFont;
                t.fontSize = 13;
                t.color = CategoryColor(_lanes[i]);
                t.alignment = TextAlignmentOptions.Right;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Overflow;
                t.raycastTarget = false;
                t.text = CategoryLabel(_lanes[i]);
            }
        }

        // Paint the events inside the current view window (3×9 blocks, category-colored,
        // dim when off). Called on structure change, zoom, pan and selection auto-scroll.
        private static void RenderMarkers()
        {
            if (_markerTex == null || _laneEvents == null) return;
            int laneCount = _laneEvents.Length;
            int texH = laneCount * TexLaneH;
            var px = new Color32[TexW * texH];
            float viewEnd = _viewStart + 1f / _zoom;

            // Measure grid: assume 4/4 at the base BPM (the level format has no time-
            // signature data), with an every-4th-measure brighter line. Spacing doubles as
            // needed so zoomed-out views don't smear into solid grey. Track pauses don't
            // pause the song, so the grid marches on through them — that's what keeps
            // post-pause events on-beat.
            double interval = 4.0 * _secPerBeat;
            double viewSpan = _totalTime / _zoom;
            while (viewSpan / interval > TexW / 26.0) interval *= 2.0;
            var faint = new Color32(255, 255, 255, 22);
            var strong = new Color32(255, 255, 255, 44);
            long k = (long)System.Math.Ceiling((_viewStart * _totalTime - _timeOffset) / interval);
            if (k < 0) k = 0;
            for (int guard = 0; guard < 2048; guard++, k++)
            {
                double frac = (_timeOffset + k * interval) / _totalTime;
                if (frac > viewEnd) break;
                int gx = Mathf.Clamp((int)((frac - _viewStart) * _zoom * (TexW - 1)), 0, TexW - 1);
                var gcol = (k & 3) == 0 ? strong : faint;
                for (int y = 0; y < texH; y++) px[y * TexW + gx] = gcol;
            }

            for (int lane = 0; lane < laneCount; lane++)
            {
                var cat = lane < _lanes.Count ? _lanes[lane] : (LevelEventCategory)(-1);
                var c = CategoryColor(cat);
                var list = _laneEvents[lane];
                int y0 = (laneCount - 1 - lane) * TexLaneH + 1; // texture row 0 = bottom
                for (int i = 0; i < list.Count; i++)
                {
                    float frac = list[i].Key;
                    if (frac < _viewStart || frac > viewEnd) continue;
                    var e = list[i].Value;
                    var col = e.active
                        ? new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 255)
                        : new Color32((byte)(c.r * 255), (byte)(c.g * 255), (byte)(c.b * 255), 90);
                    int x0 = Mathf.Clamp((int)((frac - _viewStart) * _zoom * (TexW - 4)), 0, TexW - 4);
                    for (int y = y0; y < y0 + TexLaneH - 3; y++)
                    {
                        int row = y * TexW;
                        px[row + x0] = col;
                        px[row + x0 + 1] = col;
                        px[row + x0 + 2] = col;
                        px[row + x0 + 3] = col;
                    }
                }
            }
            _markerTex.SetPixels32(px);
            _markerTex.Apply(false);
        }

        // ── tooltip ─────────────────────────────────────────────────────────

        private static string BuildTooltip(LevelEvent e, bool withFloor)
        {
            if (e == null) return null;
            var sb = new StringBuilder(256);
            sb.Append("<b>").Append(EventName(e)).Append("</b>");
            if (withFloor)
            {
                sb.Append("  <color=#94949E>tile ").Append(e.floor);
                if (_timePrefix != null && e.floor >= 0 && e.floor < _timePrefix.Length && _secPerBeat > 0.0)
                    sb.Append(" · beat ").Append(
                        ((_timePrefix[e.floor] - _timeOffset) / _secPerBeat).ToString("0.#"));
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
            _stripRect.sizeDelta = new Vector2(0f, StripPad * 2f + LaneH);
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

            // Shared hover tooltip, above everything on this canvas.
            var tipGo = new GameObject("Tooltip", typeof(RectTransform));
            tipGo.transform.SetParent(_canvasGo.transform, false);
            _tooltipGo = tipGo;
            _tooltipRect = (RectTransform)tipGo.transform;
            _tooltipRect.anchorMin = _tooltipRect.anchorMax = new Vector2(0.5f, 0.5f);
            _tooltipRect.pivot = new Vector2(0.5f, 1f);
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

            // Autoplay toggle — charters flip this constantly, so it lives on the HUD.
            var autoGo = new GameObject("Auto", typeof(RectTransform));
            autoGo.transform.SetParent(r, false);
            var ar = (RectTransform)autoGo.transform;
            ar.anchorMin = new Vector2(0f, 0.5f);
            ar.anchorMax = new Vector2(0f, 0.5f);
            ar.pivot = new Vector2(0f, 0.5f);
            ar.anchoredPosition = new Vector2(76f, 0f);
            ar.sizeDelta = new Vector2(46f, 24f);
            _autoBg = autoGo.AddComponent<RoundedRectGraphic>();
            _autoBg.Radius = 6f;
            _autoBg.color = new Color(1f, 1f, 1f, 0.1f);
            _autoBg.raycastTarget = true;
            var autoBtn = autoGo.AddComponent<Button>();
            autoBtn.targetGraphic = _autoBg;
            var acolors = autoBtn.colors;
            acolors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1f);
            acolors.pressedColor = new Color(1.8f, 1.8f, 1.8f, 1f);
            autoBtn.colors = acolors;
            autoBtn.onClick.AddListener(() => { try { RDC.auto = !RDC.auto; } catch { } });
            _autoLabel = MakeGlyph(autoGo.transform, "AUTO", 11);
            _autoLabel.color = new Color(0.75f, 0.75f, 0.78f, 1f);
            _autoShown = false;

            var clockGo = new GameObject("Clock", typeof(RectTransform));
            clockGo.transform.SetParent(r, false);
            var cr = (RectTransform)clockGo.transform;
            cr.anchorMin = new Vector2(0f, 0f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.offsetMin = new Vector2(130f, 0f);
            cr.offsetMax = new Vector2(-4f, 0f);
            _clockText = clockGo.AddComponent<TextMeshProUGUI>();
            _clockText.font = UI.Theme.TmpFont;
            _clockText.fontSize = 13;
            _clockText.color = new Color(0.85f, 0.85f, 0.88f, 1f);
            _clockText.alignment = TextAlignmentOptions.Left;
            _clockText.textWrappingMode = TextWrappingModes.NoWrap;
            _clockText.overflowMode = TextOverflowModes.Overflow;
            _clockText.raycastTarget = false;
            _transportGo.SetActive(false);
        }

        // Quick-access state chips the charter flips constantly, parked above the strip's
        // right end. Editor Mode toggles the Sapphire setting; difficulty cycles
        // Lenient→Normal→Strict (GCS.difficulty); no-fail flips GCS.useNoFail (and the
        // live controller flag mid-run).
        private static void BuildModeCluster()
        {
            var go = new GameObject("ModeCluster", typeof(RectTransform));
            go.transform.SetParent(_canvasGo.transform, false);
            _modeCluster = (RectTransform)go.transform;
            _modeCluster.anchorMin = _modeCluster.anchorMax = new Vector2(1f, 0f);
            _modeCluster.pivot = new Vector2(1f, 0f);
            _modeCluster.sizeDelta = new Vector2(64f + 78f + 64f + 12f, 24f);

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
            MakeModeChip(70f, 78f, "NORMAL", out _diffLabel, () =>
            {
                try { GCS.difficulty = (Difficulty)(((int)GCS.difficulty + 1) % 3); }
                catch { }
            });
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
            _modeCluster.gameObject.SetActive(false);
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
            try { nf = GCS.useNoFail; } catch { }
            if (nf != _nfShown && _nfBg != null)
            {
                _nfShown = nf;
                _nfBg.color = nf ? new Color(accent.r, accent.g, accent.b, 0.45f)
                                 : new Color(0.08f, 0.08f, 0.1f, 0.85f);
                if (_nfLabel != null) _nfLabel.color = nf ? Color.white : new Color(0.75f, 0.75f, 0.78f, 1f);
            }
        }

        private static void MakeTransportButton(Transform parent, string name, float x, float size,
            System.Action<GameObject> buildFace, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 0.5f);
            r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(x, 0f);
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

            bool auto = false;
            try { auto = RDC.auto; } catch { }
            if (auto != _autoShown && _autoBg != null)
            {
                _autoShown = auto;
                var accent = UI.Theme.Accent;
                _autoBg.color = auto
                    ? new Color(accent.r, accent.g, accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, 0.1f);
                if (_autoLabel != null)
                    _autoLabel.color = auto ? Color.white : new Color(0.75f, 0.75f, 0.78f, 1f);
            }
            if (_clockText == null) return;
            string txt;
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
                double beat = _secPerBeat > 0.0 ? (elapsed - _timeOffset) / _secPerBeat : 0.0;
                txt = FormatClock(elapsed / pitch) + " / " + FormatClock(_totalTime / pitch)
                    + "  ·  beat " + (beat < 0 ? 0 : (int)beat);
            }
            else if (selSeq >= 0 && _timePrefix != null && selSeq < _timePrefix.Length && _secPerBeat > 0.0)
            {
                double beat = (_timePrefix[selSeq] - _timeOffset) / _secPerBeat;
                txt = "tile " + selSeq + "  ·  beat " + beat.ToString("0.#");
            }
            else txt = "";
            if (_clockText.text != txt) _clockText.text = txt;
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
