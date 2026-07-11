using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityModManagerNet;

namespace Sapphire.UI
{
    // Searchable index of settings controls, harvested automatically as tabs build their
    // ROOT views (widget factories call Register with the ambient tab context; subpage
    // builds are suspended — their entry points are the indexed NavRows/cards, whose
    // captured onOpen navigates into them).
    internal static class SettingsSearch
    {
        internal class Entry
        {
            public string Label;
            public string Tab;
            public int TabIndex;
            public Action Open;      // optional subpage opener (NavRow/NavCard onOpen)
            public string[] Keywords; // comma-separated phrases at registration — subpage contents
        }

        internal class Result
        {
            public Entry E;
            public string Via; // matched keyword phrase, null when the label itself matched
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static string _tab;
        private static int _tabIndex = -1;
        private static int _suspend;

        internal static void Clear() { _entries.Clear(); _tab = null; _tabIndex = -1; _suspend = 0; }
        internal static void BeginTab(string name, int idx) { _tab = name; _tabIndex = idx; }
        internal static void EndTab() { _tab = null; _tabIndex = -1; }
        internal static void Suspend(bool on) { _suspend += on ? 1 : -1; }

        internal static void Register(string label, Action open = null, string keywords = null)
        {
            // <2 chars filters color-channel sliders (R/G/B/A) and other non-entries.
            if (_suspend > 0 || _tabIndex < 0 || string.IsNullOrEmpty(label) || label.Trim().Length < 2)
                return;
            string[] kw = null;
            if (!string.IsNullOrEmpty(keywords))
            {
                kw = keywords.Split(',');
                for (int i = 0; i < kw.Length; i++) kw[i] = kw[i].Trim();
            }
            _entries.Add(new Entry { Label = label.Trim(), Tab = _tab, TabIndex = _tabIndex, Open = open, Keywords = kw });
        }

