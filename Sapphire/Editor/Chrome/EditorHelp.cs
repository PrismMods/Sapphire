using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Sapphire.UI;

namespace Sapphire
{
    /* Interactive help mode ("?" in the file header strip or beside the tool label).
       While active: a dim blocker swallows all clicks; hovering any Sapphire control highlights
       it with an accent frame (EventSystem.RaycastAll looks THROUGH the blocker), and clicking
       shows that control's documentation in the side panel — What it does / How to use / Keys.
       Topics resolve by walking the hovered hierarchy up against a name→topic table, falling
       back to a per-canvas topic. ESC or the panel's Exit closes. */
    internal static class EditorHelp
    {
        private static GameObject _canvasGo;
        private static RectTransform _canvasRect;
        private static GameObject _blockerGo;
        private static RectTransform _frameRect;
        private static RoundedRectGraphic _frame;
        private static TMPro.TextMeshProUGUI _title;
        private static TMPro.TextMeshProUGUI _body;
        private static RectTransform _bodyRect;
        private static RectTransform _panelRect;
        private static RectTransform _indexPanelRect;
        // topic key → its row background (recoloured to mark the current topic)
        private static readonly Dictionary<string, RoundedRectGraphic> _indexRows =
            new Dictionary<string, RoundedRectGraphic>();
        private static RectTransform _indexContent;   // the scrollable list (rebuilt on search)
        private static string _lastTopicKey;           // re-highlighted after a filter rebuild
        private static bool _open;
        private static readonly List<RaycastResult> _hits = new List<RaycastResult>();
        private static PointerEventData _hoverPed;   // reused by HoverTarget (per-frame while open)

        internal static bool IsOpen => _open;

        internal static void Toggle() { if (_open) Close(); else Open(); }

        // Open straight onto one topic — the "?" buttons that sit on a panel already know which
        // page they want, so they skip the intro and the hunt through Contents.
        internal static void OpenTopic(string key)
        {
            EnsureUi();
            _canvasGo.SetActive(true);
            _open = true;
            ShowTopic(Topics.ContainsKey(key) ? key : "__intro");
        }

        internal static void Tick()
        {
            if (!_open) return;
            scnEditor ed = null;
            try { ed = scnEditor.instance; } catch { }
            if (ed == null || ed.playMode || !MainClass.EditorSuiteOn) { Close(); return; }
            if (Input.GetKeyDown(KeyCode.Escape)) { Close(); return; }

            // hover: topmost Sapphire element under the cursor (looking through our blocker)
            Transform target = HoverTarget(out string topicKey);
            if (target != null)
            {
                PositionFrame(target);
                if (!_frameRect.gameObject.activeSelf) _frameRect.gameObject.SetActive(true);
                if (Input.GetMouseButtonDown(0) && !OverPanel()) ShowTopic(topicKey);
            }
            else
            {
                if (_frameRect != null && _frameRect.gameObject.activeSelf) _frameRect.gameObject.SetActive(false);
            }
        }

        internal static void Dispose()
        {
            Close();
            if (_canvasGo != null) Object.Destroy(_canvasGo);
            _canvasGo = null;
        }

        private static void Open()
        {
            EnsureUi();
            _canvasGo.SetActive(true);
            _open = true;
            ShowTopic("__intro");
        }

        private static void Close()
        {
            if (_canvasGo != null) _canvasGo.SetActive(false);
            _open = false;
        }

        // ── hover resolution ────────────────────────────────────────────────
        private static Transform HoverTarget(out string topicKey)
        {
            topicKey = null;
            var es = EventSystem.current;
            if (es == null) return null;
            // Reused across frames: this runs every frame while help mode is up, and a fresh
            // PointerEventData per frame was pure garbage. (Same pattern as EditorToolbar's
            // PointerOverSapphireUI.)
            if (_hoverPed == null) _hoverPed = new PointerEventData(es);
            _hoverPed.position = Input.mousePosition;
            _hits.Clear();
            es.RaycastAll(_hoverPed, _hits);
            foreach (var h in _hits)
            {
                if (h.gameObject == null) continue;
                var canvas = h.gameObject.GetComponentInParent<Canvas>();
                var root = canvas != null ? canvas.rootCanvas : null;
                if (root == null) continue;
                string rn = root.name;
                if (rn == "SapphireHelp") continue;              // our own overlay
                // Ordinal: the default overload takes Mono's culture-sensitive compare path.
                if (!rn.StartsWith("Sapphire", System.StringComparison.Ordinal)) continue; // game UI: no topic
                // specific element name first, walking up; else the canvas fallback
                for (var t = h.gameObject.transform; t != null && t != root.transform; t = t.parent)
                {
                    if (Topics.ContainsKey(t.name)) { topicKey = t.name; return t; }
                }
                if (Topics.ContainsKey(rn)) { topicKey = rn; return h.gameObject.transform; }
                return null;
            }
            return null;
        }

        private static bool OverPanel()
        {
            var mp = Input.mousePosition;
            return (_panelRect != null && RectTransformUtility.RectangleContainsScreenPoint(_panelRect, mp, null))
                || (_indexPanelRect != null && RectTransformUtility.RectangleContainsScreenPoint(_indexPanelRect, mp, null));
        }

        private static readonly Vector3[] _corners = new Vector3[4];

