using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Sapphire
{
    /* Sapphire replacement for the editor's file bar (Premiere-classic layout: chip in
       the top-left corner). The chip shows an unsaved dot + level file name + chevron
       and opens a floating Sapphire menu whose entries proxy the game's own file buttons
       (onClick.Invoke on buttonNew/buttonOpen/… — no game logic reimplemented; labels
       are read from those buttons so localization comes for free). While enabled, the
       game's own bar is faded out through a CanvasGroup and restored on toggle-off. */
    internal static class EditorChrome
    {
        private static GameObject _canvasGo;
        private static RectTransform _canvasRect;
        private static GameObject _chipGo;
        private static RectTransform _chipRect;
        private static TextMeshProUGUI _chipLabel;
        private static RoundedRectGraphic _dot;
        private static GameObject _menuGo;
        private static CanvasGroup _fadedBar;
        private static string _shownName;
        private static bool _shownUnsaved = true;
        private static int _cooldown;
        private static System.Reflection.FieldInfo _unsavedField;
        private static System.Reflection.MethodInfo _recentLabelMethod;

        // ── panel rail state ──
        private static GameObject _railGo;
        private static readonly List<RoundedRectGraphic> _railBgs = new List<RoundedRectGraphic>();
        private static CanvasGroup _fadedTabs;
        private static int _railSig;
        private static int _railCooldown;
        private static int _railSelected = -2;

        // ── event dock state ──
        private static GameObject _dockGo;
        private static CanvasGroup _fadedEventsBar;
        private static int _dockSig;
        private static int _dockCooldown;
        private static int _dockSelected = -2;
        private static readonly List<RoundedRectGraphic> _dockCatBgs = new List<RoundedRectGraphic>();
        private static readonly List<Color> _dockCatColors = new List<Color>();

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            bool chipWant = false, railWant = false, dockWant = false;
            try
            {
                ed = scnEditor.instance;
                bool inEd = ed != null && !ed.playMode;
                chipWant = inEd && s != null && s.EditorFileChip;
                railWant = inEd && s != null && s.EditorPanelRail;
                dockWant = inEd && s != null && s.EditorEventDock;
            }
            catch { }
            if (!chipWant && !railWant && !dockWant)
            {
                if (_canvasGo != null && _canvasGo.activeSelf) { _canvasGo.SetActive(false); CloseMenu(); }
                // During play-testing keep the game chrome faded — the game re-shows it on
                // exit and un-fading here made it flash before the next fade landed.
                if (ed == null)
                {
                    RestoreBar();
                    RestoreTabs();
                    RestoreEventsBar();
                }
                return;
            }
            if (_canvasGo == null) Build();
            if (!_canvasGo.activeSelf) _canvasGo.SetActive(true);

            if (chipWant)
            {
                if (_chipGo != null && !_chipGo.activeSelf) _chipGo.SetActive(true);
                FadeGameBar(ed); // unthrottled: cheap once faded, prevents re-show flashes
                if (--_cooldown <= 0)
                {
                    _cooldown = 15;
                    UpdateChip(ed);
                }
            }
            else
            {
                if (_chipGo != null && _chipGo.activeSelf) { _chipGo.SetActive(false); CloseMenu(); }
                RestoreBar();
            }

            if (railWant) TickRail(ed);
            else
            {
                if (_railGo != null && _railGo.activeSelf) _railGo.SetActive(false);
                RestoreTabs();
            }

            if (dockWant) TickDock(ed);
            else
            {
                if (_dockGo != null && _dockGo.activeSelf) _dockGo.SetActive(false);
                RestoreEventsBar();
            }
        }

        internal static void Dispose()
        {
            RestoreBar();
            RestoreTabs();
            RestoreEventsBar();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null; _canvasRect = null; _chipGo = null; _chipRect = null;
            _chipLabel = null; _dot = null; _menuGo = null; _railGo = null; _dockGo = null;
            _railBgs.Clear(); _railSig = 0; _railSelected = -2; _dockSig = 0;
            _dockCatBgs.Clear(); _dockCatColors.Clear(); _dockSelected = -2;
            _shownName = null; _shownUnsaved = true;
        }

        // ── game bar fade ───────────────────────────────────────────────────

        private static void FadeGameBar(scnEditor ed)
        {
            try
            {
                var bar = ed.buttonFileActionDropdown != null
                    ? ed.buttonFileActionDropdown.transform.parent : null;
                if (bar == null) return;
                var cg = bar.GetComponent<CanvasGroup>();
                if (cg == null) cg = bar.gameObject.AddComponent<CanvasGroup>();
                // interactable stays TRUE: Button.OnPointerClick refuses to fire under a
                // non-interactable group, which broke every proxied click. Alpha 0 +
                // blocksRaycasts false hides it and stops direct clicks just fine.
                if (cg.alpha != 0f) { cg.alpha = 0f; cg.blocksRaycasts = false; }
                _fadedBar = cg;
            }
            catch { }
        }

        private static void RestoreBar()
        {
            if (_fadedBar == null) return;
            try { _fadedBar.alpha = 1f; _fadedBar.blocksRaycasts = true; _fadedBar.interactable = true; }
            catch { }
            _fadedBar = null;
        }

        // ── chip ────────────────────────────────────────────────────────────

        private static void UpdateChip(scnEditor ed)
        {
            string name = null;
            try
            {
                var path = ADOBase.levelPath;
                if (!string.IsNullOrEmpty(path)) name = System.IO.Path.GetFileName(path);
            }
            catch { }
            if (string.IsNullOrEmpty(name)) name = "Untitled";
            if (name.Length > 34) name = name.Substring(0, 33) + "…";

            bool unsaved = true;
            try
            {
                // The unsaved flag isn't public — read the backing field via reflection.
                if (_unsavedField == null)
                    _unsavedField = typeof(scnEditor).GetField("_unsavedChanges",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (_unsavedField != null) unsaved = (bool)_unsavedField.GetValue(ed);
            }
            catch { }

            if (name != _shownName)
            {
                _shownName = name;
                _chipLabel.text = name;
                float w = Mathf.Ceil(_chipLabel.GetPreferredValues(name).x);
                _chipRect.sizeDelta = new Vector2(w + 58f, 30f);
            }
            if (unsaved != _shownUnsaved)
            {
                _shownUnsaved = unsaved;
                _dot.color = unsaved ? UI.Theme.Accent : new Color(0.45f, 0.45f, 0.5f, 1f);
            }
        }

        // ── panel rail ──────────────────────────────────────────────────────
        // Sapphire replacement for the inspector's vertical tab strip: rail buttons reuse
        // the game tabs' own icon sprites and proxy their onClick, the game strip fades
        // out, and the highlight mirrors whichever settings panel is actually open.

        private static void TickRail(scnEditor ed)
        {
            // The settings tab strip is the settings InspectorPanel's own `tabs` container
            // (scnEditor.inspectorTabs is the EVENT inspector's per-event tabs — sweeping
            // that was the arrow-soup rail).
            GameObject tabsGo = null;
            try
            {
                if (ed.settingsPanel != null && ed.settingsPanel.tabs != null)
                    tabsGo = ed.settingsPanel.tabs.gameObject;
            }
            catch { }
            // Fade before the cooldown gate so the game strip can't flash on re-shows.
            if (tabsGo != null) FadeTabs(tabsGo);
            if (--_railCooldown > 0) return;
            _railCooldown = 10;
            if (tabsGo == null)
            {
                if (_railGo != null && _railGo.activeSelf) _railGo.SetActive(false);
                RestoreTabs();
                return;
            }
            /* One cell per DIRECT child of the strip. The tab FACE isn't a Button (only
               the nested collapse-arrow is — that made the arrow-soup rail), so cells key
               on the tab's icon image and clicks bubble UP from it to whatever pointer
               handler the tab actually uses (ExecuteHierarchy). */
            var tabRoots = new List<Transform>();
            var tabsTr = tabsGo.transform;
            for (int i = 0; i < tabsTr.childCount; i++)
            {
                var c = tabsTr.GetChild(i);
                if (c.gameObject.activeSelf) tabRoots.Add(c);
            }
            if (tabRoots.Count == 0)
            {
                if (_railGo != null && _railGo.activeSelf) _railGo.SetActive(false);
                RestoreTabs();
                return;
            }
            int sig = tabRoots.Count;
            for (int i = 0; i < tabRoots.Count; i++) sig = sig * 31 + tabRoots[i].GetInstanceID();
            if (sig != _railSig || _railGo == null)
            {
                _railSig = sig;
                BuildRail(tabRoots);
            }
            if (!_railGo.activeSelf) _railGo.SetActive(true);
            FadeTabs(tabsGo);
            SyncRailHighlight(ed);
        }

        private static void BuildRail(List<Transform> tabRoots)
        {
            if (_railGo != null) UnityEngine.Object.Destroy(_railGo);
            _railBgs.Clear();
            _railSelected = -2;

            const float btnSize = 34f, gap = 6f, pad = 7f;
            _railGo = new GameObject("PanelRail", typeof(RectTransform));
            _railGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_railGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(10f, 0f);
            r.sizeDelta = new Vector2(btnSize + pad * 2f, tabRoots.Count * (btnSize + gap) - gap + pad * 2f);
            var bg = _railGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 12f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.9f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true; // swallow clicks like a toolbar

            for (int i = 0; i < tabRoots.Count; i++)
            {
                var tabRoot = tabRoots[i];
                var cellGo = new GameObject("Tab" + i, typeof(RectTransform));
                cellGo.transform.SetParent(_railGo.transform, false);
                var cr = (RectTransform)cellGo.transform;
                cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 1f);
                cr.pivot = new Vector2(0.5f, 1f);
                cr.anchoredPosition = new Vector2(0f, -pad - i * (btnSize + gap));
                cr.sizeDelta = new Vector2(btnSize, btnSize);
                var cellBg = cellGo.AddComponent<RoundedRectGraphic>();
                cellBg.Radius = 9f;
                cellBg.color = new Color(1f, 1f, 1f, 0.05f);
                cellBg.raycastTarget = true;
                _railBgs.Add(cellBg);

                var iconImg = LargestSpriteImage(tabRoot);
                if (iconImg != null)
                {
                    var iconGo = new GameObject("Icon", typeof(RectTransform));
                    iconGo.transform.SetParent(cellGo.transform, false);
                    var ir = (RectTransform)iconGo.transform;
                    ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
                    ir.offsetMin = new Vector2(6f, 6f); ir.offsetMax = new Vector2(-6f, -6f);
                    var img = iconGo.AddComponent<Image>();
                    img.sprite = iconImg.sprite;
                    img.preserveAspect = true;
                    img.color = Color.white;
                    img.raycastTarget = false;
                }

                // Bubble the click up from the icon to the tab's own handler, wherever the
                // game put it (the tab face is NOT a Button).
                var clickFrom = iconImg != null ? iconImg.gameObject : tabRoot.gameObject;
                UI.ClickHandler.Attach(cellGo, () => ProxyClickHierarchy(clickFrom));
            }
        }

        // Largest sprite-bearing image in the subtree — the tab's icon (collapse arrows
        // and badges are far smaller).
        private static Image LargestSpriteImage(Transform root)
        {
            Image best = null;
            float bestArea = 0f;
            try
            {
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                {
                    if (img == null || img.sprite == null) continue;
                    var rt = img.rectTransform;
                    float area = Mathf.Abs(rt.rect.width * rt.rect.height);
                    if (area > bestArea) { bestArea = area; best = img; }
                }
            }
            catch { }
            return best;
        }

        // Like ProxyClick, but climbs ancestors until someone handles it.
        private static void ProxyClickHierarchy(GameObject go)
        {
            try
            {
                var ev = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left
                };
                ExecuteEvents.ExecuteHierarchy(go, ev, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.ExecuteHierarchy(go, ev, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.ExecuteHierarchy(go, ev, ExecuteEvents.pointerClickHandler);
            }
            catch (Exception ex) { SapphireLog.Debug("EditorChrome: hierarchy click failed: " + ex.Message); }
        }

        // The LARGEST sprite-bearing image under the tab that isn't the button face — its
        // icon (nested arrow glyphs and badges are smaller).
        private static Sprite FindIconSprite(Button b)
        {
            try
            {
                Sprite best = null;
                float bestArea = 0f;
                foreach (var img in b.GetComponentsInChildren<Image>(true))
                {
                    if (img == b.targetGraphic || img.sprite == null) continue;
                    var rt = img.rectTransform;
                    float area = Mathf.Abs(rt.rect.width * rt.rect.height);
                    if (area > bestArea) { bestArea = area; best = img.sprite; }
                }
                if (best != null) return best;
                var face = b.targetGraphic as Image;
                return face != null ? face.sprite : null;
            }
            catch { return null; }
        }

        /* Simulate a real pointer click instead of onClick.Invoke: several game controls
           (the event-category tabs, notably) do their work in pointer handlers with an
           empty persistent onClick, and this path drives Buttons identically anyway. */
        private static void ProxyClick(GameObject go)
        {
            try
            {
                var ev = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left
                };
                ExecuteEvents.Execute(go, ev, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(go, ev, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute(go, ev, ExecuteEvents.pointerClickHandler);
            }
            catch (Exception ex) { SapphireLog.Debug("EditorChrome: proxy click failed: " + ex.Message); }
        }


        // Highlight = the active child of the settings-panels container (deterministic,
        // unlike reading tab colors the game rewrites). -1 = no panel open.
        private static void SyncRailHighlight(scnEditor ed)
        {
            int sel = -1;
            try
            {
                var cont = ed.settingsPanelsContainer;
                if (cont != null)
                    for (int i = 0; i < cont.childCount; i++)
                        if (cont.GetChild(i).gameObject.activeInHierarchy) { sel = i; break; }
            }
            catch { }
            if (sel == _railSelected) return;
            _railSelected = sel;
            var accent = UI.Theme.Accent;
            for (int i = 0; i < _railBgs.Count; i++)
            {
                if (_railBgs[i] == null) continue;
                _railBgs[i].color = i == sel
                    ? new Color(accent.r, accent.g, accent.b, 0.3f)
                    : new Color(1f, 1f, 1f, 0.05f);
            }
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

        // ── event dock ──────────────────────────────────────────────────────
        // Sapphire port of the game's bottom event palette: category chips (borders in the
        // timeline's category colors) over the current category's event buttons, icons and
        // clicks proxied from the game's own bar, which fades out underneath. The dock
        // rebuilds whenever the game swaps the button set (category change, selection).

        private static void TickDock(scnEditor ed)
        {
            RectTransform bar = null;
            try { bar = ed.levelEventsBar; } catch { }
            // Fade before the cooldown gate so the game bar can't flash on re-shows.
            if (bar != null) FadeEventsBar(bar.gameObject);
            if (--_dockCooldown > 0) return;
            _dockCooldown = 10;
            // No selection → the game hides its palette; match it.
            if (bar == null || !bar.gameObject.activeInHierarchy)
            {
                if (_dockGo != null && _dockGo.activeSelf) _dockGo.SetActive(false);
                return;
            }

            List<Button> cats = null, evts = null;
            try
            {
                cats = ActiveButtons(ed.levelEventsBarCategories);
                evts = ActiveButtons(ed.levelEventsBarButtons);
            }
            catch { }
            if (cats == null || cats.Count == 0)
            {
                if (_dockGo != null && _dockGo.activeSelf) _dockGo.SetActive(false);
                return;
            }

            // The game pools/reuses buttons across categories, so the signature includes
            // icon sprites, not just instance ids.
            int sig = cats.Count * 397 ^ (evts != null ? evts.Count : 0);
            for (int i = 0; i < cats.Count; i++) sig = sig * 31 + cats[i].GetInstanceID();
            if (evts != null)
                for (int i = 0; i < evts.Count; i++)
                {
                    var sp = FindIconSprite(evts[i]);
                    sig = sig * 31 + evts[i].GetInstanceID() * 7 + (sp != null ? sp.GetInstanceID() : 0);
                }
            if (sig != _dockSig || _dockGo == null)
            {
                _dockSig = sig;
                BuildDock(cats, evts);
            }
            if (!_dockGo.activeSelf) _dockGo.SetActive(true);

            // Selected-category indicator: the chip fills with its own category color.
            int sel = -1;
            try { sel = (int)ed.currentCategory; } catch { }
            if (sel != _dockSelected)
            {
                _dockSelected = sel;
                for (int i = 0; i < _dockCatBgs.Count; i++)
                {
                    if (_dockCatBgs[i] == null) continue;
                    var cc = i < _dockCatColors.Count ? _dockCatColors[i] : Color.white;
                    _dockCatBgs[i].color = i == sel
                        ? new Color(cc.r, cc.g, cc.b, 0.4f)
                        : new Color(1f, 1f, 1f, 0.05f);
                }
            }

            // Stack just above the timeline strip (or hug the bottom edge without it).
            var r = (RectTransform)_dockGo.transform;
            float y = 10f;
            try { float top = EditorEvents.BottomStripTop; if (top > 0f) y = top + 8f; } catch { }
            if (!Mathf.Approximately(r.anchoredPosition.y, y)) r.anchoredPosition = new Vector2(0f, y);
        }

        private static List<Button> ActiveButtons(Transform root)
        {
            var list = new List<Button>();
            if (root == null) return list;
            foreach (var b in root.GetComponentsInChildren<Button>(true))
                if (b != null && b.gameObject.activeInHierarchy) list.Add(b);
            return list;
        }

        private static void BuildDock(List<Button> cats, List<Button> evts)
        {
            if (_dockGo != null) UnityEngine.Object.Destroy(_dockGo);
            _dockCatBgs.Clear();
            _dockCatColors.Clear();
            _dockSelected = -2;

            const float catSize = 26f, catGap = 5f, evtSize = 36f, evtGap = 6f, pad = 8f;
            float catRowW = cats.Count * (catSize + catGap) - catGap;
            float evtRowW = evts != null && evts.Count > 0 ? evts.Count * (evtSize + evtGap) - evtGap : 0f;
            float width = Mathf.Max(catRowW, evtRowW) + pad * 2f;
            bool hasEvents = evtRowW > 0f;
            float height = pad * 2f + catSize + (hasEvents ? evtSize + 6f : 0f);

            _dockGo = new GameObject("EventDock", typeof(RectTransform));
            _dockGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_dockGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.sizeDelta = new Vector2(width, height);
            var bg = _dockGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 12f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.9f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true; // toolbar: swallow clicks under it

            // Category chips along the top, tinted by the timeline's category palette
            // (the game's order matches the LevelEventCategory enum).
            for (int i = 0; i < cats.Count; i++)
            {
                var cat = EditorEvents.CategoryColor((ADOFAI.LevelEventCategory)i);
                var bg2 = MakeDockCell(cats[i], catSize,
                    new Vector2(-catRowW * 0.5f + i * (catSize + catGap), -pad),
                    new Color(cat.r, cat.g, cat.b, 0.85f));
                _dockCatBgs.Add(bg2);
                _dockCatColors.Add(cat);
            }
            if (evts != null)
                for (int i = 0; i < evts.Count; i++)
                    MakeDockCell(evts[i], evtSize,
                        new Vector2(-evtRowW * 0.5f + i * (evtSize + evtGap), -pad - catSize - 6f),
                        new Color(1f, 1f, 1f, 0.1f));
        }

        private static RoundedRectGraphic MakeDockCell(Button gameBtn, float size, Vector2 topLeft, Color border)
        {
            var cellGo = new GameObject("Cell", typeof(RectTransform));
            cellGo.transform.SetParent(_dockGo.transform, false);
            var cr = (RectTransform)cellGo.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0.5f, 1f);
            cr.pivot = new Vector2(0f, 1f);
            cr.anchoredPosition = topLeft;
            cr.sizeDelta = new Vector2(size, size);
            var cellBg = cellGo.AddComponent<RoundedRectGraphic>();
            cellBg.Radius = 7f;
            cellBg.color = new Color(1f, 1f, 1f, 0.05f);
            cellBg.BorderWidth = 1f;
            cellBg.BorderColor = border;
            cellBg.raycastTarget = true;

            var sprite = FindIconSprite(gameBtn);
            if (sprite != null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform));
                iconGo.transform.SetParent(cellGo.transform, false);
                var ir = (RectTransform)iconGo.transform;
                ir.anchorMin = Vector2.zero; ir.anchorMax = Vector2.one;
                ir.offsetMin = new Vector2(5f, 5f); ir.offsetMax = new Vector2(-5f, -5f);
                var img = iconGo.AddComponent<Image>();
                img.sprite = sprite;
                img.preserveAspect = true;
                img.color = Color.white;
                img.raycastTarget = false;
            }

            UI.ClickHandler.Attach(cellGo, () => ProxyClick(gameBtn.gameObject));
            return cellBg;
        }

        private static void FadeEventsBar(GameObject barGo)
        {
            try
            {
                var cg = barGo.GetComponent<CanvasGroup>();
                if (cg == null) cg = barGo.AddComponent<CanvasGroup>();
                if (cg.alpha != 0f) { cg.alpha = 0f; cg.blocksRaycasts = false; } // interactable stays true
                _fadedEventsBar = cg;
            }
            catch { }
        }

        private static void RestoreEventsBar()
        {
            if (_fadedEventsBar == null) return;
            try { _fadedEventsBar.alpha = 1f; _fadedEventsBar.blocksRaycasts = true; _fadedEventsBar.interactable = true; }
            catch { }
            _fadedEventsBar = null;
        }

        // ── menu ────────────────────────────────────────────────────────────

        private static void ToggleMenu()
        {
            if (_menuGo != null) { CloseMenu(); return; }
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null) return;

            // Fullscreen blocker: click anywhere outside closes.
            _menuGo = new GameObject("FileMenu", typeof(RectTransform));
            _menuGo.transform.SetParent(_canvasGo.transform, false);
            var blocker = (RectTransform)_menuGo.transform;
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = Vector2.zero; blocker.offsetMax = Vector2.zero;
            var blockImg = _menuGo.AddComponent<Image>();
            blockImg.color = new Color(0f, 0f, 0f, 0.01f);
            blockImg.raycastTarget = true;
            UI.ClickHandler.Attach(_menuGo, CloseMenu);

            var panelGo = new GameObject("Panel", typeof(RectTransform));
            panelGo.transform.SetParent(_menuGo.transform, false);
            var panel = (RectTransform)panelGo.transform;
            panel.anchorMin = panel.anchorMax = new Vector2(0f, 1f);
            panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(12f, -46f); // just under the chip
            var bg = panelGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 10f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.97f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;

            // The recent-level label ("<name> 열기") is only refreshed when the game's own
            // menu opens — freshen it ourselves so our row shows the current file.
            try
            {
                if (_recentLabelMethod == null)
                    _recentLabelMethod = typeof(scnEditor).GetMethod("UpdateOpenRecentLabel",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                _recentLabelMethod?.Invoke(ed, null);
            }
            catch { }

            // (game button, fallback label)
            var entries = new List<KeyValuePair<Button, string>>
            {
                new KeyValuePair<Button, string>(ed.buttonNew,        "New Level"),
                new KeyValuePair<Button, string>(ed.buttonOpen,       "Open…"),
                new KeyValuePair<Button, string>(ed.buttonOpenRecent, "Open Recent"),
                new KeyValuePair<Button, string>(ed.buttonOpenURL,    "Open from URL…"),
                new KeyValuePair<Button, string>(ed.buttonSave,       "Save"),
                new KeyValuePair<Button, string>(ed.buttonSaveAs,     "Save As…"),
                new KeyValuePair<Button, string>(ed.buttonPreferences,"Editor Settings"),
                new KeyValuePair<Button, string>(ed.buttonHelp,       "Help"),
                new KeyValuePair<Button, string>(ed.buttonExit,       "Exit Editor"),
            };

            const float rowH = 32f, padY = 8f, width = 280f;
            float y = -padY;
            foreach (var e in entries)
            {
                if (e.Key == null) continue;
                var btn = e.Key;
                var rowGo = new GameObject("Row", typeof(RectTransform));
                rowGo.transform.SetParent(panelGo.transform, false);
                var row = (RectTransform)rowGo.transform;
                row.anchorMin = new Vector2(0f, 1f);
                row.anchorMax = new Vector2(1f, 1f);
                row.pivot = new Vector2(0.5f, 1f);
                row.offsetMin = new Vector2(6f, 0f);
                row.offsetMax = new Vector2(-6f, 0f);
                row.anchoredPosition = new Vector2(0f, y);
                row.sizeDelta = new Vector2(row.sizeDelta.x, rowH);

                var rowBg = rowGo.AddComponent<RoundedRectGraphic>();
                rowBg.Radius = 7f;
                rowBg.color = new Color(1f, 1f, 1f, 0f);
                rowBg.raycastTarget = true;
                rowGo.AddComponent<MenuRowHover>().Bg = rowBg;

                var txtGo = new GameObject("Label", typeof(RectTransform));
                txtGo.transform.SetParent(rowGo.transform, false);
                var tr = (RectTransform)txtGo.transform;
                tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
                tr.offsetMin = new Vector2(12f, 0f); tr.offsetMax = new Vector2(-12f, 0f);
                var t = txtGo.AddComponent<TextMeshProUGUI>();
                t.font = UI.Theme.TmpFont;
                t.fontSize = 15;
                t.color = new Color(0.93f, 0.93f, 0.94f, 1f);
                t.alignment = TextAlignmentOptions.Left;
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.overflowMode = TextOverflowModes.Ellipsis;
                t.raycastTarget = false;
                t.text = GameLabel(btn, e.Value);

                UI.ClickHandler.Attach(rowGo, () =>
                {
                    CloseMenu();
                    try { btn.onClick.Invoke(); }
                    catch (Exception ex) { SapphireLog.Log("EditorChrome: menu action failed: " + ex.Message); }
                });
                y -= rowH;
            }
            panel.sizeDelta = new Vector2(width, -y + padY * 2f);
        }

        // The game's own (localized) label for a file button, falling back to English.
        private static string GameLabel(Button b, string fallback)
        {
            try
            {
                var tmp = b.GetComponentInChildren<TMP_Text>(true);
                if (tmp != null && !string.IsNullOrEmpty(tmp.text)) return tmp.text;
                var txt = b.GetComponentInChildren<Text>(true);
                if (txt != null && !string.IsNullOrEmpty(txt.text)) return txt.text;
            }
            catch { }
            return fallback;
        }

        private static void CloseMenu()
        {
            if (_menuGo != null) UnityEngine.Object.Destroy(_menuGo);
            _menuGo = null;
        }

        private class MenuRowHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public RoundedRectGraphic Bg;
            public void OnPointerEnter(PointerEventData e) { if (Bg != null) Bg.color = new Color(1f, 1f, 1f, 0.08f); }
            public void OnPointerExit(PointerEventData e) { if (Bg != null) Bg.color = new Color(1f, 1f, 1f, 0f); }
        }

        // ── construction ────────────────────────────────────────────────────

        private static void Build()
        {
            _canvasGo = new GameObject("SapphireEditorChrome", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 905;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRect = (RectTransform)_canvasGo.transform;

            _chipGo = new GameObject("FileChip", typeof(RectTransform));
            _chipGo.transform.SetParent(_canvasGo.transform, false);
            _chipRect = (RectTransform)_chipGo.transform;
            _chipRect.anchorMin = _chipRect.anchorMax = new Vector2(0f, 1f);
            _chipRect.pivot = new Vector2(0f, 1f);
            _chipRect.anchoredPosition = new Vector2(12f, -10f);
            _chipRect.sizeDelta = new Vector2(180f, 30f);
            var bg = _chipGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 9f;
            bg.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;
            UI.ClickHandler.Attach(_chipGo, ToggleMenu);

            var dotGo = new GameObject("Dot", typeof(RectTransform));
            dotGo.transform.SetParent(_chipGo.transform, false);
            var dr = (RectTransform)dotGo.transform;
            dr.anchorMin = dr.anchorMax = new Vector2(0f, 0.5f);
            dr.pivot = new Vector2(0f, 0.5f);
            dr.anchoredPosition = new Vector2(11f, 0f);
            dr.sizeDelta = new Vector2(9f, 9f);
            _dot = dotGo.AddComponent<RoundedRectGraphic>();
            _dot.Radius = 4.5f;
            _dot.color = UI.Theme.Accent;
            _dot.raycastTarget = false;

            var lblGo = new GameObject("Label", typeof(RectTransform));
            lblGo.transform.SetParent(_chipGo.transform, false);
            var lr = (RectTransform)lblGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(27f, 0f); lr.offsetMax = new Vector2(-24f, 0f);
            _chipLabel = lblGo.AddComponent<TextMeshProUGUI>();
            _chipLabel.font = UI.Theme.TmpFont;
            _chipLabel.fontSize = 14;
            _chipLabel.color = new Color(0.93f, 0.93f, 0.94f, 1f);
            _chipLabel.alignment = TextAlignmentOptions.Left;
            _chipLabel.textWrappingMode = TextWrappingModes.NoWrap;
            _chipLabel.overflowMode = TextOverflowModes.Ellipsis;
            _chipLabel.raycastTarget = false;

            // Down chevron: the proven "›" glyph rotated, since user fonts lack ▼/▾.
            var chevGo = new GameObject("Chevron", typeof(RectTransform));
            chevGo.transform.SetParent(_chipGo.transform, false);
            var cr = (RectTransform)chevGo.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(1f, 0.5f);
            cr.pivot = new Vector2(0.5f, 0.5f);
            cr.anchoredPosition = new Vector2(-14f, -1f);
            cr.sizeDelta = new Vector2(16f, 16f);
            cr.localRotation = Quaternion.Euler(0f, 0f, -90f);
            var chev = chevGo.AddComponent<TextMeshProUGUI>();
            chev.font = UI.Theme.TmpFont;
            chev.fontSize = 15;
            chev.color = new Color(0.58f, 0.58f, 0.62f, 1f);
            chev.alignment = TextAlignmentOptions.Center;
            chev.raycastTarget = false;
            chev.text = "›";
        }
    }
}