        internal static List<Result> Query(string q, int max)
        {
            var result = new List<Result>();
            if (string.IsNullOrWhiteSpace(q)) return result;
            q = q.Trim();
            foreach (var e in _entries) // label prefix matches rank first
                if (e.Label.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                    AddUnique(result, e, null, max);
            foreach (var e in _entries)
            {
                if (result.Count >= max) break;
                if (e.Label.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    AddUnique(result, e, null, max);
            }
            foreach (var e in _entries) // keyword matches rank last, shown with the hit
            {
                if (result.Count >= max) break;
                if (e.Keywords == null) continue;
                foreach (var k in e.Keywords)
                    if (k.Length > 0 && k.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        AddUnique(result, e, k, max);
                        break;
                    }
            }
            return result;
        }

        private static void AddUnique(List<Result> list, Entry e, string via, int max)
        {
            if (list.Count >= max) return;
            foreach (var x in list)
                if (x.E.TabIndex == e.TabIndex && x.E.Label == e.Label) return;
            list.Add(new Result { E = e, Via = via });
        }
    }

    internal static class UICore
    {
        private const string ToggleHint = "Ctrl + E";
        private static readonly Vector2 ReferenceResolution = new Vector2(1920, 1080);

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static CanvasScaler _scaler;
        private static RectTransform _panel;
        private static TabRail _tabs;
        private static bool _isOpen;
        private static Action _onSettingsChanged;
        private static Settings _settings;
        private static UnityModManager.ModEntry _modEntry;
        private static List<FontLoader.FontEntry> _availableFonts;

        public static TabRail Tabs { get { return _tabs; } }
        public static Settings Settings { get { return _settings; } }
        public static Action OnSettingsChanged { get { return _onSettingsChanged; } }
        // Separate from OnSettingsChanged because Key Viewer structural changes
        // (add/remove preset, switch active preset) need a full layout rebuild, not just
        // ApplySettings's color/position re-push. Wired by MainClass.
        public static Action OnKeyViewerRebuild;
        public static bool IsOpen { get { return _isOpen; } }
        public static GameObject CanvasRoot { get { return _canvasGo; } }
        public static IList<FontLoader.FontEntry> AvailableFonts { get { return _availableFonts; } }

        public static void Initialize(UnityModManager.ModEntry modEntry, Settings settings, Action onChanged, List<FontLoader.FontEntry> fonts = null)
        {
            if (_canvasGo != null) { SapphireLog.Log("[UI] Initialize skipped (already built)"); return; }
            _modEntry = modEntry;
            _settings = settings;
            _onSettingsChanged = onChanged;
            _availableFonts = fonts ?? new List<FontLoader.FontEntry>();

            try
            {
                // Apply saved theme BEFORE building so widgets pick up the right Accent/Font.
                Theme.ApplyAccent(new Color(settings.UiAccentR, settings.UiAccentG, settings.UiAccentB, 1f));
                var savedFont = ResolveSavedFont();
                if (savedFont != null) Theme.ApplyFont(savedFont);

                BuildRoot();
                BuildPanel();
                BuildTitleBar();
                BuildBody();
                BuildResizeGrip();
                // Re-attach resize handles LAST so they sit on top in sibling order, else the
                // footer/titlebar/rail render over them and absorb drags (grip visible but
                // unresponsive). Handles' raycast Images are transparent, so the dots show through.
                ResizeHandle.AttachAll(_panel);
                ApplyScale(_settings.UiScale);
                PrewarmGlyphs();
                _canvasGo.SetActive(false);
                SapphireLog.Log("[UI] Initialized — hotkey Ctrl+B armed");
            }
            catch (Exception ex)
            {
                SapphireLog.Log("[UI] Initialize FAILED: " + ex);
                if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
                _canvasGo = null;
            }
        }

        // Pay the deferred first-activation cost now, at build time, instead of on the first
        // Open(): lay the panel out and rasterize every TMP glyph into the dynamic SDF atlas
        // up front (incl. inactive tabs — they share the atlas). Unity skips both for inactive
        // objects, which is why the first open otherwise hitches.
        private static void PrewarmGlyphs()
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                LayoutRebuilder.ForceRebuildLayoutImmediate(_panel);
                var texts = _canvasGo.GetComponentsInChildren<TMP_Text>(true);
                foreach (var t in texts) if (t != null) t.ForceMeshUpdate();
                sw.Stop();
                SapphireLog.Debug($"[UI] prewarm: {texts.Length} TMP texts laid out + rasterized in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) { SapphireLog.Log("[UI] prewarm skipped: " + ex.Message); }
        }

        private static TMP_FontAsset ResolveSavedFont()
        {
            var entry = FontLoader.Find(_availableFonts, _settings.UiFontName)
                        ?? (_availableFonts.Count > 0 ? _availableFonts[0] : null);
            return entry?.TmpFont;
        }

        // Tracks the scale at which the panel's sizeDelta + anchoredPosition currently make sense.
        // Starts at 1.0 because BuildPanel sets canonical (840×540) at canvas-scale=1.0 units.
        private static float _appliedScale = 1f;

        public static void ApplyScale(float scale)
        {
            if (_scaler == null) return;
            scale = Mathf.Clamp(scale, 0.5f, 2f);
            _settings.UiScale = scale;
            // Bigger UI scale → smaller reference resolution → canvas-scale multiplies up,
            // so widget fontSize / preferredHeight / row spacing all render larger.
            _scaler.referenceResolution = ReferenceResolution / scale;

            // Counter-resize the panel rect so its on-screen size stays constant: screen-px =
            // sizeDelta × canvasScale ∝ uiScale, so sizeDelta shrinks by (applied/new). Same
            // for anchoredPosition, pinning a dragged-off-center panel to the same screen point.
            if (_panel != null && _appliedScale > 0.001f)
            {
                float ratio = _appliedScale / scale;
                _panel.sizeDelta *= ratio;
                _panel.anchoredPosition *= ratio;
            }
            _appliedScale = scale;
        }

        public static void ApplyAccent(Color color)
        {
            _settings.UiAccentR = color.r;
            _settings.UiAccentG = color.g;
            _settings.UiAccentB = color.b;
            Theme.ApplyAccent(color, _canvasGo);
        }

        public static void ApplyFont(FontLoader.FontEntry entry)
        {
            if (entry == null || entry.TmpFont == null) return;
            _settings.UiFontName = entry.Name;
            Theme.ApplyFont(entry.TmpFont, _canvasGo);
        }

        public static void Dispose()
        {
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            _canvas = null;
            _panel = null;
            _tabs = null;
            _isOpen = false;
        }

        private static void BuildRoot()
        {
            _canvasGo = new GameObject("SapphireUI", typeof(RectTransform));
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);

            _canvas = _canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 32000;

            _scaler = _canvasGo.AddComponent<CanvasScaler>();
            _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            _scaler.referenceResolution = ReferenceResolution;
            _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            _scaler.matchWidthOrHeight = 0.5f;

            _canvasGo.AddComponent<GraphicRaycaster>();

            // Sapphire runs alongside the game's own EventSystem. Only add one if none exists;
            // parent it OUTSIDE our DDOL canvas so it isn't force-kept once the game supplies one.
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("SapphireEventSystem");
                UnityEngine.Object.DontDestroyOnLoad(esGo);
                esGo.AddComponent<EventSystem>();
                esGo.AddComponent<StandaloneInputModule>();
            }
        }

