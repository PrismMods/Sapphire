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
        private static Canvas _railGameCanvas;
        private static readonly Vector3[] _railCorners = new Vector3[4];

        // ── event dock state ──
        private static GameObject _dockGo;
        private static CanvasGroup _fadedEventsBar;
        private static int _dockSig;
        private static int _dockCooldown;
        private static int _dockSelected = -2;
        private static readonly List<RoundedRectGraphic> _dockCatBgs = new List<RoundedRectGraphic>();
        private static readonly List<Color> _dockCatColors = new List<Color>();
        // Event cells become persistent tools (like the pseudo tool) instead of one-shot inserts.
        private static readonly List<RoundedRectGraphic> _dockEvtBgs = new List<RoundedRectGraphic>();
        private static readonly List<int> _dockEvtTypes = new List<int>();
        private static int _dockEvtSelected = int.MinValue;
        // tall-category overflow: the event column scrolls inside a mask
        private static RectTransform _dockEvtContent;
        private static float _dockScroll, _dockScrollMax;

        // The editor zooms on ANY wheel input — the ZoomCamera patch yields while the dock
        // (with an overflowing column) is hovered so the wheel can scroll it.
        internal static bool DockHovered
        {
            get
            {
                try
                {
                    return _dockGo != null && _dockGo.activeInHierarchy && _dockScrollMax > 0f
                        && RectTransformUtility.RectangleContainsScreenPoint(
                            (RectTransform)_dockGo.transform, Input.mousePosition, null);
                }
                catch { return false; }
            }
        }

        internal static void Tick()
        {
            var s = MainClass.Settings;
            scnEditor ed = null;
            bool chipWant = false, railWant = false, dockWant = false;
            try
            {
                ed = scnEditor.instance;
                bool inEd = ed != null && !ed.playMode;
                bool master = MainClass.EditorSuiteOn;
                chipWant = inEd && master && s != null && s.EditorFileChip;
                railWant = inEd && master && s != null && s.EditorPanelRail;
                dockWant = inEd && master && s != null && s.EditorEventDock;
            }
            catch { }
            if (!chipWant && !railWant && !dockWant)
            {
                if (_canvasGo != null && _canvasGo.activeSelf) { _canvasGo.SetActive(false); CloseMenu(); }
                // During play-testing keep the game chrome faded — the game re-shows it on
                // exit and un-fading here made it flash before the next fade landed. But
                // master-off in the editor must restore, or the file bar stays invisible.
                bool playing = false;
                try { playing = ed != null && ed.playMode; } catch { }
                if (!playing)
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
                // The game's preferences panel hangs off the faded top bar — un-fade while it's
                // open (it was opening invisibly at alpha 0 = "settings button does nothing").
                if (PrefsOpen(ed)) RestoreBar();
                else FadeGameBar(ed); // unthrottled: cheap once faded, prevents re-show flashes
                CloseGhostFileActions(ed);
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

            // The settings panel now lives in the Level-settings popup (EditorLevelMenu hides it in
            // place), so its edge rail has nothing to ride — suppress it and let the popup show the
            // game's own tabs.
            if (railWant && !EditorLevelMenu.ManagesPanel) TickRail(ed);
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
            _railBgs.Clear(); _railSig = 0; _railSelected = -2; _railGameCanvas = null; _dockSig = 0;
            _dockCatBgs.Clear(); _dockCatColors.Clear(); _dockSelected = -2;
            _dockEvtBgs.Clear(); _dockEvtTypes.Clear(); _dockEvtSelected = int.MinValue;
            try { EditorToolbar.ClearEventTool(); } catch { }
            _shownName = null; _shownUnsaved = true;
        }

        // ── game bar fade ───────────────────────────────────────────────────

        // The game's ESC toggles its file-actions panel, which is faded to nothing while the
        // chip replaces it — an ESC press would open INVISIBLE UI (and eat the next ESC to
        // close it). Shut it the moment it shows.
        private static System.Reflection.FieldInfo _showingFileActionsFi;

        private static void CloseGhostFileActions(scnEditor ed)
        {
            try
            {
                if (_showingFileActionsFi == null)
                    _showingFileActionsFi = typeof(scnEditor).GetField("showingFileActions",
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (_showingFileActionsFi != null && (bool)_showingFileActionsFi.GetValue(ed))
                    ed.ShowFileActionsPanel(false);
            }
            catch { }
        }

        private static bool PrefsOpen(scnEditor ed)
        {
            try { return ed.prefsContainer != null && ed.prefsContainer.gameObject.activeInHierarchy; }
            catch { return false; }
        }

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
            // Track every frame: the strip hangs off the settings panel's edge and slides
            // with it, so the rail rides the panel open/closed instead of covering it.
            if (_railGo != null && _railGo.activeSelf && tabsGo != null)
                TrackRailPosition((RectTransform)tabsGo.transform);
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
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0f, 1f); // top-left pinned to the game strip's top-left
            r.sizeDelta = new Vector2(btnSize + pad * 2f, tabRoots.Count * (btnSize + gap) - gap + pad * 2f);
            try { _railGameCanvas = tabRoots[0].GetComponentInParent<Canvas>()?.rootCanvas; }
            catch { _railGameCanvas = null; }
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
        internal static void ProxyClick(GameObject go,
            PointerEventData.InputButton button = PointerEventData.InputButton.Left)
        {
            try
            {
                var ev = new PointerEventData(EventSystem.current)
                {
                    button = button
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

        // Pin the rail's top-left to the (faded) game strip's top-left, converting through
        // the game's canvas camera. Per frame: this is what makes it ride the slide tween.
        private static void TrackRailPosition(RectTransform tabsRt)
        {
            try
            {
                tabsRt.GetWorldCorners(_railCorners);
                Camera cam = null;
                if (_railGameCanvas != null && _railGameCanvas.renderMode != RenderMode.ScreenSpaceOverlay)
                    cam = _railGameCanvas.worldCamera;
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, _railCorners[1]);
                Vector2 local;
                if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screen, null, out local))
                    ((RectTransform)_railGo.transform).anchoredPosition = local;
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
            // Keyboard flow runs EVERY frame (GetKeyDown is missed behind the rebuild cooldown):
            // digits pick the nth event of the current category; Enter stamps the selected tile.
            TickDockKeys(ed);
            // Wheel scrolls an overflowing event column while hovered.
            if (_dockScrollMax > 0f && _dockEvtContent != null && DockHovered)
            {
                float wheel = MainClass.WheelY;
                if (wheel != 0f)
                {
                    _dockScroll = Mathf.Clamp(_dockScroll - wheel * 40f, 0f, _dockScrollMax);
                    _dockEvtContent.anchoredPosition = new Vector2(0f, _dockScroll);
                }
            }
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

            // Selected event TOOL: fill the picked event cell (mirrors EditorToolbar's active tool).
            int evtSel = -1;
            try { evtSel = EditorToolbar.EventTool; } catch { }
            if (evtSel != _dockEvtSelected)
            {
                _dockEvtSelected = evtSel;
                for (int i = 0; i < _dockEvtBgs.Count; i++)
                {
                    if (_dockEvtBgs[i] == null) continue;
                    bool on = _dockEvtTypes[i] == evtSel && evtSel >= 0;
                    _dockEvtBgs[i].color = on
                        ? new Color(UI.Theme.Accent.r, UI.Theme.Accent.g, UI.Theme.Accent.b, 0.5f)
                        : new Color(1f, 1f, 1f, 0.05f);
                }
            }

            // Photoshop-style: left edge, top-aligned below the file chip row.
            var r = (RectTransform)_dockGo.transform;
            var pos = new Vector2(10f, -56f);
            if (r.anchoredPosition != pos) r.anchoredPosition = pos;
        }

        /* Enter must not stamp when it was really submitting a text field. The game's
           userIsEditingAnInputField (and TMP focus) is already FALSE on the frame Enter lands —
           the field commits and releases focus in the same frame — so a one-frame latch of
           "a field was focused" is the reliable guard. Covers the game's event-settings /
           angle-input fields AND Sapphire's own TMP fields (EventSystem selection). */
        private static int _fieldFocusFrame = -10;

        private static void TrackFieldFocus(scnEditor ed)
        {
            bool focused = false;
            try { focused = ed != null && ed.userIsEditingAnInputField; } catch { }
            if (!focused)
            {
                try
                {
                    var es = UnityEngine.EventSystems.EventSystem.current;
                    var go = es != null ? es.currentSelectedGameObject : null;
                    focused = go != null && (go.GetComponent<TMPro.TMP_InputField>() != null
                                          || go.GetComponent<UnityEngine.UI.InputField>() != null);
                }
                catch { }
            }
            if (focused) _fieldFocusFrame = Time.frameCount;
        }

        private static bool FieldFocusedRecently => Time.frameCount - _fieldFocusFrame <= 1;

        private static void TickDockKeys(scnEditor ed)
        {
            TrackFieldFocus(ed);
            try
            {
                if (EditorToolbar.PseudoToolOn) return;         // pseudo owns the digits
                if (ed.userIsEditingAnInputField) return;
                if (ed.selectedFloors == null || ed.selectedFloors.Count == 0) return;
            }
            catch { return; }
            for (int d = 0; d < 9 && d < _dockEvtTypes.Count; d++)
            {
                if (!Input.GetKeyDown(KeyCode.Alpha1 + d)) continue;
                int type = _dockEvtTypes[d];
                if (type < 0) continue;
                string name;
                try { name = ((ADOFAI.LevelEventType)type).ToString(); } catch { name = "event " + type; }
                EditorToolbar.SelectEventTool(type, name);
                break;
            }
            if ((Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
                && !FieldFocusedRecently)
                EditorToolbar.StampOnSelectedTile();
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
            _dockEvtBgs.Clear();
            _dockEvtTypes.Clear();
            _dockSelected = -2;
            _dockEvtSelected = int.MinValue;

            const float catSize = 34f, catGap = 6f, evtSize = 34f, evtGap = 6f, pad = 8f, colGap = 8f;
            int nEvt = evts != null ? evts.Count : 0;
            float catColH = cats.Count > 0 ? cats.Count * (catSize + catGap) - catGap : 0f;
            float evtColH = nEvt > 0 ? nEvt * (evtSize + evtGap) - evtGap : 0f;
            // Tall categories would run past the screen — cap to the space between the dock's
            // top anchor (-56) and the timeline, and let the event column scroll inside a mask.
            float maxColH = 1080f;
            try { maxColH = _canvasRect.rect.height - 56f - 140f; } catch { }
            float shownEvtH = Mathf.Min(evtColH, maxColH);
            _dockScrollMax = Mathf.Max(0f, evtColH - shownEvtH);
            _dockScroll = Mathf.Clamp(_dockScroll, 0f, _dockScrollMax);
            float height = Mathf.Max(catColH, shownEvtH) + pad * 2f;
            float width = pad * 2f + catSize + (nEvt > 0 ? colGap + evtSize : 0f);

            _dockGo = new GameObject("EventDock", typeof(RectTransform));
            _dockGo.transform.SetParent(_canvasGo.transform, false);
            var r = (RectTransform)_dockGo.transform;
            r.anchorMin = r.anchorMax = new Vector2(0f, 1f); // left edge, TOP-aligned (Adobe palette)
            r.pivot = new Vector2(0f, 1f);
            r.sizeDelta = new Vector2(width, height);
            var bg = _dockGo.AddComponent<RoundedRectGraphic>();
            bg.Radius = 12f;
            bg.color = new Color(0.07f, 0.07f, 0.09f, 0.9f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.12f);
            bg.raycastTarget = true; // toolbar: swallow clicks under it

            // Category column down the left, tinted by the timeline's category palette
            // (the game's order matches the LevelEventCategory enum); its column is centred
            // against the taller of the two columns.
            float catTop = -pad; // top-aligned (Adobe palette)
            for (int i = 0; i < cats.Count; i++)
            {
                var cat = EditorEvents.CategoryColor((ADOFAI.LevelEventCategory)i);
                var bg2 = MakeDockCell(cats[i], catSize,
                    new Vector2(pad, catTop - i * (catSize + catGap)),
                    new Color(cat.r, cat.g, cat.b, 0.85f));
                _dockCatBgs.Add(bg2);
                _dockCatColors.Add(cat);
            }
            // Current category's events in a column to the right of the categories. Each event is a
            // persistent TOOL (pick it, then click tiles to keep inserting) rather than a one-shot.
            scnEditor edNow = null; try { edNow = scnEditor.instance; } catch { }
            float evtX = pad + catSize + colGap;

            // masked viewport for the event column; its content shifts up when scrolled
            var viewGo = new GameObject("EvtView", typeof(RectTransform));
            viewGo.transform.SetParent(_dockGo.transform, false);
            var vr = (RectTransform)viewGo.transform;
            vr.anchorMin = vr.anchorMax = new Vector2(0f, 1f);
            vr.pivot = new Vector2(0f, 1f);
            vr.anchoredPosition = new Vector2(evtX, -pad);
            vr.sizeDelta = new Vector2(evtSize, shownEvtH);
            viewGo.AddComponent<RectMask2D>();
            var contentGo = new GameObject("EvtContent", typeof(RectTransform));
            contentGo.transform.SetParent(viewGo.transform, false);
            _dockEvtContent = (RectTransform)contentGo.transform;
            _dockEvtContent.anchorMin = _dockEvtContent.anchorMax = new Vector2(0f, 1f);
            _dockEvtContent.pivot = new Vector2(0f, 1f);
            _dockEvtContent.anchoredPosition = new Vector2(0f, _dockScroll);
            _dockEvtContent.sizeDelta = new Vector2(evtSize, evtColH);

            for (int i = 0; i < nEvt; i++)
            {
                int type = ResolveEventType(edNow, evts[i]);
                var evtBg = MakeDockCell(evts[i], evtSize,
                    new Vector2(0f, -i * (evtSize + evtGap)),
                    new Color(1f, 1f, 1f, 0.1f), type, _dockEvtContent);
                _dockEvtBgs.Add(evtBg);
                _dockEvtTypes.Add(type);
            }
        }

        // Map a dock event cell (a game Button) back to its LevelEventType via the game's own
        // eventButtons registry (LevelEventButton.button/.type are public). −1 if unknown.
        private static int ResolveEventType(scnEditor ed, Button gameBtn)
        {
            try
            {
                if (ed == null || ed.eventButtons == null) return -1;
                foreach (var kv in ed.eventButtons)
                    foreach (var leb in kv.Value)
                        if (leb != null && leb.button == gameBtn) return (int)leb.type;
            }
            catch { }
            return -1;
        }

        private static RoundedRectGraphic MakeDockCell(Button gameBtn, float size, Vector2 topLeft, Color border, int eventType = -2, Transform parent = null)
        {
            var cellGo = new GameObject("Cell", typeof(RectTransform));
            cellGo.transform.SetParent(parent != null ? parent : _dockGo.transform, false);
            var cr = (RectTransform)cellGo.transform;
            cr.anchorMin = cr.anchorMax = new Vector2(0f, 1f); // panel top-left origin
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

            if (eventType >= 0)
            {
                int t = eventType;
                string name = ((ADOFAI.LevelEventType)t).ToString();
                UI.ClickHandler.Attach(cellGo, () => EditorToolbar.SelectEventTool(t, name));
            }
            else
            {
                // Category chips (and events whose type couldn't be resolved) keep the one-shot proxy.
                UI.ClickHandler.Attach(cellGo, () => ProxyClick(gameBtn.gameObject));
            }
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

            // (game button, fallback label) — settings and exit live as chips beside the
            // file chip instead of menu rows.
            var entries = new List<KeyValuePair<Button, string>>
            {
                new KeyValuePair<Button, string>(ed.buttonNew,        "New Level"),
                new KeyValuePair<Button, string>(ed.buttonOpen,       "Open…"),
                new KeyValuePair<Button, string>(ed.buttonOpenRecent, "Open Recent"),
                new KeyValuePair<Button, string>(ed.buttonOpenURL,    "Open from URL…"),
                new KeyValuePair<Button, string>(ed.buttonSave,       "Save"),
                new KeyValuePair<Button, string>(ed.buttonSaveAs,     "Save As…"),
                new KeyValuePair<Button, string>(ed.buttonHelp,       "Help"),
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

            /* Settings + leave chips beside the file chip. Children of the chip anchored
               to its RIGHT edge, so they follow filename width changes and hide with it.
               Settings drives the public ShowPreferences() (the game wires
               buttonPreferences to it, but the button reference can be flaky — the menu
               row used to vanish); leave proxies the game's exit button. */
            var settingsGo = MakeSideChip("SettingsChip", 6f);
            for (int i = 0; i < 3; i++) // no ⚙ in the user fonts — procedural slider bars
            {
                var barGo = new GameObject("Bar", typeof(RectTransform));
                barGo.transform.SetParent(settingsGo.transform, false);
                var br = (RectTransform)barGo.transform;
                br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f);
                br.pivot = new Vector2(0.5f, 0.5f);
                br.anchoredPosition = new Vector2(i == 1 ? 2f : -1f, 5f - i * 5f);
                br.sizeDelta = new Vector2(i == 1 ? 10f : 14f, 2.5f);
                var bar = barGo.AddComponent<RoundedRectGraphic>();
                bar.Radius = 1.25f;
                bar.color = new Color(0.8f, 0.8f, 0.84f, 1f);
                bar.raycastTarget = false;
            }
            UI.ClickHandler.Attach(settingsGo, () =>
            {
                CloseMenu();
                try
                {
                    var ed = scnEditor.instance;
                    if (ed == null) return;
                    if (ed.buttonPreferences != null) ed.buttonPreferences.onClick.Invoke();
                    else ed.ShowPreferences();
                }
                catch (Exception ex) { SapphireLog.Log("EditorChrome: settings failed: " + ex.Message); }
            });

            // Level settings — toggles the game's song/level settings inspector
            // (scnEditor.buttonSettings), reskinned by the dark theme + panel rail.
            var levelSetGo = MakeSideChip("LevelSettingsChip", 42f);
            var docGo = new GameObject("Doc", typeof(RectTransform));
            docGo.transform.SetParent(levelSetGo.transform, false);
            var docR = (RectTransform)docGo.transform;
            docR.anchorMin = docR.anchorMax = new Vector2(0.5f, 0.5f);
            docR.pivot = new Vector2(0.5f, 0.5f);
            docR.sizeDelta = new Vector2(12f, 15f);
            var doc = docGo.AddComponent<RoundedRectGraphic>();
            doc.Radius = 2.5f; // card outline = the settings panel
            doc.color = new Color(0f, 0f, 0f, 0f);
            doc.BorderWidth = 1.6f;
            doc.BorderColor = new Color(0.8f, 0.8f, 0.84f, 1f);
            doc.raycastTarget = false;
            for (int i = 0; i < 2; i++) // two title lines inside the card
            {
                var lineGo = new GameObject("Line", typeof(RectTransform));
                lineGo.transform.SetParent(levelSetGo.transform, false);
                var lnR = (RectTransform)lineGo.transform;
                lnR.anchorMin = lnR.anchorMax = new Vector2(0.5f, 0.5f);
                lnR.pivot = new Vector2(0.5f, 0.5f);
                lnR.anchoredPosition = new Vector2(0f, 2f - i * 4f);
                lnR.sizeDelta = new Vector2(6f, 1.6f);
                var ln = lineGo.AddComponent<RoundedRectGraphic>();
                ln.Radius = 0.8f;
                ln.color = new Color(0.8f, 0.8f, 0.84f, 1f);
                ln.raycastTarget = false;
            }
            UI.ClickHandler.Attach(levelSetGo, () =>
            {
                CloseMenu();
                try { EditorLevelMenu.Toggle(); } // hosts the game panel in a Sapphire popup
                catch (Exception ex) { SapphireLog.Log("EditorChrome: level settings failed: " + ex.Message); }
            });

            // Game settings (the shared SettingsMenu the pause menu drives; public Show()).
            var gameSetGo = MakeSideChip("GameSettingsChip", 78f);
            // Procedural gear (no ⚙ in the user fonts): a ring with 8 rim teeth.
            var iconCol = new Color(0.8f, 0.8f, 0.84f, 1f);
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var toothGo = new GameObject("Tooth", typeof(RectTransform));
                toothGo.transform.SetParent(gameSetGo.transform, false);
                var tr = (RectTransform)toothGo.transform;
                tr.anchorMin = tr.anchorMax = new Vector2(0.5f, 0.5f);
                tr.pivot = new Vector2(0.5f, 0.5f);
                tr.anchoredPosition = new Vector2(Mathf.Sin(a) * 6.5f, Mathf.Cos(a) * 6.5f);
                tr.sizeDelta = new Vector2(3f, 4.5f);
                tr.localRotation = Quaternion.Euler(0f, 0f, -i * 45f);
                var tooth = toothGo.AddComponent<RoundedRectGraphic>();
                tooth.Radius = 1.2f;
                tooth.color = iconCol;
                tooth.raycastTarget = false;
            }
            var ringGo = new GameObject("Ring", typeof(RectTransform));
            ringGo.transform.SetParent(gameSetGo.transform, false);
            var rr = (RectTransform)ringGo.transform;
            rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 0.5f);
            rr.pivot = new Vector2(0.5f, 0.5f);
            rr.anchoredPosition = Vector2.zero;
            rr.sizeDelta = new Vector2(11f, 11f);
            var ring = ringGo.AddComponent<RoundedRectGraphic>();
            ring.Radius = 5.5f;
            ring.color = new Color(0f, 0f, 0f, 0f);
            ring.BorderWidth = 2.4f;
            ring.BorderColor = iconCol;
            ring.raycastTarget = false;
            UI.ClickHandler.Attach(gameSetGo, () =>
            {
                CloseMenu();
                // The scene's SettingsMenu lives under the inactive PauseMenu(Clone) and can't
                // show there — EditorGameSettings initializes it and hosts it in a popup.
                try { EditorGameSettings.Toggle(); }
                catch (Exception ex) { SapphireLog.Log("EditorChrome: game settings failed: " + ex.Message); }
            });

            // Help — interactive help mode (hover-highlight + click-for-docs).
            var helpGo = MakeSideChip("HelpChip", 150f);
            var hGlyphGo = new GameObject("Glyph", typeof(RectTransform));
            hGlyphGo.transform.SetParent(helpGo.transform, false);
            var hgr = (RectTransform)hGlyphGo.transform;
            hgr.anchorMin = Vector2.zero; hgr.anchorMax = Vector2.one;
            hgr.offsetMin = Vector2.zero; hgr.offsetMax = Vector2.zero;
            var hGlyph = hGlyphGo.AddComponent<TextMeshProUGUI>();
            hGlyph.font = UI.Theme.TmpFont;
            hGlyph.fontSize = 15f;
            hGlyph.color = new Color(0.8f, 0.8f, 0.84f, 1f);
            hGlyph.alignment = TMPro.TextAlignmentOptions.Center;
            hGlyph.raycastTarget = false;
            hGlyph.text = "?";
            UI.ClickHandler.Attach(helpGo, () => { CloseMenu(); EditorHelp.Toggle(); });

            var leaveGo = MakeSideChip("LeaveChip", 114f);
            var xGo = new GameObject("Glyph", typeof(RectTransform));
            xGo.transform.SetParent(leaveGo.transform, false);
            var xr = (RectTransform)xGo.transform;
            xr.anchorMin = Vector2.zero; xr.anchorMax = Vector2.one;
            xr.offsetMin = Vector2.zero; xr.offsetMax = Vector2.zero;
            var x = xGo.AddComponent<TextMeshProUGUI>();
            x.font = UI.Theme.TmpFont;
            x.fontSize = 16;
            x.color = new Color(0.85f, 0.6f, 0.62f, 1f);
            x.alignment = TextAlignmentOptions.Center;
            x.raycastTarget = false;
            x.text = "×";
            UI.ClickHandler.Attach(leaveGo, () =>
            {
                CloseMenu();
                try
                {
                    var ed = scnEditor.instance;
                    if (ed != null && ed.buttonExit != null) ed.buttonExit.onClick.Invoke();
                }
                catch (Exception ex) { SapphireLog.Log("EditorChrome: exit failed: " + ex.Message); }
            });
        }

        private static GameObject MakeSideChip(string name, float xOffset)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_chipGo.transform, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(1f, 0.5f);
            r.pivot = new Vector2(0f, 0.5f);
            r.anchoredPosition = new Vector2(xOffset, 0f);
            r.sizeDelta = new Vector2(30f, 30f);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 9f;
            bg.color = new Color(0.08f, 0.08f, 0.1f, 0.85f);
            bg.BorderWidth = 1f;
            bg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            bg.raycastTarget = true;
            var hover = go.AddComponent<ChipHover>();
            hover.Bg = bg;
            hover.Base = bg.color;
            return go;
        }

        // MenuRowHover resets to transparent on exit (menu rows have no fill); chips need
        // their dark base back instead.
        private class ChipHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public RoundedRectGraphic Bg;
            public Color Base;
            public void OnPointerEnter(PointerEventData e)
            { if (Bg != null) Bg.color = new Color(0.17f, 0.17f, 0.2f, 0.95f); }
            public void OnPointerExit(PointerEventData e)
            { if (Bg != null) Bg.color = Base; }
        }
    }
}
