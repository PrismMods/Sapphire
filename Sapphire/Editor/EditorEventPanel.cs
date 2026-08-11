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
        private static long _sig;
        private const long EmptySig = -1L;     // _sig marker: scanned, tile has no events
        private static bool _empty;            // last scan found nothing on this floor
        private static bool _dirty;            // a scan/rebuild is pending
        private static int _scanCd;            // frames until the next external-change rescan
        private static CanvasGroup _gameCg;    // the hidden game panel
        /* Free selection across the tree: ctrl-click toggles a row, shift-click takes the range
           since the last anchor, in the flattened display order (_flat). Plain clicks stay
           expand/collapse — the tree is still primarily a browser. Selection drives the batch
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
                _floor = floor; SeedExpansion(); _scroll = 0f; _dirty = true;
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
                _empty = false;
                // Shell (panel/header/viewport/resize) built once + on floor change; expanding
                // a section rebuilds only the CONTENT tree.
                bool rebuildContent = false;
                if (!K.Built || floorChanged) { BuildShell(ed); rebuildContent = true; }
                long sig = Sig(ed, floor, events);
                if (sig != _sig) { _sig = sig; rebuildContent = true; }
                if (rebuildContent) BuildContent(ed, events);
            }
            // Nothing to show until the first scan, or when the latch says the tile is empty
            // (K may still be built from a previously selected tile).
            else if (!K.Built || _empty) { K.Show(false); return; }

            SyncGameSelection(ed);
            K.Show(true);
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

        // Every tile opens fully COLLAPSED (user request July 19) — the tree is a compact
        // scannable index; expand what you need.
        private static void SeedExpansion()
        {
            _expandedTypes.Clear();
            _expandedInst.Clear();
        }

        private static ADOFAI.LevelEventInfo InfoOf(ADOFAI.LevelEvent evt)
        {
            try
            {
                ADOFAI.LevelEventInfo info;
                if (GCS.levelEventsInfo.TryGetValue(evt.eventType.ToString(), out info)) return info;
            }
            catch { }
            return null;
        }

        private static string EventTitle(ADOFAI.LevelEvent evt)
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
            /* Groups read as FOLDERS, singles as rows: chevron disclosure + a count, against
               the +/− the leaf rows use. Same visual language as the deco browser's tag
               folders, so "this contains things" looks the same everywhere. */
            string title = single
                ? (tExp ? "− " : "+ ") + EventTitle(list[0]) + TagSuffix(list[0])
                : (tExp ? "‹ " : "› ") + EventTitle(list[0]) + "  ×" + list.Count;
            string preview = single ? Preview(list[0]) : TagList(list);

            float pw = _size.x; // live panel width — headers/× must track it like the value rows do
            float headW = pw - Pad * 2f - DelW - ActGap;
            var head = HeaderCell(title, preview, Pad, y, headW, () =>
            {
                _gameSel = list[0];   // hand the game's hotkeys this type's first event
                if (SelectClick(list)) return;
                if (!_expandedTypes.Add(type)) _expandedTypes.Remove(type);
                _sig = 0;
            });
            // Collapsed folders sit brighter than leaf rows so the two tiers separate at a glance.
            head.color = RowTint(list, tExp, 0.4f, single ? 0.06f : 0.11f);
            var group = list;
            EventRows.Cell(_content, "×", pw - Pad - DelW, y, DelW, RowH, () =>
            {
                if (single) { DeleteEvent(ed, group[0]); return; }
                // Deleting a whole group is the one destructive action here that isn't one row.
                ConfirmBox.Ask(Loc.T("Delete this event group?") + "\n" + EventTitle(group[0]) + " · " + group.Count,
                    Loc.T("Delete"), () => DeleteEvents(ed, group));
            }, true);
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
                string label = (iExp ? "− " : "+ ") + (i + 1) + "." + TagSuffix(evt);
                var sub = HeaderCell(label, prev, Pad + 12f, y,
                    pw - Pad * 2f - 12f - DelW - ActGap, () =>
                {
                    _gameSel = evt;
                    if (SelectClickOne(evt)) return;
                    if (!_expandedInst.Add(key)) _expandedInst.Remove(key);
                    _sig = 0;
                });
                sub.color = _sel.Contains(evt) ? SelTint
                    : iExp ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.25f)
                           : new Color(1f, 1f, 1f, 0.04f);
                EventRows.Cell(_content, "×", pw - Pad - DelW, y, DelW, RowH, () => DeleteEvent(ed, evt), true);
                y -= RowH + Gap;
                if (iExp) y = InstanceBody(ed, evt, y);
            }
            return y;
        }

        private static readonly Color SelTint = new Color(0.35f, 0.76f, 1f, 0.45f);

        private static Color RowTint(List<ADOFAI.LevelEvent> list, bool expanded, float onA, float offA)
        {
            foreach (var e in list) if (_sel.Contains(e)) return SelTint;
            return expanded
                ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, onA)
                : new Color(1f, 1f, 1f, offA);
        }

        // ── free selection (ctrl toggle · shift range) ───────────────────────

        private static bool SelectClickOne(ADOFAI.LevelEvent evt)
            => SelectClick(new List<ADOFAI.LevelEvent> { evt });

        // Returns true when the click was a SELECTION gesture, so the caller skips its normal
        // expand/collapse. A group row selects (or clears) all of its instances at once.
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
            float half = (w - Gap) * 0.5f;
            EventRows.Cell(_content, Loc.T("Filter manager"), Pad, y, half, RowH,
                () => EditorFilterPicker.Open(filt, _floor), true);
            EventRows.Cell(_content, Loc.T("Delete all events") + " (" + all.Count + ")",
                Pad + half + Gap, y, half, RowH,
                () => ConfirmBox.Ask(Loc.T("Delete all events on this tile?") + "\n#" + _floor + " · " + all.Count,
                    Loc.T("Delete"), () => DeleteEvents(ed, all)), true)
                .color = new Color(0.80f, 0.22f, 0.26f, 0.4f);
            return y - (RowH + Gap);
        }

        /* Copy = load Sapphire's event clipboard (the inspector tool's capture buffer) and arm
           that tool, so the very next right-click on a tile pastes — filtered by the copy panel
           exactly like a tile capture. Copies are detached (LevelEvent.Copy) so later edits to
           the source don't rewrite the clipboard. */
        private static void CopyEvents(List<ADOFAI.LevelEvent> list)
        {
            if (list == null || list.Count == 0) return;
            _gameSel = list[0];   // keep the game's own Ctrl+C on the same event we just copied
            var buf = new List<ADOFAI.LevelEvent>(list.Count);
            foreach (var e in list) { try { if (e != null) buf.Add(e.Copy()); } catch { } }
            EditorToolbar.LoadInspectorBuffer(buf);
            EditorToolbar.ArmInspector();
            SapphireLog.Log("EventPanel: copied " + buf.Count + " event(s) to the paste buffer");
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

        private static string TagSuffix(ADOFAI.LevelEvent evt)
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

        // [chevron+title .......... muted preview]
        private static RoundedRectGraphic HeaderCell(string title, string preview, float x, float y,
            float w, Action onClick)
        {
            var bg = EventRows.Cell(_content, title, x, y, w, RowH, onClick, false, TextAnchor.MiddleLeft);
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