        // Two EventSystems in one scene (ours, DDOL, + the game's editor one) makes Unity flake
        // between them — dead TMP carets, lost typing. When the game provides one, drop ours.
        internal static void DedupEventSystem()
        {
            EventSystem[] all;
            try { all = UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None); }
            catch { return; }
            if (all == null || all.Length < 2) return;

            bool hasGameOwned = false;
            foreach (var e in all)
                if (e != null && e.gameObject.name != "SapphireEventSystem") { hasGameOwned = true; break; }
            if (!hasGameOwned) return;

            foreach (var e in all)
            {
                if (e != null && e.gameObject.name == "SapphireEventSystem")
                {
                    SapphireLog.Log("Dropping redundant SapphireEventSystem (game supplies one)");
                    UnityEngine.Object.Destroy(e.gameObject);
                }
            }
        }

        private static void BuildPanel()
        {
            var panelGo = UIBuilder.Rect("Panel", _canvasGo.transform);
            _panel = (RectTransform)panelGo.transform;
            _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0.5f);
            _panel.pivot = new Vector2(0.5f, 0.5f);
            // Restore saved dimensions; ApplyScale will adjust for current UI scale right after.
            _panel.sizeDelta = new Vector2(_settings.UiPanelWidth, _settings.UiPanelHeight);
            _panel.anchoredPosition = Vector2.zero;

            UIBuilder.SolidImage(panelGo, Theme.Panel);
            UIBuilder.AddBorder(panelGo, Theme.PanelBorder, 1f);
            // ResizeHandle.AttachAll is called from Initialize() as the very last step,
            // so handles sit on top of the footer / titlebar / rail and catch drags in
            // their overlapping edge zones.
        }

        // Visible 6-dot staircase in the bottom-right corner — affordance for the existing
        // invisible bottom-right ResizeHandle. The dots have raycastTarget=false so drag
        // events fall through to the underlying handle.
        private static void BuildResizeGrip()
        {
            var gripGo = UIBuilder.Rect("ResizeGrip", _panel);
            var gripRect = (RectTransform)gripGo.transform;
            gripRect.anchorMin = new Vector2(1, 0);
            gripRect.anchorMax = new Vector2(1, 0);
            gripRect.pivot = new Vector2(1, 0);
            gripRect.sizeDelta = new Vector2(16f, 16f);
            gripRect.anchoredPosition = new Vector2(-4f, 4f);

            // Staircase pointing into the corner: 3 dots bottom, 2 dots middle, 1 dot top.
            // Coordinates are bottom-left anchored inside the 16×16 grip box.
            Vector2[] dotPositions = new[]
            {
                new Vector2(0f, 0f),  new Vector2(6f, 0f),  new Vector2(12f, 0f),
                new Vector2(6f, 6f),  new Vector2(12f, 6f),
                new Vector2(12f, 12f),
            };
            foreach (var pos in dotPositions)
            {
                var dotGo = UIBuilder.Rect("Dot", gripGo.transform);
                var dotRect = (RectTransform)dotGo.transform;
                dotRect.anchorMin = new Vector2(0, 0);
                dotRect.anchorMax = new Vector2(0, 0);
                dotRect.pivot = new Vector2(0, 0);
                dotRect.sizeDelta = new Vector2(3f, 3f);
                dotRect.anchoredPosition = pos;
                var img = dotGo.AddComponent<Image>();
                img.sprite = Theme.White;
                img.color = Theme.TextMuted;
                img.raycastTarget = false;
            }
        }

        private static RectTransform _railRect;
        private static RectTransform _pageHostRect;

        // ── Settings search (titlebar) ─────────────────────────────────────

        private static TMP_InputField _searchInput;
        private static RectTransform _searchBox;
        private static GameObject _searchPopup;

