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
        private static readonly PanelKit K = new PanelKit("SapphireEventPanel", 902, PanelW);
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
                _floor = -1; _sig = 0;
                return;
            }

            var events = FloorEvents(ed, floor);
            if (events.Count == 0) { K.Show(false); _floor = -1; _sig = 0; return; }
            long sig = Sig(ed, floor, events);
            if (!K.Built || sig != _sig)
            {
                _sig = sig;
                if (floor != _floor)
                {
                    _floor = floor;
                    SeedExpansion(ed, events);
                    _scroll = 0f;
                }
                Build(ed, events);
            }
            K.Show(true);
            K.TickDock(TopMargin(), BottomInset());
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
            _viewport = null; _content = null; _sig = 0; _floor = -1;
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
            // the game panel's own selection is part of the view (rail clicks route there)
            try
            {
                var sel = ed.levelEventsPanel != null ? ed.levelEventsPanel.selectedEvent : null;
                if (sel != null) h = h * 31 + sel.GetHashCode();
            }
            catch { }
            return h;
        }

        // default expansion follows the game panel's currently shown event when possible
        private static void SeedExpansion(scnEditor ed, List<ADOFAI.LevelEvent> events)
        {
            _expandedTypes.Clear();
            _expandedInst.Clear();
            ADOFAI.LevelEvent target = null;
            try
            {
                var sel = ed.levelEventsPanel != null ? ed.levelEventsPanel.selectedEvent : null;
                if (sel != null && events.Contains(sel)) target = sel;
            }
            catch { }
            if (target == null && events.Count > 0) target = events[0];
            if (target == null) return;
            int t = (int)target.eventType;
            int inst = 0;
            foreach (var e in events)
            {
                if (ReferenceEquals(e, target)) break;
                if ((int)e.eventType == t) inst++;
            }
            _expandedTypes.Add(t);
            _expandedInst.Add(t * 1000L + inst);
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

        private static void Build(scnEditor ed, List<ADOFAI.LevelEvent> events)
        {
            K.LblW = 118f;
            K.Rebuild(Loc.T("Events") + " · #" + _floor, () =>
            {
                // × just collapses until the selection changes (deselect = natural close)
                try { ed.DeselectFloors(); } catch { }
            }, new Vector2(1495f, -70f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 280f, 240f); // rebuilds destroy old handles with the panel
            K.OnDragEnd = () => K.SnapDockOnDragEnd(); // Adobe-style edge docking
            if (!_dockInited) { _dockInited = true; K.DockSide = 2; } // docked right by default

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
                Cell("×", PanelW - Pad - 22f, y, 22f, RowH, () => DeleteEvent(ed, list[0]), true);
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
                Cell("×", PanelW - Pad - 22f, y, 22f, RowH, () => DeleteEvent(ed, evt), true);
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
            var data = EditorEvents.EventData(evt);
            if (info == null || data == null)
            {
                Label(Loc.T("Edited in the event panel"), Pad, y, PanelW - Pad * 2f, RowH, Theme.TextMuted);
                return y - (RowH + Gap);
            }

            foreach (var kvp in info.propertiesInfo)
            {
                var pi = kvp.Value;
                string key = kvp.Key;
                if (pi == null) continue;
                try { if (pi.invisible) continue; } catch { }
                string ctl = "";
                try { ctl = pi.controlType.ToString(); } catch { }
                if (ctl == "Note" || ctl == "Export") continue;
                bool shown = true;
                try { shown = pi.CheckIfShown(evt, null); } catch { }
                if (!shown) continue;

                object val;
                if (!data.TryGetValue(key, out val)) continue;

                y = PropertyRow(ed, evt, pi, key, val, y);
            }

            // the filter manager (browser + parameter column) opens from here now — its
            // chip used to ride the game panel, which is hidden
            if (evt.eventType == ADOFAI.LevelEventType.SetFilterAdvanced
                || evt.eventType == ADOFAI.LevelEventType.SetFilter)
            {
                var e2 = evt;
                Cell(Loc.T("Filter manager"), Pad, y, PanelW - Pad * 2f, RowH,
                    () => EditorFilterPicker.Open(e2), true);
                y -= RowH + Gap;
            }
            y -= 4f;
            return y;
        }

        // [chevron+title .......... muted preview]
        private static RoundedRectGraphic HeaderCell(string title, string preview, float x, float y,
            float w, Action onClick)
        {
            var bg = Cell(title, x, y, w, RowH, onClick, false, TextAnchor.MiddleLeft);
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

        private static float PropertyRow(scnEditor ed, ADOFAI.LevelEvent evt, ADOFAI.PropertyInfo pi,
            string key, object val, float y)
        {
            string lbl = key;
            try
            {
                string lockey = null;
                try { lockey = pi.customLocalizationKey; } catch { }
                bool ex = false;
                string loc = null;
                if (!string.IsNullOrEmpty(lockey)) loc = RDString.GetWithCheck(lockey, out ex, null);
                if (!ex) loc = RDString.GetWithCheck("editor." + evt.eventType + "." + key, out ex, null);
                if (!ex) loc = RDString.GetWithCheck("editor." + key, out ex, null);
                lbl = ex && !string.IsNullOrEmpty(loc) ? loc : Loc.T(key);
            }
            catch { }

            // per-property enable checkbox (the game's own disabled dictionary)
            bool canDisable = false;
            try { canDisable = pi.canBeDisabled; } catch { }
            bool isDisabled = false;
            try { isDisabled = evt.disabled != null && evt.disabled.TryGetValue(key, out var dv) && dv; } catch { }
            float x = Pad;
            float w = PanelW - Pad * 2f;
            if (canDisable)
            {
                var box = Cell(isDisabled ? "" : "✓", x, y, 20f, RowH, () =>
                {
                    try
                    {
                        using (new SaveStateScope(ed))
                            evt.disabled[key] = !isDisabled;
                        AfterCommit(ed, evt, pi);
                    }
                    catch (Exception ex2) { SapphireLog.Log("EventPanel: disable toggle failed: " + ex2.Message); }
                    _sig = 0;
                }, true);
                box.color = isDisabled ? new Color(1f, 1f, 1f, 0.05f)
                    : new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f);
                x += 24f;
                w -= 24f;
            }

            var lblCol = isDisabled ? new Color(0.45f, 0.45f, 0.5f, 1f) : Theme.TextMuted;

            string k = key;
            var e2 = evt;
            var p2 = pi;

            string[] strOpts = null;
            try { if (val is string && pi.enumType != null) strOpts = Enum.GetNames(pi.enumType); } catch { }

            if (val is bool bv)
            {
                var cellBg = Cell(lbl, x, y, w, RowH, () => Commit(ed, e2, p2, k, !bv), false, TextAnchor.MiddleLeft);
                cellBg.color = bv && !isDisabled
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.35f)
                    : new Color(1f, 1f, 1f, 0.05f);
                return y - (RowH + Gap);
            }
            if (val is DG.Tweening.Ease ez)
            {
                Label(lbl, x, y, w, 16f, lblCol); y -= 18f;
                RoundedRectGraphic bg = null;
                bg = Cell(LocEnum("Ease", ez.ToString()), x, y, w, RowH, () =>
                {
                    Vector2 pos = bg != null ? (Vector2)bg.transform.position : (Vector2)Input.mousePosition;
                    EditorEasePicker.Open(Loc.T("Ease"), ez, pos, nv => Commit(ed, e2, p2, k, nv));
                }, true);
                return y - (RowH + Gap);
            }
            if (val is Enum en)
            {
                Label(lbl, x, y, w, 16f, lblCol); y -= 18f;
                var cell = Cell(LocEnum(en.GetType().Name, en.ToString()), x, y, w, RowH,
                    () => Commit(ed, e2, p2, k, StepEnum(en, +1)), false);
                var ch = cell.gameObject.GetComponent<UI.ClickHandler>();
                if (ch != null) ch.OnRightClick = () => Commit(ed, e2, p2, k, StepEnum(en, -1));
                return y - (RowH + Gap);
            }
            if (strOpts != null && strOpts.Length > 0)
            {
                string sv = (string)val;
                string tn = null;
                try { tn = pi.enumType.Name; } catch { }
                int cur = Mathf.Max(0, Array.IndexOf(strOpts, sv));
                var opts = strOpts;
                Label(lbl, x, y, w, 16f, lblCol); y -= 18f;
                var cell = Cell(LocEnum(tn, sv), x, y, w, RowH,
                    () => Commit(ed, e2, p2, k, opts[(cur + 1) % opts.Length]), false);
                var ch = cell.gameObject.GetComponent<UI.ClickHandler>();
                if (ch != null) ch.OnRightClick = () => Commit(ed, e2, p2, k, opts[(cur - 1 + opts.Length) % opts.Length]);
                return y - (RowH + Gap);
            }
            if (val is Vector2 v2)
            {
                // NaN = the game's "keep current value" sentinel — shown/entered as empty
                Label(lbl, x, y, w, 16f, lblCol); y -= 18f;
                float fw = (w - Gap) * 0.5f;
                InputRow(x, y, fw, VecComp(v2.x), sv =>
                    Commit(ed, e2, p2, k, new Vector2(ParseComp(sv), ((Vector2)ValOf(e2, k, v2)).y)));
                InputRow(x + fw + Gap, y, fw, VecComp(v2.y), sv =>
                    Commit(ed, e2, p2, k, new Vector2(((Vector2)ValOf(e2, k, v2)).x, ParseComp(sv))));
                return y - (RowH + Gap);
            }
            if (val is Tuple<int, TileRelativeTo> tile)
            {
                Label(lbl, x, y, w, 16f, lblCol); y -= 18f;
                float fw = (w - Gap) * 0.45f;
                InputRow(x, y, fw, tile.Item1.ToString(), sv =>
                {
                    int n;
                    if (int.TryParse(sv, out n))
                    {
                        var cur2 = ValOf(e2, k, tile) as Tuple<int, TileRelativeTo>;
                        Commit(ed, e2, p2, k, Tuple.Create(n, cur2 != null ? cur2.Item2 : tile.Item2));
                    }
                });
                var cell = Cell(LocEnum("TileRelativeTo", tile.Item2.ToString()), x + fw + Gap, y, w - fw - Gap, RowH, () =>
                {
                    var cur2 = ValOf(e2, k, tile) as Tuple<int, TileRelativeTo> ?? tile;
                    var next = (TileRelativeTo)(((int)cur2.Item2 + 1) % 3);
                    Commit(ed, e2, p2, k, Tuple.Create(cur2.Item1, next));
                }, false);
                return y - (RowH + Gap);
            }

            // color-typed strings get a live swatch beside the hex field
            bool isColor = false;
            try { isColor = pi.controlType.ToString().IndexOf("Color", StringComparison.OrdinalIgnoreCase) >= 0; }
            catch { }
            Label(lbl, x, y, w, 16f, lblCol); y -= 18f;
            float inputW = w - (isColor ? RowH + Gap : 0f);
            InputRow(x, y, inputW, FormatVal(val), sv => CommitText(ed, e2, p2, k, sv, val));
            if (isColor)
            {
                var swGo = new GameObject("Sw", typeof(RectTransform));
                swGo.transform.SetParent(_content, false);
                var sr = (RectTransform)swGo.transform;
                sr.anchorMin = sr.anchorMax = new Vector2(0f, 1f);
                sr.pivot = new Vector2(0f, 1f);
                sr.anchoredPosition = new Vector2(x + inputW + Gap, y);
                sr.sizeDelta = new Vector2(RowH, RowH);
                var swBg = swGo.AddComponent<RoundedRectGraphic>();
                swBg.Radius = 5f;
                Color c;
                swBg.color = ColorUtility.TryParseHtmlString("#" + FormatVal(val).TrimStart('#'), out c)
                    ? c : Color.magenta;
                swBg.BorderWidth = 1f;
                swBg.BorderColor = new Color(1f, 1f, 1f, 0.2f);
            }
            return y - (RowH + Gap);
        }

        private static string VecComp(float v) => float.IsNaN(v) ? "" : v.ToString("0.###");

        private static float ParseComp(string sv)
        {
            float f;
            return float.TryParse((sv ?? "").Trim(), out f) ? f : float.NaN;
        }

        private static object ValOf(ADOFAI.LevelEvent evt, string key, object fallback)
        {
            try
            {
                var d = EditorEvents.EventData(evt);
                object v;
                if (d != null && d.TryGetValue(key, out v) && v != null) return v;
            }
            catch { }
            return fallback;
        }

        // ── commits (SaveStateScope, fidelity from the registry's affect flags) ──

        private static void Commit(scnEditor ed, ADOFAI.LevelEvent evt, ADOFAI.PropertyInfo pi, string key, object v)
        {
            try
            {
                using (new SaveStateScope(ed))
                    evt[key] = v;
                AfterCommit(ed, evt, pi);
            }
            catch (Exception ex) { SapphireLog.Log("EventPanel: edit failed: " + ex.Message); }
            _sig = 0; // toggles/enums redraw
        }

        private static void CommitText(scnEditor ed, ADOFAI.LevelEvent evt, ADOFAI.PropertyInfo pi,
            string key, string raw, object oldVal)
        {
            try
            {
                object v = CoerceLike(raw, oldVal);
                using (new SaveStateScope(ed))
                    evt[key] = v;
                AfterCommit(ed, evt, pi);
            }
            catch (Exception ex) { SapphireLog.Log("EventPanel: edit failed: " + ex.Message); }
        }

        private static object CoerceLike(string raw, object oldVal)
        {
            raw = (raw ?? "").Trim();
            try
            {
                if (oldVal is int) return (int)float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (oldVal is float) return float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (oldVal is double) return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (oldVal is long) return (long)float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch { return oldVal; }
            return raw;
        }

        private static void AfterCommit(scnEditor ed, ADOFAI.LevelEvent evt, ADOFAI.PropertyInfo pi)
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
            // keep the (hidden) game panel's state in step — other modules sync through it
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

        private static void Label(string text, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject("L", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, h);
            var t = UIBuilder.Tmp(go, text, 11.5f, TextAnchor.MiddleLeft, color);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
        }

        private static RoundedRectGraphic Cell(string text, float x, float y, float w, float h,
            Action onClick, bool button, TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("C", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = button ? new Color(1f, 1f, 1f, 0.08f) : new Color(1f, 1f, 1f, 0.05f);
            bg.raycastTarget = true;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(anchor == TextAnchor.MiddleLeft ? 8f : 0f, 0f);
            lr.offsetMax = Vector2.zero;
            var t = UIBuilder.Tmp(lGo, text, 12f, anchor, Theme.Text);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            UI.ClickHandler.Attach(go, onClick);
            return bg;
        }

        private static void InputRow(float x, float y, float w, string value, Action<string> commit)
        {
            var go = new GameObject("F", typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, RowH);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.08f);
            bg.raycastTarget = true;
            var txtGo = new GameObject("T", typeof(RectTransform));
            txtGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)txtGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(7f, 0f); tr.offsetMax = new Vector2(-7f, 0f);
            var txt = UIBuilder.Tmp(txtGo, value, 12f, TextAnchor.MiddleLeft, Theme.Text);
            txt.richText = false;
            var field = UIBuilder.BuildInputField(go, txt);
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = value;
            field.onEndEdit.AddListener(v => commit(v));
        }

        // ── shared helpers (same semantics as the filter manager) ────────────

        private static string LocEnum(string enumType, string value)
        {
            if (!string.IsNullOrEmpty(enumType))
                try
                {
                    bool ex;
                    var loc = RDString.GetWithCheck("enum." + enumType + "." + value, out ex, null);
                    if (ex && !string.IsNullOrEmpty(loc)) return loc;
                }
                catch { }
            return Loc.T(value);
        }

        private static object StepEnum(Enum cur, int dir)
        {
            var vals = Enum.GetValues(cur.GetType());
            int idx = Array.IndexOf(vals, cur);
            return vals.GetValue(((idx + dir) % vals.Length + vals.Length) % vals.Length);
        }

        private static string FormatVal(object v)
        {
            if (v == null) return "";
            if (v is string s) return s;
            if (v is bool b) return b ? "true" : "false";
            if (v is float f) return f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            if (v is double d) return d.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            if (v is Vector2 p) return p.x + ", " + p.y;
            return v.ToString();
        }
    }
}
