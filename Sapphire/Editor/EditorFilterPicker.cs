using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Filter browser for SetFilterAdvanced — the vanilla UI is one ~300-entry dropdown.
       A "Browse" chip rides the game's inspector panel whenever a SetFilterAdvanced event
       is open; it opens a browser with a live SEARCH field, a CATEGORY rail (parsed from
       the CameraFilterPack_<Category>_<Name> naming), and a scrollable grid. Clicking a
       filter applies it immediately (SaveStateScope = undo) and KEEPS the browser open so
       filters can be auditioned rapidly; the current one stays highlighted.

       The filter list is enumerated from the game assembly at runtime (every
       CameraFilterPack_* MonoBehaviour), so game updates that add filters need nothing. */
    internal static class EditorFilterPicker
    {
        private static GameObject _canvasGo;
        private static RectTransform _canvasRect;
        private static GameObject _browseBtnGo;
        private static GameObject _popupGo;
        private static RectTransform _gridContent;
        private static RectTransform _gridView;
        private static RectTransform _railContent;
        private static RectTransform _railView;
        private static float _railScroll;
        private static TMP_InputField _searchField;
        private static ADOFAI.LevelEvent _target;   // event the grid applies to (= expanded section)
        private static int _floor = -1;              // tile whose filter events are listed
        private static string _search = "";
        private static string _category = null; // null = All
        private static float _scroll;
        private static readonly List<RoundedRectGraphic> _catBgs = new List<RoundedRectGraphic>();
        private static readonly List<string> _catNames = new List<string>();

        private const float PanelW = 1240f, PanelH = 640f;
        private const float RailW = 190f, CellH = 58f, GridPad = 10f;
        private const int GridCols = 3;
        private static TextMeshProUGUI _paramInfo;
        private static readonly Dictionary<string, string> _paramCache = new Dictionary<string, string>();
        private static readonly Dictionary<string, List<string>> _paramKeysCache = new Dictionary<string, List<string>>();
        private static RectTransform _propView, _propContent;
        private static float _propScroll;
        private const float PropW = 260f;

        // (fullTypeName, category, displayName), sorted
        private static List<(string full, string cat, string name)> _filters;
        private static List<(string full, string cat, string name)> _legacyFilters;
        private static bool _legacyMode; // SetFilter (Filter enum) vs SetFilterAdvanced
        private static List<(string full, string cat, string name)> Active =>
            _legacyMode ? _legacyFilters : _filters;

        internal static bool IsOpen => _popupGo != null;

        internal static void Tick()
        {
            var ed = scnEditor.instance;
            bool inEditor = false;
            try { inEditor = ed != null && !ed.playMode && MainClass.EditorSuiteOn; }
            catch { }

            // Browse chip rides the panel while a SetFilterAdvanced event is open.
            ADOFAI.LevelEvent evt = null;
            try
            {
                if (inEditor && ed.levelEventsPanel != null
                    && ed.levelEventsPanel.showInspector                       // panel actually open —
                    && ed.levelEventsPanel.gameObject.activeInHierarchy       // selectedEvent lingers after close
                    && (ed.levelEventsPanel.selectedEventType == ADOFAI.LevelEventType.SetFilterAdvanced
                        || ed.levelEventsPanel.selectedEventType == ADOFAI.LevelEventType.SetFilter))
                    evt = ed.levelEventsPanel.selectedEvent;
            }
            catch { }
            // native inspector hides the game panel the chip rides on — and hosts its own
            // Filter manager button — so the chip stands down there
            try
            {
                var st = MainClass.Settings;
                if (evt != null && st != null && st.EditorNativeInspector && MainClass.EditorSuiteOn)
                    evt = null;
            }
            catch { }
            SyncBrowseButton(ed, evt);

            if (_popupGo == null) return;
            if (!inEditor || Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
            // resizing invalidates the grid's cell metrics (positions bake in the view width)
            var pr = (RectTransform)_popupGo.transform;
            if ((pr.sizeDelta - _lastPanelSize).sqrMagnitude > 1f)
            {
                _lastPanelSize = pr.sizeDelta;
                RebuildGrid();
            }
            if (_target == null || (ed != null && !ed.events.Contains(_target)))
            {
                var next = FirstFilterEventOnTile(ed);
                if (next == null) { Close(); return; }
                SetTarget(next);
            }

            // wheel scrolls whichever pane is hovered (grid or category rail)
            float wheel = MainClass.WheelY;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                if (_gridView != null
                    && RectTransformUtility.RectangleContainsScreenPoint(_gridView, Input.mousePosition, null))
                {
                    _scroll = Mathf.Clamp(_scroll + wheel * 60f, 0f, MaxScroll());
                    _gridContent.anchoredPosition = new Vector2(0f, _scroll);
                }
                else if (_railView != null
                    && RectTransformUtility.RectangleContainsScreenPoint(_railView, Input.mousePosition, null))
                {
                    float max = Mathf.Max(0f, _railContent.sizeDelta.y - _railView.rect.height);
                    _railScroll = Mathf.Clamp(_railScroll + wheel * 60f, 0f, max);
                    _railContent.anchoredPosition = new Vector2(0f, _railScroll);
                }
                else if (_propView != null
                    && RectTransformUtility.RectangleContainsScreenPoint(_propView, Input.mousePosition, null))
                {
                    float max = Mathf.Max(0f, _propContent.sizeDelta.y - _propView.rect.height);
                    _propScroll = Mathf.Clamp(_propScroll + wheel * 60f, 0f, max);
                    _propContent.anchoredPosition = new Vector2(0f, _propScroll);
                }
            }
        }

        internal static void Dispose()
        {
            Close();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _canvasRect = null; _browseBtnGo = null;
        }

        // ── filter census ────────────────────────────────────────────────────
        private static void EnsureFilters()
        {
            if (_filters != null) return;
            _filters = new List<(string, string, string)>();
            var seen = new HashSet<string>();
            try
            {
                /* Match the game's own dropdown EXACTLY: types from Assembly-CSharp-
                   firstpass ONLY (level load resolves filters via
                   Type.GetType("{0}, Assembly-CSharp-firstpass") — anything elsewhere can
                   never run), minus ffxSetFilterAdvancedPlus.blacklistedFilterKeywords. */
                string[] blacklist;
                try { blacklist = ffxSetFilterAdvancedPlus.blacklistedFilterKeywords; }
                catch { blacklist = new[] { "Blend2Camera_", "Antialiasing_FXAA", "Colors_Adjust_PreFilters" }; }
                var fpAsm = typeof(CameraFilterPack_AAA_SuperComputer).Assembly;
                {
                    Type[] types;
                    try { types = fpAsm.GetTypes(); } catch { types = new Type[0]; }
                    foreach (var t in types)
                    {
                        if (t == null || t.Name == null || !t.Name.StartsWith("CameraFilterPack_")) continue;
                        if (!typeof(MonoBehaviour).IsAssignableFrom(t)) continue;
                        bool banned = false;
                        foreach (var kw in blacklist)
                            if (t.Name.Contains(kw)) { banned = true; break; }
                        if (banned) continue;
                        if (!seen.Add(t.Name)) continue;
                        string rest = t.Name.Substring("CameraFilterPack_".Length);
                        string cat, name;
                        if (rest.StartsWith("NewGlitch"))
                        {
                            // NewGlitch1..7 are one-filter categories — fold them together
                            cat = "NewGlitch";
                            name = "Glitch " + rest.Substring("NewGlitch".Length);
                        }
                        else
                        {
                            int us = rest.IndexOf('_');
                            cat = us > 0 ? rest.Substring(0, us) : rest;
                            name = us > 0 ? rest.Substring(us + 1).Replace('_', ' ') : rest;
                        }
                        // the game's dropdown recipe: strip the prefix, look up
                        // editor.CameraFilterPack.<stripped>, else UnCamelCase it
                        try
                        {
                            bool exists;
                            var loc = RDString.GetWithCheck("editor.CameraFilterPack." + rest, out exists, null);
                            if (exists && !string.IsNullOrEmpty(loc)) name = loc;
                        }
                        catch { }
                        _filters.Add((t.Name, cat, name));
                    }
                }
            }
            catch (Exception ex) { SapphireLog.Log("FilterPicker: census failed: " + ex.Message); }
            int Rank(string cat) => cat == "NewGlitch" ? 1 : 0; // grab-bag sinks to the bottom
            _filters.Sort((a, b) =>
            {
                int c = Rank(a.cat).CompareTo(Rank(b.cat));
                if (c != 0) return c;
                c = string.CompareOrdinal(a.cat, b.cat);
                return c != 0 ? c : string.CompareOrdinal(a.name, b.name);
            });
            SapphireLog.Log("FilterPicker: " + _filters.Count + " advanced filters found");

            // legacy set: the SetFilter event's Filter enum
            _legacyFilters = new List<(string, string, string)>();
            try
            {
                var ft = typeof(scnEditor).Assembly.GetType("Filter");
                if (ft != null && ft.IsEnum)
                    foreach (var v in Enum.GetNames(ft))
                    {
                        // the game localizes these — reuse its strings
                        string disp = v;
                        try
                        {
                            var loc = RDString.Get("enum.Filter." + v);
                            if (!string.IsNullOrEmpty(loc) && !loc.Contains("enum.Filter")) disp = loc;
                        }
                        catch { }
                        _legacyFilters.Add((v, "Legacy", disp));
                    }
            }
            catch (Exception ex) { SapphireLog.Log("FilterPicker: legacy census failed: " + ex.Message); }
        }

        private static void SyncCatNames()
        {
            _catNames.Clear();
            foreach (var f in Active)
                if (!_catNames.Contains(f.cat)) _catNames.Add(f.cat);
        }

        // The adjustable parameters of a filter = its declared public fields — exactly
        // the filter_* keys SetFilterAdvanced's filterProperties accepts.
        private static List<string> ParamKeys(string fullTypeName)
        {
            if (_paramKeysCache.TryGetValue(fullTypeName, out var cached)) return cached;
            var keys = new List<string>();
            try
            {
                Type t = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try { t = asm.GetType(fullTypeName); } catch { }
                    if (t != null) break;
                }
                if (t != null)
                    foreach (var f in t.GetFields(System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
                    {
                        var ft = f.FieldType;
                        if (ft != typeof(float) && ft != typeof(int) && ft != typeof(bool)
                            && ft != typeof(Color) && ft != typeof(Vector2) && ft != typeof(Vector3)
                            && ft != typeof(Vector4) && ft != typeof(string)) continue;
                        keys.Add("filter_" + f.Name);
                    }
            }
            catch { }
            _paramKeysCache[fullTypeName] = keys;
            return keys;
        }

        private static string ParamsOf(string fullTypeName)
        {
            if (_paramCache.TryGetValue(fullTypeName, out var cached)) return cached;
            var keys = ParamKeys(fullTypeName);
            string result = keys.Count == 0 ? "" : string.Join(" · ", keys);
            _paramCache[fullTypeName] = result;
            return result;
        }

        // ── filterProperties string ⇄ ordered pairs (raw value text preserved) ──
        private static List<KeyValuePair<string, string>> ParseProps(string s)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            if (string.IsNullOrEmpty(s)) return pairs;
            int i = 0, n = s.Length;
            while (i < n)
            {
                int kq = s.IndexOf('"', i);
                if (kq < 0) break;
                int kq2 = s.IndexOf('"', kq + 1);
                if (kq2 < 0) break;
                string key = s.Substring(kq + 1, kq2 - kq - 1);
                int colon = s.IndexOf(':', kq2);
                if (colon < 0) break;
                int j = colon + 1, depth = 0;
                bool inStr = false;
                int vStart = j;
                for (; j < n; j++)
                {
                    char c = s[j];
                    if (inStr) { if (c == '"') inStr = false; continue; }
                    if (c == '"') inStr = true;
                    else if (c == '[' || c == '{') depth++;
                    else if (c == ']' || c == '}') depth--;
                    else if (c == ',' && depth == 0) break;
                }
                pairs.Add(new KeyValuePair<string, string>(key, s.Substring(vStart, j - vStart).Trim()));
                i = j + 1;
            }
            return pairs;
        }

        private static string SerializeProps(List<KeyValuePair<string, string>> pairs)
        {
            var parts = new List<string>();
            foreach (var kv in pairs) parts.Add("\"" + kv.Key + "\": " + kv.Value);
            return string.Join(", ", parts) + " ";
        }

        private static string CurrentFilter(ADOFAI.LevelEvent evt)
        {
            try
            {
                var d = EditorEvents.EventData(evt);
                if (d != null && d.TryGetValue("filter", out var v) && v != null) return v.ToString();
            }
            catch { }
            return "";
        }

        private static void Apply(string fullName)
        {
            var ed = scnEditor.instance;
            var evt = _target;
            if (ed == null || evt == null) return;
            try
            {
                object cur = null;
                var d = EditorEvents.EventData(evt);
                if (d != null) d.TryGetValue("filter", out cur);
                using (new SaveStateScope(ed))
                {
                    // SetFilterAdvanced stores the filter as a string; stay type-faithful
                    // if a future version switches to an enum.
                    if (cur != null && cur.GetType().IsEnum)
                        evt["filter"] = Enum.Parse(cur.GetType(), fullName);
                    else
                        evt["filter"] = fullName;
                }
                // refresh the open panel (same instance-index dance as elsewhere)
                int idx = 0;
                foreach (var e in ed.events)
                {
                    if (e == null || e.floor != evt.floor || e.eventType != evt.eventType) continue;
                    if (ReferenceEquals(e, evt)) break;
                    idx++;
                }
                ed.levelEventsPanel.ShowPanel(evt.eventType, idx);
                RebuildGrid(); // keep open: re-highlight, keep auditioning
                RebuildProps();
            }
            catch (Exception ex) { SapphireLog.Log("FilterPicker: apply failed: " + ex.Message); }
        }

        /* Canvas is shared by the browse button and the picker popup; keep it (and its
           raycaster) live only while one of them is actually shown. */
        private static void SyncCanvasActive()
        {
            if (_canvasGo == null) return;
            bool need = (_browseBtnGo != null && _browseBtnGo.activeSelf) || _popupGo != null;
            if (_canvasGo.activeSelf != need) _canvasGo.SetActive(need);
        }

        // ── browse chip riding the game panel ───────────────────────────────
        private static void SyncBrowseButton(scnEditor ed, ADOFAI.LevelEvent evt)
        {
            bool want = evt != null;
            if (!want)
            {
                if (_browseBtnGo != null && _browseBtnGo.activeSelf) _browseBtnGo.SetActive(false);
                SyncCanvasActive();
                return;
            }
            if (_canvasGo == null) BuildCanvas();
            else if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);
            if (_browseBtnGo == null) BuildBrowseButton();
            if (!_browseBtnGo.activeSelf) _browseBtnGo.SetActive(true);
            // park at the panel's top-left corner, just outside it
            try
            {
                var prt = (RectTransform)ed.levelEventsPanel.gameObject.transform;
                var corners = new Vector3[4];
                prt.GetWorldCorners(corners); // 1 = top-left
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, corners[1]);
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out local))
                {
                    // above the panel's top-left corner — the instance chips own the space
                    // hanging off its left edge
                    var br = (RectTransform)_browseBtnGo.transform;
                    br.pivot = new Vector2(1f, 0f);
                    var half = _canvasRect.rect.size * 0.5f;
                    float y = Mathf.Min(local.y + 6f, half.y - 36f);
                    br.anchoredPosition = new Vector2(local.x - 8f, y);
                }
            }
            catch { }
        }

        private static void BuildBrowseButton()
        {
            _browseBtnGo = new GameObject("FilterBrowseBtn", typeof(RectTransform));
            _browseBtnGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_browseBtnGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(1f, 1f);
            r.sizeDelta = new Vector2(120f, 30f);
            var bg = _browseBtnGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 7f;
            bg.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.16f);
            bg.raycastTarget = true;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(_browseBtnGo.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            var lt = UIBuilder.Tmp(lGo, Loc.T("Filters…"), 13f, TextAnchor.MiddleCenter, Theme.Text);
            lt.raycastTarget = false;
            UI.ClickHandler.Attach(_browseBtnGo, () =>
            {
                var ed = scnEditor.instance;
                ADOFAI.LevelEvent evt = null;
                try { evt = ed?.levelEventsPanel?.selectedEvent; } catch { }
                if (evt != null) Open(evt);
            });
        }

        // ── browser popup ────────────────────────────────────────────────────
        internal static void Open(ADOFAI.LevelEvent evt)
        {
            Close();
            EnsureFilters();
            _target = evt;
            _floor = evt != null ? evt.floor : -1;
            _legacyMode = evt != null && evt.eventType == ADOFAI.LevelEventType.SetFilter;
            SyncCatNames();
            _search = ""; _category = null; _scroll = 0f;
            if (_canvasGo == null) BuildCanvas();
            else if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

            // Floating window (user request July 16): no dim blocker — the editor stays live
            // behind it. Drag by the title row, resize from edges/corners; geometry persists
            // for the session.
            _popupGo = new GameObject("FilterPopup", typeof(RectTransform));
            _popupGo.transform.SetParent(_canvasGo.transform, false);
            var panelGo = _popupGo;
            var panel = (RectTransform)_popupGo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.pivot = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = _mgrPos;
            panel.sizeDelta = _mgrSize;
            _lastPanelSize = _mgrSize;
            var bg = panelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 12f;
            bg.color = new Color(0.06f, 0.06f, 0.08f, 0.98f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;

            // title + close
            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(panelGo.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.sizeDelta = new Vector2(0f, 24f);
            tr.anchoredPosition = new Vector2(0f, -10f);
            var title = UIBuilder.Tmp(titleGo,
                Loc.T(_legacyMode ? "Filter manager (legacy)" : "Filter manager") + " · #" + evt.floor,
                14.5f, TextAnchor.MiddleCenter, Theme.Text);
            title.raycastTarget = false;

            var xGo = new GameObject("X", typeof(RectTransform));
            xGo.transform.SetParent(panelGo.transform, false);
            var xr = (RectTransform)xGo.transform;
            xr.anchorMin = xr.anchorMax = new Vector2(1f, 1f);
            xr.pivot = new Vector2(1f, 1f);
            xr.anchoredPosition = new Vector2(-10f, -8f);
            xr.sizeDelta = new Vector2(26f, 26f);
            var xbg = xGo.AddComponent<RoundedRectGraphic>();
            xbg.Radius = 6f;
            xbg.color = new Color(1f, 1f, 1f, 0.07f);
            xbg.raycastTarget = true;
            var xlGo = new GameObject("L", typeof(RectTransform));
            xlGo.transform.SetParent(xGo.transform, false);
            var xlr = (RectTransform)xlGo.transform;
            xlr.anchorMin = Vector2.zero; xlr.anchorMax = Vector2.one;
            xlr.offsetMin = xlr.offsetMax = Vector2.zero;
            var xl = UIBuilder.Tmp(xlGo, "×", 16f, TextAnchor.MiddleCenter, Theme.TextMuted);
            xl.raycastTarget = false;
            UI.ClickHandler.Attach(xGo, Close);

            // search field
            var sGo = new GameObject("Search", typeof(RectTransform));
            sGo.transform.SetParent(panelGo.transform, false);
            var sr = (RectTransform)sGo.transform;
            sr.anchorMin = new Vector2(0f, 1f); sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.offsetMin = new Vector2(14f, -74f); sr.offsetMax = new Vector2(-14f, -42f);
            var sbg = sGo.AddComponent<RoundedRectGraphic>();
            sbg.Radius = 7f;
            sbg.color = new Color(1f, 1f, 1f, 0.07f);
            sbg.BorderWidth = 1f;
            sbg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            sbg.raycastTarget = true;
            var stGo = new GameObject("T", typeof(RectTransform));
            stGo.transform.SetParent(sGo.transform, false);
            var str2 = (RectTransform)stGo.transform;
            str2.anchorMin = Vector2.zero; str2.anchorMax = Vector2.one;
            str2.offsetMin = new Vector2(10f, 0f); str2.offsetMax = new Vector2(-10f, 0f);
            var stxt = UIBuilder.Tmp(stGo, "", 14f, TextAnchor.MiddleLeft, Theme.Text);
            stxt.richText = false;
            _searchField = UIBuilder.BuildInputField(sGo, stxt);
            _searchField.lineType = TMP_InputField.LineType.SingleLine;
            _searchField.onValueChanged.AddListener(sv => { _search = sv ?? ""; _scroll = 0f; RebuildGrid(); });
            var phGo = new GameObject("PH", typeof(RectTransform));
            phGo.transform.SetParent(sGo.transform, false);
            var phr = (RectTransform)phGo.transform;
            phr.anchorMin = Vector2.zero; phr.anchorMax = Vector2.one;
            phr.offsetMin = new Vector2(10f, 0f); phr.offsetMax = new Vector2(-10f, 0f);
            var ph = UIBuilder.Tmp(phGo, Loc.T("Search filters"), 14f, TextAnchor.MiddleLeft,
                new Color(1f, 1f, 1f, 0.28f));
            ph.raycastTarget = false;
            _searchField.placeholder = ph;

            // category rail: masked viewport (20+ categories overflowed the panel)
            var railGo = new GameObject("Rail", typeof(RectTransform));
            railGo.transform.SetParent(panelGo.transform, false);
            _railView = (RectTransform)railGo.transform;
            _railView.anchorMin = new Vector2(0f, 0f); _railView.anchorMax = new Vector2(0f, 1f);
            _railView.pivot = new Vector2(0f, 0.5f);
            _railView.offsetMin = new Vector2(14f, 14f); _railView.offsetMax = new Vector2(14f + RailW, -82f);
            railGo.AddComponent<RectMask2D>();
            var railImg = railGo.AddComponent<Image>();
            railImg.color = new Color(0f, 0f, 0f, 0.01f);
            railImg.raycastTarget = true; // wheel target
            var railContentGo = new GameObject("Content", typeof(RectTransform));
            railContentGo.transform.SetParent(railGo.transform, false);
            _railContent = (RectTransform)railContentGo.transform;
            _railContent.anchorMin = new Vector2(0f, 1f); _railContent.anchorMax = new Vector2(1f, 1f);
            _railContent.pivot = new Vector2(0.5f, 1f);
            _railContent.anchoredPosition = Vector2.zero;
            _railScroll = 0f;
            BuildRail(_railContent);
            _railContent.sizeDelta = new Vector2(0f, (1 + _catNames.Count) * 30f);

            // grid viewport (masked) + content
            var viewGo = new GameObject("GridView", typeof(RectTransform));
            viewGo.transform.SetParent(panelGo.transform, false);
            _gridView = (RectTransform)viewGo.transform;
            _gridView.anchorMin = new Vector2(0f, 0f); _gridView.anchorMax = new Vector2(1f, 1f);
            _gridView.pivot = new Vector2(0.5f, 0.5f);
            _gridView.offsetMin = new Vector2(14f + RailW + 12f, 44f);
            _gridView.offsetMax = new Vector2(-14f - PropW - 12f, -82f);
            var mask = viewGo.AddComponent<RectMask2D>();
            var vImg = viewGo.AddComponent<Image>();
            vImg.color = new Color(0f, 0f, 0f, 0.01f);
            vImg.raycastTarget = true; // wheel target

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewGo.transform, false);
            _gridContent = (RectTransform)contentGo.transform;
            _gridContent.anchorMin = new Vector2(0f, 1f); _gridContent.anchorMax = new Vector2(1f, 1f);
            _gridContent.pivot = new Vector2(0.5f, 1f);
            _gridContent.anchoredPosition = Vector2.zero;

            // bottom info bar: the hovered filter's adjustable parameters
            var infoGo = new GameObject("ParamInfo", typeof(RectTransform));
            infoGo.transform.SetParent(panelGo.transform, false);
            var ir = (RectTransform)infoGo.transform;
            ir.anchorMin = new Vector2(0f, 0f); ir.anchorMax = new Vector2(1f, 0f);
            ir.pivot = new Vector2(0.5f, 0f);
            ir.offsetMin = new Vector2(14f + RailW + 12f, 10f);
            ir.offsetMax = new Vector2(-14f, 38f);
            _paramInfo = UIBuilder.Tmp(infoGo, "", 12f, TextAnchor.MiddleLeft, Theme.TextMuted);
            _paramInfo.textWrappingMode = TextWrappingModes.NoWrap;
            _paramInfo.overflowMode = TextOverflowModes.Ellipsis;
            _paramInfo.raycastTarget = false;

            // properties column: edit the ACTIVE filter's parameter values + delete
            var propGo = new GameObject("Props", typeof(RectTransform));
            propGo.transform.SetParent(panelGo.transform, false);
            _propView = (RectTransform)propGo.transform;
            _propView.anchorMin = new Vector2(1f, 0f); _propView.anchorMax = new Vector2(1f, 1f);
            _propView.pivot = new Vector2(1f, 0.5f);
            _propView.offsetMin = new Vector2(-14f - PropW, 14f);
            _propView.offsetMax = new Vector2(-14f, -82f);
            var pbg = propGo.AddComponent<RoundedRectGraphic>();
            pbg.Radius = 8f;
            pbg.color = new Color(1f, 1f, 1f, 0.03f);
            pbg.raycastTarget = true; // wheel target
            propGo.AddComponent<RectMask2D>();
            var propContentGo = new GameObject("Content", typeof(RectTransform));
            propContentGo.transform.SetParent(propGo.transform, false);
            _propContent = (RectTransform)propContentGo.transform;
            _propContent.anchorMin = new Vector2(0f, 1f); _propContent.anchorMax = new Vector2(1f, 1f);
            _propContent.pivot = new Vector2(0.5f, 1f);
            _propContent.anchoredPosition = Vector2.zero;
            _propScroll = 0f;

            // drag strip over the title row (added last so it wins the raycast) + resize
            var dragGo = new GameObject("Drag", typeof(RectTransform));
            dragGo.transform.SetParent(panelGo.transform, false);
            var dr = (RectTransform)dragGo.transform;
            dr.anchorMin = new Vector2(0f, 1f); dr.anchorMax = new Vector2(1f, 1f);
            dr.pivot = new Vector2(0.5f, 1f);
            dr.anchoredPosition = Vector2.zero;
            dr.sizeDelta = new Vector2(-80f, 38f); // leaves the × corner clickable
            var dImg = dragGo.AddComponent<Image>();
            dImg.color = new Color(0f, 0f, 0f, 0.01f);
            dImg.raycastTarget = true;
            dragGo.AddComponent<DragHandle>();
            ResizeHandle.AttachAll(panel, true);

            RebuildGrid();
            RebuildProps();
        }

        // floating-window geometry (session-scoped) + resize watch
        private static Vector2 _mgrPos = Vector2.zero;
        private static Vector2 _mgrSize = new Vector2(PanelW, PanelH);
        private static Vector2 _lastPanelSize;

        // ── tile filter sections ─────────────────────────────────────────────
        private static List<ADOFAI.LevelEvent> TileFilterEvents(scnEditor ed)
        {
            var list = new List<ADOFAI.LevelEvent>();
            if (ed == null) return list;
            foreach (var e in ed.events)
            {
                if (e == null || e.floor != _floor) continue;
                if (e.eventType == ADOFAI.LevelEventType.SetFilterAdvanced
                    || e.eventType == ADOFAI.LevelEventType.SetFilter) list.Add(e);
            }
            return list;
        }

        private static ADOFAI.LevelEvent FirstFilterEventOnTile(scnEditor ed)
        {
            var list = TileFilterEvents(ed);
            return list.Count > 0 ? list[0] : null;
        }

        // Expanding a section retargets the grid too (its list follows the event's type).
        private static void SetTarget(ADOFAI.LevelEvent evt)
        {
            _target = evt;
            bool legacy = evt != null && evt.eventType == ADOFAI.LevelEventType.SetFilter;
            if (legacy != _legacyMode)
            {
                _legacyMode = legacy;
                _category = null; _scroll = 0f;
                SyncCatNames();
                // rail categories changed → rebuild it
                if (_railContent != null)
                {
                    for (int i = _railContent.childCount - 1; i >= 0; i--)
                        UnityEngine.Object.Destroy(_railContent.GetChild(i).gameObject);
                    BuildRail(_railContent);
                    _railContent.sizeDelta = new Vector2(0f, (1 + _catNames.Count) * 30f);
                    _railScroll = 0f;
                    _railContent.anchoredPosition = Vector2.zero;
                }
            }
            RebuildGrid();
            RebuildProps();
        }

        /* The column lists EVERY filter event on the tile as an expandable section.
           The expanded one is the grid's target; its parameters edit inline (empty
           field = not overridden) and each section deletes independently — all through
           SaveStateScope, so undo reverts each step. */
        private static void RebuildProps()
        {
            if (_propContent == null) return;
            var ed = scnEditor.instance;
            if (ed == null) return;
            for (int i = _propContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_propContent.GetChild(i).gameObject);

            float y = -6f;
            var events = TileFilterEvents(ed);
            if (events.Count == 0)
            {
                MakePropLabel(Loc.T("(no filter events on this tile)"), y, 12f, Theme.TextMuted);
                y -= 26f;
            }
            foreach (var evt in events)
            {
                var e = evt;
                bool expanded = ReferenceEquals(e, _target);
                bool legacy = e.eventType == ADOFAI.LevelEventType.SetFilter;
                string cur = CurrentFilter(e);
                string disp = cur;
                foreach (var f in legacy ? _legacyFilters : _filters)
                    if (f.full == cur) { disp = f.name; break; }
                if (string.IsNullOrEmpty(disp)) disp = "—";

                // section header: chevron + name (+ legacy tag), click toggles
                MakeSectionHeader((expanded ? "− " : "+ ") + disp + (legacy ? "  ·  SetFilter" : ""),
                    y, expanded, () => SetTarget(e));
                y -= 30f;
                if (!expanded) continue;

                // event-level settings (enable, duration, ease, tags, …) — the manager
                // should cover everything so the game's inspector isn't needed
                y = BuildEventRows(e, y);

                if (!legacy && cur.Length > 0)
                {
                    var pairs = PropsPairsOf(e);
                    var keys = new List<string>(ParamKeys(cur));
                    foreach (var kv in pairs) if (!keys.Contains(kv.Key)) keys.Add(kv.Key);
                    foreach (var key in keys)
                    {
                        string val = "";
                        foreach (var kv in pairs) if (kv.Key == key) { val = kv.Value; break; }
                        string k = key;
                        string lbl = key.StartsWith("filter_") ? key.Substring(7) : key;
                        try
                        {
                            bool ex2;
                            var loc = RDString.GetWithCheck("editor.CameraFilterPackFields." + lbl, out ex2, null);
                            if (ex2 && !string.IsNullOrEmpty(loc)) lbl = loc;
                        }
                        catch { }
                        MakePropLabel(lbl, y, 12f, Theme.TextMuted);
                        y -= 18f;
                        MakePropField(val, y, sv => CommitProp(k, sv));
                        y -= 32f;
                    }
                }
                MakePropButton(Loc.T("Delete event"), y, () => DeleteEvent(e));
                y -= 40f;
            }
            _propContent.sizeDelta = new Vector2(0f, -y + 8f);
            _propScroll = Mathf.Clamp(_propScroll, 0f, Mathf.Max(0f, _propContent.sizeDelta.y - _propView.rect.height));
            _propContent.anchoredPosition = new Vector2(0f, _propScroll);
        }

        private static void MakeSectionHeader(string text, float y, bool active, Action onClick)
        {
            var go = new GameObject("SH", typeof(RectTransform));
            go.transform.SetParent(_propContent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(-12f, 26f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            var a = Theme.Accent;
            bg.color = active ? new Color(a.r, a.g, a.b, 0.4f) : new Color(1f, 1f, 1f, 0.06f);
            bg.raycastTarget = true;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(8f, 0f); lr.offsetMax = new Vector2(-6f, 0f);
            var lt = UIBuilder.Tmp(lGo, text, 12.5f, TextAnchor.MiddleLeft,
                active ? Theme.Text : Theme.TextMuted);
            lt.textWrappingMode = TextWrappingModes.NoWrap;
            lt.overflowMode = TextOverflowModes.Ellipsis;
            lt.raycastTarget = false;
            UI.ClickHandler.Attach(go, onClick);
        }

        /* The decoder FLATTENS filterProperties into the event data as individual
           TYPED filter_* keys (data["filter_Size"] = 100f, data["filter_Speed"] = 0, …);
           data["filterProperties"] itself stays null. Read those keys directly; commits
           write typed values back (coerced to the existing value's type, or the filter
           field's reflected type when the key is new), and clearing a field REMOVES the
           key — an absent key means "not overridden", exactly like the level format. */
        private static string FormatVal(object v)
        {
            if (v == null) return "";
            if (v is string s) return s;
            if (v is bool b) return b ? "true" : "false";
            if (v is float f) return f.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            if (v is double db) return db.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
            if (v is Vector2 p) return p.x + ", " + p.y;
            return v.ToString();
        }

        private static object CoerceTo(string raw, Type target)
        {
            raw = raw.Trim();
            try
            {
                if (target == typeof(bool)) return raw == "true" || raw == "1";
                if (target == typeof(int)) return (int)float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (target == typeof(float)) return float.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (target == typeof(double)) return double.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);
                if (target == typeof(Vector2))
                {
                    var parts = raw.Trim('[', ']').Split(',');
                    return new Vector2(
                        float.Parse(parts[0].Trim(), System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[1].Trim(), System.Globalization.CultureInfo.InvariantCulture));
                }
            }
            catch { }
            return raw; // strings (hex colors etc.) pass through
        }

        private static Type FieldTypeOf(string filterFull, string paramKey)
        {
            try
            {
                string fieldName = paramKey.StartsWith("filter_") ? paramKey.Substring(7) : paramKey;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type t = null;
                    try { t = asm.GetType(filterFull); } catch { }
                    if (t == null) continue;
                    var fi = t.GetField(fieldName, System.Reflection.BindingFlags.Public
                        | System.Reflection.BindingFlags.Instance);
                    if (fi != null) return fi.FieldType;
                    break;
                }
            }
            catch { }
            return typeof(float);
        }

        private static List<KeyValuePair<string, string>> PropsPairsOf(ADOFAI.LevelEvent evt)
        {
            var pairs = new List<KeyValuePair<string, string>>();
            var d = EditorEvents.EventData(evt);
            if (d == null) return pairs;
            foreach (var kv in d)
            {
                if (!kv.Key.StartsWith("filter_") || kv.Value == null) continue;
                pairs.Add(new KeyValuePair<string, string>(kv.Key, FormatVal(kv.Value)));
            }
            return pairs;
        }

        private static void CommitProp(string key, string raw)
        {
            var ed = scnEditor.instance;
            var evt = _target;
            if (ed == null || evt == null) return;
            try
            {
                var d = EditorEvents.EventData(evt);
                using (new SaveStateScope(ed))
                {
                    if (string.IsNullOrWhiteSpace(raw))
                    {
                        if (d != null && d.ContainsKey(key)) d.Remove(key); // absent = not overridden
                    }
                    else
                    {
                        Type target = d != null && d.TryGetValue(key, out var curV) && curV != null
                            ? curV.GetType()
                            : FieldTypeOf(CurrentFilter(evt), key);
                        evt[key] = CoerceTo(raw, target);
                    }
                }
                int idx = 0;
                foreach (var e in ed.events)
                {
                    if (e == null || e.floor != evt.floor || e.eventType != evt.eventType) continue;
                    if (ReferenceEquals(e, evt)) break;
                    idx++;
                }
                ed.levelEventsPanel.ShowPanel(evt.eventType, idx);
            }
            catch (Exception ex) { SapphireLog.Log("FilterPicker: prop edit failed: " + ex.Message); }
        }

        /* Generic event-level rows: every key of a fresh default event (∪ the live one),
           minus the filter choice (that's the grid) and the flattened filter_* params.
           Bools toggle, enums cycle (right-click backwards), Ease opens the shared curve
           grid, everything else edits as text coerced to the default value's type. Being
           key-driven keeps the manager complete across game updates. */
        private static float BuildEventRows(ADOFAI.LevelEvent evt, float y)
        {
            Dictionary<string, object> defaults = null;
            try { defaults = EditorEvents.EventData(new ADOFAI.LevelEvent(evt.floor, evt.eventType)); } catch { }
            var live = EditorEvents.EventData(evt);
            var keys = new List<string>();
            if (defaults != null) foreach (var k2 in defaults.Keys) keys.Add(k2);
            if (live != null) foreach (var k2 in live.Keys) if (!keys.Contains(k2)) keys.Add(k2);

            // the game's own property registry: exact localization keys, plus which
            // properties are informational (Note — the warning banner) or invisible
            ADOFAI.LevelEventInfo info = null;
            try { GCS.levelEventsInfo.TryGetValue(evt.eventType.ToString(), out info); } catch { }

            foreach (var key in keys)
            {
                if (key == "filter" || key == "filterProperties" || key.StartsWith("filter_")) continue;

                ADOFAI.PropertyInfo pi = null;
                try { if (info != null) info.propertiesInfo.TryGetValue(key, out pi); } catch { }
                if (pi != null)
                {
                    try { if (pi.controlType.ToString() == "Note") continue; } catch { }
                    try { if (pi.invisible) continue; } catch { }
                }

                object val = null;
                if (live != null && live.TryGetValue(key, out var lv) && lv != null) val = lv;
                if (val == null && defaults != null) defaults.TryGetValue(key, out val);
                // valueless keys are informational banners (the "warning" Note) — nothing to edit
                if (val == null) continue;

                string lbl = key;
                try
                {
                    string lockey = null;
                    try { if (pi != null) lockey = pi.customLocalizationKey; } catch { }
                    if (string.IsNullOrEmpty(lockey)) lockey = "editor." + key;
                    bool ex2;
                    var loc = RDString.GetWithCheck(lockey, out ex2, null);
                    lbl = ex2 && !string.IsNullOrEmpty(loc) ? loc : Loc.T(key); // our table last
                }
                catch { }

                string k = key;
                var e2 = evt;
                string[] strOpts = null; // enum-typed STRING properties (targetType, plane…) cycle
                try
                {
                    if (val is string && pi != null && pi.enumType != null)
                        strOpts = Enum.GetNames(pi.enumType);
                }
                catch { }
                if (val is bool bv)
                {
                    MakePropToggle(lbl, bv, y, v => CommitEventValue(e2, k, v));
                    y -= 32f;
                }
                else if (val is DG.Tweening.Ease ez)
                {
                    MakePropLabel(lbl, y, 12f, Theme.TextMuted);
                    y -= 18f;
                    MakePropCell(LocEnum("Ease", ez.ToString()), y, go =>
                        EditorEasePicker.Open(Loc.T("Ease"), ez, (Vector2)go.transform.position,
                            nv => CommitEventValue(e2, k, nv)), null);
                    y -= 32f;
                }
                else if (val is Enum en)
                {
                    MakePropLabel(lbl, y, 12f, Theme.TextMuted);
                    y -= 18f;
                    MakePropCell(LocEnum(en.GetType().Name, en.ToString()), y,
                        _ => CommitEventValue(e2, k, StepEnum(en, +1)),
                        () => CommitEventValue(e2, k, StepEnum(en, -1)));
                    y -= 32f;
                }
                else if (strOpts != null && strOpts.Length > 0)
                {
                    string sv2 = (string)val;
                    string typeName = null;
                    try { typeName = pi.enumType.Name; } catch { }
                    int cur2 = Mathf.Max(0, Array.IndexOf(strOpts, sv2));
                    var opts = strOpts;
                    MakePropLabel(lbl, y, 12f, Theme.TextMuted);
                    y -= 18f;
                    MakePropCell(LocEnum(typeName, sv2), y,
                        _ => CommitEventValue(e2, k, opts[(cur2 + 1) % opts.Length]),
                        () => CommitEventValue(e2, k, opts[(cur2 - 1 + opts.Length) % opts.Length]));
                    y -= 32f;
                }
                else
                {
                    MakePropLabel(lbl, y, 12f, Theme.TextMuted);
                    y -= 18f;
                    object defVal = null;
                    if (defaults != null) defaults.TryGetValue(k, out defVal);
                    var defType = (val ?? defVal) != null ? (val ?? defVal).GetType() : typeof(string);
                    MakePropField(FormatVal(val), y, sv => CommitEventText(e2, k, sv, defType, defVal));
                    y -= 32f;
                }
            }
            y -= 4f;
            return y;
        }

        // enum values shown the way the game shows them in dropdowns; raw name when unmapped
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
            return Loc.T(value); // our table covers the game's unmapped enum names
        }

        private static object StepEnum(Enum cur, int dir)
        {
            var vals = Enum.GetValues(cur.GetType());
            int idx = Array.IndexOf(vals, cur);
            return vals.GetValue(((idx + dir) % vals.Length + vals.Length) % vals.Length);
        }

        private static void CommitEventValue(ADOFAI.LevelEvent evt, string key, object v)
        {
            var ed = scnEditor.instance;
            if (ed == null || evt == null) return;
            try
            {
                using (new SaveStateScope(ed))
                    evt[key] = v;
                RefreshGamePanel(ed, evt);
            }
            catch (Exception ex) { SapphireLog.Log("FilterPicker: event edit failed: " + ex.Message); }
            RebuildProps(); // toggles/enums show state — redraw
        }

        private static void CommitEventText(ADOFAI.LevelEvent evt, string key, string raw, Type defType, object defVal)
        {
            var ed = scnEditor.instance;
            if (ed == null || evt == null) return;
            try
            {
                using (new SaveStateScope(ed))
                {
                    if (string.IsNullOrWhiteSpace(raw) && defVal != null)
                        evt[key] = defVal;                  // empty = back to the default
                    else
                        evt[key] = CoerceTo(raw, defType);
                }
                RefreshGamePanel(ed, evt);
            }
            catch (Exception ex) { SapphireLog.Log("FilterPicker: event edit failed: " + ex.Message); }
        }

        private static void RefreshGamePanel(scnEditor ed, ADOFAI.LevelEvent evt)
        {
            int idx = 0;
            foreach (var e in ed.events)
            {
                if (e == null || e.floor != evt.floor || e.eventType != evt.eventType) continue;
                if (ReferenceEquals(e, evt)) break;
                idx++;
            }
            try { ed.levelEventsPanel.ShowPanel(evt.eventType, idx); } catch { }
        }

        // [label ......... On/Off] — full-width toggle cell, accent-tinted while on
        private static void MakePropToggle(string label, bool on, float y, Action<bool> commit)
        {
            var go = new GameObject("PT", typeof(RectTransform));
            go.transform.SetParent(_propContent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(-20f, 26f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            var a = Theme.Accent;
            bg.color = on ? new Color(a.r, a.g, a.b, 0.35f) : new Color(1f, 1f, 1f, 0.06f);
            bg.raycastTarget = true;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(8f, 0f); lr.offsetMax = new Vector2(-8f, 0f);
            var lt = UIBuilder.Tmp(lGo, label, 12f, TextAnchor.MiddleLeft, Theme.Text);
            lt.textWrappingMode = TextWrappingModes.NoWrap;
            lt.overflowMode = TextOverflowModes.Ellipsis;
            lt.raycastTarget = false;
            var vGo = new GameObject("V", typeof(RectTransform));
            vGo.transform.SetParent(go.transform, false);
            var vr = (RectTransform)vGo.transform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(8f, 0f); vr.offsetMax = new Vector2(-8f, 0f);
            var vt = UIBuilder.Tmp(vGo, Loc.T(on ? "On" : "Off"), 12f, TextAnchor.MiddleRight,
                on ? Theme.Text : Theme.TextMuted);
            vt.raycastTarget = false;
            UI.ClickHandler.Attach(go, () => commit(!on));
        }

        // value cell: left-click = primary action (cycle/open picker), right-click optional
        private static void MakePropCell(string text, float y, Action<GameObject> onClick, Action onRight)
        {
            var go = new GameObject("PC", typeof(RectTransform));
            go.transform.SetParent(_propContent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(-20f, 26f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.07f);
            bg.raycastTarget = true;
            var tGo = new GameObject("T", typeof(RectTransform));
            tGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)tGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(7f, 0f); tr.offsetMax = new Vector2(-7f, 0f);
            var txt = UIBuilder.Tmp(tGo, text, 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            txt.textWrappingMode = TextWrappingModes.NoWrap;
            txt.overflowMode = TextOverflowModes.Ellipsis;
            txt.raycastTarget = false;
            var ch = UI.ClickHandler.Attach(go, () => onClick(go));
            if (onRight != null) ch.OnRightClick = onRight;
        }

        private static void DeleteEvent(ADOFAI.LevelEvent evt)
        {
            var ed = scnEditor.instance;
            if (ed == null || evt == null) return;
            try
            {
                var type = evt.eventType;
                using (new SaveStateScope(ed))
                {
                    ed.events.Remove(evt);
                    ed.ApplyEventsToFloors();
                }
                int remaining = 0;
                foreach (var e in ed.events)
                    if (e != null && e.floor == _floor && e.eventType == type) remaining++;
                if (remaining > 0) ed.levelEventsPanel.ShowPanel(type, 0);
                else try { ed.levelEventsPanel.ShowInspector(false, true); } catch { }
            }
            catch (Exception ex) { SapphireLog.Log("FilterPicker: delete failed: " + ex.Message); }
            // stay open — retarget to another section if the active one just died
            if (ReferenceEquals(evt, _target))
            {
                var next = FirstFilterEventOnTile(ed);
                if (next != null) { SetTarget(next); return; }
                Close();
                return;
            }
            RebuildProps();
        }

        private static void MakePropLabel(string text, float y, float size, Color col)
        {
            var go = new GameObject("PL", typeof(RectTransform));
            go.transform.SetParent(_propContent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(-20f, 18f);
            var t = UIBuilder.Tmp(go, text, size, TextAnchor.MiddleLeft, col);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
        }

        private static void MakePropField(string value, float y, Action<string> commit)
        {
            var go = new GameObject("PF", typeof(RectTransform));
            go.transform.SetParent(_propContent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(-20f, 26f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = new Color(1f, 1f, 1f, 0.07f);
            bg.raycastTarget = true;
            var tGo = new GameObject("T", typeof(RectTransform));
            tGo.transform.SetParent(go.transform, false);
            var tr = (RectTransform)tGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(7f, 0f); tr.offsetMax = new Vector2(-7f, 0f);
            var txt = UIBuilder.Tmp(tGo, value, 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            txt.richText = false;
            var field = UIBuilder.BuildInputField(go, txt);
            field.lineType = TMP_InputField.LineType.SingleLine;
            field.text = value;
            field.onEndEdit.AddListener(sv => commit(sv));
        }

        private static void MakePropButton(string label, float y, Action onClick)
        {
            var go = new GameObject("PB", typeof(RectTransform));
            go.transform.SetParent(_propContent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(-20f, 30f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 7f;
            bg.color = new Color(0.85f, 0.28f, 0.3f, 0.35f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 0.45f, 0.45f, 0.5f);
            bg.raycastTarget = true;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = lr.offsetMax = Vector2.zero;
            var lt = UIBuilder.Tmp(lGo, label, 13f, TextAnchor.MiddleCenter, Theme.Text);
            lt.raycastTarget = false;
            UI.ClickHandler.Attach(go, onClick);
        }

        private static void BuildRail(Transform rail)
        {
            _catBgs.Clear();
            float y = 0f;
            _catBgs.Add(MakeRailRow(rail, Loc.T("All"), null, y)); y -= 30f;
            foreach (var cat in _catNames)
            {
                _catBgs.Add(MakeRailRow(rail, cat, cat, y));
                y -= 30f;
            }
            SyncRail();
        }

        private static RoundedRectGraphic MakeRailRow(Transform rail, string label, string cat, float y)
        {
            var go = new GameObject("Cat_" + label, typeof(RectTransform));
            go.transform.SetParent(rail, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(0f, 27f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 6f;
            bg.color = new Color(1f, 1f, 1f, 0.04f);
            bg.raycastTarget = true;
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(10f, 0f); lr.offsetMax = new Vector2(-6f, 0f);
            var lt = UIBuilder.Tmp(lGo, label, 13f, TextAnchor.MiddleLeft, Theme.Text);
            lt.textWrappingMode = TextWrappingModes.NoWrap;
            lt.overflowMode = TextOverflowModes.Ellipsis;
            lt.raycastTarget = false;
            UI.ClickHandler.Attach(go, () => { _category = cat; _scroll = 0f; SyncRail(); RebuildGrid(); });
            return bg;
        }

        private static void SyncRail()
        {
            for (int i = 0; i < _catBgs.Count; i++)
            {
                if (_catBgs[i] == null) continue;
                bool sel = i == 0 ? _category == null : (i - 1 < _catNames.Count && _catNames[i - 1] == _category);
                var a = Theme.Accent;
                _catBgs[i].color = sel ? new Color(a.r, a.g, a.b, 0.45f) : new Color(1f, 1f, 1f, 0.04f);
            }
        }

        private static float MaxScroll()
        {
            if (_gridContent == null || _gridView == null) return 0f;
            return Mathf.Max(0f, _gridContent.sizeDelta.y - _gridView.rect.height);
        }

        private static void RebuildGrid()
        {
            if (_gridContent == null || _target == null) return;
            for (int i = _gridContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_gridContent.GetChild(i).gameObject);

            string cur = CurrentFilter(_target);
            string q = (_search ?? "").Trim().ToLowerInvariant();
            float cellW = (_gridView.rect.width - GridPad * (GridCols - 1)) / GridCols;
            int made = 0;
            foreach (var f in Active)
            {
                if (_category != null && f.cat != _category) continue;
                if (q.Length > 0 && !f.full.ToLowerInvariant().Contains(q)
                    && !f.name.ToLowerInvariant().Contains(q)) continue;
                int col = made % GridCols, row = made / GridCols;
                MakeFilterCell(f, f.full == cur, col * (cellW + GridPad), -row * (CellH + 6f), cellW);
                made++;
            }
            int rows = (made + GridCols - 1) / GridCols;
            _gridContent.sizeDelta = new Vector2(0f, rows * (CellH + 6f));
            _scroll = Mathf.Clamp(_scroll, 0f, MaxScroll());
            _gridContent.anchoredPosition = new Vector2(0f, _scroll);
        }

        private static void MakeFilterCell((string full, string cat, string name) f, bool current,
            float x, float y, float w)
        {
            var go = new GameObject("F_" + f.name, typeof(RectTransform));
            go.transform.SetParent(_gridContent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
            r.pivot = new Vector2(0f, 1f);
            r.anchoredPosition = new Vector2(x, y);
            r.sizeDelta = new Vector2(w, CellH);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 8f;
            var a = Theme.Accent;
            bg.color = current ? new Color(a.r, a.g, a.b, 0.45f) : new Color(1f, 1f, 1f, 0.05f);
            bg.BorderWidth = 1f;
            bg.BorderColor = current ? new Color(a.r, a.g, a.b, 0.9f) : new Color(1f, 1f, 1f, 0.08f);
            bg.raycastTarget = true;

            var nGo = new GameObject("N", typeof(RectTransform));
            nGo.transform.SetParent(go.transform, false);
            var nr = (RectTransform)nGo.transform;
            nr.anchorMin = new Vector2(0f, 0.5f); nr.anchorMax = new Vector2(1f, 1f);
            nr.offsetMin = new Vector2(12f, 0f); nr.offsetMax = new Vector2(-8f, -4f);
            var nt = UIBuilder.Tmp(nGo, f.name, 14.5f, TextAnchor.LowerLeft,
                current ? Theme.Text : new Color(0.92f, 0.92f, 0.94f));
            nt.textWrappingMode = TextWrappingModes.NoWrap;
            nt.overflowMode = TextOverflowModes.Ellipsis;
            nt.raycastTarget = false;

            var cGo = new GameObject("C", typeof(RectTransform));
            cGo.transform.SetParent(go.transform, false);
            var cr = (RectTransform)cGo.transform;
            cr.anchorMin = new Vector2(0f, 0f); cr.anchorMax = new Vector2(1f, 0.5f);
            cr.offsetMin = new Vector2(12f, 5f); cr.offsetMax = new Vector2(-8f, 0f);
            var ct = UIBuilder.Tmp(cGo, f.cat, 11.5f, TextAnchor.UpperLeft, Theme.TextMuted);
            ct.textWrappingMode = TextWrappingModes.NoWrap;
            ct.overflowMode = TextOverflowModes.Ellipsis;
            ct.raycastTarget = false;

            var hover = go.AddComponent<HoverHandler>();
            hover.OnEnter = () =>
            {
                if (_paramInfo != null)
                {
                    var p = _legacyMode ? "intensity" : ParamsOf(f.full);
                    _paramInfo.text = p.Length > 0 ? f.name + ":  " + p : f.name;
                }
                if (!current) bg.color = new Color(1f, 1f, 1f, 0.11f);
            };
            hover.OnExit = () =>
            {
                if (_paramInfo != null) _paramInfo.text = "";
                if (!current) bg.color = new Color(1f, 1f, 1f, 0.05f);
            };
            UI.ClickHandler.Attach(go, () => Apply(f.full));
        }

        private static void Close()
        {
            if (_popupGo != null)
            {
                try
                {
                    var r = (RectTransform)_popupGo.transform;
                    _mgrPos = r.anchoredPosition;
                    _mgrSize = r.sizeDelta;
                }
                catch { }
                UnityEngine.Object.Destroy(_popupGo);
            }
            _popupGo = null; _target = null; _gridContent = null; _gridView = null;
            _railContent = null; _railView = null; _paramInfo = null;
            _propView = null; _propContent = null;
            _searchField = null; _catBgs.Clear();
            SyncCanvasActive(); // idle the canvas unless the browse button is still up
        }

        private static void BuildCanvas()
        {
            _canvasGo = new GameObject("SapphireFilterPicker", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 945;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRect = (RectTransform)_canvasGo.transform;
        }
    }
}