        private static void BuildSearchBar(GameObject bar)
        {
            var box = UIBuilder.Rect("Search", bar.transform);
            _searchBox = (RectTransform)box.transform;
            _searchBox.anchorMin = _searchBox.anchorMax = new Vector2(1f, 0.5f);
            _searchBox.pivot = new Vector2(1f, 0.5f);
            _searchBox.anchoredPosition = new Vector2(-52f, 0f);
            _searchBox.sizeDelta = new Vector2(220f, 24f);
            var bg = box.AddComponent<RoundedRectGraphic>();
            bg.Radius = 4f;
            bg.AAFringe = 0.5f;
            bg.color = new Color(1f, 1f, 1f, 0.06f);
            bg.raycastTarget = true;

            var txtGo = UIBuilder.Rect("Text", box.transform);
            var txtRect = (RectTransform)txtGo.transform;
            txtRect.anchorMin = Vector2.zero;
            txtRect.anchorMax = Vector2.one;
            txtRect.offsetMin = new Vector2(8f, 0);
            txtRect.offsetMax = new Vector2(-8f, 0);
            var txt = UIBuilder.Tmp(txtGo, "", 13, TextAnchor.MiddleLeft, Theme.Text);
            txt.richText = false;

            var phGo = UIBuilder.Rect("Placeholder", box.transform);
            var phRect = (RectTransform)phGo.transform;
            phRect.anchorMin = Vector2.zero;
            phRect.anchorMax = Vector2.one;
            phRect.offsetMin = new Vector2(8f, 0);
            phRect.offsetMax = new Vector2(-8f, 0);
            var ph = UIBuilder.Tmp(phGo, "Search settings...", 13, TextAnchor.MiddleLeft, Theme.TextMuted);

            _searchInput = UIBuilder.BuildInputField(box, txt);
            _searchInput.placeholder = ph;
            _searchInput.onValueChanged.AddListener(RefreshSearchResults);
            _searchInput.onSelect.AddListener(_ => RefreshSearchResults(_searchInput.text));
            // Deselect fires on the same pointer-down that may be selecting a result row,
            // so hide with a grace window; result rows navigate on pointer-down.
            _searchInput.onDeselect.AddListener(_ =>
            {
                if (_searchPopup == null) return;
                var d = _searchPopup.GetComponent<DeferredDestroy>() ?? _searchPopup.AddComponent<DeferredDestroy>();
                d.Frames = 8;
            });
        }

        private static void HideSearchPopup()
        {
            if (_searchPopup == null) return;
            UnityEngine.Object.Destroy(_searchPopup);
            _searchPopup = null;
        }

        private static void RefreshSearchResults(string q)
        {
            HideSearchPopup();
            var results = SettingsSearch.Query(q, 9);
            if (results.Count == 0) return;

            const float rowH = 26f;
            _searchPopup = UIBuilder.Rect("SearchResults", _canvasGo.transform);
            var pRect = (RectTransform)_searchPopup.transform;
            pRect.pivot = new Vector2(1f, 1f);
            pRect.sizeDelta = new Vector2(340f, results.Count * rowH + 10f);
            UIBuilder.SolidImage(_searchPopup, Theme.Panel);
            UIBuilder.AddBorder(_searchPopup, Theme.PanelBorder, 1f);

            var corners = new Vector3[4];
            _searchBox.GetWorldCorners(corners);
            float sf = _canvas != null ? _canvas.scaleFactor : 1f;
            _searchPopup.transform.position = corners[3] + new Vector3(0f, -4f * sf, 0f); // below bottom-right

            var list = UIBuilder.VGroup(_searchPopup.transform, "List", 0f);
            var lRect = (RectTransform)list.transform;
            lRect.anchorMin = Vector2.zero;
            lRect.anchorMax = Vector2.one;
            lRect.offsetMin = new Vector2(1f, 1f);
            lRect.offsetMax = new Vector2(-1f, -1f);
            list.GetComponent<VerticalLayoutGroup>().padding = new RectOffset(0, 0, 4, 4);

            foreach (var r in results)
            {
                var entry = r.E;
                var row = UIBuilder.Rect("R", list.transform);
                var le = row.AddComponent<LayoutElement>();
                le.preferredHeight = rowH;
                le.minHeight = rowH;
                var rbg = UIBuilder.SolidImage(row, new Color(0, 0, 0, 0));
                rbg.raycastTarget = true;
                var hh = row.AddComponent<HoverHandler>();
                hh.OnEnter = () => rbg.color = Theme.RowBgHover;
                hh.OnExit = () => rbg.color = new Color(0, 0, 0, 0);

                string text = entry.Tab + "  ›  " + entry.Label;
                if (r.Via != null) text += "   <color=#94949E>· " + r.Via + "</color>";
                var lbl = UIBuilder.Label(row.transform, text, 13, TextAnchor.MiddleLeft, Theme.Text);
                lbl.rectTransform.offsetMin = new Vector2(10f, 0);
                lbl.rectTransform.offsetMax = new Vector2(-10f, 0);

                var down = row.AddComponent<SearchRowDown>();
                down.OnDown = () =>
                {
                    _tabs?.OpenSearchResult(entry.TabIndex, entry.Open);
                    if (_searchInput != null) _searchInput.SetTextWithoutNotify("");
                    HideSearchPopup();
                };
            }
        }

