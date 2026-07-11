using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sapphire
{
    /* Sapphire replacement for the event inspector's tab column ("arrow soup"): one icon
       cell per InspectorTab, borders tinted with the timeline's category palette, clicks
       proxied to the game's own tabs (which fade out underneath, keeping their logic and
       shortcuts), plus a ‹ n/m › pill for tabs that cycle several events of one type.
       The rail tracks the game column's screen rect every frame, so it rides the
       inspector's slide tween — and follows any "paneltabs" layout override for free. */
    internal static class EditorInspector
    {
        private static GameObject _canvasGo;
        private static RectTransform _canvasRect;
        private static GameObject _railGo;
        private static RectTransform _railRect;
        private static CanvasGroup _fadedTabs;
        private static Canvas _gameCanvas;
        private static int _sig;
        private static int _cooldown;
        private static int _selected = -2;
        private static readonly List<RoundedRectGraphic> _cellBgs = new List<RoundedRectGraphic>();
        private static readonly List<Color> _cellColors = new List<Color>();
        private static readonly List<ADOFAI.InspectorTab> _tabs = new List<ADOFAI.InspectorTab>();
        private static GameObject _cycleGo;
        private static RectTransform _cycleRect;
        private static TextMeshProUGUI _cycleLabel;
        private static CycleButtons _cycleTarget;
        private static readonly Vector3[] _corners = new Vector3[4];

        private const float Cell = 34f, Gap = 6f, Pad = 7f;

        internal static void Tick()
        {
            var s = MainClass.Settings;
            bool enabled = s != null && MainClass.EditorSuiteOn && s.EditorEventInspector;
            scnEditor ed = null;
            bool inEd = false;
            try
            {
                ed = scnEditor.instance;
                inEd = ed != null && !ed.playMode;
            }
            catch { }
            if (!inEd || !enabled)
            {
                HideRail();
                // Keep the game column faded through play-testing (restore→flash→refade);
                // restore only when leaving the editor or turning the toggle off.
                if (ed == null || !enabled) RestoreTabs();
                return;
            }

            ADOFAI.InspectorPanel panel = null;
            RectTransform tabsRt = null;
            try
            {
                panel = ed.levelEventsPanel;
                if (panel != null) tabsRt = panel.tabs;
            }
            catch { }
            if (tabsRt == null) { HideRail(); RestoreTabs(); return; }

            // Unthrottled like the other chrome fades: no flash when the game re-shows it.
            FadeTabs(tabsRt.gameObject);

            /* The rail lives with the TAB COLUMN, not the panel: clicking the selected
               tab collapses the panel (ShowInspector(false, force) → permaHideInspector,
               which then suppresses every auto-open), and the game's way back is clicking
               a tab again. Gating the rail on showInspector removed that way back — the
               "event menu never reappears" bug. */
            bool show = false;
            try { show = tabsRt.gameObject.activeInHierarchy; } catch { }
            if (!show) { HideRail(); return; }

            // No events on the tile → the game's (empty) column blinks in and out; showing an
            // empty rail just flickers along with it. Nothing to list = nothing to show.
            int tabCount = 0;
            try
            {
                for (int i = 0; i < tabsRt.childCount; i++)
                {
                    var c = tabsRt.GetChild(i);
                    if (c.gameObject.activeSelf && c.GetComponent<ADOFAI.InspectorTab>() != null) tabCount++;
                }
            }
            catch { }
            if (tabCount == 0) { HideRail(); return; }

            if (_canvasGo == null) BuildCanvas();
            if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

            if (--_cooldown <= 0)
            {
                _cooldown = 10;
                SyncStructure(tabsRt);
                if (_tabs.Count == 0) { HideRail(); return; }
                SyncSelection(panel);
            }
            if (_railGo != null && _railGo.activeSelf) TrackPosition(tabsRt);
        }

        internal static void Dispose()
        {
            RestoreTabs();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _canvasRect = null; _railGo = null; _railRect = null;
            _cycleGo = null; _cycleRect = null; _cycleLabel = null; _cycleTarget = null;
            _gameCanvas = null; _cellBgs.Clear(); _cellColors.Clear(); _tabs.Clear();
            _sig = 0; _selected = -2; _cooldown = 0;
        }

        private static void HideRail()
        {
            if (_canvasGo != null && _canvasGo.activeSelf) _canvasGo.SetActive(false);
        }

        // ── structure ───────────────────────────────────────────────────────

        private static void SyncStructure(RectTransform tabsRt)
        {
            _tabs.Clear();
            for (int i = 0; i < tabsRt.childCount; i++)
            {
                var c = tabsRt.GetChild(i);
                if (!c.gameObject.activeSelf) continue;
                var tab = c.GetComponent<ADOFAI.InspectorTab>();
                if (tab != null) _tabs.Add(tab);
            }
            // Tabs are pooled/retyped across floors, so the signature includes the event
            // type and icon sprite, not just instance ids.
            int sig = _tabs.Count;
            for (int i = 0; i < _tabs.Count; i++)
            {
                var t = _tabs[i];
                sig = sig * 31 + t.GetInstanceID();
                sig = sig * 31 + (int)t.levelEventType;
                var sp = t.icon != null ? t.icon.sprite : null;
                sig = sig * 31 + (sp != null ? sp.GetInstanceID() : 0);
            }
            if (sig == _sig && _railGo != null) return;
            _sig = sig;
            BuildRail(tabsRt);
        }

        private static void BuildRail(RectTransform tabsRt)
        {
            if (_railGo != null) UnityEngine.Object.Destroy(_railGo);
            _cellBgs.Clear();
            _cellColors.Clear();
            _selected = -2;
            _cycleGo = null; _cycleLabel = null; _cycleTarget = null;
            _instGo = null; _instRect = null; _instBgs.Clear(); _instSig = 0;
            try { _gameCanvas = tabsRt.GetComponentInParent<Canvas>()?.rootCanvas; } catch { _gameCanvas = null; }

            _railGo = new GameObject("EventTabRail", typeof(RectTransform));
            _railGo.transform.SetParent(_canvasGo.transform, false);
            _railRect = (RectTransform)_railGo.transform;
            _railRect.anchorMin = _railRect.anchorMax = new Vector2(0.5f, 0.5f);
            _railRect.pivot = new Vector2(1f, 1f); // top-right pinned where the game column sits
            _railRect.sizeDelta = new Vector2(Cell + Pad * 2f, _tabs.Count * (Cell + Gap) - Gap + Pad * 2f);
            var bg = _railGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 12f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.9f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true; // swallow clicks like a toolbar

            for (int i = 0; i < _tabs.Count; i++)
            {
                var tab = _tabs[i];
                var cat = TabCategoryColor(tab.levelEventType);
                _cellColors.Add(cat);

                var cellGo = new GameObject("Tab" + i, typeof(RectTransform));
                cellGo.transform.SetParent(_railGo.transform, false);
                var cr = (RectTransform)cellGo.transform;
                cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 1f);
                cr.pivot = new Vector2(0.5f, 1f);
                cr.anchoredPosition = new Vector2(0f, -Pad - i * (Cell + Gap));
                cr.sizeDelta = new Vector2(Cell, Cell);
                var cellBg = cellGo.AddComponent<RoundedRectGraphic>();
                cellBg.Radius = 9f;
                cellBg.color = new Color(1f, 1f, 1f, 0.05f);
                cellBg.BorderWidth = 1f;
                cellBg.BorderColor = new Color(cat.r, cat.g, cat.b, 0.85f);
                cellBg.raycastTarget = true;
                _cellBgs.Add(cellBg);

                var sprite = tab.icon != null ? tab.icon.sprite : null;
                if (sprite != null)
                {
                    var iconGo = new GameObject("Icon", typeof(RectTransform));
                    iconGo.transform.SetParent(cellGo.transform, false);
                    var ir = (RectTransform)iconGo.transform;
                    ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
                    ir.offsetMin = new Vector2(6f, 6f); ir.offsetMax = new Vector2(-6f, -6f);
                    var img = iconGo.AddComponent<Image>();
                    img.sprite = sprite;
                    img.preserveAspect = true;
                    img.color = Color.white;
                    img.raycastTarget = false;
                }

                // The tab itself is the IPointerClickHandler (its Button's persistent
                // onClick is empty) — proxy the pointer click at the tab GameObject.
                // Right-click parity: the game deletes the tab's event on right click.
                var tabGo = tab.gameObject;
                var click = UI.ClickHandler.Attach(cellGo, () => EditorChrome.ProxyClick(tabGo));
                click.OnRightClick = () =>
                    EditorChrome.ProxyClick(tabGo, PointerEventData.InputButton.Right);
            }

            BuildCyclePill();
        }

        // ── selection + cycle pill ──────────────────────────────────────────

        private static void SyncSelection(ADOFAI.InspectorPanel panel)
        {
            // Collapsed panel = no filled cell (the rail is the drawer handle then);
            // clicking any cell proxies the tab, which force-reopens the panel.
            ADOFAI.InspectorTab sel = null;
            try { if (panel.showInspector) sel = panel.GetSelectedEventTab(); } catch { }
            int idx = sel != null ? _tabs.IndexOf(sel) : -1;
            if (idx != _selected)
            {
                _selected = idx;
                for (int i = 0; i < _cellBgs.Count; i++)
                {
                    if (_cellBgs[i] == null) continue;
                    var cc = i < _cellColors.Count ? _cellColors[i] : Color.white;
                    _cellBgs[i].color = i == idx
                        ? new Color(cc.r, cc.g, cc.b, 0.4f)
                        : new Color(1f, 1f, 1f, 0.05f);
                }
            }

            // Several events of one type on the tile: numbered chips hang off the selected
            // tab for DIRECT selection (cycling one-by-one is slow) — the game's public
            // InspectorPanel.ShowPanel(type, index) jumps straight to instance i. The old
            // ‹ n/m › cycle pill stays as the fallback for absurd counts.
            CycleButtons cyc = null;
            try
            {
                if (idx >= 0 && sel.cycleButtons != null && sel.cycleButtons.gameObject.activeInHierarchy)
                    cyc = sel.cycleButtons;
            }
            catch { }
            _cycleTarget = cyc;

            int count = 0, cur = -1;
            if (cyc != null && sel != null)
            {
                try
                {
                    var evs = scnEditor.instance.GetSelectedFloorEvents(sel.levelEventType);
                    count = evs != null ? evs.Count : 0;
                }
                catch { }
                try { cur = sel.eventIndex; } catch { }
            }
            bool useChips = count >= 2 && count <= MaxChips;

            SyncInstanceChips(panel, useChips ? sel : null, count, cur, idx);

            if (_cycleGo == null) return;
            if (cyc == null || useChips)
            {
                if (_cycleGo.activeSelf) _cycleGo.SetActive(false);
                return;
            }
            if (!_cycleGo.activeSelf) _cycleGo.SetActive(true);
            _cycleRect.anchoredPosition = new Vector2(-6f, -Pad - idx * (Cell + Gap) - Cell * 0.5f);
            try
            {
                var txt = cyc.text != null ? cyc.text.text : null;
                if (!string.IsNullOrEmpty(txt) && _cycleLabel.text != txt) _cycleLabel.text = txt;
            }
            catch { }
        }

        // ── instance chips (direct pick among same-type events) ────────────
        private const int MaxChips = 12;
        private static GameObject _instGo;
        private static RectTransform _instRect;
        private static readonly List<RoundedRectGraphic> _instBgs = new List<RoundedRectGraphic>();
        private static int _instSig;

        private static void SyncInstanceChips(ADOFAI.InspectorPanel panel, ADOFAI.InspectorTab sel,
            int count, int cur, int idx)
        {
            if (sel == null)
            {
                if (_instGo != null && _instGo.activeSelf) _instGo.SetActive(false);
                return;
            }
            int sig = ((int)sel.levelEventType) * 1000 + count;
            if (sig != _instSig || _instGo == null) BuildInstanceChips(sel.levelEventType, count);
            _instSig = sig;
            if (!_instGo.activeSelf) _instGo.SetActive(true);
            _instRect.anchoredPosition = new Vector2(-6f, -Pad - idx * (Cell + Gap) - Cell * 0.5f);
            for (int i = 0; i < _instBgs.Count; i++)
            {
                if (_instBgs[i] == null) continue;
                _instBgs[i].color = i == cur
                    ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, 0.07f);
            }
        }

        private static void BuildInstanceChips(ADOFAI.LevelEventType type, int count)
        {
            if (_instGo != null) UnityEngine.Object.Destroy(_instGo);
            _instBgs.Clear();

            const float chip = 22f, gap = 4f, pad = 6f;
            float width = pad * 2f + count * (chip + gap) - gap;
            _instGo = new GameObject("InstanceChips", typeof(RectTransform));
            _instGo.transform.SetParent(_railGo.transform, false);
            _instRect = (RectTransform)_instGo.transform;
            _instRect.anchorMin = _instRect.anchorMax = new Vector2(0f, 1f);
            _instRect.pivot = new Vector2(1f, 0.5f); // hangs off the rail's left edge
            _instRect.sizeDelta = new Vector2(width, 30f);
            var bg = _instGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 9f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.9f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            for (int i = 0; i < count; i++)
            {
                int n = i;
                var cGo = new GameObject("C" + i, typeof(RectTransform));
                cGo.transform.SetParent(_instGo.transform, false);
                var cr = (RectTransform)cGo.transform;
                cr.anchorMin = cr.anchorMax = new Vector2(0f, 0.5f);
                cr.pivot = new Vector2(0f, 0.5f);
                cr.anchoredPosition = new Vector2(pad + i * (chip + gap), 0f);
                cr.sizeDelta = new Vector2(chip, chip);
                var cbg = cGo.AddComponent<RoundedRectGraphic>();
                cbg.Radius = 6f;
                cbg.color = new Color(1f, 1f, 1f, 0.07f);
                cbg.raycastTarget = true;
                _instBgs.Add(cbg);
                var lblGo = new GameObject("L", typeof(RectTransform));
                lblGo.transform.SetParent(cGo.transform, false);
                var lr = (RectTransform)lblGo.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = Vector2.zero; lr.offsetMax = Vector2.zero;
                var lbl = lblGo.AddComponent<TextMeshProUGUI>();
                lbl.font = UI.Theme.TmpFont;
                lbl.fontSize = 12.5f;
                lbl.color = new Color(0.93f, 0.93f, 0.94f, 1f);
                lbl.alignment = TextAlignmentOptions.Center;
                lbl.raycastTarget = false;
                lbl.text = (i + 1).ToString();
                UI.ClickHandler.Attach(cGo, () =>
                {
                    try { scnEditor.instance.levelEventsPanel.ShowPanel(type, n); }
                    catch (System.Exception ex) { SapphireLog.Log("Inspector: pick instance failed: " + ex.Message); }
                });
            }
        }

        private static void BuildCyclePill()
        {
            _cycleGo = new GameObject("CyclePill", typeof(RectTransform));
            _cycleGo.transform.SetParent(_railGo.transform, false);
            _cycleRect = (RectTransform)_cycleGo.transform;
            _cycleRect.anchorMin = _cycleRect.anchorMax = new Vector2(0f, 1f);
            _cycleRect.pivot = new Vector2(1f, 0.5f); // hangs off the rail's left edge
            _cycleRect.sizeDelta = new Vector2(92f, 26f);
            var bg = _cycleGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 9f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.9f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true;

            MakeCycleArrow("Prev", 0f, 180f, false);
            MakeCycleArrow("Next", 1f, 0f, true);

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(_cycleGo.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(22f, 0f); lr.offsetMax = new Vector2(-22f, 0f);
            _cycleLabel = lblGo.AddComponent<TextMeshProUGUI>();
            _cycleLabel.font = UI.Theme.TmpFont;
            _cycleLabel.fontSize = 13;
            _cycleLabel.color = new Color(0.93f, 0.93f, 0.94f, 1f);
            _cycleLabel.alignment = TextAlignmentOptions.Center;
            _cycleLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _cycleLabel.raycastTarget = false;

            _cycleGo.SetActive(false);
        }

        private static void MakeCycleArrow(string name, float anchorX, float rotZ, bool next)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_cycleGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(anchorX, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = new Vector2(anchorX < 0.5f ? 12f : -12f, 0f);
            r.sizeDelta = new Vector2(22f, 26f);
            // Invisible hit surface so the whole arrow end of the pill is clickable.
            var hit = go.AddComponent<Image>();
            hit.color = new Color(0f, 0f, 0f, 0f);
            hit.raycastTarget = true;

            var chevGo = new GameObject("Glyph", typeof(RectTransform));
            chevGo.transform.SetParent(go.transform, false);
            var cr = (RectTransform)chevGo.transform;
            cr.anchorMin = Vector2.zero; cr.anchorMax = Vector2.one;
            cr.offsetMin = Vector2.zero; cr.offsetMax = Vector2.zero;
            cr.localRotation = Quaternion.Euler(0f, 0f, rotZ); // "‹" isn't in the user fonts; "›" rotated is
            var chev = chevGo.AddComponent<TextMeshProUGUI>();
            chev.font = UI.Theme.TmpFont;
            chev.fontSize = 15;
            chev.color = new Color(0.58f, 0.58f, 0.62f, 1f);
            chev.alignment = TextAlignmentOptions.Center;
            chev.raycastTarget = false;
            chev.text = "›";

            UI.ClickHandler.Attach(go, () =>
            {
                var cyc = _cycleTarget;
                if (cyc == null) return;
                var btn = next ? cyc.nextButton : cyc.prevButton;
                if (btn != null) EditorChrome.ProxyClick(btn.gameObject);
            });
        }

        // ── helpers ─────────────────────────────────────────────────────────

        // Category tint for an event type, matching the timeline/dock palette. Favorites
        // is a UI shelf, never a real category.
        private static Color TabCategoryColor(ADOFAI.LevelEventType t)
        {
            try
            {
                ADOFAI.LevelEventInfo info;
                if (GCS.levelEventsInfo != null && GCS.levelEventsInfo.TryGetValue(t.ToString(), out info)
                    && info != null && info.categories != null)
                    foreach (var c in info.categories)
                        if (c != ADOFAI.LevelEventCategory.Favorites)
                            return EditorEvents.CategoryColor(c);
            }
            catch { }
            return new Color(1f, 1f, 1f, 0.35f);
        }

        // Pin the rail's top-right to the game tab column's top-right, converting through
        // whatever canvas/camera the game is using. Runs every frame: it's what makes the
        // rail ride the inspector's slide tween.
        private static void TrackPosition(RectTransform tabsRt)
        {
            try
            {
                tabsRt.GetWorldCorners(_corners);
                Camera cam = null;
                if (_gameCanvas != null && _gameCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = _gameCanvas.worldCamera;
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, _corners[2]);
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out local))
                    _railRect.anchoredPosition = local;
            }
            catch { }
        }

        private static void FadeTabs(GameObject tabsGo)
        {
            try
            {
                var cg = tabsGo.GetComponent<CanvasGroup>();
                if (cg == null) cg = tabsGo.AddComponent<CanvasGroup>();
                if (cg.alpha != 0f) { cg.alpha = 0f; cg.blocksRaycasts = false; } // interactable stays true
                _fadedTabs = cg;
            }
            catch { }
        }

        private static void RestoreTabs()
        {
            if (_fadedTabs == null) return;
            try { _fadedTabs.alpha = 1f; _fadedTabs.blocksRaycasts = true; _fadedTabs.interactable = true; }
            catch { }
            _fadedTabs = null;
        }

        private static void BuildCanvas()
        {
            _canvasGo = new GameObject("SapphireEventTabs", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 906;
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
