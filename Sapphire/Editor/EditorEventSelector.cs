using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Sapphire-native EVENT SELECTOR — the revamped palette replacing the old dock (which
       proxied the game's bottom-bar buttons). Persistent while editing, no selection
       required: a searchable, category-railed list of every event type, built from the
       game's own registries (ed.eventButtons for the catalog, GCS.levelEventIcons /
       eventCategoryIcons for art, RDString for names). Clicking arms the event as the
       stamp tool; digits pick the nth visible event while a tile is selected; Enter
       stamps — with the same input-field / angle-entry guards the old dock had.

       The SHELL (window, search field, category rail, viewport) is built once and
       persists; only the LIST rebuilds on search/category/armed changes — rebuilding the
       shell mid-keystroke destroyed the search field after every character. × collapses
       to a small reopen chip (and hands the game's bottom bar back). Dragging to a screen
       edge docks it Adobe-style. */
    internal static class EditorEventSelector
    {
        private static readonly PanelKit K = new PanelKit("SapphireEventSelector", 905, PanelW, focusable: true);
        private const float PanelW = 264f;
        private const float Pad = PanelKit.Pad, RowH = 26f, Gap = 3f;
        private const float RailW = 34f;

        private static Vector2 _size = new Vector2(PanelW, 560f);
        private static RectTransform _viewport, _content;
        private static TMP_InputField _searchField;
        private static float _scroll;
        private static int _cat;
        private static string _search = "";
        private static bool _userHidden;
        private static int _lastArmed = int.MinValue;
        private static bool _listDirty = true;
        private static CanvasGroup _barCg;
        private static GameObject _chipGo;

        private static readonly List<int> _catIds = new List<int>();
        private static readonly List<List<int>> _catTypes = new List<List<int>>();
        private static readonly List<RoundedRectGraphic> _railBgs = new List<RoundedRectGraphic>();
        private static readonly List<int> _visible = new List<int>();  // digit targets

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

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            bool baseWant = s != null && MainClass.EditorSuiteOn && s.EditorEventDock
                         && ed != null && !ed.playMode;
            bool want = baseWant && !_userHidden;

            SyncGameBar(ed, want);      // hidden palette hands the vanilla bar back
            ShowChip(baseWant && _userHidden);
            if (!want)
            {
                K.Show(false);
                return;
            }

            EnsureCatalog(ed);
            if (_catIds.Count == 0) { K.Show(false); return; }

            if (!K.Built)
            {
                BuildShell();
                if (!_dockInited) { _dockInited = true; K.SetDock(1); } // docked left by default
                _listDirty = true;
            }
            int armed = EditorToolbar.CurrentEventTool;
            if (armed != _lastArmed) { _lastArmed = armed; _listDirty = true; }
            if (_listDirty) { _listDirty = false; BuildList(); }

            K.Show(true);
            ClampIntoView();
            TickKeys(ed);
            TickScroll();
            TickResize();
        }

        private static bool _dockInited;

        // chrome-aware dock bounds: below the file row / toolbar, above the timeline strip
        private static float TopMargin() => 56f;

        private static float BottomInset()
        {
            float strip = 0f;
            try { strip = EditorEvents.BottomStripTop; } catch { }
            // clear the button rows riding above the strip (they hide with it)
            return strip > 0f ? strip + 100f : 12f;
        }

        internal static void Dispose()
        {
            RestoreGameBar();
            K.Dispose();
            if (_chipGo != null) UnityEngine.Object.Destroy(_chipGo);
            _chipGo = null;
            _viewport = null; _content = null; _searchField = null;
            _catIds.Clear(); _catTypes.Clear(); _visible.Clear(); _railBgs.Clear();
            _listDirty = true; _lastArmed = int.MinValue;
        }

        // ── collapse chip (the way back after ×) ─────────────────────────────

        private static void ShowChip(bool show)
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
                r.anchorMin = r.anchorMax = new Vector2(0f, 1f);
                r.pivot = new Vector2(0f, 1f);
                r.anchoredPosition = new Vector2(10f, -64f);
                r.sizeDelta = new Vector2(84f, 24f);
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
                var lt = UIBuilder.Tmp(lGo, "› " + Loc.T("Events"), 11.5f, TextAnchor.MiddleCenter, Theme.Text);
                lt.raycastTarget = false;
                UI.ClickHandler.Attach(_chipGo, () => { _userHidden = false; });
            }
            if (!_chipGo.activeSelf) _chipGo.SetActive(true);
        }

        // ── the game's own bottom event bar is redundant while we're up ─────

        private static void SyncGameBar(scnEditor ed, bool hide)
        {
            try
            {
                var bar = ed != null ? ed.levelEventsBar : null;
                if (bar == null) { _barCg = null; return; }
                var go = bar.gameObject;
                if (_barCg == null || _barCg.gameObject != go)
                    _barCg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
                float a = hide ? 0f : 1f;
                if (_barCg.alpha != a)
                {
                    _barCg.alpha = a;
                    _barCg.blocksRaycasts = !hide;
                }
            }
            catch { }
        }

        private static void RestoreGameBar()
        {
            if (_barCg != null)
            {
                try { _barCg.alpha = 1f; _barCg.blocksRaycasts = true; } catch { }
                _barCg = null;
            }
        }

        // ── catalog from the game's registry ────────────────────────────────

        private static void EnsureCatalog(scnEditor ed)
        {
            if (_catIds.Count > 0) return;
            try
            {
                foreach (var kv in ed.eventButtons)
                {
                    int cat = (int)kv.Key;
                    var types = new List<int>();
                    foreach (var leb in kv.Value)
                        if (leb != null && !types.Contains((int)leb.type)) types.Add((int)leb.type);
                    if (types.Count == 0) continue;
                    _catIds.Add(cat);
                    _catTypes.Add(types);
                }
            }
            catch { }
        }

        private static string CatName(int cat)
        {
            switch (cat)
            {
                case 0: return Loc.T("Gameplay");
                case 1: return Loc.T("Track");
                case 2: return Loc.T("Decorations");
                case 3: return Loc.T("VFX");
                case 4: return Loc.T("Modifiers");
                case 5: return Loc.T("Conveniences");
                case 6: return Loc.T("DLC");
                case 7: return Loc.T("Favorites");
                default: return Loc.T("Other");
            }
        }

        private static string TypeName(int type)
        {
            try
            {
                bool ex;
                var loc = RDString.GetWithCheck("editor." + (ADOFAI.LevelEventType)type, out ex, null);
                if (ex && !string.IsNullOrEmpty(loc)) return loc;
            }
            catch { }
            try { return ((ADOFAI.LevelEventType)type).ToString(); } catch { return type.ToString(); }
        }

        private static Sprite TypeIcon(int type)
        {
            try
            {
                Sprite sp;
                if (GCS.levelEventIcons.TryGetValue((ADOFAI.LevelEventType)type, out sp)) return sp;
            }
            catch { }
            return null;
        }

        private static Sprite CatIcon(int cat)
        {
            try
            {
                Sprite sp;
                if (GCS.eventCategoryIcons.TryGetValue((ADOFAI.LevelEventCategory)cat, out sp)) return sp;
            }
            catch { }
            return null;
        }

        // ── shell: built once, survives list refreshes (and keystrokes) ─────

        private static void BuildShell()
        {
            K.Rebuild(Loc.T("Events"), () => { _userHidden = true; }, new Vector2(10f, -96f));
            var panel = (RectTransform)K.PanelGo.transform;
            panel.sizeDelta = _size;
            ResizeHandle.AttachAll(panel, true, 200f, 240f);
            K.OnDragEnd = () => K.SnapDockOnDragEnd();

            // search field under the header
            var sGo = new GameObject("Search", typeof(RectTransform));
            sGo.transform.SetParent(K.PanelGo.transform, false);
            var sr = (RectTransform)sGo.transform;
            sr.anchorMin = new Vector2(0f, 1f); sr.anchorMax = new Vector2(1f, 1f);
            sr.pivot = new Vector2(0.5f, 1f);
            sr.offsetMin = new Vector2(Pad, -58f); sr.offsetMax = new Vector2(-Pad, -32f);
            var sbg = sGo.AddComponent<RoundedRectGraphic>();
            sbg.Radius = 6f;
            sbg.color = new Color(1f, 1f, 1f, 0.07f);
            sbg.raycastTarget = true;
            var stGo = new GameObject("T", typeof(RectTransform));
            stGo.transform.SetParent(sGo.transform, false);
            var str = (RectTransform)stGo.transform;
            str.anchorMin = Vector2.zero; str.anchorMax = Vector2.one;
            str.offsetMin = new Vector2(8f, 0f); str.offsetMax = new Vector2(-8f, 0f);
            var stxt = UIBuilder.Tmp(stGo, _search, 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            stxt.richText = false;
            _searchField = UIBuilder.BuildInputField(sGo, stxt);
            _searchField.lineType = TMP_InputField.LineType.SingleLine;
            _searchField.SetTextWithoutNotify(_search);
            _searchField.onValueChanged.AddListener(v =>
            {
                _search = v ?? "";
                _scroll = 0f;
                _listDirty = true;   // list only — the field itself must survive
            });
            var phGo = new GameObject("PH", typeof(RectTransform));
            phGo.transform.SetParent(sGo.transform, false);
            var phr = (RectTransform)phGo.transform;
            phr.anchorMin = Vector2.zero; phr.anchorMax = Vector2.one;
            phr.offsetMin = new Vector2(8f, 0f); phr.offsetMax = new Vector2(-8f, 0f);
            var ph = UIBuilder.Tmp(phGo, Loc.T("Search events"), 12.5f, TextAnchor.MiddleLeft,
                new Color(1f, 1f, 1f, 0.28f));
            ph.raycastTarget = false;
            _searchField.placeholder = ph;

            // category icon rail (persistent; dimmed by tint sync while searching)
            _railBgs.Clear();
            float cy = -64f;
            for (int i = 0; i < _catIds.Count; i++)
            {
                int idx = i;
                var cGo = new GameObject("Cat", typeof(RectTransform));
                cGo.transform.SetParent(K.PanelGo.transform, false);
                var cr = (RectTransform)cGo.transform;
                cr.anchorMin = cr.anchorMax = new Vector2(0f, 1f);
                cr.pivot = new Vector2(0f, 1f);
                cr.anchoredPosition = new Vector2(Pad, cy);
                cr.sizeDelta = new Vector2(RailW - 4f, RailW - 4f);
                var cbg = cGo.AddComponent<RoundedRectGraphic>();
                cbg.Radius = 6f;
                cbg.raycastTarget = true;
                _railBgs.Add(cbg);
                var icon = CatIcon(_catIds[i]);
                if (icon != null)
                {
                    var iGo = new GameObject("I", typeof(RectTransform));
                    iGo.transform.SetParent(cGo.transform, false);
                    var ir = (RectTransform)iGo.transform;
                    ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
                    ir.offsetMin = new Vector2(5f, 5f); ir.offsetMax = new Vector2(-5f, -5f);
                    var img = iGo.AddComponent<Image>();
                    img.sprite = icon;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                }
                else
                {
                    var lGo = new GameObject("L", typeof(RectTransform));
                    lGo.transform.SetParent(cGo.transform, false);
                    var lr = (RectTransform)lGo.transform;
                    lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                    lr.offsetMin = lr.offsetMax = Vector2.zero;
                    var lt = UIBuilder.Tmp(lGo, CatName(_catIds[i]).Substring(0, 1), 12f,
                        TextAnchor.MiddleCenter, Theme.Text);
                    lt.raycastTarget = false;
                }
                UI.ClickHandler.Attach(cGo, () =>
                {
                    _cat = idx;
                    _scroll = 0f;
                    // picking a category ends a search — matches Adobe palettes
                    _search = "";
                    if (_searchField != null) _searchField.SetTextWithoutNotify("");
                    _listDirty = true;
                });
                cy -= RailW;
            }

            // event list viewport
            var vpGo = new GameObject("View", typeof(RectTransform));
            vpGo.transform.SetParent(K.PanelGo.transform, false);
            _viewport = (RectTransform)vpGo.transform;
            _viewport.anchorMin = new Vector2(0f, 0f);
            _viewport.anchorMax = new Vector2(1f, 1f);
            _viewport.offsetMin = new Vector2(Pad + RailW, 8f);
            _viewport.offsetMax = new Vector2(-4f, -64f);
            vpGo.AddComponent<RectMask2D>();
            var vImg = vpGo.AddComponent<Image>();
            vImg.color = new Color(0f, 0f, 0f, 0.01f);
            vImg.raycastTarget = true;

            var cGo2 = new GameObject("Content", typeof(RectTransform));
            cGo2.transform.SetParent(vpGo.transform, false);
            _content = (RectTransform)cGo2.transform;
            _content.anchorMin = new Vector2(0f, 1f);
            _content.anchorMax = new Vector2(1f, 1f);
            _content.pivot = new Vector2(0.5f, 1f);
            _content.anchoredPosition = new Vector2(0f, _scroll);
        }

        // ── list: the only part that rebuilds ───────────────────────────────

        private static void BuildList()
        {
            if (_content == null) return;
            for (int i = _content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_content.GetChild(i).gameObject);

            bool searching = !string.IsNullOrEmpty(_search);
            for (int i = 0; i < _railBgs.Count; i++)
            {
                if (_railBgs[i] == null) continue;
                _railBgs[i].color = !searching && i == _cat
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, searching ? 0.03f : 0.06f);
            }

            _visible.Clear();
            if (searching)
            {
                string q = _search.Trim().ToLowerInvariant();
                var seen = new HashSet<int>();
                foreach (var types in _catTypes)
                    foreach (var t in types)
                    {
                        if (!seen.Add(t)) continue;
                        if (TypeName(t).ToLowerInvariant().Contains(q)
                            || ((ADOFAI.LevelEventType)t).ToString().ToLowerInvariant().Contains(q))
                            _visible.Add(t);
                    }
            }
            else if (_cat >= 0 && _cat < _catTypes.Count)
                _visible.AddRange(_catTypes[_cat]);

            int armed = EditorToolbar.CurrentEventTool;
            float y = -2f;
            for (int i = 0; i < _visible.Count; i++)
            {
                int type = _visible[i];
                string name = TypeName(type);
                var row = new GameObject("Evt", typeof(RectTransform));
                row.transform.SetParent(_content, false);
                var rr = (RectTransform)row.transform;
                rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f);
                rr.pivot = new Vector2(0.5f, 1f);
                rr.anchoredPosition = new Vector2(0f, y);
                rr.sizeDelta = new Vector2(-6f, RowH);
                var bg = row.AddComponent<RoundedRectGraphic>();
                bg.Radius = 6f;
                bg.color = type == armed
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, 0.05f);
                bg.raycastTarget = true;

                var icon = TypeIcon(type);
                if (icon != null)
                {
                    var iGo = new GameObject("I", typeof(RectTransform));
                    iGo.transform.SetParent(row.transform, false);
                    var ir = (RectTransform)iGo.transform;
                    ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
                    ir.pivot = new Vector2(0f, 0.5f);
                    ir.anchoredPosition = new Vector2(5f, 0f);
                    ir.sizeDelta = new Vector2(RowH - 8f, RowH - 8f);
                    var img = iGo.AddComponent<Image>();
                    img.sprite = icon;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                }
                string prefix = i < 9 ? (i + 1) + ". " : "";
                var lGo2 = new GameObject("L", typeof(RectTransform));
                lGo2.transform.SetParent(row.transform, false);
                var lr2 = (RectTransform)lGo2.transform;
                lr2.anchorMin = Vector2.zero; lr2.anchorMax = Vector2.one;
                lr2.offsetMin = new Vector2(RowH + 2f, 0f); lr2.offsetMax = new Vector2(-4f, 0f);
                var lt2 = UIBuilder.Tmp(lGo2, prefix + name, 12f, TextAnchor.MiddleLeft, Theme.Text);
                lt2.textWrappingMode = TextWrappingModes.NoWrap;
                lt2.overflowMode = TextOverflowModes.Ellipsis;
                lt2.raycastTarget = false;

                int t2 = type;
                string n2 = name;
                UI.ClickHandler.Attach(row, () =>
                {
                    EditorToolbar.SelectEventTool(t2, n2);
                    _listDirty = true; // re-tint the armed row
                });
                y -= RowH + Gap;
            }
            _content.sizeDelta = new Vector2(0f, -y + 4f);
            ClampScroll();
        }

        // ── keys: digits arm the nth visible event, Enter stamps ────────────

        private static int _fieldFocusFrame = -10;

        private static void TickKeys(scnEditor ed)
        {
            bool focused = false;
            try
            {
                focused = ed.userIsEditingAnInputField;
                if (!focused)
                {
                    var es = EventSystem.current;
                    var sel = es != null ? es.currentSelectedGameObject : null;
                    focused = sel != null && (sel.GetComponent<TMP_InputField>() != null
                                           || sel.GetComponent<InputField>() != null);
                }
            }
            catch { }
            if (focused) { _fieldFocusFrame = Time.frameCount; return; }
            if (Time.frameCount - _fieldFocusFrame <= 1) return;

            try
            {
                if (EditorToolbar.PseudoToolOn) return;   // pseudo/zip own the digits
                if (ed.selectedFloors == null || ed.selectedFloors.Count == 0) return;
            }
            catch { return; }

            for (int d = 0; d < 9 && d < _visible.Count; d++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + d)) continue;
                EditorToolbar.SelectEventTool(_visible[d], TypeName(_visible[d]));
                _listDirty = true;
                break;
            }
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                && !ArbitraryAngleInputOpen(ed))
                EditorToolbar.StampOnSelectedTile();
        }

        private static bool ArbitraryAngleInputOpen(scnEditor ed)
        {
            try
            {
                return ed.floorButtonArbitraryContainer != null
                    && ed.floorButtonArbitraryContainer.activeInHierarchy;
            }
            catch { return false; }
        }

        // ── scroll / resize / clamp ─────────────────────────────────────────

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
    }
}