        private static void BuildTitleBar()
        {
            const float titleH = 36f;
            var bar = UIBuilder.Rect("TitleBar", _panel);
            var r = (RectTransform)bar.transform;
            r.anchorMin = new Vector2(0, 1);
            r.anchorMax = new Vector2(1, 1);
            r.pivot = new Vector2(0.5f, 1f);
            r.sizeDelta = new Vector2(0, titleH);
            r.anchoredPosition = Vector2.zero;
            UIBuilder.SolidImage(bar, Theme.TitleBar);

            // Single bottom divider line — sharper than a full border
            var divider = UIBuilder.Rect("Divider", bar.transform);
            var dr = (RectTransform)divider.transform;
            dr.anchorMin = new Vector2(0, 0);
            dr.anchorMax = new Vector2(1, 0);
            dr.offsetMin = Vector2.zero;
            dr.offsetMax = new Vector2(0, 1f);
            var dImg = divider.AddComponent<Image>();
            dImg.sprite = Theme.White;
            dImg.color = Theme.PanelBorder;
            dImg.raycastTarget = false;

            bar.AddComponent<DragHandle>();

            // Title
            var titleGo = UIBuilder.Rect("Title", bar.transform);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = new Vector2(0, 0);
            tr.anchorMax = new Vector2(1, 1);
            tr.offsetMin = new Vector2(12f, 0f);
            tr.offsetMax = new Vector2(-280f, 0f); // clear of the search box + close button
            var title = titleGo.AddComponent<TextMeshProUGUI>();
            title.text = "Sapphire";
            title.font = Theme.TmpFont;
            title.fontSize = 15;
            title.color = Theme.Text;
            title.fontStyle = FontStyles.Bold;
            title.alignment = TextAlignmentOptions.Left;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Overflow;
            title.raycastTarget = false;

            BuildSearchBar(bar);

            // Close — Windows-style: a full-height strip flush with the corner that
            // floods solid red on hover.
            var close = UIBuilder.Rect("Close", bar.transform);
            var cr = (RectTransform)close.transform;
            cr.anchorMin = new Vector2(1f, 0f);
            cr.anchorMax = new Vector2(1f, 1f);
            cr.pivot = new Vector2(1f, 0.5f);
            cr.anchoredPosition = Vector2.zero;
            cr.sizeDelta = new Vector2(44f, 0f);
            var closeBg = UIBuilder.SolidImage(close, new Color(0, 0, 0, 0));
            closeBg.raycastTarget = true;
            var x = UIBuilder.Label(close.transform, "×", 22, TextAnchor.MiddleCenter, Theme.Text);
            var closeHover = close.AddComponent<HoverHandler>();
            closeHover.OnEnter = () => { closeBg.color = Theme.CloseHover; x.color = Color.white; };
            closeHover.OnExit = () => { closeBg.color = new Color(0, 0, 0, 0); x.color = Theme.Text; };
            ClickHandler.Attach(close, () => Close());
        }

