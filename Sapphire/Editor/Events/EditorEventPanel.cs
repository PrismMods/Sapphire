using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Sapphire-native EVENT INSPECTOR — replaces the game's event settings panel visuals.
       The game panel stays alive INVISIBLY as the model (its state, undo plumbing, and
       ShowPanel refreshes keep working, so every module that syncs through it still does);
       this window renders the view from the game's own PropertyInfo registry
       (GCS.levelEventsInfo): localized labels, showIf gating, per-property disable
       toggles, and control types matched to the value. Floating + resizable.

       Scope guard: shown only for a SINGLE selected floor. Decorations are NOT this
       panel's job — EditorDecoInspector owns them — but the game panel stays hidden for
       them too, so a selected decoration can't resurrect the vanilla inspector on top. */
    internal static class EditorEventPanel
    {
        private static readonly PanelKit K = new PanelKit("SapphireEventPanel", 902, PanelW, focusable: true);
        private const float PanelW = 370f;
        private const float Pad = PanelKit.Pad, RowH = PanelKit.RowH, Gap = PanelKit.Gap;
        private const float HeaderH = 28f;

        private static Vector2 _size = new Vector2(PanelW, 640f);
        private static RectTransform _viewport, _content;
        private static float _scroll;
        private static int _floor = -1;
        private static readonly HashSet<int> _expandedTypes = new HashSet<int>();   // tree: type nodes
        private static readonly HashSet<long> _expandedInst = new HashSet<long>();   // type*1000+instance
        // Switching tiles fades the rebuilt list in.
        private static float _listAnim = 1f;
        private static bool _animList;
        /* Expand/collapse motion. Toggling rebuilds the whole tree, so the rows that move are NEW
           rows: the toggled header's y is recorded while building, every row below it starts
           where its predecessor sat (offset by the height change) and slides home, and an
           expand's body is uncovered by that slide rather than fading in under rows still
           passing over it. */
        private static long _motionKey = long.MinValue;   // node toggled; built rows are measured against it
        private static float _motionOldH, _motionHeadY, _motionT = 1f, _motionDelta, _motionCurtain;
        private static bool _motionHeadSeen;
        private static int _motionFirstChild;             // rows before this index are the destroyed old tree
        private static readonly List<RectTransform> _mRows = new List<RectTransform>();
        private static readonly List<Vector2> _mBase = new List<Vector2>();
        private static readonly List<float> _mBottom = new List<float>();   // NaN = slides; else body row, fades
        /* Keyboard rows: every visible header in display order (a group header selects its whole
           group, like a click), rebuilt with the content. The cursor is held by row KEY, not
           index, so it survives an expand shifting everything below it. */
        private static readonly List<List<ADOFAI.LevelEvent>> _rows = new List<List<ADOFAI.LevelEvent>>();
        private static readonly List<long> _rowKeys = new List<long>();
        private static readonly List<float> _rowY = new List<float>();
        private static long _cursorKey = long.MinValue, _rowAnchorKey = long.MinValue;
        private static bool _revealCursor;
        private static int _shownFrame = -10;
        private static long _sig;
        private const long EmptySig = -1L;     // _sig marker: scanned, tile has no events
        private static bool _empty;            // last scan found nothing on this floor
        private static bool _dirty;            // a scan/rebuild is pending
        private static int _scanCd;            // frames until the next external-change rescan
        private static CanvasGroup _gameCg;    // the hidden game panel
        /* Free selection across the tree: ctrl-click toggles a row, shift-click takes the range
           since the last anchor, in the flattened display order (_flat). A plain click selects
           one row; the +/› glyph expands. Selection drives the batch
           bar (copy / delete) and survives content rebuilds because it holds the LevelEvent
           references themselves, not row indices. */
        private static readonly HashSet<ADOFAI.LevelEvent> _sel = new HashSet<ADOFAI.LevelEvent>();
        private static readonly List<ADOFAI.LevelEvent> _flat = new List<ADOFAI.LevelEvent>();
        private static ADOFAI.LevelEvent _anchor;
        // Last row the user touched — mirrored onto the hidden game panel so ITS hotkeys act on
        // the same event (see SyncGameSelection).
        private static ADOFAI.LevelEvent _gameSel;
        private static bool _userHidden;       // × collapsed the panel to the chip (tile stays selected)
        private static GameObject _chipGo;     // reopen chip while collapsed
        private static TextMeshProUGUI _chipLabel;

        internal static bool PointerOver() =>
            K.Visible && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)K.PanelGo.transform, Input.mousePosition, null);

        internal static int CurrentFloor => _floor;

        /* What a drag from `row` carries: the whole selection when the row is part of it (every
           type in it, display order), else just the row — the way a file manager drags. */
        internal static List<ADOFAI.LevelEvent> DragSet(List<ADOFAI.LevelEvent> row)
        {
            bool inSel = false;
            foreach (var e in row) if (_sel.Contains(e)) { inSel = true; break; }
            if (!inSel || _sel.Count <= 1) return row;
            var all = new List<ADOFAI.LevelEvent>();
            foreach (var e in _flat) if (_sel.Contains(e)) all.Add(e);
            return all;
        }

        // Values changed under the panel (a variable edit): redraw so formula fields show them.
        internal static void Refresh() => _sig = 0;

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool active = s != null && MainClass.EditorSuiteOn && s.EditorNativeInspector
                       && ed != null && !ed.playMode;

            int floor = -1;
            if (active)
            {
                try
                {
                    if (ed.selectedFloors != null && ed.selectedFloors.Count == 1 && ed.selectedFloors[0] != null)
                        floor = ed.selectedFloors[0].seqID;
                }
                catch { }
            }

            // Sapphire owns the decoration inspector now (EditorDecoInspector) — a selected
            // decoration must not resurrect the game's panel behind it (SelectDecoration sets
            // decorationSelected, which un-hides it at alpha 1 with raycasts on).
            SyncGamePanelHidden(ed, active);

            // Collapsible like the event selector: × collapses to a reopen chip (keeping the tile
            // SELECTED — better chart visibility, esp. in quick-chart), the chip reopens it.
            bool baseWant = active && floor >= 0;
            ShowChip(baseWant && _userHidden, floor);
            if (!baseWant || _userHidden)
            {
                K.Show(false);
                // Don't close the SHARED dropdown here: it may belong to the level-settings panel,
                // not us. On a File>New level no floor is selected (NewLevel DeselectFloors), so this
                // path runs every frame and was killing the level-menu dropdown the instant it opened.
                // Our own dropdowns still close via EditorDropdown.Tick's backstop (hidden content →
                // trigger !activeInHierarchy), which runs later in the same frame.
                if (!baseWant) { _floor = -1; _sig = 0; _empty = false; } // real close resets; collapse keeps state
                return;
            }

            /* Per-frame cost control: FloorEvents (allocs a list, scans ALL level events) and
               Sig are only run when something might have changed — a floor change (cheap to
               detect above), one of OUR edits (which force _sig = 0), or a periodic rescan
               to catch external edits (undo). On a static selection the tick is nearly free;
               scanning every frame is what dropped a big level from 120 to ~80fps. */
            bool floorChanged = floor != _floor;
            if (floorChanged)
            {
                _floor = floor; SeedExpansion(); _scroll = 0f; _dirty = true; _animList = true;
                _sel.Clear(); _anchor = null; _gameSel = null;   // selection is per-tile
            }
            if (_sig == 0) _dirty = true;                       // an edit / tab click asked to redraw
            if (--_scanCd <= 0) { _scanCd = 12; _dirty = true; } // ~1/12-frame external-change catch

            if (_dirty)
            {
                _dirty = false;
                var events = FloorEvents(ed, floor);
                /* Empty tile: latch it instead of clobbering _floor/_sig. Resetting those
                   made floorChanged (and the _sig == 0 redraw request) true again on the
                   very next frame, so FloorEvents — a list alloc plus a scan of EVERY event
                   in the level — re-ran every frame for as long as an eventless tile stayed
                   selected, which is most of the time while building. EmptySig is only a
                   "scanned, nothing here" marker; floor changes, our own edits (_sig = 0)
                   and the 12-frame external-change rescan all still force a re-scan. */
                if (events.Count == 0) { K.Show(false); _empty = true; _sig = EmptySig; return; }
                // A new tile, or the first event landing on an empty one.
                if (floorChanged || _empty) SeedSelection(events);
                _empty = false;
                // Shell (panel/header/viewport/resize) built once + on floor change; expanding
                // a section rebuilds only the CONTENT tree.
                bool rebuildContent = false;
                if (!K.Built || floorChanged) { BuildShell(ed); rebuildContent = true; }
                long sig = Sig(ed, floor, events);
                if (sig != _sig) { _sig = sig; rebuildContent = true; }
                if (rebuildContent) { BuildContent(ed, events); BeginMotion(); RevealCursor(); }
                if (_animList) { _animList = false; _listAnim = 0f; _motionKey = long.MinValue; }
            }
            // Nothing to show until the first scan, or when the latch says the tile is empty
            // (K may still be built from a previously selected tile).
            else if (!K.Built || _empty) { K.Show(false); return; }

            SyncGameSelection(ed);
            if (_content != null) UI.UiAnim.Step(_content.gameObject, true, ref _listAnim, false);
            TickMotion();
            K.Show(true);
            _shownFrame = Time.frameCount;
            TickArrows();
            ClampIntoView();
            TickScroll();
            TickResize();
        }

        /* The game's event hotkeys — Ctrl+C (copy this event), Ctrl+Shift+C (every event of its
           type), Ctrl+X — don't look at any UI: scnEditor.CopyOfFloor / CutFloor read
           InspectorPanel.selectedEvent + selectedEventType straight off the panel we keep hidden,
           which only ever moves when the GAME shows a panel. So a row picked in this window has
           to be written onto those two fields or the hotkey copies whatever tab the game last
           opened.

           Re-asserted every frame rather than once per click: the game rewrites both fields
           whenever it shows a panel itself (tile reselect, undo, our own AfterCommit refresh),
           and a stale pair silently copies the wrong event. */
        private static void SyncGameSelection(scnEditor ed)
        {
            if (_gameSel == null) return;
            try
            {
                var p = ed.levelEventsPanel;
                if (p == null) return;
                if (!ReferenceEquals(p.selectedEvent, _gameSel)) p.selectedEvent = _gameSel;
                if (p.selectedEventType != _gameSel.eventType) p.selectedEventType = _gameSel.eventType;
            }
            catch { }
        }

        private static bool _dockInited;

        private static float TopMargin() => 56f;

        private static float BottomInset()
        {
            // Measured top of the whole bottom chrome — strip plus the chip rows above it.
            float below = 0f;
            try { below = EditorEvents.BottomChromeTop; } catch { }
            return below > 0f ? below : 12f;
        }

        internal static void Dispose()
        {
            RestoreGamePanel();
            K.Dispose();
            if (_chipGo != null) UnityEngine.Object.Destroy(_chipGo);
            _chipGo = null; _chipLabel = null; _userHidden = false;
            _viewport = null; _content = null; _sig = 0; _floor = -1; _empty = false;
        }

        // ── game panel hide/restore (visuals only — it stays the model) ─────

        private static CanvasGroup _tabsCg;   // the tab strip lives on a SEPARATE root

        private static void SyncGamePanelHidden(scnEditor ed, bool hide)
        {
            try
            {
                var panel = ed != null ? ed.levelEventsPanel : null;
                if (panel == null) { _gameCg = null; _tabsCg = null; return; }
                var go = panel.gameObject;
                if (_gameCg == null || _gameCg.gameObject != go)
                    _gameCg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
                float a = hide ? 0f : 1f;
                bool blk = !hide;
                // blocksRaycasts must track `hide` EVERY frame, not only when alpha changes:
                // the game can re-enable raycasts on its (invisible) panel, and a stuck-true
                // blocker on a hidden-but-onscreen panel silently eats tile clicks.
                if (_gameCg.alpha != a) _gameCg.alpha = a;
                if (_gameCg.blocksRaycasts != blk) _gameCg.blocksRaycasts = blk;
                // titleCanvas/messageCanvas are NESTED Canvas objects, so the parent CanvasGroup's
                // alpha=0 does NOT hide them (a child Canvas breaks alpha inheritance) — they keep
                // rendering (reskinned) and show through once docked panels stop occluding them.
                // Disable the Canvas component outright while hidden.
                SetCanvasEnabled(panel.titleCanvas, !hide);
                SetCanvasEnabled(panel.messageCanvas, !hide);
                // the event-type tab strip is its own root object — our sections replace it
                var tabs = ed.inspectorTabs;
                if (tabs != null)
                {
                    var tGo = tabs.gameObject;
                    if (_tabsCg == null || _tabsCg.gameObject != tGo)
                        _tabsCg = tGo.GetComponent<CanvasGroup>() ?? tGo.AddComponent<CanvasGroup>();
                    if (_tabsCg.alpha != a) _tabsCg.alpha = a;
                    if (_tabsCg.blocksRaycasts != blk) _tabsCg.blocksRaycasts = blk;
                }
            }
            catch { }
        }

        // Nested Canvas child (titleCanvas/messageCanvas): toggle its Canvas.enabled without
        // touching activeSelf, so we don't fight the game's own show/hide of these objects.
        private static void SetCanvasEnabled(GameObject go, bool enabled)
        {
            if (go == null) return;
            try { var c = go.GetComponent<Canvas>(); if (c != null && c.enabled != enabled) c.enabled = enabled; }
            catch { }
        }

        private static void RestoreGamePanel()
        {
            if (_gameCg != null)
            {
                try { _gameCg.alpha = 1f; _gameCg.blocksRaycasts = true; } catch { }
                _gameCg = null;
            }
            if (_tabsCg != null)
            {
                try { _tabsCg.alpha = 1f; _tabsCg.blocksRaycasts = true; } catch { }
                _tabsCg = null;
            }
            try
            {
                var p = scnEditor.instance != null ? scnEditor.instance.levelEventsPanel : null;
                if (p != null) { SetCanvasEnabled(p.titleCanvas, true); SetCanvasEnabled(p.messageCanvas, true); }
            }
            catch { }
        }

        // ── collapse chip (the way back after ×; the tile stays selected) ────
        private static void ShowChip(bool show, int floor)
        {
            K.ChipAlive = show; // keeps the panel's canvas alive for the chip while the panel is hidden
            if (!show)
            {
                if (_chipGo != null && _chipGo.activeSelf) _chipGo.SetActive(false);
                return;
            }
            if (_chipGo == null)
            {
                if (K.CanvasGo == null) return; // canvas exists once the shell was ever built
                _chipGo = new GameObject("Chip", typeof(RectTransform));
                _chipGo.transform.SetParent(K.CanvasGo.transform, false);
                var r = (RectTransform)_chipGo.transform;
                r.anchorMin = r.anchorMax = new Vector2(1f, 1f); // top-RIGHT (this is the right panel)
                r.pivot = new Vector2(1f, 1f);
                r.anchoredPosition = new Vector2(-10f, -64f);    // below the master switch
                r.sizeDelta = new Vector2(104f, 24f);
                var bg = _chipGo.AddComponent<RoundedRectGraphic>();
                bg.Radius = 7f;
                bg.color = new Color(0.10f, 0.10f, 0.12f, 0.94f);
                bg.BorderWidth = 1f;
                bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
                bg.raycastTarget = true;
                var lGo = new GameObject("L", typeof(RectTransform));
                lGo.transform.SetParent(_chipGo.transform, false);
                var lr = (RectTransform)lGo.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = lr.offsetMax = Vector2.zero;
                _chipLabel = UIBuilder.Tmp(lGo, "", 11.5f, TextAnchor.MiddleCenter, Theme.Text);
                _chipLabel.raycastTarget = false;
                UI.ClickHandler.Attach(_chipGo, () => { _userHidden = false; _sig = 0; });
            }
            if (_chipLabel != null)
            {
                string t = "› " + Loc.T("Events") + " #" + floor;
                if (_chipLabel.text != t) _chipLabel.text = t;
            }
            if (!_chipGo.activeSelf) _chipGo.SetActive(true);
        }

        // ── data ─────────────────────────────────────────────────────────────

        private static List<ADOFAI.LevelEvent> FloorEvents(scnEditor ed, int floor)
        {
            var list = new List<ADOFAI.LevelEvent>();
            try
            {
                foreach (var e in ed.events)
                    if (e != null && e.floor == floor) list.Add(e);
            }
            catch { }
            return list;
        }

        private static long Sig(scnEditor ed, int floor, List<ADOFAI.LevelEvent> events)
        {
            long h = 17;
            h = h * 31 + floor;
            foreach (var t in _expandedTypes) h += (t + 1) * 733;   // commutative — set order varies
            foreach (var k in _expandedInst) h += (k + 1) * 977;
            foreach (var e in events)
            {
                h = h * 31 + (int)e.eventType;
                h = h * 31 + TargetTag(e).GetHashCode();
                if (_sel.Contains(e)) h += 4099;   // commutative — selection tints the rows
            }
            return h;
        }

        private static void SeedExpansion()
        {
            _expandedTypes.Clear();
            _expandedInst.Clear();
        }

        /* One event: open it, there is nothing else to scan past. Several: select the first so
           the game's hotkeys and the batch bar have a target, but leave the tree collapsed as a
           scannable index. */
        private static void SeedSelection(List<ADOFAI.LevelEvent> events)
        {
            var first = events[0];   // events[0] is also the first row: groups keep first-seen order
            if (events.Count == 1) _expandedTypes.Add((int)first.eventType);
            _sel.Clear(); _sel.Add(first); _anchor = first; _gameSel = first;
            _cursorKey = _rowAnchorKey = -1L - (int)first.eventType;
        }

        internal static ADOFAI.LevelEventInfo InfoOf(ADOFAI.LevelEvent evt)
        {
            try
            {
                ADOFAI.LevelEventInfo info;
                if (GCS.levelEventsInfo.TryGetValue(evt.eventType.ToString(), out info)) return info;
            }
            catch { }
            return null;
        }

        internal static string EventTitle(ADOFAI.LevelEvent evt)
        {
            try
            {
                bool ex;
                var loc = RDString.GetWithCheck("editor." + evt.eventType, out ex, null);
                if (ex && !string.IsNullOrEmpty(loc)) return loc;
            }
            catch { }
            return evt.eventType.ToString();
        }

        // ── UI ───────────────────────────────────────────────────────────────

        // built once (+ on floor change): the window, header, resize handles, scroll viewport
        private static void BuildShell(scnEditor ed)
        {
            K.LblW = 118f;
            K.Rebuild(Loc.T("Events") + " · #" + _floor, () =>
            {
                _userHidden = true; // collapse to the reopen chip; keep the tile SELECTED
            }, new Vector2(1495f, -72f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 280f, 240f);
            K.OnDragEnd = () => K.SnapDockOnDragEnd(); // Adobe-style edge docking
            if (!_dockInited) { _dockInited = true; K.SetDock(2); } // docked right by default

            // masked scroll viewport between header and bottom pad
            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(0f, 3f);
            // Flush to the header: the first row's own top gap is Gap, like every other row's.
            _viewport.offsetMax = new Vector2(0f, -HeaderH);
            vpGo.AddComponent<RectMask2D>();
            var vpImg = vpGo.AddComponent<Image>();
            vpImg.color = new Color(0f, 0f, 0f, 0.01f);
            vpImg.raycastTarget = true; // wheel target

            var cGo = new GameObject("Content", typeof(RectTransform));
            cGo.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        // rebuilt on every expand/collapse — ONLY the scroll content, not the whole panel
        private static void BuildContent(scnEditor ed, List<ADOFAI.LevelEvent> events)
        {
            if (_content == null) return;
            _ctx.PanelW = _size.x; // rows/dropdowns re-fit the current panel width (viewport = full width)
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);
            // Destroy is deferred: the old rows stay children until the frame ends.
            _motionFirstChild = _content.childCount;
            _mRows.Clear(); _mBase.Clear(); _mBottom.Clear(); _motionT = 1f;
            _rows.Clear(); _rowKeys.Clear(); _rowY.Clear();

            // tree view: events grouped by TYPE, instances as child nodes
            var order = new List<int>();
            var groups = new Dictionary<int, List<ADOFAI.LevelEvent>>();
            foreach (var e in events)
            {
                int t = (int)e.eventType;
                List<ADOFAI.LevelEvent> l;
                if (!groups.TryGetValue(t, out l)) { l = new List<ADOFAI.LevelEvent>(); groups[t] = l; order.Add(t); }
                l.Add(e);
            }
            // Flattened display order backs shift-range selection; group order, then instances.
            _flat.Clear();
            foreach (var t in order) _flat.AddRange(groups[t]);
            _sel.RemoveWhere(e => !_flat.Contains(e));   // deleted/undone events drop out
            if (_gameSel != null && !_flat.Contains(_gameSel)) _gameSel = null;

            float y = -Gap;   // uniform: every row owns Gap above it, including the first
            y = TopBar(ed, events, y);
            foreach (var t in order)
                y = TypeSection(ed, t, groups[t], y);
            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        /* One action per row, to the RIGHT of every header: [×]. Copy used to sit here too, on
           every row, which cost ~48px of title width on all of them to duplicate what the
           selection bar already does — ctrl-click rows, then Copy there. */
        private const float DelW = 22f, ActGap = 4f;

        private static float TypeSection(scnEditor ed, int type, List<ADOFAI.LevelEvent> list, float y)
        {
            bool tExp = _expandedTypes.Contains(type);
            bool single = list.Count == 1;
            // Groups carry a count; the toggle button in front of every header does the disclosing.
            string title = single
                ? EventTitle(list[0]) + TagSuffix(list[0])
                : EventTitle(list[0]) + "  ×" + list.Count;
            string preview = single ? Preview(list[0]) : TagList(list);
            long typeKey = -1L - type;   // instance keys are >= 0
            if (_motionKey == typeKey) { _motionHeadY = y; _motionHeadSeen = true; }

            float pw = _size.x; // live panel width — headers/× must track it like the value rows do
            float headW = pw - Pad * 2f - DelW - ActGap;
            AddRow(typeKey, list, y);
            var head = HeaderCell(title, preview, Pad, y, headW, tExp, EditorEventSelector.TypeIcon(type),
                () => SelectRow(list, typeKey), () =>
            {
                if (!_expandedTypes.Add(type)) _expandedTypes.Remove(type);
                ArmMotion(typeKey);
            });
            // Collapsed folders sit brighter than leaf rows so the two tiers separate at a glance.
            head.color = RowTint(list, tExp, 0.4f, single ? 0.06f : 0.11f);
            head.gameObject.AddComponent<EventDragSource>().Events = () => DragSet(list);   // into the tray / onto a tile
            var group = list;
            var delGroup = EventRows.Cell(_content, "×", pw - Pad - DelW, y, DelW, RowH, () =>
            {
                if (single) { DeleteEvent(ed, group[0]); return; }
                // Deleting a whole group is the one destructive action here that isn't one row.
                ConfirmBox.Ask(Loc.T("Delete this event group?") + "\n" + EventTitle(group[0]) + " · " + group.Count,
                    Loc.T("Delete"), () => DeleteEvents(ed, group));
            }, true);
            UI.HoverTip.Attach(delGroup.gameObject, Loc.T(single ? "Delete event" : "Delete all events in this group"));
            y -= RowH + Gap;
            if (!tExp) return y;

            if (single)
                return InstanceBody(ed, list[0], y);

            for (int i = 0; i < list.Count; i++)
            {
                var evt = list[i];
                long key = type * 1000L + i;
                bool iExp = _expandedInst.Contains(key);
                string prev = Preview(evt);
                string label = (i + 1) + "." + TagSuffix(evt);
                if (_motionKey == key) { _motionHeadY = y; _motionHeadSeen = true; }
                var one = new List<ADOFAI.LevelEvent> { evt };
                AddRow(key, one, y);
                var sub = HeaderCell(label, prev, Pad + 12f, y,
                    pw - Pad * 2f - 12f - DelW - ActGap, iExp, null,
                    () => SelectRow(one, key), () =>
                {
                    if (!_expandedInst.Add(key)) _expandedInst.Remove(key);
                    ArmMotion(key);
                });
                sub.gameObject.AddComponent<EventDragSource>().Events = () => DragSet(one);
                sub.color = _sel.Contains(evt) ? SelTint
                    : iExp ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.25f)
                           : new Color(1f, 1f, 1f, 0.04f);
                UI.HoverTip.Attach(EventRows.Cell(_content, "×", pw - Pad - DelW, y, DelW, RowH, () => DeleteEvent(ed, evt), true).gameObject,
                    Loc.T("Delete event"));
                y -= RowH + Gap;
                if (iExp) y = InstanceBody(ed, evt, y);
            }
            return y;
        }

        private static readonly Color SelTint = new Color(0.35f, 0.76f, 1f, 0.45f);

        // ── expand/collapse motion ─────────────────────────────────────────────

        private static void ArmMotion(long key)
        {
            _motionKey = key; _motionHeadSeen = false;
            _motionOldH = _content != null ? _content.sizeDelta.y : 0f;
            _sig = 0;
        }

        private static void BeginMotion()
        {
            bool seen = _motionHeadSeen;
            _motionKey = long.MinValue; _motionHeadSeen = false;
            if (!seen || _content == null || !UI.UiAnim.Enabled) return;
            float delta = _content.sizeDelta.y - _motionOldH;    // + opened, − closed
            if (Mathf.Abs(delta) < 1f) return;
            float pivot = _motionHeadY - RowH - Gap * 0.5f;      // between the header and what follows
            float curtain = pivot - Mathf.Max(delta, 0f);        // an expand's body sits above this
            var corners = new Vector3[4];
            for (int i = _motionFirstChild; i < _content.childCount; i++)
            {
                var rt = _content.GetChild(i) as RectTransform;
                if (rt == null) continue;
                rt.GetWorldCorners(corners);
                float top = _content.InverseTransformPoint(corners[1]).y;
                if (top > pivot) continue;                        // the header and everything above it stay
                bool body = top > curtain;
                if (body)
                {
                    var cg = rt.GetComponent<CanvasGroup>();
                    if (cg == null) rt.gameObject.AddComponent<CanvasGroup>();
                }
                _mRows.Add(rt);
                _mBase.Add(rt.anchoredPosition);
                _mBottom.Add(body ? _content.InverseTransformPoint(corners[0]).y : float.NaN);
            }
            _motionDelta = delta; _motionCurtain = curtain; _motionT = 0f;
            ApplyMotion(0f);
        }

        private static void TickMotion()
        {
            if (_motionT >= 1f) return;
            float sec = UI.UiAnim.Sec;
            _motionT = sec > 0f ? Mathf.Min(1f, _motionT + Time.unscaledDeltaTime / sec) : 1f;
            ApplyMotion(1f - (1f - _motionT) * (1f - _motionT));   // ease out
            if (_motionT >= 1f) { _mRows.Clear(); _mBase.Clear(); _mBottom.Clear(); }
        }

        private static void ApplyMotion(float k)
        {
            float off = _motionDelta * (1f - k);
            float curtain = _motionCurtain + off;   // top edge of the rows sliding over the body
            for (int i = 0; i < _mRows.Count; i++)
            {
                var rt = _mRows[i];
                if (rt == null) continue;
                if (float.IsNaN(_mBottom[i]))
                {
                    rt.anchoredPosition = _mBase[i] + new Vector2(0f, off);
                    continue;
                }
                var cg = rt.GetComponent<CanvasGroup>();
                if (cg != null) cg.alpha = k >= 1f ? 1f : Mathf.Clamp01((_mBottom[i] - curtain) / RowH + 1f);
            }
        }

        private static Color RowTint(List<ADOFAI.LevelEvent> list, bool expanded, float onA, float offA)
        {
            foreach (var e in list) if (_sel.Contains(e)) return SelTint;
            return expanded
                ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, onA)
                : new Color(1f, 1f, 1f, offA);
        }

        // ── free selection (ctrl toggle · shift range) ───────────────────────

        // A plain click selects just this row; Ctrl/Shift extend. Expanding is the glyph's job.
        private static void SelectRow(List<ADOFAI.LevelEvent> rowEvents, long key)
        {
            _cursorKey = key;
            if (SelectClick(rowEvents)) return;
            _rowAnchorKey = key;
            _sel.Clear();
            foreach (var e in rowEvents) _sel.Add(e);
            _anchor = rowEvents[rowEvents.Count - 1];
            _gameSel = rowEvents[0];   // hand the game's hotkeys the row's first event
            _sig = 0;
        }

        private static void AddRow(long key, List<ADOFAI.LevelEvent> events, float y)
        { _rows.Add(events); _rowKeys.Add(key); _rowY.Add(y); }

        // ── Up / Down ──────────────────────────────────────────────────────────

        // Also read by the Harmony guards that stand the game's own Up/Down actions down.
        internal static bool OwnsArrows()
        {
            if (Time.frameCount - _shownFrame > 1 || _rows.Count == 0 || UI.FieldNav.Typing) return false;
            if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)
                || Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) return false;
            try { var ed = scnEditor.instance; return ed != null && !ed.playMode; } catch { return false; }
        }

        // Up/Down move one row; with Shift they extend from the row the selection started on.
        private static void TickArrows()
        {
            bool up = Input.GetKeyDown(KeyCode.UpArrow), down = Input.GetKeyDown(KeyCode.DownArrow);
            if ((!up && !down) || !OwnsArrows()) return;
            int cur = _rowKeys.IndexOf(_cursorKey);
            int next = cur < 0 ? 0 : Mathf.Clamp(cur + (down ? 1 : -1), 0, _rows.Count - 1);
            if (next == cur) return;
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            int a = shift ? _rowKeys.IndexOf(_rowAnchorKey) : -1;
            if (a < 0) { a = next; _rowAnchorKey = _rowKeys[next]; }
            _sel.Clear();
            for (int i = Mathf.Min(a, next); i <= Mathf.Max(a, next); i++)
                foreach (var e in _rows[i]) _sel.Add(e);
            var row = _rows[next];
            _cursorKey = _rowKeys[next];
            _anchor = row[row.Count - 1];
            _gameSel = row[0];
            _revealCursor = true;
            _sig = 0;
        }

        // Scroll the keyboard cursor's row into view after the rebuild that moved it.
        private static void RevealCursor()
        {
            if (!_revealCursor || _viewport == null || _content == null) return;
            _revealCursor = false;
            int i = _rowKeys.IndexOf(_cursorKey);
            if (i < 0) return;
            float top = -_rowY[i], viewH = _viewport.rect.height;
            if (top - Gap < _scroll) _scroll = top - Gap;
            else if (top + RowH + Gap > _scroll + viewH) _scroll = top + RowH + Gap - viewH;
            ClampScroll();
        }

        // Returns true for a Ctrl/Shift gesture. A group row selects (or clears) all of its
        // instances at once.
        private static bool SelectClick(List<ADOFAI.LevelEvent> rowEvents)
        {
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)
                     || Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand);
            bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (!ctrl && !shift) return false;
            if (rowEvents == null || rowEvents.Count == 0) return false;
            var last = rowEvents[rowEvents.Count - 1];
            _gameSel = last;

            if (ctrl)
            {
                bool allOn = true;
                foreach (var e in rowEvents) if (!_sel.Contains(e)) { allOn = false; break; }
                foreach (var e in rowEvents) { if (allOn) _sel.Remove(e); else _sel.Add(e); }
                _anchor = allOn ? null : last;
            }
            else
            {
                int b = _flat.IndexOf(last);
                if (b < 0) return true;
                int a = _anchor != null ? _flat.IndexOf(_anchor) : -1;
                if (a < 0) a = b;
                for (int i = Mathf.Min(a, b); i <= Mathf.Max(a, b); i++) _sel.Add(_flat[i]);
                _anchor = last;
            }
            _sig = 0;
            return true;
        }

        /* Tile-wide actions + the batch bar for the current selection. Delete-all is always
           offered (it is the reason this panel exists for a 40-event tile); copy/delete of a
           selection only appear once something is selected. */
        private static float TopBar(scnEditor ed, List<ADOFAI.LevelEvent> events, float y)
        {
            float w = _size.x - Pad * 2f;
            if (_sel.Count > 0)
            {
                float third = (w - Gap * 2f) / 3f;
                var selected = new List<ADOFAI.LevelEvent>(_sel);
                EventRows.Cell(_content, _sel.Count + " " + Loc.T("selected"), Pad, y, third, RowH,
                    () => { _sel.Clear(); _anchor = null; _sig = 0; }, false).color = SelTint;
                EventRows.Cell(_content, Loc.T("Copy"), Pad + third + Gap, y, third, RowH,
                    () => CopyEvents(selected), true);
                EventRows.Cell(_content, Loc.T("Delete"), Pad + (third + Gap) * 2f, y, third, RowH,
                    () => ConfirmBox.Ask(Loc.T("Delete selected events?") + "\n" + selected.Count,
                        Loc.T("Delete"), () => DeleteEvents(ed, selected)), true);
                y -= RowH + Gap;
            }
            var all = new List<ADOFAI.LevelEvent>(events);
            /* Filter manager is reachable from EVERY tile, not just one that already has a
               filter event — it's how you add the first one. Opens on this tile's filter event
               when there is one, else unbound (Open handles null). */
            ADOFAI.LevelEvent filt = null;
            foreach (var e in events)
                if (e.eventType == ADOFAI.LevelEventType.SetFilterAdvanced
                    || e.eventType == ADOFAI.LevelEventType.SetFilter) { filt = e; break; }
            const float trayW = 56f;
            float half = (w - trayW - Gap * 2f) * 0.5f;
            EventRows.Cell(_content, Loc.T("Filter manager"), Pad, y, half, RowH,
                () => EditorFilterPicker.Open(filt, _floor), true);
            UI.HoverTip.Attach(EventRows.Cell(_content, Loc.T("Tray"), Pad + half + Gap, y, trayW, RowH,
                EditorEventTray.Toggle, true).gameObject, Loc.T("Drag rows into the tray to keep them"));
            EventRows.Cell(_content, Loc.T("Delete all events") + " (" + all.Count + ")",
                Pad + half + trayW + Gap * 2f, y, half, RowH,
                () => ConfirmBox.Ask(Loc.T("Delete all events on this tile?") + "\n#" + _floor + " · " + all.Count,
                    Loc.T("Delete"), () => DeleteEvents(ed, all)), true)
                .color = new Color(0.80f, 0.22f, 0.26f, 0.4f);
            return y - (RowH + Gap);
        }

        /* Copy = detached copies (LevelEvent.Copy, so later edits to the source don't rewrite the
           clipboard) that Ctrl+V pastes onto the selected tile(s). No tool is armed and no panel
           opens; the notification is the whole response. */
        private static void CopyEvents(List<ADOFAI.LevelEvent> list)
        {
            if (list == null || list.Count == 0) return;
            _gameSel = list[0];   // keep the game's own Ctrl+C on the same event we just copied
            int n = EditorToolbar.CopyEventsForPaste(list);
            try { scnEditor.instance.ShowNotification(Loc.T("Events copied") + " · " + n, null, 0f); } catch { }
        }

        // tag / filter-name shown beside the node — the tree stays scannable while collapsed
        private static string Preview(ADOFAI.LevelEvent evt)
        {
            try
            {
                var d = EditorEvents.EventData(evt);
                if (d == null) return "";
                object f;
                if (evt.eventType == ADOFAI.LevelEventType.SetFilterAdvanced
                    && d.TryGetValue("filter", out f) && f is string fs && fs.Length > 0)
                    return fs.StartsWith("CameraFilterPack_") ? fs.Substring("CameraFilterPack_".Length) : fs;
                if (evt.eventType == ADOFAI.LevelEventType.SetFilter
                    && d.TryGetValue("filter", out f) && f != null)
                    return f.ToString();
                object tag;
                if (d.TryGetValue("eventTag", out tag) && tag is string ts && ts.Length > 0)
                    return ts;
            }
            catch { }
            return "";
        }

        /* Decoration-targeting events (MoveDecorations, SetText, SetObject, SetParticle,
           EmitParticle) carry their target in `tag`. Seven MoveDecorations on one tile were
           seven identical "+ 1." rows without it. Kept separate from Preview() — Preview owns
           the RIGHT-hand slot (eventTag / filter), this owns the left label. */
        private static string TargetTag(ADOFAI.LevelEvent evt)
        {
            try
            {
                var d = EditorEvents.EventData(evt);
                object v;
                if (d != null && d.TryGetValue("tag", out v) && v is string s) return s;
            }
            catch { }
            return "";
        }

        internal static string TagSuffix(ADOFAI.LevelEvent evt)
        {
            string t = TargetTag(evt);
            return t.Length > 0 ? "  " + t : "";
        }

        // Distinct target tags across a collapsed group, first-seen order. No manual
        // truncation: the preview TMP is NoWrap + Ellipsis, so it clips to the panel width.
        private static string TagList(List<ADOFAI.LevelEvent> list)
        {
            var seen = new HashSet<string>();
            var sb = new System.Text.StringBuilder();
            foreach (var e in list)
            {
                string t = TargetTag(e);
                if (t.Length == 0 || !seen.Add(t)) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(t);
            }
            return sb.ToString();
        }

        private static float InstanceBody(scnEditor ed, ADOFAI.LevelEvent evt, float y)
        {
            var info = InfoOf(evt);
            if (info == null || EditorEvents.EventData(evt) == null)
            {
                EventRows.Label(_content, Loc.T("Edited in the event panel"), Pad, y, _size.x - Pad * 2f, RowH, Theme.TextMuted);
                return y - (RowH + Gap);
            }

            _ctx.Content = _content;
            y = EventRows.Render(_ctx, ed, info, evt, y);

            // the filter manager (browser + parameter column) opens from here now — its
            // chip used to ride the game panel, which is hidden
            if (evt.eventType == ADOFAI.LevelEventType.SetFilterAdvanced
                || evt.eventType == ADOFAI.LevelEventType.SetFilter)
            {
                var e2 = evt;
                EventRows.Cell(_content, Loc.T("Filter manager"), Pad, y, _size.x - Pad * 2f, RowH,
                    () => EditorFilterPicker.Open(e2), true);
                y -= RowH + Gap;
            }
            y -= 4f;
            return y;
        }

        // shared row-engine context; MarkDirty forces a content-only rebuild next tick,
        // AfterCommit refreshes the (hidden) game panel so other modules stay in sync
        private static readonly EventRows.Ctx _ctx = new EventRows.Ctx
        {
            PanelW = PanelW,
            MarkDirty = () => _sig = 0,
            AfterCommit = EventAfterCommit,
        };

        private static void EventAfterCommit(scnEditor ed, ADOFAI.LevelEvent evt, ADOFAI.PropertyInfo pi)
        {
            bool path = false, floors = false;
            try { path = pi != null && pi.affectsPath; } catch { }
            try { floors = pi != null && pi.affectsFloors; } catch { }
            try
            {
                if (path) ed.RemakePath(true, true);
                else if (floors) ed.ApplyEventsToFloors();
            }
            catch { }
            try
            {
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

        // [▸ icon | title .......... muted preview]
        private const float DiscW = 28f, DiscIconW = 48f, ChevW = 20f, IconSz = 16f;

        private static RoundedRectGraphic HeaderCell(string title, string preview, float x, float y,
            float w, bool expanded, Sprite icon, Action onSelect, Action onToggle)
        {
            var bg = EventRows.Cell(_content, title, x, y, w, RowH, onSelect, false, TextAnchor.MiddleLeft);
            float dw = icon != null ? DiscIconW : DiscW;
            var lbl = bg.transform.Find("L") as RectTransform;
            if (lbl != null) lbl.offsetMin = new Vector2(dw + 8f, lbl.offsetMin.y);
            // A visible button, not a glyph-sized hit zone. Its own handler takes the click before
            // the row's, so it expands without selecting.
            var dGo = new GameObject("D", typeof(RectTransform));
            dGo.transform.SetParent(bg.transform, false);
            var dr = (RectTransform)dGo.transform;
            dr.anchorMin = Vector2.zero; dr.anchorMax = new Vector2(0f, 1f);
            dr.offsetMin = new Vector2(2f, 2f); dr.offsetMax = new Vector2(2f + dw, -2f);
            var dbg = dGo.AddComponent<RoundedRectGraphic>();
            dbg.Radius = 4f;
            dbg.color = new Color(1f, 1f, 1f, expanded ? 0.16f : 0.09f);
            UI.ClickHandler.Attach(dGo, onToggle);
            UI.HoverTip.Attach(dGo, Loc.T(expanded ? "Collapse" : "Expand"));
            var cGo = new GameObject("V", typeof(RectTransform));
            cGo.transform.SetParent(dGo.transform, false);
            var cr = (RectTransform)cGo.transform;
            cr.anchorMin = Vector2.zero; cr.anchorMax = new Vector2(0f, 1f);
            cr.offsetMin = Vector2.zero; cr.offsetMax = new Vector2(ChevW, 0f);
            UIBuilder.Tmp(cGo, expanded ? "▾" : "▸", 13f, TextAnchor.MiddleCenter, Theme.Text).raycastTarget = false;
            if (icon != null)
            {
                var iGo = new GameObject("I", typeof(RectTransform));
                iGo.transform.SetParent(dGo.transform, false);
                var ir = (RectTransform)iGo.transform;
                ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
                ir.pivot = new Vector2(0f, 0.5f);
                ir.anchoredPosition = new Vector2(ChevW, 0f);
                ir.sizeDelta = new Vector2(IconSz, IconSz);
                var img = iGo.AddComponent<Image>();
                img.sprite = icon;
                img.preserveAspect = true;
                img.raycastTarget = false;
            }
            if (!string.IsNullOrEmpty(preview))
            {
                var pGo = new GameObject("P", typeof(RectTransform));
                pGo.transform.SetParent(bg.transform, false);
                var pr = (RectTransform)pGo.transform;
                pr.anchorMin = Vector2.zero; pr.anchorMax = Vector2.one;
                pr.offsetMin = new Vector2(8f, 0f); pr.offsetMax = new Vector2(-8f, 0f);
                var pt = UIBuilder.Tmp(pGo, preview, 11f, TextAnchor.MiddleRight, Theme.TextMuted);
                pt.textWrappingMode = TextWrappingModes.NoWrap;
                pt.overflowMode = TextOverflowModes.Ellipsis;
                pt.raycastTarget = false;
            }
            return bg;
        }

        // ── commits (SaveStateScope, fidelity from the registry's affect flags) ──

        private static void DeleteEvent(scnEditor ed, ADOFAI.LevelEvent evt)
        {
            try
            {
                using (new SaveStateScope(ed))
                {
                    ed.events.Remove(evt);
                    ed.ApplyEventsToFloors();
                    ed.RemakePath(true, true);
                }
            }
            catch (Exception ex) { SapphireLog.Log("EventPanel: delete failed: " + ex.Message); }
            _expandedInst.Clear(); // instance indices shifted; keep the type nodes open
            _sel.Remove(evt);
            _sig = 0;
        }

        // Batch delete (selection or whole tile): one SaveStateScope, one path remake — deleting
        // 40 events one call at a time meant 40 undo states and 40 full path rebuilds.
        private static void DeleteEvents(scnEditor ed, List<ADOFAI.LevelEvent> list)
        {
            if (list == null || list.Count == 0) return;
            try
            {
                using (new SaveStateScope(ed))
                {
                    foreach (var e in list) { try { ed.events.Remove(e); } catch { } }
                    ed.ApplyEventsToFloors();
                    ed.RemakePath(true, true);
                }
            }
            catch (Exception ex) { SapphireLog.Log("EventPanel: batch delete failed: " + ex.Message); }
            foreach (var e in list) _sel.Remove(e);
            _anchor = null;
            _expandedInst.Clear();
            _sig = 0;
        }

        // ── scroll + resize ──────────────────────────────────────────────────

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

        // the header must stay reachable — off-screen spawn/drag made the window unmovable
        private static void ClampIntoView()
        {
            if (!K.Built) return;
            var r = (RectTransform)K.PanelGo.transform;
            var canvas = (RectTransform)K.CanvasGo.transform;
            var p = r.anchoredPosition;
            p.x = Mathf.Clamp(p.x, 0f, Mathf.Max(0f, canvas.rect.width - 80f));
            p.y = Mathf.Clamp(p.y, -(canvas.rect.height - 40f), 0f);
            if ((p - r.anchoredPosition).sqrMagnitude > 0.01f) r.anchoredPosition = p;
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
            if ((r.sizeDelta - _size).sqrMagnitude > 1f)
            {
                _size = r.sizeDelta;
                _sig = 0;  // width changed → rebuild rows/dropdowns at the new content width
                ClampScroll();
            }
        }

        // ── small builders parented to the scroll content ────────────────────

        // ── shared helpers (same semantics as the filter manager) ────────────

    }
}
