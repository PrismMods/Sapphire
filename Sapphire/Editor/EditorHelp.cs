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
        private static bool _open;
        private static readonly List<RaycastResult> _hits = new List<RaycastResult>();

        internal static bool IsOpen => _open;

        internal static void Toggle() { if (_open) Close(); else Open(); }

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
            var pd = new PointerEventData(es) { position = Input.mousePosition };
            _hits.Clear();
            es.RaycastAll(pd, _hits);
            foreach (var h in _hits)
            {
                if (h.gameObject == null) continue;
                var canvas = h.gameObject.GetComponentInParent<Canvas>();
                var root = canvas != null ? canvas.rootCanvas : null;
                if (root == null) continue;
                string rn = root.name;
                if (rn == "SapphireHelp") continue;              // our own overlay
                if (!rn.StartsWith("Sapphire")) continue;        // game UI: no topic
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
            return _panelRect != null && RectTransformUtility.RectangleContainsScreenPoint(
                _panelRect, Input.mousePosition, null);
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
        }

        // ── UI ──────────────────────────────────────────────────────────────
        private static void EnsureUi()
        {
            if (_canvasGo != null) return;
            _canvasGo = new GameObject("SapphireHelp", typeof(RectTransform));
            Object.DontDestroyOnLoad(_canvasGo);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 949; // above every Sapphire canvas
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
        }

        // ── documentation ───────────────────────────────────────────────────
        // key → (title, body). Keys are GameObject names (specific) or root canvas names
        // (per-feature fallback). KEEP CURRENT when features change.
        private static readonly Dictionary<string, KeyValuePair<string, string>> Topics = Build();

        private static void Add(Dictionary<string, KeyValuePair<string, string>> d,
            string key, string title, string body)
            => d[key] = new KeyValuePair<string, string>(title, body);

        private static Dictionary<string, KeyValuePair<string, string>> Build()
        {
            var d = new Dictionary<string, KeyValuePair<string, string>>();
            Add(d, "__intro", "Help mode",
"<b>What this is</b>\nHover any Sapphire control — it highlights. Click it to read its documentation here.\n\n<b>Keys</b>\nESC — exit help mode.");

            // toolbar tools
            Add(d, "Tool0", "Circular path",
"<b>What it does</b>\nGenerates stars, circles and midspin-circles after the selected tile (Star Calculator parameters).\n\n<b>How to use</b>\nSelect a tile, open the tool, set Pseudo per round / interval / angle, optional Reverse, Keep BPM, mid-spin. Apply builds in one undo.\n\n<b>Keys</b>\n1 — open (no tile selected).");
            Add(d, "Tool1", "Free angle",
"<b>What it does</b>\nAim the next tile freely with the mouse.\n\n<b>How to use</b>\nToggle the tool (or hold left-Alt) with a single tile selected; the preview follows the cursor. Left-click places. Leaving without placing reverts the preview.\n\n<b>Keys</b>\n2 — toggle (no tile selected). Left-Alt — hold for quick use.");
            Add(d, "Tool2", "Pseudo",
"<b>What it does</b>\nConverts tiles into pseudos (multi-hit tiles). Beat-neutral: a pseudo replaces one beat with K hits.\n\n<b>How to use</b>\nSingle tile: with the tool on, click the selected tile again to convert. Multi-select: a dialog offers interval + style (Upwards / Sideways / Inline) — pseudos are added ON TOP of the selected path.\n\n<b>Submenu</b>\nKey count (buttons or typed), tap angle presets + custom field, Midspin toggle (interleaved tap+midspin pairs), Custom per-tile angles.\n\n<b>Keys</b>\n3 — toggle (no tile selected). Digits set the key count while active.");
            Add(d, "Tool3", "Camera path",
"<b>What it does</b>\nOverlays every MoveCamera keyframe: cyan dots (orange = player-relative) joined by dotted lines.\n\n<b>How to use</b>\nClick a dot for its details card and the framed-area box. ▶ on the card previews that move with its real duration and ease.\n\n<b>Submenu</b>\n▶ Play all — run the whole sequence. ▶ Sel — from the selected keyframe. Gaps — wait out the real beat gaps between events.\n\n<b>Keys</b>\n4 — toggle (no tile selected).");
            Add(d, "Tool4", "VFX preview",
"<b>What it does</b>\nHides ALL UI — Sapphire and the game's — for a clean view of the level. Stays hidden through play-testing.\n\n<b>Keys</b>\n5 — toggle (no tile selected). ESC — exit (the only way out; the toolbar is hidden too).");
            Add(d, "Tool5", "Inspector",
"<b>What it does</b>\nEvent format-painter: copy one tile's events, paste onto others.\n\n<b>How to use</b>\nWith the tool on, click the selected tile again to CAPTURE its events. Right-click any tile to PASTE. The panel that appears is the paste filter — untick types you don't want pasted.\n\n<b>Keys</b>\n6 — toggle (no tile selected).");
            Add(d, "ToolBar", "Toolbox",
"<b>What it does</b>\nThe Sapphire tool strip. Hover a tool for a hint below the bar; click a tool's icon here in help mode for its full docs.\n\n<b>Keys</b>\nDigits 1–6 select tools when no tile is selected.");
            Add(d, "PseudoMenu", "Pseudo submenu",
"<b>What it does</b>\nSettings for the pseudo tool.\n\n<b>Rows</b>\nKeys — hit count (buttons, or type any N). Midspin — interleaved tap+midspin construction (exact return to course). Angle — tap angle presets + free field. Custom — space-separated per-tile angles (overrides Keys).");
            Add(d, "CameraMenu", "Camera playback",
"<b>What it does</b>\nPlays the camera keyframe sequence on the overlay.\n\n<b>Buttons</b>\n▶ Play all — from the first keyframe. ▶ Sel — from the selected one. Gaps — hold each keyframe until the next event's real song time (cutting long tweens short, like the game would).");
            Add(d, "ToolLabel", "Current tool",
"<b>What it does</b>\nShows the active tool (pseudo key count, event tool name, …). The ? beside it opens help mode.");
            Add(d, "Help", "Help button",
"<b>What it does</b>\nOpens this interactive help mode.");

            // chrome
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

            // canvas fallbacks
            Add(d, "SapphireToolbar", "Toolbox",
"<b>What it does</b>\nThe Sapphire tool strip and its submenus. Click a specific tool icon for details.\n\n<b>Keys</b>\nDigits 1–6 select tools when no tile is selected.");
            Add(d, "SapphireEditorEvents", "Timeline",
"<b>What it does</b>\nEvent timeline on real song time: markers by category, playhead, zoom, transport (play/rewind · clock · BPM), mode cluster (EDITOR / difficulty / NO FAIL / AUTO).\n\n<b>How to use</b>\nClick a marker — jumps to its tile and opens that exact event. Click empty strip — moves the playhead (drag to scrub). Wheel pans when zoomed.\n\n<b>Keys</b>\nThe centre-bottom arrow folds/expands the strip.");
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
            Add(d, "SapphirePopup", "Message box",
"<b>What it does</b>\nSapphire-styled version of the editor's popups; buttons proxy the game's own.");
            return d;
        }
    }
}