        private static void BuildBody()
        {
            const float titleH = 36f;
            const float footerH = 26f;

            // Left rail
            var rail = UIBuilder.Rect("Rail", _panel);
            _railRect = (RectTransform)rail.transform;
            _railRect.anchorMin = new Vector2(0, 0);
            _railRect.anchorMax = new Vector2(0, 1);
            _railRect.pivot = new Vector2(0, 0.5f);
            _railRect.sizeDelta = new Vector2(TabRail.Width, 0);
            _railRect.offsetMin = new Vector2(0, footerH);
            _railRect.offsetMax = new Vector2(TabRail.Width, -titleH);

            // Rail divider
            var rd = UIBuilder.Rect("RailDivider", _panel);
            var rdr = (RectTransform)rd.transform;
            rdr.anchorMin = new Vector2(0, 0);
            rdr.anchorMax = new Vector2(0, 1);
            rdr.pivot = new Vector2(0, 0.5f);
            rdr.sizeDelta = new Vector2(1f, 0);
            rdr.offsetMin = new Vector2(TabRail.Width, footerH);
            rdr.offsetMax = new Vector2(TabRail.Width + 1f, -titleH);
            var rdImg = rd.AddComponent<Image>();
            rdImg.sprite = Theme.White;
            rdImg.color = Theme.PanelBorder;
            rdImg.raycastTarget = false;

            // Page host (right of rail, below title, above footer)
            var pageHost = UIBuilder.Rect("PageHost", _panel);
            _pageHostRect = (RectTransform)pageHost.transform;
            _pageHostRect.anchorMin = new Vector2(0, 0);
            _pageHostRect.anchorMax = new Vector2(1, 1);
            _pageHostRect.offsetMin = new Vector2(TabRail.Width + 1f, footerH);
            _pageHostRect.offsetMax = new Vector2(0, -titleH);

            // Footer
            var footer = UIBuilder.Rect("Footer", _panel);
            var fr = (RectTransform)footer.transform;
            fr.anchorMin = new Vector2(0, 0);
            fr.anchorMax = new Vector2(1, 0);
            fr.pivot = new Vector2(0.5f, 0f);
            fr.sizeDelta = new Vector2(0, footerH);
            fr.anchoredPosition = Vector2.zero;
            UIBuilder.SolidImage(footer, Theme.TitleBar);

            // Footer top divider
            var fd = UIBuilder.Rect("FooterDivider", footer.transform);
            var fdr = (RectTransform)fd.transform;
            fdr.anchorMin = new Vector2(0, 1);
            fdr.anchorMax = new Vector2(1, 1);
            fdr.offsetMin = new Vector2(0, -1f);
            fdr.offsetMax = Vector2.zero;
            var fdImg = fd.AddComponent<Image>();
            fdImg.sprite = Theme.White;
            fdImg.color = Theme.PanelBorder;
            fdImg.raycastTarget = false;

            var hint = UIBuilder.Label(footer.transform, ToggleHint + " to toggle", 12, TextAnchor.MiddleLeft, Theme.TextMuted);
            hint.rectTransform.offsetMin = new Vector2(10f, 0f);
            hint.rectTransform.offsetMax = new Vector2(-10f, 0f);

            string ver = (_modEntry != null && _modEntry.Info != null) ? _modEntry.Info.Version : "";
            var ver2 = UIBuilder.Label(footer.transform, "v" + ver, 12, TextAnchor.MiddleRight, Theme.TextMuted);
            ver2.rectTransform.offsetMin = new Vector2(10f, 0f);
            // Right offset kept clear of the resize grip's hit area in the bottom-right corner.
            ver2.rectTransform.offsetMax = new Vector2(-28f, 0f);

            _tabs = new TabRail(_railRect, _pageHostRect);
        }

        public static void HandleUpdate()
        {
            if (_canvasGo == null) return;
            bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
            if (ctrl && Input.GetKeyDown(KeyCode.E)) Toggle();
        }

        public static void Toggle() { if (_isOpen) Close(); else Open(); }

        public static void Open()
        {
            if (_isOpen) return;
            _isOpen = true;
            // Always re-center on open. Dimensions are restored from settings on Build /
            // re-applied via ApplyScale; only position resets to (0,0).
            if (_panel != null) _panel.anchoredPosition = Vector2.zero;
            _canvasGo.SetActive(true);
            Cursor.visible = true;
            Cursor.lockState = CursorLockMode.None;
        }

        public static void Close()
        {
            if (!_isOpen) return;
            _isOpen = false;
            HideSearchPopup();
            if (_searchInput != null) _searchInput.SetTextWithoutNotify("");
            // Save dimensions in canonical (scale=1.0) units so they restore across UI scale
            // changes. ApplyScale scales sizeDelta inversely, so ×_appliedScale undoes that.
            if (_panel != null && _settings != null)
            {
                _settings.UiPanelWidth  = _panel.sizeDelta.x * _appliedScale;
                _settings.UiPanelHeight = _panel.sizeDelta.y * _appliedScale;
            }
            _canvasGo.SetActive(false);
        }
    }

    // Search result rows navigate on pointer DOWN — the same event deselects the search
    // input, whose delayed-hide grace window this beats.
    internal class SearchRowDown : MonoBehaviour, IPointerDownHandler
    {
        public Action OnDown;
        public void OnPointerDown(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Left) OnDown?.Invoke();
        }
    }

    // Destroys its GameObject after N frames — the search popup's deselect grace window.
    internal class DeferredDestroy : MonoBehaviour
    {
        public int Frames;
        private void Update()
        {
            if (--Frames <= 0) Destroy(gameObject);
        }
    }
}