        private static void PositionFrame(Transform target)
        {
            var rt = target as RectTransform;
            if (rt == null) rt = target.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.GetWorldCorners(_corners); // overlay canvases: world == screen
            Vector2 min, max;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, _corners[0], null, out min);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, _corners[2], null, out max);
            _frameRect.anchoredPosition = (min + max) * 0.5f;
            _frameRect.sizeDelta = new Vector2(Mathf.Abs(max.x - min.x) + 8f, Mathf.Abs(max.y - min.y) + 8f);
        }

        private static void ShowTopic(string key)
        {
            if (key == null || !Topics.TryGetValue(key, out var t)) return;
            if (_title != null) _title.text = t.Key;
            if (_body != null)
            {
                _body.text = t.Value;
                _body.ForceMeshUpdate();
                _bodyRect.sizeDelta = new Vector2(0f, _body.preferredHeight + 20f);
                _bodyRect.anchoredPosition = Vector2.zero;
            }
            HighlightIndexRow(key);
        }

        // mark the current topic in the browsable index (does nothing for __intro / unlisted keys)
        private static void HighlightIndexRow(string key)
        {
            _lastTopicKey = key; // remembered so a search rebuild can re-highlight the open topic
            foreach (var kv in _indexRows)
                kv.Value.color = kv.Key == key ? _rowSelColor : _rowBaseColor;
        }

        private static readonly Color _rowBaseColor = new Color(1f, 1f, 1f, 0.025f);
        private static readonly Color _rowSelColor =
            new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.20f);

        // ── UI ──────────────────────────────────────────────────────────────
        private static void EnsureUi()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SapphireHelp", typeof(RectTransform));
            Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 960; // ABOVE every Sapphire canvas (toolbar 953…master 958) so the
                                       // blocker actually swallows the click — else clicking a tool in
                                       // help mode both showed its topic AND activated the tool.
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            _canvasRect = (RectTransform)_canvasGo.transform;

            // dim blocker: swallows clicks so hovering/clicking can't activate controls
            _blockerGo = new GameObject("Blocker", typeof(RectTransform));
            _blockerGo.transform.SetParent(_canvasGo.transform, false);
            var br = (RectTransform)_blockerGo.transform;
            br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one;
            br.offsetMin = Vector2.zero; br.offsetMax = Vector2.zero;
            var bi = _blockerGo.AddComponent<Image>();
            bi.color = new Color(0f, 0f, 0f, 0.25f);
            bi.raycastTarget = true;

            // accent frame that rides the hovered element
            var frameGo = new GameObject("Frame", typeof(RectTransform));
            frameGo.transform.SetParent(_canvasGo.transform, false);
            _frameRect = (RectTransform)frameGo.transform;
            _frameRect.anchorMin = _frameRect.anchorMax = new Vector2(0.5f, 0.5f);
            _frameRect.pivot = new Vector2(0.5f, 0.5f);
            _frame = frameGo.AddComponent<RoundedRectGraphic>();
            _frame.Radius = 8f;
            _frame.color = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.12f);
            _frame.BorderWidth = 2f;
            _frame.BorderColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.9f);
            _frame.raycastTarget = false;
            frameGo.SetActive(false);

            // documentation panel, right side
            var panelGo = new GameObject("DocPanel", typeof(RectTransform));
            panelGo.transform.SetParent(_canvasGo.transform, false);
            _panelRect = (RectTransform)panelGo.transform;
            _panelRect.anchorMin = new Vector2(1f, 0.5f);
            _panelRect.anchorMax = new Vector2(1f, 0.5f);
            _panelRect.pivot = new Vector2(1f, 0.5f);
            _panelRect.anchoredPosition = new Vector2(-16f, 0f);
            _panelRect.sizeDelta = new Vector2(380f, 560f);
            var pbg = panelGo.AddComponent<RoundedRectGraphic>();
            pbg.Radius = 12f;
            pbg.color = new Color(0.07f, 0.07f, 0.09f, 0.97f);
            pbg.BorderWidth = 1f;
            pbg.BorderColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f);
            pbg.raycastTarget = true;

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(panelGo.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = new Vector2(0f, 1f); tr.anchorMax = new Vector2(1f, 1f);
            tr.pivot = new Vector2(0.5f, 1f);
            tr.anchoredPosition = new Vector2(0f, -14f);
            tr.sizeDelta = new Vector2(-80f, 24f);
            _title = UIBuilder.Tmp(titleGo, "Help", 16f, TextAnchor.MiddleLeft, Theme.Text);
            _title.fontStyle = TMPro.FontStyles.Bold;
            tr.offsetMin = new Vector2(16f, tr.offsetMin.y);

            // exit button
            var exitGo = new GameObject("Exit", typeof(RectTransform));
            exitGo.transform.SetParent(panelGo.transform, false);
            var er = (RectTransform)exitGo.transform;
            er.anchorMin = er.anchorMax = new Vector2(1f, 1f);
            er.pivot = new Vector2(1f, 1f);
            er.anchoredPosition = new Vector2(-10f, -10f);
            er.sizeDelta = new Vector2(56f, 22f);
            var ebg = exitGo.AddComponent<RoundedRectGraphic>();
            ebg.Radius = 6f;
            ebg.color = new Color(1f, 1f, 1f, 0.07f);
            ebg.BorderWidth = 1f;
            ebg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            ebg.raycastTarget = true;
            var elGo = new GameObject("L", typeof(RectTransform));
            elGo.transform.SetParent(exitGo.transform, false);
            var elr = (RectTransform)elGo.transform;
            elr.anchorMin = Vector2.zero; elr.anchorMax = Vector2.one;
            elr.offsetMin = Vector2.zero; elr.offsetMax = Vector2.zero;
            var el = UIBuilder.Tmp(elGo, "× Exit", 12f, TextAnchor.MiddleCenter, Theme.Text);
            el.raycastTarget = false;
            UI.ClickHandler.Attach(exitGo, Close);

            // scrollable body
            var viewGo = new GameObject("Viewport", typeof(RectTransform));
            viewGo.transform.SetParent(panelGo.transform, false);
            var vr = (RectTransform)viewGo.transform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(16f, 14f); vr.offsetMax = new Vector2(-12f, -44f);
            viewGo.AddComponent<RectMask2D>();
            var vi = viewGo.AddComponent<Image>();
            vi.color = new Color(0f, 0f, 0f, 0.01f);
            vi.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewGo.transform, false);
            _bodyRect = (RectTransform)contentGo.transform;
            _bodyRect.anchorMin = new Vector2(0f, 1f); _bodyRect.anchorMax = new Vector2(1f, 1f);
            _bodyRect.pivot = new Vector2(0.5f, 1f);
            _body = UIBuilder.Tmp(contentGo, "", 13f, TextAnchor.UpperLeft, Theme.Text);
            _body.richText = true;
            _body.textWrappingMode = TMPro.TextWrappingModes.Normal;
            _body.raycastTarget = false;

            var scroll = viewGo.AddComponent<ScrollRect>();
            scroll.content = _bodyRect;
            scroll.viewport = vr;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            BuildIndex();
        }

        // ── browsable index ──────────────────────────────────────────────────
        // Left-hand directory: categorised, scrollable, clickable list of every topic.
        // Grouping is data (Index) so it stays maintainable; only keys present in Topics
        // are shown. Clicking a row is the non-hover way into a topic's docs.
        private static void BuildIndex()
        {
            _indexRows.Clear();
            var panelGo = new GameObject("IndexPanel", typeof(RectTransform));
            panelGo.transform.SetParent(_canvasGo.transform, false);
            _indexPanelRect = (RectTransform)panelGo.transform;
            _indexPanelRect.anchorMin = new Vector2(0f, 0.5f);
            _indexPanelRect.anchorMax = new Vector2(0f, 0.5f);
            _indexPanelRect.pivot = new Vector2(0f, 0.5f);
            _indexPanelRect.anchoredPosition = new Vector2(16f, 0f);
            _indexPanelRect.sizeDelta = new Vector2(300f, 560f);
            var pbg = panelGo.AddComponent<RoundedRectGraphic>();
            pbg.Radius = 12f;
            pbg.color = new Color(0.07f, 0.07f, 0.09f, 0.97f);
            pbg.BorderWidth = 1f;
            pbg.BorderColor = new Color(Theme.Accent.r, Theme.Accent.g, Theme.Accent.b, 0.4f);
            pbg.raycastTarget = true;

            // draggable header strip — drag it to move the whole Contents window
            var headGo = new GameObject("Header", typeof(RectTransform));
            headGo.transform.SetParent(panelGo.transform, false);
            var hdr = (RectTransform)headGo.transform;
            hdr.anchorMin = new Vector2(0f, 1f); hdr.anchorMax = new Vector2(1f, 1f);
            hdr.pivot = new Vector2(0.5f, 1f);
            hdr.offsetMin = new Vector2(0f, -34f); hdr.offsetMax = new Vector2(0f, 0f);
            var hbg = headGo.AddComponent<RoundedRectGraphic>();
            hbg.Radius = 12f;
            hbg.color = new Color(1f, 1f, 1f, 0.04f);
            hbg.raycastTarget = true; // the drag surface
            headGo.AddComponent<UI.DragHandle>();

            var titleGo = new GameObject("Title", typeof(RectTransform));
            titleGo.transform.SetParent(headGo.transform, false);
            var tr = (RectTransform)titleGo.transform;
            tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one;
            tr.offsetMin = new Vector2(14f, 0f); tr.offsetMax = new Vector2(-14f, 0f);
            var ttmp = UIBuilder.Tmp(titleGo, Loc.Korean ? "목차" : "Contents", 16f, TextAnchor.MiddleLeft, Theme.Text);
            ttmp.fontStyle = TMPro.FontStyles.Bold;
            ttmp.raycastTarget = false;

            // search box — filters the list live
            var fieldGo = new GameObject("Search", typeof(RectTransform));
            fieldGo.transform.SetParent(panelGo.transform, false);
            var fr = (RectTransform)fieldGo.transform;
            fr.anchorMin = new Vector2(0f, 1f); fr.anchorMax = new Vector2(1f, 1f);
            fr.pivot = new Vector2(0.5f, 1f);
            fr.offsetMin = new Vector2(12f, 0f); fr.offsetMax = new Vector2(-12f, 0f);
            fr.anchoredPosition = new Vector2(0f, -42f);
            fr.sizeDelta = new Vector2(0f, 26f);
            var fbg = fieldGo.AddComponent<RoundedRectGraphic>();
            fbg.Radius = 5f;
            fbg.color = new Color(1f, 1f, 1f, 0.07f);
            fbg.BorderWidth = 1f;
            fbg.BorderColor = new Color(1f, 1f, 1f, 0.14f);
            fbg.raycastTarget = true;
            var stGo = new GameObject("T", typeof(RectTransform));
            stGo.transform.SetParent(fieldGo.transform, false);
            var str = (RectTransform)stGo.transform;
            str.anchorMin = Vector2.zero; str.anchorMax = Vector2.one;
            str.offsetMin = new Vector2(8f, 0f); str.offsetMax = new Vector2(-8f, 0f);
            var stxt = UIBuilder.Tmp(stGo, "", 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            stxt.richText = false;
            var phGo = new GameObject("PH", typeof(RectTransform));
            phGo.transform.SetParent(fieldGo.transform, false);
            var phr = (RectTransform)phGo.transform;
            phr.anchorMin = Vector2.zero; phr.anchorMax = Vector2.one;
            phr.offsetMin = new Vector2(8f, 0f); phr.offsetMax = new Vector2(-8f, 0f);
            var phTmp = UIBuilder.Tmp(phGo, Loc.Korean ? "검색…" : "Search…", 12.5f, TextAnchor.MiddleLeft, Theme.TextMuted);
            phTmp.raycastTarget = false;
            var input = UIBuilder.BuildInputField(fieldGo, stxt);
            input.lineType = TMPro.TMP_InputField.LineType.SingleLine;
            input.placeholder = phTmp; // TMP shows/hides it as the field empties/fills
            input.onValueChanged.AddListener(RebuildIndexList);

            // scroll viewport (below the title + search)
            var viewGo = new GameObject("Viewport", typeof(RectTransform));
            viewGo.transform.SetParent(panelGo.transform, false);
            var vr = (RectTransform)viewGo.transform;
            vr.anchorMin = Vector2.zero; vr.anchorMax = Vector2.one;
            vr.offsetMin = new Vector2(10f, 12f); vr.offsetMax = new Vector2(-8f, -80f);
            viewGo.AddComponent<RectMask2D>();
            var vi = viewGo.AddComponent<Image>();
            vi.color = new Color(0f, 0f, 0f, 0.01f);
            vi.raycastTarget = true;

            var contentGo = new GameObject("Content", typeof(RectTransform));
            contentGo.transform.SetParent(viewGo.transform, false);
            _indexContent = (RectTransform)contentGo.transform;
            _indexContent.anchorMin = new Vector2(0f, 1f); _indexContent.anchorMax = new Vector2(1f, 1f);
            _indexContent.pivot = new Vector2(0.5f, 1f);
            _indexContent.anchoredPosition = Vector2.zero;
            _indexContent.sizeDelta = Vector2.zero;

            var scroll = viewGo.AddComponent<ScrollRect>();
            scroll.content = _indexContent;
            scroll.viewport = vr;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 25f;

            RebuildIndexList("");
        }

        // (Re)build the directory rows, filtered by a case-insensitive title match. MANUAL y-layout
        // with explicit top-stretch rows — the earlier VerticalLayoutGroup left-clipped the labels.
        private static void RebuildIndexList(string filter)
        {
            if (_indexContent == null) return;
            for (int i = _indexContent.childCount - 1; i >= 0; i--)
                UnityEngine.Object.DestroyImmediate(_indexContent.GetChild(i).gameObject);
            _indexRows.Clear();

            string f = filter != null ? filter.Trim() : "";
            float y = 0f;
            const float rowH = 24f, headH = 22f, gap = 2f;
            foreach (var cat in Index)
            {
                var matches = new List<string>();
                foreach (var k in cat.Keys)
                {
                    if (!Topics.ContainsKey(k)) continue;
                    if (f.Length == 0 || Topics[k].Key.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) >= 0)
                        matches.Add(k);
                }
                if (matches.Count == 0) continue;
                MakeIndexHeader(Loc.Korean ? cat.Ko : cat.En, y, headH); y -= headH + gap;
                foreach (var k in matches) { MakeIndexRow(k, Topics[k].Key, y, rowH); y -= rowH + gap; }
                y -= 4f; // gap between categories
            }
            _indexContent.sizeDelta = new Vector2(0f, -y + 4f);
            _indexContent.anchoredPosition = Vector2.zero;
            HighlightIndexRow(_lastTopicKey);
        }

        private static void MakeIndexHeader(string label, float y, float h)
        {
            var go = new GameObject("Header", typeof(RectTransform));
            go.transform.SetParent(_indexContent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(0f, h);
            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(6f, 0f); lr.offsetMax = new Vector2(-4f, 0f);
            var t = UIBuilder.Tmp(lGo, label, 12f, TextAnchor.LowerLeft, Theme.Accent);
            t.fontStyle = TMPro.FontStyles.Bold | TMPro.FontStyles.UpperCase;
            t.raycastTarget = false;
        }

        private static void MakeIndexRow(string key, string title, float y, float h)
        {
            var go = new GameObject("Row", typeof(RectTransform));
            go.transform.SetParent(_indexContent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = new Vector2(0f, 1f); r.anchorMax = new Vector2(1f, 1f);
            r.pivot = new Vector2(0.5f, 1f);
            r.anchoredPosition = new Vector2(0f, y);
            r.sizeDelta = new Vector2(0f, h);
            var bg = go.AddComponent<RoundedRectGraphic>();
            bg.Radius = 5f;
            bg.color = _rowBaseColor;
            bg.raycastTarget = true;
            _indexRows[key] = bg;

            var lGo = new GameObject("L", typeof(RectTransform));
            lGo.transform.SetParent(go.transform, false);
            var lr = (RectTransform)lGo.transform;
            lr.anchorMin = Vector2.zero; lr.anchorMax = Vector2.one;
            lr.offsetMin = new Vector2(8f, 0f); lr.offsetMax = new Vector2(-6f, 0f);
            var lbl = UIBuilder.Tmp(lGo, title, 12.5f, TextAnchor.MiddleLeft, Theme.Text);
            lbl.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
            lbl.overflowMode = TMPro.TextOverflowModes.Ellipsis;
            lbl.raycastTarget = false;

            UI.ClickHandler.Attach(go, () => ShowTopic(key));
        }

        // category → topic keys, ordered. Localised header + only-existing-keys enforced at build.
        private sealed class Cat
        {
            public readonly string En, Ko;
            public readonly string[] Keys;
            public Cat(string en, string ko, string[] keys) { En = en; Ko = ko; Keys = keys; }
        }

        private static readonly Cat[] Index =
        {
            new Cat("Tools", "도구", new[]
            {
                "ToolCircle", "ToolFreeAngle", "ToolPseudo", "ToolZip", "ToolMagic",
                "ToolTrack", "ToolDeco", "ToolInspector", "ToolCamera", "ToolVfx", "ToolBar",
                "PseudoMenu", "ZipMenu",
            }),
            new Cat("Panels", "패널", new[]
            {
                "AnglePad",
                "EventDock", "SapphireLevelMenu", "SapphireCopyPanel", "SapphirePresets",
                "SapphireCameraCard", "SapphireEasePicker", "SapphireTileMenu", "SapphireFilterPicker",
                "SapphirePopup",
            }),
            new Cat("Timeline & camera", "타임라인 · 카메라", new[]
            {
                "SapphireEditorEvents", "SapphireTimelineFold", "SapphireEventTabs", "CamMode",
                "Lane", "CamInspector", "CameraMenu", "GraphBtn", "SapphireGraph", "SapphireBezier",
                "SapphirePitch",
            }),
            new Cat("Chrome", "크롬", new[]
            {
                "FileChip", "SettingsChip", "LevelSettingsChip", "GameSettingsChip", "LeaveChip",
                "HelpChip", "SapphireMasterSwitch", "SapphireEditorChrome", "SapphireToolbar",
            }),
        };

        // ── documentation ───────────────────────────────────────────────────
        // key → (title, body). Keys are GameObject names (specific) or root canvas names
        // (per-feature fallback). KEEP CURRENT when features change.
        private static readonly Dictionary<string, KeyValuePair<string, string>> Topics = Build();

        // en/ko variants picked once at table build (language changes need a rebuild = relaunch).
        // English text lives here; the Korean title/body come from EditorHelpKo (the one file to
        // edit for translations). A key missing there falls back to English.
        private static void Add(Dictionary<string, KeyValuePair<string, string>> d,
            string key, string title, string body)
        {
            if (Loc.Korean && EditorHelpKo.TryGet(key, out var kt, out var kb))
                d[key] = new KeyValuePair<string, string>(kt ?? title, kb ?? body);
            else
                d[key] = new KeyValuePair<string, string>(title, body);
        }

        private static Dictionary<string, KeyValuePair<string, string>> Build()
        {
            var d = new Dictionary<string, KeyValuePair<string, string>>();
            Add(d, "__intro", "Help mode",
"<b>What this is</b>\nBrowse every tool and panel from the <b>Contents</b> list on the left — click an entry to read its docs here.\n\nOr point at any Sapphire control: it highlights, and clicking it jumps straight to its documentation.\n\n<b>Keys</b>\nESC — exit help mode.");

            Add(d, "AnglePad", "Angle pad",
"<b>What it does</b>\nAppends a whole run of tiles from a line of RELATIVE angles (the charter convention: 180 = straight, 90 = quarter turn, 0 = U-turn). Quick chart keeps one pad open at all times.\n\n<b>Syntax</b>\nSpace-separated angles. Maths works per value — 180-30, 360/8, 2*45. A trailing <b>t</b> twirls that tile: 30t 30t 180. Parenthesised groups repeat with *: (30t 150 180)*4, and groups can nest.\n\n<b>Buttons</b>\nSwirl — flip the FIRST tile's twirl. A twirl reverses the turn direction, so the leading one decides which way the whole run bends; drop it when the path already enters turning the way you want. It lights up while the first tile is twirled.\n+ — duplicate this pad, carrying its text and repeat count (each pad is a scratch preset).\n× n — lay the expression down n times. Separate from (…)*n on purpose: as a count the expression stays one UNIT, so flipping its leading twirl costs only the FIRST pass, and the spin carries into the rest.\nAdd to Shape Library — save the run (repeats included) under Non-repeating shapes.\nPlace — build it onto the selected tile, in one undo.\n\n<b>Keys</b>\nShift+G — new pad. Enter — Place (with several pads open, Enter arms a pick and the digit keys choose one).");

            Add(d, "ToolCircle", "Circular path",
"<b>What it does</b>\nGenerates stars, circles and midspin-circles after the selected tile (Star Calculator parameters).\n\n<b>How to use</b>\nSelect a tile, open the tool, set Pseudo per round / interval / angle, optional Reverse, Keep BPM, mid-spin. Apply builds in one undo.\n\n<b>Keys</b>\n1 — open (no tile selected).");

            Add(d, "ToolFreeAngle", "Free angle",
"<b>What it does</b>\nAim the next tile freely with the mouse.\n\n<b>How to use</b>\nToggle the tool (or hold left-Alt) with a single tile selected; the preview follows the cursor. Left-click places. Leaving without placing reverts the preview.\n\n<b>Keys</b>\n2 — toggle (no tile selected). Left-Alt — hold for quick use.");

            Add(d, "ToolPseudo", "Pseudo",
"<b>What it does</b>\nConverts tiles into pseudos (multi-hit tiles). Beat-neutral: a pseudo replaces one beat with K hits.\n\n<b>How to use</b>\nSingle tile: with the tool on, click the selected tile again to convert. Multi-select: a dialog offers interval + style (Upwards / Sideways / Inline) — pseudos are added ON TOP of the selected path.\n\n<b>Submenu</b>\nKey count (buttons or typed), tap angle presets + custom field, Midspin toggle (interleaved tap+midspin pairs), Custom per-tile angles.\n\n<b>Keys</b>\n3 — toggle (no tile selected). Digits set the key count while active.");

            Add(d, "ToolCamera", "Camera path",
"<b>What it does</b>\nOverlays every MoveCamera keyframe: cyan dots (orange = player-relative) joined by dotted lines.\n\n<b>How to use</b>\nClick a dot for its details card and the framed-area box. ▶ on the card previews that move with its real duration and ease.\n\n<b>Submenu</b>\n▶ Play all — run the whole sequence. ▶ Sel — from the selected keyframe. Gaps — wait out the real beat gaps between events.\n\n<b>Keys</b>\n9 — toggle (no tile selected).");

            Add(d, "ToolVfx", "VFX preview",
"<b>What it does</b>\nHides ALL UI — Sapphire and the game's — for a clean view of the level. Stays hidden through play-testing.\n\n<b>Keys</b>\n0 — toggle (no tile selected). ESC — exit (the only way out; the toolbar is hidden too).");

            Add(d, "ToolInspector", "Inspector",
"<b>What it does</b>\nEvent format-painter: copy one tile's events, paste onto others.\n\n<b>How to use</b>\nWith the tool on, click the selected tile again to CAPTURE its events. Right-click any tile to PASTE. The panel that appears is the paste filter — untick types you don't want pasted.\n\n<b>Keys</b>\n8 — toggle (no tile selected).");

            Add(d, "ToolZip", "Zip",
"<b>What it does</b>\nReplaces a tile with a zip — a run of redirecting tiles + swirls totalling 360° (8k = 45° per tile).\n\n<b>How to use</b>\nWith the tool on, click the selected tile again to zip it. The submenu picks the key count (from 4k) and the total duration in beats (default 2 = 360°).\n\n<b>Keys</b>\n4 — toggle (no tile selected). Digits 4–8 set the key count while active.");

            Add(d, "ToolMagic", "Magic shape",
"<b>What it does</b>\nMagic-circle toolkit: MULTIPLY retimes the selection so every hit lands at a target BPM (or ×multiplier, or reshapes the angles instead), CREATE sweeps a tile range into an N-vertex shape with a ghost preview, ROTATE offsets tile angles across a range.\n\n<b>How to use</b>\nOpen the panel, pick a tab, set the range (Sel = selection, All = whole level) and Apply. Errors show in the status line.\n\n<b>Keys</b>\n5 — toggle (no tile selected).\n\n<b>Credits</b>\nMagicShapeMultiply (tjwogud, JofoDuh) + MappingHelper (Sprout34).");

            Add(d, "ToolTrack", "Track tools",
"<b>What it does</b>\nTrack VFX generators: FADE IN/OUT (randomized MoveTrack animations), EXPLODE (rippling shockwave), SIZE (eased scale ramps), MULTI (decoration copies of the track ± fake planets, and animating tagged copies), GENERATE (append tiles from an angle string, T = twirl, with ghost preview).\n\n<b>How to use</b>\nPick a tab, set the tile range, tune the randomization rows (click a row label to enable/disable it) and Apply — one undo step.\n\n<b>Keys</b>\n6 — toggle (no tile selected).\n\n<b>Credits</b>\nMappingHelper (Sprout34).");

            Add(d, "ToolDeco", "Deco tools",
"<b>What it does</b>\nDecoration generators: FLIPBOOK (image-sequence folder → animated decoration), EXTRACT (video → frame folder), 3D STACK (N lerped decoration copies with a color gradient), LYRICS (text parts as game text or font-rendered PNGs, with appear/disappear moves).\n\n<b>How to use</b>\nSave the level first — file paths are relative to the level folder. Pick a tab, fill the fields and Apply.\n\n<b>Keys</b>\n7 — toggle (no tile selected).\n\n<b>Credits</b>\nMappingHelper (Sprout34).");

            Add(d, "ToolBar", "Toolbox",
"<b>What it does</b>\nThe Sapphire tool strip, grouped by function: build (curved path, free angle, pseudo, zip, magic shape) · generate (track tools, deco tools) · events (inspector) · view (camera path, VFX preview). Hover a tool for a hint below the bar; click a tool's icon here in help mode for its full docs.\n\n<b>Keys</b>\nDigits 1–0 select tools when no tile is selected.");

            Add(d, "PseudoMenu", "Pseudo submenu",
"<b>What it does</b>\nSettings for the pseudo tool.\n\n<b>Rows</b>\nKeys — hit count (buttons, or type any N). Midspin — interleaved tap+midspin construction (exact return to course). Angle — tap angle presets + free field. Custom — space-separated per-tile angles (overrides Keys).");

            Add(d, "ZipMenu", "Zip submenu",
"<b>What it does</b>\nParameters for the zip tool.\n\n<b>Keys</b> — hit count, minimum 4.\n<b>Beats</b> — total sweep duration; 2 beats = 360° (the default). Each tile's charter = beats×180/N (2-beat 8k = 45°).");

            Add(d, "CameraMenu", "Camera playback",
"<b>What it does</b>\nPlays the camera keyframe sequence on the overlay.\n\n<b>Buttons</b>\n▶ Play all — from the first keyframe. ▶ Sel — from the selected one. Gaps — hold each keyframe until the next event's real song time (cutting long tweens short, like the game would).");

            Add(d, "ToolLabel", "Current tool",
"<b>What it does</b>\nShows the active tool (pseudo key count, event tool name, …). The ? beside it opens help mode.");

            Add(d, "Help", "Help button",
"<b>What it does</b>\nOpens this interactive help mode.");

            Add(d, "FileChip", "File menu",
"<b>What it does</b>\nReplaces the game's file bar: level name + unsaved dot; click for New / Open / Open Recent / Save / …\n\n<b>Note</b>\nAll entries proxy the game's own buttons — shortcuts still work.");

            Add(d, "SettingsChip", "Editor preferences",
"<b>What it does</b>\nOpens ADOFAI's editor preferences panel.");

            Add(d, "LevelSettingsChip", "Level settings",
"<b>What it does</b>\nOpens the level settings (song, level, track, background, camera, …) in a wide popup with a labeled tab rail.\n\n<b>Keys</b>\nESC closes (background clicks don't).");

            Add(d, "GameSettingsChip", "Game settings",
"<b>What it does</b>\nOpens the game's own settings screen (the pause-menu settings) from the editor.");

            Add(d, "LeaveChip", "Leave editor",
"<b>What it does</b>\nExits the editor (proxies the game's exit button).");

            Add(d, "HelpChip", "Help",
"<b>What it does</b>\nOpens this interactive help mode.");

            Add(d, "EventDock", "Event palette",
"<b>What it does</b>\nThe event palette as persistent TOOLS: pick an event, then stamp it on tiles repeatedly.\n\n<b>How to use</b>\nLeft column switches category. Click an event to select it as the tool. RIGHT-click tiles to stamp rapidly; LEFT-click selects a tile first, a second click stamps.\n\n<b>Keys</b>\nWith a tile selected: digits 1–9 pick the nth event of the current category, Enter stamps it. ESC deselects the tool.");

            Add(d, "SapphireEditorChrome", "Editor chrome",
"<b>What it does</b>\nThe file header strip and event palette — Sapphire replacements for the game's editor chrome. Click a specific control for details.");

            Add(d, "SapphireToolbar", "Toolbox",
"<b>What it does</b>\nThe Sapphire tool strip and its submenus. Click a specific tool icon for details.\n\n<b>Keys</b>\nDigits 1–0 select tools when no tile is selected.");

            Add(d, "SapphireEditorEvents", "Timeline",
"<b>What it does</b>\nEvent timeline on real song time: markers by category, playhead, zoom, transport (play/rewind · clock · BPM), mode cluster (EDITOR / difficulty / NO FAIL / AUTO).\n\n<b>How to use</b>\nClick a marker — jumps to its tile and opens that exact event. Click empty strip — moves the playhead (drag to scrub). Wheel pans when zoomed.\n\n<b>Modes</b>\nThe mode button under the zoom controls switches between NORMAL / CAM / DECO / FILTER — the CDF workspaces. In help mode, click that button for details; the CAM workspace guide covers keyframe editing.\n\n<b>Keys</b>\nThe centre-bottom arrow folds/expands the strip; drag the strip's TOP EDGE to resize lane height.");

            Add(d, "SapphireTimelineFold", "Timeline fold",
"<b>What it does</b>\nFolds the timeline away / brings it back. Points down when open, up when folded.");

            Add(d, "SapphireEventTabs", "Event tab rail",
"<b>What it does</b>\nThe selected tile's events as icon tabs.\n\n<b>How to use</b>\nClick a tab to open that event; right-click deletes it. With several events of one type, numbered chips appear — click a number to jump straight to that instance.");

            Add(d, "SapphireCopyPanel", "Mirror & selective copy",
"<b>What it does</b>\nAppears with 2+ tiles selected.\n\n<b>Mirror</b>\nFlips the selection AND mirrors decoration/event positions (the vanilla flip doesn't). Preserve beats adds a twirl on the first tile.\n\n<b>Copy</b>\nPer-category / per-type checkboxes choose what a copy carries. Copy, then paste normally.\n\n<b>Inspector mode</b>\nWhile the Inspector tool holds a capture, this panel becomes its paste filter.");

            Add(d, "SapphirePitch", "Practice pitch",
"<b>What it does</b>\nPractice-only playback speed — song and hitsounds together. Never touches the saved level.\n\n<b>How to use</b>\nSet a % (or ±10 with ‹ ›). Takes effect when playback starts. Reset returns to normal.");

            Add(d, "SapphireMasterSwitch", "Master switch",
"<b>What it does</b>\nTurns the whole Sapphire editor suite on/off. Off restores all vanilla UI; the switch itself stays so you can come back.");

            Add(d, "SapphireLevelMenu", "Level settings popup",
"<b>What it does</b>\nThe game's level-settings panel, hosted wide with a labeled tab rail. The game owns every field — Sapphire only hosts it.\n\n<b>Keys</b>\nESC closes.");

            Add(d, "SapphireCameraCard", "Camera keyframe card",
"<b>What it does</b>\nDetails for the selected camera keyframe: floor, relativeTo, offset, zoom, rotation, duration, ease. ▶ previews the move on the overlay box.");

            Add(d, "SapphireTileMenu", "Tile menu",
"<b>What it does</b>\nRight-click a tile: Copy / Cut / Paste / Delete / Rotate.\n\n<b>Note</b>\nWhile an event or Inspector tool is active, right-click belongs to that tool instead.");

            Add(d, "SapphirePresets", "Event presets",
"<b>What it does</b>\nNamed bundles of events, applied per tile via the Inspector tool.\n\n<b>How to use</b>\nCapture a tile (Inspector), then + Save capture. Click a preset to load it as the capture — stamp tiles as usual. Right-click a row to rename, × deletes.\n\n<b>Note</b>\nPresets persist across sessions.");

            Add(d, "SapphireEasePicker", "Ease picker",
"<b>What it does</b>\nPicks an ease with VISUAL curve previews — every curve is plotted from the game's own runtime easing, overshoots included.\n\n<b>How to use</b>\nRight-click a keyframe in the timeline's CAM mode. The current ease is highlighted; click a cell to apply (undo works). The Custom cell opens the bezier editor. ESC or clicking outside closes.");

            Add(d, "CamMode", "Timeline mode menu",
"<b>What it does</b>\nSwitches the strip between the CDF workspaces: NORMAL (all events by category), CAM (camera keyframes), DECO (decoration events, lanes = the tags visible in the current view, top 8 by use), FILTER (SetFilter events, lanes per filter).\n\n<b>Deco / Filter</b>\nSame bar view as CAM: tweens as duration bars, whole bar clickable, right-click = ease picker. Lanes follow the view — pan to see other tags.");

            Add(d, "Lane", "CAM mode — camera keyframe workspace",
"<b>Layout</b>\nOne layer per property: Position / Rotation / Zoom. Tweens draw as duration bars (head, body to the end beat, cap); duration-0 SET keyframes are thin ticks. A set tick and a tween starting together are a pair.\n\n<b>Selecting</b>\nClick anywhere on a bar (whole body counts) — the keyframe turns white, its tile is selected, and an inline row of editable fields opens at the top of the strip.\n\n<b>Retiming</b>\nClick a LANE LABEL (e.g. Zoom) to expand its diamond sub-row — dragging happens only there. Pairs fan apart so each diamond stays clickable; a ghost diamond follows the cursor. Retiming one property of an event that carries several SPLITS it into its own event (one undo).\n\n<b>Creating</b>\nRIGHT-click EMPTY lane space = new set keyframe for that property at that tile, carrying the current value.\n\n<b>More</b>\nRIGHT-click a keyframe = visual ease picker. GRAPH button (or the row's Graph button) = AE-style graph editor. ESC deselects.");

            Add(d, "CamInspector", "Inline keyframe fields",
"<b>What it does</b>\nEditable fields for the selected keyframe: duration (beats), position X/Y, rotation, zoom — plus Ease and Graph buttons.\n\n<b>How to use</b>\nType a value to set AND enable that property; CLEAR a field to disable it (the panel's on/off toggle equivalent). Every commit is one undo step.");

            Add(d, "GraphBtn", "GRAPH button",
"<b>What it does</b>\nOpens the AE-style graph editor on the current view — no selection needed. With a keyframe selected it focuses on that tween instead.");

            Add(d, "SapphireGraph", "Graph editor",
"<b>What it does</b>\nAn After-Effects-style value graph for camera properties: the property's value over song time, drawn through each tween's REAL runtime easing, keyframes as diamonds.\n\n<b>How to use</b>\nOpen from the GRAPH button under the timeline's zoom controls (whole level view) or from a keyframe's inline row (focused on that keyframe). Tabs: Position (X and Y overlaid), X, Y, Rotation, Zoom; axes show values with units and beats. WHEEL over the plot zooms time; over the LEFT margin it zooms the value axis. Dragging empty plot pans in BOTH directions (vertical pan switches the value axis to manual scale). In the Position tab the X·Y button links the coordinate pair: linked (default) retimes both together on one event; unlinked, components split into their own events when retimed. Click a diamond to select (the view focuses on it), DRAG vertically to change the value, horizontally to retime — one undo per drag. Right-click a diamond for the ease picker.\n\n<b>Window</b>\nDrag the Graph View header to move the window; drag any edge or corner to resize.\n\n<b>Keys</b>\nESC or × closes.");

            Add(d, "SapphireBezier", "Custom bezier",
"<b>What it does</b>\nA fully custom easing curve for a camera tween. The game can't play one natively, so Apply DECOMPOSES the tween into short Linear segments (default 10) sampling your curve — one undo reverts it.\n\n<b>How to use</b>\nDrag the two control points; baselines mark 0 and 1 (overshoot allowed). Needs an EARLIER keyframe of the same property to read the start value from — the duration-0 set partner of a pair works.");

            Add(d, "SapphireFilterPicker", "Filter manager",
"<b>What it does</b>\nManages a filter event end to end: search + category rail + card grid for ~300 advanced filters (legacy SetFilter events get the game's localized filter list instead), a parameter editor for the active filter, and delete.\n\n<b>How to use</b>\nOpen a SetFilter/SetFilterAdvanced event — a Filters… chip appears by the panel. Clicking a card APPLIES it immediately (undo works) and keeps the manager open for auditioning. The right column edits the active filter's parameters — empty field = not overridden — and Delete event removes the event (undoable). Hovering a card previews its parameters in the bottom bar.\n\n<b>Keys</b>\nESC or × closes; background clicks do NOT close.");

            Add(d, "SapphirePopup", "Message box",
"<b>What it does</b>\nSapphire-styled version of the editor's popups; buttons proxy the game's own.");
            return d;
        }
    }
}
