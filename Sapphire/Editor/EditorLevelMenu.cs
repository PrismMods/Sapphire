using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Hosts the game's OWN level-settings inspector (scnEditor.settingsPanel, an
       ADOFAI.InspectorPanel) inside a Sapphire popup — the file-row "Level settings" chip toggles
       it. Ctrl+E-style layout: a labeled tab rail down the left (icon + name, proxying the game's
       own tab clicks) with the game panel WIDENED on the right (its rows anchor-stretch, so they
       follow); the game's icon-only tab strip is faded while hosted. The panel's transform is
       captured on open and fully restored on close; the game keeps owning all fields/logic. */
    internal static class EditorLevelMenu
    {
        private static GameObject _canvasGo;
        private static GameObject _popupGo;
        private static RectTransform _cardRect;
        private static RectTransform _hostRect;
        private static RectTransform _railRect;
        private static bool _open;

        // rail rows (built per open)
        private static readonly List<RoundedRectGraphic> _rowBgs = new List<RoundedRectGraphic>();
        private static readonly List<ADOFAI.InspectorTab> _rowTabs = new List<ADOFAI.InspectorTab>();
        private static int _rowSelected = -2;

        // captured game-panel transform state (for a clean restore)
        private static RectTransform _panelRect;
        private static Transform _origParent;
        private static int _origSibling;
        private static Vector2 _origAnchorMin, _origAnchorMax, _origPivot, _origAnchoredPos, _origSizeDelta;
        private static CanvasGroup _tabsCg;   // the game's own tab strip, faded while hosted

        // Persistent hide of the game's level-settings panel (so it stops eating the left of the
        // screen): a CanvasGroup we drive to alpha 0 while the popup is closed.
        private static CanvasGroup _panelCg;

        // 1920×1080-reference canvas: wide panel — a popup isn't screen-estate constrained.
        private const float RailW = 210f, PanelW = 900f, PanelH = 840f, Pad = 12f;

        internal static bool IsOpen => _open;
        // True while we're keeping the game panel off-screen — the Sapphire settings rail suppresses
        // itself then (the panel is reached only through the popup).
        internal static bool ManagesPanel { get; private set; }

        internal static void Toggle() { if (_open) Close(); else Open(); }

        internal static void Open()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            var panel = ed != null ? ed.settingsPanel : null;
            if (panel == null) { SapphireLog.Log("LevelMenu: no settingsPanel"); return; }
            _panelRect = panel.GetComponent<RectTransform>();
            if (_panelRect == null) { SapphireLog.Log("LevelMenu: settingsPanel has no RectTransform"); return; }

            try { panel.ShowInspector(true, true); } catch { } // enable contents (skip the slide)

            EnsureCanvas();
            BuildPopup();

            // capture the panel's current transform so Close can put it back exactly
            _origParent = _panelRect.parent;
            _origSibling = _panelRect.GetSiblingIndex();
            _origAnchorMin = _panelRect.anchorMin; _origAnchorMax = _panelRect.anchorMax;
            _origPivot = _panelRect.pivot; _origAnchoredPos = _panelRect.anchoredPosition;
            _origSizeDelta = _panelRect.sizeDelta;

            _panelRect.SetParent(_hostRect, false);
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;
            _panelRect.sizeDelta = new Vector2(PanelW, PanelH);
            ShowPanelInPlace();   // un-hide it now that it lives in the popup

            // Our labeled rail replaces the game's icon strip while hosted.
            BuildRail(panel);
            try
            {
                if (panel.tabs != null)
                {
                    var go = panel.tabs.gameObject;
                    _tabsCg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
                    _tabsCg.alpha = 0f; _tabsCg.blocksRaycasts = false;
                }
            }
            catch { }

            _open = true;
        }

        internal static void Close()
        {
            if (_tabsCg != null)
            {
                try { _tabsCg.alpha = 1f; _tabsCg.blocksRaycasts = true; } catch { }
                _tabsCg = null;
            }
            if (_panelRect != null && _origParent != null)
            {
                try
                {
                    _panelRect.SetParent(_origParent, false);
                    _panelRect.SetSiblingIndex(_origSibling);
                    _panelRect.anchorMin = _origAnchorMin; _panelRect.anchorMax = _origAnchorMax;
                    _panelRect.pivot = _origPivot;
                    _panelRect.sizeDelta = _origSizeDelta;
                    _panelRect.anchoredPosition = _origAnchoredPos;
                    var ed = scnEditor.instance;
                    if (ed != null && ed.settingsPanel != null) ed.settingsPanel.ShowInspector(false, true);
                }
                catch (Exception ex) { SapphireLog.Log("LevelMenu: restore failed: " + ex.Message); }
            }
            if (_popupGo != null) UnityEngine.Object.Destroy(_popupGo);
            _popupGo = null; _cardRect = null; _hostRect = null; _railRect = null;
            _panelRect = null; _origParent = null;
            _rowBgs.Clear(); _rowTabs.Clear(); _rowSelected = -2;
            _helpSeen.Clear(); _helpCooldown = 0;
            _open = false;
        }

        internal static void Tick()
        {
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }

            if (_open)
            {
                if (ed == null || ed.playMode || _panelRect == null || ed.settingsPanel == null
                    || !MainClass.EditorSuiteOn)
                { Close(); return; }
                if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }
                // Hold it in our card against the game's own slide/relayout.
                if (_hostRect != null && _panelRect.parent != _hostRect)
                    _panelRect.SetParent(_hostRect, false);
                _panelRect.anchoredPosition = Vector2.zero;
                var want = new Vector2(PanelW, PanelH);
                if (_panelRect.sizeDelta != want) _panelRect.sizeDelta = want;
                if (--_helpCooldown <= 0) { _helpCooldown = 20; ShrinkHelpButtons(); }
                SyncRailHighlight(ed.settingsPanel);
                ManagesPanel = true;
                return;
            }

            // Popup closed: keep the game's level-settings panel off-screen. The Level-settings
            // chip (→ this popup) is the only way to reach it. Vanilla ESC toggles this panel —
            // that would "open" invisible UI (the phantom the user keeps hitting), so shut it
            // the moment the game raises it.
            bool inEditor = ed != null && !ed.playMode && ed.settingsPanel != null && MainClass.EditorSuiteOn;
            if (inEditor)
            {
                HidePanelInPlace(ed);
                ManagesPanel = true;
                try
                {
                    if (ed.settingsPanel.showInspector) ed.settingsPanel.ShowInspector(false, true);
                }
                catch { }
            }
            else
            {
                ShowPanelInPlace();
                RestorePanelGeometry();
                ManagesPanel = false;
            }
        }

        // Belt-and-braces: if our wide hosted size ever survives a hand-back (mod toggled
        // off mid-layout, exception during Close), re-assert the captured vanilla geometry.
        private static void RestorePanelGeometry()
        {
            if (_panelRect == null || _origParent == null) return;
            try
            {
                if (_panelRect.parent != _origParent) return; // still hosted → Close() owns it
                if ((_panelRect.sizeDelta - _origSizeDelta).sqrMagnitude > 1f)
                {
                    _panelRect.anchorMin = _origAnchorMin; _panelRect.anchorMax = _origAnchorMax;
                    _panelRect.pivot = _origPivot;
                    _panelRect.sizeDelta = _origSizeDelta;
                    _panelRect.anchoredPosition = _origAnchoredPos;
                }
            }
            catch { }
        }

        private static void HidePanelInPlace(scnEditor ed)
        {
            var go = ed.settingsPanel.gameObject;
            if (_panelCg == null || _panelCg.gameObject != go)
                _panelCg = go.GetComponent<CanvasGroup>() ?? go.AddComponent<CanvasGroup>();
            if (_panelCg.alpha != 0f) { _panelCg.alpha = 0f; _panelCg.blocksRaycasts = false; }
        }

        private static void ShowPanelInPlace()
        {
            if (_panelCg != null) { _panelCg.alpha = 1f; _panelCg.blocksRaycasts = true; }
        }

        internal static void Dispose()
        {
            if (_open) Close();
            ShowPanelInPlace();   // leave the game panel visible if the mod is turned off
            _panelCg = null; ManagesPanel = false;
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
        }

        private static void EnsureCanvas()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SapphireLevelMenu", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 909; // above the tile menu / popups
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
        }

        private static void BuildPopup()
        {
            _popupGo = new GameObject("Popup", typeof(RectTransform));
            _popupGo.transform.SetParent(_canvasGo.transform, false);
            var blocker = (RectTransform)_popupGo.transform;
            blocker.anchorMin = Vector2.zero; blocker.anchorMax = Vector2.one;
            blocker.offsetMin = Vector2.zero; blocker.offsetMax = Vector2.zero;
            var blockImg = _popupGo.AddComponent<Image>();
            blockImg.color = new Color(0f, 0f, 0f, 0.4f);
            blockImg.raycastTarget = true; // swallows stray clicks; ESC or the chip closes

            var cardGo = new GameObject("Card", typeof(RectTransform));
            cardGo.transform.SetParent(_popupGo.transform, false);
            _cardRect = (RectTransform)cardGo.transform;
            _cardRect.anchorMin = _cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            _cardRect.pivot = new Vector2(0.5f, 0.5f);
            _cardRect.anchoredPosition = Vector2.zero;
            _cardRect.sizeDelta = new Vector2(Pad + RailW + Pad + PanelW + Pad, PanelH + Pad * 2f);
            var cardBg = cardGo.AddComponent<RoundedRectGraphic>();
            cardBg.Radius = 14f;
            cardBg.color = new Color(0.07f, 0.07f, 0.09f, 0.98f);
            cardBg.BorderWidth = 1f;
            cardBg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            cardBg.raycastTarget = true; // clicks on the card must not close it

            var railGo = new GameObject("Rail", typeof(RectTransform));
            railGo.transform.SetParent(cardGo.transform, false);
            _railRect = (RectTransform)railGo.transform;
            _railRect.anchorMin = new Vector2(0f, 0f); _railRect.anchorMax = new Vector2(0f, 1f);
            _railRect.pivot = new Vector2(0f, 1f);
            _railRect.anchoredPosition = new Vector2(Pad, -Pad);
            _railRect.sizeDelta = new Vector2(RailW, -Pad * 2f);

            var hostGo = new GameObject("Host", typeof(RectTransform));
            hostGo.transform.SetParent(cardGo.transform, false);
            _hostRect = (RectTransform)hostGo.transform;
            _hostRect.anchorMin = new Vector2(1f, 0.5f); _hostRect.anchorMax = new Vector2(1f, 0.5f);
            _hostRect.pivot = new Vector2(1f, 0.5f);
            _hostRect.anchoredPosition = new Vector2(-Pad, 0f);
            _hostRect.sizeDelta = new Vector2(PanelW, PanelH);
        }

        // ── labeled tab rail (Ctrl+E style) ─────────────────────────────────
        private static void BuildRail(ADOFAI.InspectorPanel panel)
        {
            _rowBgs.Clear(); _rowTabs.Clear(); _rowSelected = -2;
            RectTransform tabsRt = null;
            try { tabsRt = panel.tabs; } catch { }
            if (tabsRt == null || _railRect == null) return;

            const float rowH = 44f, rowGap = 6f;
            float y = 0f;
            for (int i = 0; i < tabsRt.childCount; i++)
            {
                var tabTr = tabsRt.GetChild(i);
                if (!tabTr.gameObject.activeSelf) continue;
                var tab = tabTr.GetComponent<ADOFAI.InspectorTab>();
                if (tab == null) continue;

                var rowGo = new GameObject("Row", typeof(RectTransform));
                rowGo.transform.SetParent(_railRect, false);
                var rr = (RectTransform)rowGo.transform;
                rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f);
                rr.pivot = new Vector2(0.5f, 1f);
                rr.offsetMin = new Vector2(0f, 0f); rr.offsetMax = new Vector2(0f, 0f);
                rr.anchoredPosition = new Vector2(0f, y);
                rr.sizeDelta = new Vector2(0f, rowH);
                var bg = rowGo.AddComponent<RoundedRectGraphic>();
                bg.Radius = 9f;
                bg.color = new Color(1f, 1f, 1f, 0.05f);
                bg.raycastTarget = true;
                _rowBgs.Add(bg);
                _rowTabs.Add(tab);

                var sprite = FindIcon(tabTr);
                if (sprite != null)
                {
                    var iconGo = new GameObject("Icon", typeof(RectTransform));
                    iconGo.transform.SetParent(rowGo.transform, false);
                    var ir = (RectTransform)iconGo.transform;
                    ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f);
                    ir.pivot = new Vector2(0f, 0.5f);
                    ir.anchoredPosition = new Vector2(10f, 0f);
                    ir.sizeDelta = new Vector2(24f, 24f);
                    var img = iconGo.AddComponent<Image>();
                    img.sprite = sprite;
                    img.preserveAspect = true;
                    img.raycastTarget = false;
                }

                var lblGo = new GameObject("Label", typeof(RectTransform));
                lblGo.transform.SetParent(rowGo.transform, false);
                var lr = (RectTransform)lblGo.transform;
                lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
                lr.offsetMin = new Vector2(44f, 0f); lr.offsetMax = new Vector2(-8f, 0f);
                var lbl = UIBuilder.Tmp(lblGo, TabName(tab), 14f, TextAnchor.MiddleLeft, Theme.Text);
                lbl.raycastTarget = false;

                var tabGo = tabTr.gameObject;
                UI.ClickHandler.Attach(rowGo, () => EditorChrome.ProxyClick(tabGo));

                y -= rowH + rowGap;
            }
        }

        private static void SyncRailHighlight(ADOFAI.InspectorPanel panel)
        {
            ADOFAI.InspectorTab sel = null;
            try { sel = panel.GetSelectedEventTab(); } catch { }
            int idx = sel != null ? _rowTabs.IndexOf(sel) : -1;
            if (idx == _rowSelected) return;
            _rowSelected = idx;
            for (int i = 0; i < _rowBgs.Count; i++)
            {
                if (_rowBgs[i] == null) continue;
                _rowBgs[i].color = i == idx
                    ? new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.45f)
                    : new Color(1f, 1f, 1f, 0.05f);
            }
        }

        // The game's "?" help buttons render oversized in the widened panel and sit on top of
        // the fields. Shrink any button whose visible label is just "?" to a row-sized square.
        // Throttled re-sweep: rows rebuild when tabs switch.
        private static int _helpCooldown;
        private static readonly HashSet<int> _helpSeen = new HashSet<int>();

        private static void ShrinkHelpButtons()
        {
            if (_panelRect == null) return;
            try
            {
                foreach (var b in _panelRect.GetComponentsInChildren<Button>(true))
                {
                    if (b == null || !_helpSeen.Add(b.GetInstanceID())) continue;
                    string label = null;
                    var tmp = b.GetComponentInChildren<TMPro.TMP_Text>(true);
                    if (tmp != null) label = tmp.text;
                    else
                    {
                        var txt = b.GetComponentInChildren<Text>(true);
                        if (txt != null) label = txt.text;
                    }
                    if (label == null || label.Trim() != "?") continue;
                    var rt = (RectTransform)b.transform;
                    rt.sizeDelta = new Vector2(24f, 24f);
                }
            }
            catch { }
        }

        private static Sprite FindIcon(Transform root)
        {
            try
            {
                foreach (var img in root.GetComponentsInChildren<Image>(true))
                    if (img != null && img.sprite != null) return img.sprite;
            }
            catch { }
            return null;
        }

        // Localized tab names — the game's own panel titles use RDString.Get("editor." + type)
        // (e.g. "editor.SongSettings" → "곡 설정"); fall back to the prettified enum name.
        private static string TabName(ADOFAI.InspectorTab tab)
        {
            string s;
            try { s = tab.levelEventType.ToString(); }
            catch { s = tab.name; }
            try
            {
                string loc = RDString.Get("editor." + s);
                if (!string.IsNullOrEmpty(loc) && loc != "editor." + s) return loc;
            }
            catch { }
            if (s.EndsWith("Settings") && s.Length > 8) s = s.Substring(0, s.Length - 8);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' ');
                sb.Append(s[i]);
            }
            return sb.ToString();
        }
    }
}
