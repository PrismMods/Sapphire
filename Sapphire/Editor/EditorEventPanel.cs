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

       Scope guard: shown only for a SINGLE selected floor. Selecting a decoration
       un-hides the game panel — decoration editing still lives there for now. */
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

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool active = s != null && MainClass.EditorSuiteOn && s.EditorNativeInspector
                       && ed != null && !ed.playMode;

            int floor = -1;
            bool decorationSelected = false;
            if (active)
            {
                try
                {
                    if (ed.selectedFloors != null && ed.selectedFloors.Count == 1 && ed.selectedFloors[0] != null)
                        floor = ed.selectedFloors[0].seqID;
                    decorationSelected = ed.selectedDecorations != null && ed.selectedDecorations.Count > 0;
                }
                catch { }
            }

            // hide the game panel only while WE are the inspector; decorations still need it
            bool hideGame = active && !decorationSelected;
            SyncGamePanelHidden(ed, hideGame);

            bool want = active && floor >= 0 && !decorationSelected;
            if (!want)
            {
                K.Show(false);
                _floor = -1; _sig = 0; _empty = false;
                return;
            }

            /* Per-frame cost control: FloorEvents (allocs a list, scans ALL level events) and
               Sig are only run when something might have changed — a floor change (cheap to
               detect above), one of OUR edits (which force _sig = 0), or a periodic rescan
               to catch external edits (undo). On a static selection the tick is nearly free;
               scanning every frame is what dropped a big level from 120 to ~80fps. */
            bool floorChanged = floor != _floor;
            if (floorChanged) { _floor = floor; SeedExpansion(); _scroll = 0f; _dirty = true; }
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

            K.Show(true);
            ClampIntoView();
            TickScroll();
            TickResize();
        }

        private static bool _dockInited;

        private static float TopMargin() => 56f;

        private static float BottomInset()
        {
            float strip = 0f;
            try { strip = EditorEvents.BottomStripTop; } catch { }
            // the button rows above the strip (STRICT/NO FAIL/AUTO, zoom/CAM/GRAPH) need
            // clearance too; they hide with the strip
            return strip > 0f ? strip + 100f : 12f;
        }

        internal static void Dispose()
        {
            RestoreGamePanel();
            K.Dispose();
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
                if (_gameCg.alpha != a)
                {
                    _gameCg.alpha = a;
                    _gameCg.blocksRaycasts = !hide;
                }
                // the event-type tab strip is its own root object — our sections replace it
                var tabs = ed.inspectorTabs;
                if (tabs != null)
                {
                    var tGo = tabs.gameObject;
                    if (_tabsCg == null || _tabsCg.gameObject != tGo)
                        _tabsCg = tGo.GetComponent<CanvasGroup>() ?? tGo.AddComponent<CanvasGroup>();
                    if (_tabsCg.alpha != a)
                    {
                        _tabsCg.alpha = a;
                        _tabsCg.blocksRaycasts = !hide;
                    }
                }
            }
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
            foreach (var e in events) h = h * 31 + (int)e.eventType;
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
                // × just collapses until the selection changes (deselect = natural close)
                try { ed.DeselectFloors(); } catch { }
            }, new Vector2(1495f, -70f));
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
            _viewport.offsetMin = new Vector2(0f, 8f);
            _viewport.offsetMax = new Vector2(0f, -HeaderH - 2f);
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
            float y = -2f;
            foreach (var t in order)
                y = TypeSection(ed, t, groups[t], y);
            _content.sizeDelta = new Vector2(0f, -y + 6f);
            ClampScroll();
        }

        private static float TypeSection(scnEditor ed, int type, List<ADOFAI.LevelEvent> list, float y)
        {
            bool tExp = _expandedTypes.Contains(type);
            bool single = list.Count == 1;
            string title = (tExp ? "− " : "+ ") + EventTitle(list[0])
                + (single ? "" : "  ×" + list.Count);
            string preview = single ? Preview(list[0]) : "";

            float headW = PanelW - Pad * 2f - (single ? 26f : 0f);
            var head = HeaderCell(title, preview, Pad, y, headW, () =>
            {
                if (!_expandedTypes.Add(type)) _expandedTypes.Remove(type);
                _sig = 0;
            });
            head.color = tExp
                ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f)
                : new Color(1f, 1f, 1f, 0.06f);
            if (single)
                EventRows.Cell(_content, "×", PanelW - Pad - 22f, y, 22f, RowH, () => DeleteEvent(ed, list[0]), true);
            y -= RowH + Gap;
            if (!tExp) return y;

            if (single)
                return InstanceBody(ed, list[0], y);

            for (int i = 0; i < list.Count; i++)
            {
                int idx = i;
                var evt = list[i];
                long key = type * 1000L + i;
                bool iExp = _expandedInst.Contains(key);
                string prev = Preview(evt);
                string label = (iExp ? "− " : "+ ") + (i + 1) + ".";
                var sub = HeaderCell(label, prev, Pad + 12f, y, PanelW - Pad * 2f - 12f - 26f, () =>
                {
                    if (!_expandedInst.Add(key)) _expandedInst.Remove(key);
                    _sig = 0;
                });
                sub.color = iExp
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.25f)
                    : new Color(1f, 1f, 1f, 0.04f);
                EventRows.Cell(_content, "×", PanelW - Pad - 22f, y, 22f, RowH, () => DeleteEvent(ed, evt), true);
                y -= RowH + Gap;
                if (iExp) y = InstanceBody(ed, evt, y);
            }
            return y;
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

        private static float InstanceBody(scnEditor ed, ADOFAI.LevelEvent evt, float y)
        {
            var info = InfoOf(evt);
            if (info == null || EditorEvents.EventData(evt) == null)
            {
                EventRows.Label(_content, Loc.T("Edited in the event panel"), Pad, y, PanelW - Pad * 2f, RowH, Theme.TextMuted);
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
                EventRows.Cell(_content, Loc.T("Filter manager"), Pad, y, PanelW - Pad * 2f, RowH,
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
                ClampScroll();
            }
        }

        // ── small builders parented to the scroll content ────────────────────

        // ── shared helpers (same semantics as the filter manager) ────────────

    }
}
