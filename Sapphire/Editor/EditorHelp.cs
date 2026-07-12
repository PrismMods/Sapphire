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

        // en/ko variants picked once at table build (language changes need a rebuild = relaunch).
        private static void Add(Dictionary<string, KeyValuePair<string, string>> d,
            string key, string title, string body, string koTitle = null, string koBody = null)
        {
            bool ko = Loc.Korean && koBody != null;
            d[key] = new KeyValuePair<string, string>(ko ? (koTitle ?? title) : title, ko ? koBody : body);
        }

        private static Dictionary<string, KeyValuePair<string, string>> Build()
        {
            var d = new Dictionary<string, KeyValuePair<string, string>>();
            Add(d, "__intro", "Help mode",
"<b>What this is</b>\nHover any Sapphire control — it highlights. Click it to read its documentation here.\n\n<b>Keys</b>\nESC — exit help mode.",
"도움말 모드",
"<b>사용법</b>\nSapphire UI에 마우스를 올리면 강조 표시됩니다. 클릭하면 해당 기능의 설명이 여기에 표시됩니다.\n\n<b>단축키</b>\nESC — 도움말 모드 종료.");

            Add(d, "Tool0", "Circular path",
"<b>What it does</b>\nGenerates stars, circles and midspin-circles after the selected tile (Star Calculator parameters).\n\n<b>How to use</b>\nSelect a tile, open the tool, set Pseudo per round / interval / angle, optional Reverse, Keep BPM, mid-spin. Apply builds in one undo.\n\n<b>Keys</b>\n1 — open (no tile selected).",
"원형 경로",
"<b>기능</b>\n선택한 타일 뒤에 별/원/미드스핀 원을 생성합니다 (Star Calculator 방식 파라미터).\n\n<b>사용법</b>\n타일을 선택하고 도구를 연 뒤 라운드당 동타 수 / 간격 / 각도를 설정합니다. Reverse, BPM 유지, 미드스핀 옵션 제공. 적용 시 실행 취소 1회로 묶입니다.\n\n<b>단축키</b>\n1 — 열기 (타일 미선택 시).");

            Add(d, "Tool1", "Free angle",
"<b>What it does</b>\nAim the next tile freely with the mouse.\n\n<b>How to use</b>\nToggle the tool (or hold left-Alt) with a single tile selected; the preview follows the cursor. Left-click places. Leaving without placing reverts the preview.\n\n<b>Keys</b>\n2 — toggle (no tile selected). Left-Alt — hold for quick use.",
"자유 각도",
"<b>기능</b>\n다음 타일의 각도를 마우스로 자유롭게 지정합니다.\n\n<b>사용법</b>\n타일 하나를 선택한 상태에서 도구를 켜거나 왼쪽 Alt를 누르고 있으면 미리보기가 커서를 따라옵니다. 좌클릭으로 배치, 배치하지 않고 나가면 원래대로 돌아갑니다.\n\n<b>단축키</b>\n2 — 토글 (타일 미선택 시). 왼쪽 Alt — 누르는 동안 활성화.");

            Add(d, "Tool2", "Pseudo",
"<b>What it does</b>\nConverts tiles into pseudos (multi-hit tiles). Beat-neutral: a pseudo replaces one beat with K hits.\n\n<b>How to use</b>\nSingle tile: with the tool on, click the selected tile again to convert. Multi-select: a dialog offers interval + style (Upwards / Sideways / Inline) — pseudos are added ON TOP of the selected path.\n\n<b>Submenu</b>\nKey count (buttons or typed), tap angle presets + custom field, Midspin toggle (interleaved tap+midspin pairs), Custom per-tile angles.\n\n<b>Keys</b>\n3 — toggle (no tile selected). Digits set the key count while active.",
"동타",
"<b>기능</b>\n타일을 동타(여러 번 치는 타일)로 변환합니다. 박자 중립: 동타는 1박자를 K번의 입력으로 대체합니다.\n\n<b>사용법</b>\n단일 타일: 도구를 켠 상태에서 선택된 타일을 다시 클릭하면 변환됩니다. 다중 선택: 간격 + 스타일(위로 / 옆으로 / 인라인) 대화상자가 열리며, 기존 경로 위에 동타가 추가됩니다.\n\n<b>서브메뉴</b>\n키 수(버튼 또는 직접 입력), 각도 프리셋 + 자유 입력, 미드스핀 토글(탭+미드스핀 교차 구성), 타일별 커스텀 각도.\n\n<b>단축키</b>\n3 — 토글 (타일 미선택 시). 활성 중 숫자 키 = 키 수 설정.");

            Add(d, "Tool3", "Camera path",
"<b>What it does</b>\nOverlays every MoveCamera keyframe: cyan dots (orange = player-relative) joined by dotted lines.\n\n<b>How to use</b>\nClick a dot for its details card and the framed-area box. ▶ on the card previews that move with its real duration and ease.\n\n<b>Submenu</b>\n▶ Play all — run the whole sequence. ▶ Sel — from the selected keyframe. Gaps — wait out the real beat gaps between events.\n\n<b>Keys</b>\n4 — toggle (no tile selected).",
"카메라 경로",
"<b>기능</b>\n모든 MoveCamera 키프레임을 표시합니다: 청록 점(주황 = 플레이어 기준)이 점선으로 연결됩니다.\n\n<b>사용법</b>\n점을 클릭하면 상세 카드와 화면 영역 박스가 표시됩니다. 카드의 ▶는 실제 길이와 이징으로 해당 이동을 미리 재생합니다.\n\n<b>서브메뉴</b>\n▶ 전체 재생 — 시퀀스 전체. ▶ 선택부터 — 선택한 키프레임부터. 박자 간격 — 이벤트 사이의 실제 박자 간격을 기다립니다.\n\n<b>단축키</b>\n4 — 토글 (타일 미선택 시).");

            Add(d, "Tool4", "VFX preview",
"<b>What it does</b>\nHides ALL UI — Sapphire and the game's — for a clean view of the level. Stays hidden through play-testing.\n\n<b>Keys</b>\n5 — toggle (no tile selected). ESC — exit (the only way out; the toolbar is hidden too).",
"VFX 미리보기",
"<b>기능</b>\nSapphire와 게임의 모든 UI를 숨겨 레벨만 깔끔하게 봅니다. 플레이 테스트 중에도 유지됩니다.\n\n<b>단축키</b>\n5 — 토글 (타일 미선택 시). ESC — 종료 (툴바도 숨겨지므로 유일한 종료 방법).");

            Add(d, "Tool5", "Inspector",
"<b>What it does</b>\nEvent format-painter: copy one tile's events, paste onto others.\n\n<b>How to use</b>\nWith the tool on, click the selected tile again to CAPTURE its events. Right-click any tile to PASTE. The panel that appears is the paste filter — untick types you don't want pasted.\n\n<b>Keys</b>\n6 — toggle (no tile selected).",
"인스펙터",
"<b>기능</b>\n이벤트 서식 복사 도구: 한 타일의 이벤트를 복사해 다른 타일에 붙여넣습니다.\n\n<b>사용법</b>\n도구를 켠 상태에서 선택된 타일을 다시 클릭하면 이벤트를 캡처합니다. 아무 타일이나 우클릭하면 붙여넣습니다. 표시되는 패널은 붙여넣기 필터입니다 — 원하지 않는 유형은 체크를 해제하세요.\n\n<b>단축키</b>\n6 — 토글 (타일 미선택 시).");

            Add(d, "Tool6", "Zip",
"<b>What it does</b>\nReplaces a tile with a zip — a run of redirecting tiles + swirls totalling 360° (8k = 45° per tile).\n\n<b>How to use</b>\nWith the tool on, click the selected tile again to zip it. The submenu picks the key count (from 4k) and the total duration in beats (default 2 = 360°).\n\n<b>Keys</b>\n7 — toggle (no tile selected). Digits 4–8 set the key count while active.",
"집",
"<b>기능</b>\n타일을 집(zip)으로 대체합니다 — 총 360°를 이루는 방향 전환 타일 + 소용돌이의 연속 (8키 = 타일당 45°).\n\n<b>사용법</b>\n도구를 켠 상태에서 선택된 타일을 다시 클릭합니다. 서브메뉴에서 키 수(4부터)와 총 길이(박자, 기본 2박자 = 360°)를 설정합니다.\n\n<b>단축키</b>\n7 — 토글 (타일 미선택 시). 활성 중 숫자 4–8 = 키 수.");

            Add(d, "ToolBar", "Toolbox",
"<b>What it does</b>\nThe Sapphire tool strip. Hover a tool for a hint below the bar; click a tool's icon here in help mode for its full docs.\n\n<b>Keys</b>\nDigits 1–7 select tools when no tile is selected.",
"도구 모음",
"<b>기능</b>\nSapphire 도구 모음입니다. 도구에 마우스를 올리면 아래에 힌트가 표시됩니다. 도움말 모드에서 아이콘을 클릭하면 상세 설명을 볼 수 있습니다.\n\n<b>단축키</b>\n타일 미선택 시 숫자 1–7로 도구 선택.");

            Add(d, "PseudoMenu", "Pseudo submenu",
"<b>What it does</b>\nSettings for the pseudo tool.\n\n<b>Rows</b>\nKeys — hit count (buttons, or type any N). Midspin — interleaved tap+midspin construction (exact return to course). Angle — tap angle presets + free field. Custom — space-separated per-tile angles (overrides Keys).",
"동타 서브메뉴",
"<b>기능</b>\n동타 도구의 설정입니다.\n\n<b>항목</b>\n키 수 — 입력 횟수 (버튼 또는 직접 입력). 미드스핀 — 탭+미드스핀 교차 구성 (경로가 정확히 복귀). 각도 — 프리셋 + 자유 입력. 커스텀 — 공백으로 구분한 타일별 각도 (키 수보다 우선).");

            Add(d, "ZipMenu", "Zip submenu",
"<b>What it does</b>\nParameters for the zip tool.\n\n<b>Keys</b> — hit count, minimum 4.\n<b>Beats</b> — total sweep duration; 2 beats = 360° (the default). Each tile's charter = beats×180/N (2-beat 8k = 45°).",
"집 서브메뉴",
"<b>기능</b>\n집 도구의 파라미터입니다.\n\n<b>키 수</b> — 입력 횟수, 최소 4.\n<b>박자</b> — 전체 길이. 2박자 = 360° (기본값). 타일당 각도 = 박자×180/N (2박자 8키 = 45°).");

            Add(d, "CameraMenu", "Camera playback",
"<b>What it does</b>\nPlays the camera keyframe sequence on the overlay.\n\n<b>Buttons</b>\n▶ Play all — from the first keyframe. ▶ Sel — from the selected one. Gaps — hold each keyframe until the next event's real song time (cutting long tweens short, like the game would).",
"카메라 재생",
"<b>기능</b>\n카메라 키프레임 시퀀스를 오버레이에서 재생합니다.\n\n<b>버튼</b>\n▶ 전체 재생 — 첫 키프레임부터. ▶ 선택부터 — 선택한 키프레임부터. 박자 간격 — 다음 이벤트의 실제 시간까지 대기 (게임처럼 긴 트윈은 중간에 끊음).");

            Add(d, "ToolLabel", "Current tool",
"<b>What it does</b>\nShows the active tool (pseudo key count, event tool name, …). The ? beside it opens help mode.",
"현재 도구",
"<b>기능</b>\n활성 도구를 표시합니다 (동타 키 수, 이벤트 도구 이름 등). 옆의 ?는 도움말 모드를 엽니다.");

            Add(d, "Help", "Help button",
"<b>What it does</b>\nOpens this interactive help mode.",
"도움말 버튼",
"<b>기능</b>\n이 도움말 모드를 엽니다.");

            Add(d, "FileChip", "File menu",
"<b>What it does</b>\nReplaces the game's file bar: level name + unsaved dot; click for New / Open / Open Recent / Save / …\n\n<b>Note</b>\nAll entries proxy the game's own buttons — shortcuts still work.",
"파일 메뉴",
"<b>기능</b>\n게임의 파일 바를 대체합니다: 레벨 이름 + 저장 안 됨 표시. 클릭하면 새로 만들기 / 열기 / 최근 파일 / 저장 등이 열립니다.\n\n<b>참고</b>\n모든 항목은 게임 자체 버튼을 그대로 사용하므로 단축키도 정상 동작합니다.");

            Add(d, "SettingsChip", "Editor preferences",
"<b>What it does</b>\nOpens ADOFAI's editor preferences panel.",
"에디터 환경설정",
"<b>기능</b>\nADOFAI 에디터 환경설정 패널을 엽니다.");

            Add(d, "LevelSettingsChip", "Level settings",
"<b>What it does</b>\nOpens the level settings (song, level, track, background, camera, …) in a wide popup with a labeled tab rail.\n\n<b>Keys</b>\nESC closes (background clicks don't).",
"레벨 설정",
"<b>기능</b>\n레벨 설정(곡, 레벨, 트랙, 배경, 카메라 등)을 라벨 탭이 있는 넓은 팝업으로 엽니다.\n\n<b>단축키</b>\nESC로 닫기 (배경 클릭으로는 닫히지 않음).");

            Add(d, "GameSettingsChip", "Game settings",
"<b>What it does</b>\nOpens the game's own settings screen (the pause-menu settings) from the editor.",
"게임 설정",
"<b>기능</b>\n게임 자체 설정 화면(일시정지 메뉴의 설정)을 에디터에서 엽니다.");

            Add(d, "LeaveChip", "Leave editor",
"<b>What it does</b>\nExits the editor (proxies the game's exit button).",
"에디터 나가기",
"<b>기능</b>\n에디터를 종료합니다 (게임의 나가기 버튼과 동일).");

            Add(d, "HelpChip", "Help",
"<b>What it does</b>\nOpens this interactive help mode.",
"도움말",
"<b>기능</b>\n이 도움말 모드를 엽니다.");

            Add(d, "EventDock", "Event palette",
"<b>What it does</b>\nThe event palette as persistent TOOLS: pick an event, then stamp it on tiles repeatedly.\n\n<b>How to use</b>\nLeft column switches category. Click an event to select it as the tool. RIGHT-click tiles to stamp rapidly; LEFT-click selects a tile first, a second click stamps.\n\n<b>Keys</b>\nWith a tile selected: digits 1–9 pick the nth event of the current category, Enter stamps it. ESC deselects the tool.",
"이벤트 팔레트",
"<b>기능</b>\n이벤트 팔레트를 지속 도구로 사용합니다: 이벤트를 고르면 타일에 반복해서 배치할 수 있습니다.\n\n<b>사용법</b>\n왼쪽 열은 카테고리 전환. 이벤트를 클릭하면 도구로 선택됩니다. 타일을 우클릭하면 빠르게 배치, 좌클릭은 먼저 타일 선택 → 같은 타일 재클릭 시 배치.\n\n<b>단축키</b>\n타일 선택 중: 숫자 1–9 = 현재 카테고리의 n번째 이벤트 선택, Enter = 선택된 타일에 배치. ESC = 도구 해제.");

            Add(d, "SapphireEditorChrome", "Editor chrome",
"<b>What it does</b>\nThe file header strip and event palette — Sapphire replacements for the game's editor chrome. Click a specific control for details.",
"에디터 크롬",
"<b>기능</b>\n파일 헤더 바와 이벤트 팔레트 — 게임 에디터 UI의 Sapphire 대체입니다. 개별 컨트롤을 클릭하면 상세 설명이 표시됩니다.");

            Add(d, "SapphireToolbar", "Toolbox",
"<b>What it does</b>\nThe Sapphire tool strip and its submenus. Click a specific tool icon for details.\n\n<b>Keys</b>\nDigits 1–7 select tools when no tile is selected.",
"도구 모음",
"<b>기능</b>\nSapphire 도구 모음과 서브메뉴입니다. 개별 도구 아이콘을 클릭하면 상세 설명이 표시됩니다.\n\n<b>단축키</b>\n타일 미선택 시 숫자 1–7로 도구 선택.");

            Add(d, "SapphireEditorEvents", "Timeline",
"<b>What it does</b>\nEvent timeline on real song time: markers by category, playhead, zoom, transport (play/rewind · clock · BPM), mode cluster (EDITOR / difficulty / NO FAIL / AUTO).\n\n<b>How to use</b>\nClick a marker — jumps to its tile and opens that exact event. Click empty strip — moves the playhead (drag to scrub). Wheel pans when zoomed.\n\n<b>Modes</b>\nThe mode button under the zoom controls switches between NORMAL / CAM / DECO / FILTER — the CDF workspaces. In help mode, click that button for details; the CAM workspace guide covers keyframe editing.\n\n<b>Keys</b>\nThe centre-bottom arrow folds/expands the strip.",
"타임라인",
"<b>기능</b>\n실제 곡 시간 기준의 이벤트 타임라인: 카테고리별 마커, 재생 헤드, 줌, 트랜스포트(재생/되감기 · 시계 · BPM), 모드 클러스터(EDITOR / 난이도 / NO FAIL / AUTO).\n\n<b>사용법</b>\n마커 클릭 — 해당 타일로 이동하며 그 이벤트를 바로 엽니다. 빈 곳 클릭 — 재생 헤드 이동 (드래그로 스크럽). 줌 상태에서 휠 = 이동.\n\n<b>모드</b>\n줌 버튼 아래 모드 버튼으로 NORMAL / CAM / DECO / FILTER — CDF 작업 공간을 전환합니다. 도움말 모드에서 그 버튼을 클릭하면 상세 설명을, CAM 가이드에서 키프레임 편집을 볼 수 있습니다.\n\n<b>단축키</b>\n하단 중앙 화살표로 접기/펼치기.");

            Add(d, "SapphireTimelineFold", "Timeline fold",
"<b>What it does</b>\nFolds the timeline away / brings it back. Points down when open, up when folded.",
"타임라인 접기",
"<b>기능</b>\n타임라인을 접거나 다시 펼칩니다. 열려 있으면 ▼, 접혀 있으면 ▲.");

            Add(d, "SapphireEventTabs", "Event tab rail",
"<b>What it does</b>\nThe selected tile's events as icon tabs.\n\n<b>How to use</b>\nClick a tab to open that event; right-click deletes it. With several events of one type, numbered chips appear — click a number to jump straight to that instance.",
"이벤트 탭",
"<b>기능</b>\n선택된 타일의 이벤트를 아이콘 탭으로 표시합니다.\n\n<b>사용법</b>\n탭 클릭 = 해당 이벤트 열기, 우클릭 = 삭제. 같은 유형 이벤트가 여러 개면 번호 칩이 표시됩니다 — 번호를 클릭하면 해당 항목으로 바로 이동.");

            Add(d, "SapphireCopyPanel", "Mirror & selective copy",
"<b>What it does</b>\nAppears with 2+ tiles selected.\n\n<b>Mirror</b>\nFlips the selection AND mirrors decoration/event positions (the vanilla flip doesn't). Preserve beats adds a twirl on the first tile.\n\n<b>Copy</b>\nPer-category / per-type checkboxes choose what a copy carries. Copy, then paste normally.\n\n<b>Inspector mode</b>\nWhile the Inspector tool holds a capture, this panel becomes its paste filter.",
"미러 · 선택 복사",
"<b>기능</b>\n타일을 2개 이상 선택하면 표시됩니다.\n\n<b>미러</b>\n선택을 반전하면서 장식/이벤트 좌표도 함께 반전합니다 (기본 반전은 좌표를 반전하지 않음). 박자 유지는 첫 타일에 소용돌이를 추가합니다.\n\n<b>복사</b>\n카테고리/유형별 체크박스로 복사에 포함할 항목을 고릅니다. 복사 후 평소처럼 붙여넣으세요.\n\n<b>인스펙터 모드</b>\n인스펙터 도구가 캡처를 들고 있는 동안 이 패널은 붙여넣기 필터가 됩니다.");

            Add(d, "SapphirePitch", "Practice pitch",
"<b>What it does</b>\nPractice-only playback speed — song and hitsounds together. Never touches the saved level.\n\n<b>How to use</b>\nSet a % (or ±10 with ‹ ›). Takes effect when playback starts. Reset returns to normal.",
"연습 피치",
"<b>기능</b>\n연습 전용 재생 속도 — 곡과 히트사운드가 함께 변합니다. 저장되는 레벨 데이터는 건드리지 않습니다.\n\n<b>사용법</b>\n%를 입력하거나 ‹ ›로 ±10. 재생 시작 시 적용됩니다. 초기화로 원래 속도로 복귀.");

            Add(d, "SapphireMasterSwitch", "Master switch",
"<b>What it does</b>\nTurns the whole Sapphire editor suite on/off. Off restores all vanilla UI; the switch itself stays so you can come back.",
"마스터 스위치",
"<b>기능</b>\nSapphire 에디터 기능 전체를 켜고 끕니다. 끄면 기본 UI가 모두 복원되며, 다시 켤 수 있도록 스위치는 항상 표시됩니다.");

            Add(d, "SapphireLevelMenu", "Level settings popup",
"<b>What it does</b>\nThe game's level-settings panel, hosted wide with a labeled tab rail. The game owns every field — Sapphire only hosts it.\n\n<b>Keys</b>\nESC closes.",
"레벨 설정 팝업",
"<b>기능</b>\n게임의 레벨 설정 패널을 라벨 탭과 함께 넓게 표시합니다. 모든 항목은 게임이 직접 관리하며 Sapphire는 표시만 담당합니다.\n\n<b>단축키</b>\nESC로 닫기.");

            Add(d, "SapphireCameraCard", "Camera keyframe card",
"<b>What it does</b>\nDetails for the selected camera keyframe: floor, relativeTo, offset, zoom, rotation, duration, ease. ▶ previews the move on the overlay box.",
"카메라 키프레임 카드",
"<b>기능</b>\n선택한 카메라 키프레임의 상세 정보: 타일 번호, 기준(relativeTo), 오프셋, 줌, 회전, 길이, 이징. ▶는 오버레이 박스로 이동을 미리 재생합니다.");

            Add(d, "SapphireTileMenu", "Tile menu",
"<b>What it does</b>\nRight-click a tile: Copy / Cut / Paste / Delete / Rotate.\n\n<b>Note</b>\nWhile an event or Inspector tool is active, right-click belongs to that tool instead.",
"타일 메뉴",
"<b>기능</b>\n타일 우클릭: 복사 / 잘라내기 / 붙여넣기 / 삭제 / 회전.\n\n<b>참고</b>\n이벤트 도구나 인스펙터가 활성화된 동안에는 우클릭이 해당 도구에 사용됩니다.");

            Add(d, "SapphirePresets", "Event presets",
"<b>What it does</b>\nNamed bundles of events, applied per tile via the Inspector tool.\n\n<b>How to use</b>\nCapture a tile (Inspector), then + Save capture. Click a preset to load it as the capture — stamp tiles as usual. Right-click a row to rename, × deletes.\n\n<b>Note</b>\nPresets persist across sessions.",
"이벤트 프리셋",
"<b>기능</b>\n이름을 붙인 이벤트 묶음을 인스펙터 도구로 타일에 적용합니다.\n\n<b>사용법</b>\n타일을 캡처(인스펙터)한 뒤 '+ 캡처 저장'. 프리셋을 클릭하면 캡처로 불러와 평소처럼 타일에 배치할 수 있습니다. 행 우클릭 = 이름 변경, × = 삭제.\n\n<b>참고</b>\n프리셋은 게임을 껐다 켜도 유지됩니다.");

            Add(d, "SapphireEasePicker", "Ease picker",
"<b>What it does</b>\nPicks an ease with VISUAL curve previews — every curve is plotted from the game's own runtime easing, overshoots included.\n\n<b>How to use</b>\nRight-click a keyframe in the timeline's CAM mode. The current ease is highlighted; click a cell to apply (undo works). The Custom cell opens the bezier editor. ESC or clicking outside closes.",
"이징 선택기",
"<b>기능</b>\n곡선 미리보기를 보면서 이징을 고릅니다 — 모든 곡선은 게임의 실제 런타임 이징으로 그려집니다 (오버슈트 포함).\n\n<b>사용법</b>\n타임라인 CAM 모드에서 키프레임을 우클릭하세요. 현재 이징이 강조 표시되며, 셀을 클릭하면 적용됩니다 (실행 취소 가능). Custom 셀은 베지어 편집기를 엽니다. ESC 또는 바깥 클릭으로 닫기.");

            Add(d, "CamMode", "Timeline mode menu",
"<b>What it does</b>\nSwitches the strip between the CDF workspaces: NORMAL (all events by category), CAM (camera keyframes), DECO (decoration events, lanes = the tags visible in the current view, top 8 by use), FILTER (SetFilter events, lanes per filter).\n\n<b>Deco / Filter</b>\nSame bar view as CAM: tweens as duration bars, whole bar clickable, right-click = ease picker. Lanes follow the view — pan to see other tags.",
"타임라인 모드 메뉴",
"<b>기능</b>\n스트립을 CDF 작업 공간으로 전환합니다: NORMAL(카테고리별 전체 이벤트), CAM(카메라 키프레임), DECO(장식 이벤트 — 레인은 현재 보기에 나타나는 태그 상위 8개), FILTER(SetFilter 이벤트 — 필터별 레인).\n\n<b>데코 / 필터</b>\nCAM과 같은 막대 뷰입니다: 트윈은 길이 막대, 막대 전체 클릭 가능, 우클릭 = 이징 선택기. 레인은 보기 범위를 따라갑니다 — 이동하면 다른 태그가 나타납니다.");

            Add(d, "Lane", "CAM mode — camera keyframe workspace",
"<b>Layout</b>\nOne layer per property: Position / Rotation / Zoom. Tweens draw as duration bars (head, body to the end beat, cap); duration-0 SET keyframes are thin ticks. A set tick and a tween starting together are a pair.\n\n<b>Selecting</b>\nClick anywhere on a bar (whole body counts) — the keyframe turns white, its tile is selected, and an inline row of editable fields opens at the top of the strip.\n\n<b>Retiming</b>\nClick a LANE LABEL (e.g. Zoom) to expand its diamond sub-row — dragging happens only there. Pairs fan apart so each diamond stays clickable; a ghost diamond follows the cursor. Retiming one property of an event that carries several SPLITS it into its own event (one undo).\n\n<b>Creating</b>\nRIGHT-click EMPTY lane space = new set keyframe for that property at that tile, carrying the current value.\n\n<b>More</b>\nRIGHT-click a keyframe = visual ease picker. GRAPH button (or the row's Graph button) = AE-style graph editor. ESC deselects.",
"CAM 모드 — 카메라 키프레임 작업 공간",
"<b>구성</b>\n속성별 레이어: 위치 / 회전 / 줌. 트윈은 길이 막대(머리·몸통·끝)로, duration 0 설정 키프레임은 얇은 틱으로 표시됩니다. 같은 지점에서 시작하는 틱+막대가 한 쌍입니다.\n\n<b>선택</b>\n막대의 아무 곳이나 클릭하면(몸통 전체 클릭 가능) 키프레임이 흰색으로 강조되고, 타일이 선택되며, 스트립 상단에 인라인 편집 행이 열립니다.\n\n<b>시간 이동</b>\n레인 라벨(예: 줌)을 클릭하면 다이아몬드 서브 행이 펼쳐지고, 드래그는 거기서만 가능합니다. 쌍은 옆으로 펼쳐져 각각 클릭할 수 있고, 커서를 따라 고스트 다이아몬드가 표시됩니다. 여러 속성을 가진 이벤트의 한 속성만 옮기면 이벤트가 분리됩니다 (실행 취소 1회).\n\n<b>생성</b>\n빈 레인 공간을 우클릭하면 그 타일에 해당 속성의 설정 키프레임이 생성됩니다 (현재 값 유지).\n\n<b>기타</b>\n키프레임 우클릭 = 이징 선택기. GRAPH 버튼(또는 인라인 행의 그래프 버튼) = AE 스타일 그래프 편집기. ESC = 선택 해제.");

            Add(d, "CamInspector", "Inline keyframe fields",
"<b>What it does</b>\nEditable fields for the selected keyframe: duration (beats), position X/Y, rotation, zoom — plus Ease and Graph buttons.\n\n<b>How to use</b>\nType a value to set AND enable that property; CLEAR a field to disable it (the panel's on/off toggle equivalent). Every commit is one undo step.",
"인라인 키프레임 필드",
"<b>기능</b>\n선택된 키프레임의 편집 필드: 길이(박자), 위치 X/Y, 회전, 줌 — 그리고 이징·그래프 버튼.\n\n<b>사용법</b>\n값을 입력하면 해당 속성이 설정되고 켜집니다. 필드를 비우면 속성이 꺼집니다 (패널의 켜기/끄기 토글과 동일). 모든 변경은 실행 취소 1회 단위입니다.");

            Add(d, "GraphBtn", "GRAPH button",
"<b>What it does</b>\nOpens the AE-style graph editor on the current view — no selection needed. With a keyframe selected it focuses on that tween instead.",
"GRAPH 버튼",
"<b>기능</b>\n현재 보기 범위로 AE 스타일 그래프 편집기를 엽니다 — 선택 없이도 사용 가능합니다. 키프레임이 선택되어 있으면 해당 트윈에 포커스됩니다.");

            Add(d, "SapphireGraph", "Graph editor",
"<b>What it does</b>\nAn After-Effects-style value graph for camera properties: the property's value over song time, drawn through each tween's REAL runtime easing, keyframes as diamonds.\n\n<b>How to use</b>\nOpen from the GRAPH button under the timeline's zoom controls (whole level view) or from a keyframe's inline row (focused on that keyframe). Tabs: Position (X and Y overlaid), X, Y, Rotation, Zoom; axes show values with units and beats. WHEEL over the plot zooms time; over the LEFT margin it zooms the value axis. Dragging empty plot pans in BOTH directions (vertical pan switches the value axis to manual scale). In the Position tab the X·Y button links the coordinate pair: linked (default) retimes both together on one event; unlinked, components split into their own events when retimed. Click a diamond to select (the view focuses on it), DRAG vertically to change the value, horizontally to retime — one undo per drag. Right-click a diamond for the ease picker.\n\n<b>Keys</b>\nESC or × closes.",
"그래프 편집기",
"<b>기능</b>\nAfter Effects 스타일의 카메라 속성 값 그래프: 곡 시간에 따른 속성 값을 각 트윈의 실제 이징으로 그리고, 키프레임은 다이아몬드로 표시합니다.\n\n<b>사용법</b>\n타임라인 줌 버튼 아래 GRAPH 버튼(전체 보기) 또는 키프레임 인라인 행(해당 키프레임에 포커스)으로 엽니다. 탭: 위치(X·Y 겹쳐 보기), X, Y, 회전, 줌. 축에는 단위가 표시되고 가로축은 박자입니다. 플롯 위 휠 = 시간 줌, 왼쪽 여백 위 휠 = 값 축 줌. 빈 곳 드래그 = 가로·세로 이동 (세로 이동 시 값 축이 수동 스케일로 전환). 위치 탭의 X·Y 버튼은 좌표 쌍 연결 여부입니다: 연결(기본)이면 함께 이동해 이벤트가 유지되고, 해제하면 성분별로 분리되어 새 이벤트가 생깁니다. 다이아몬드 클릭 = 선택 (뷰가 포커스됨), 세로 드래그 = 값 변경, 가로 드래그 = 타일 이동 — 드래그당 실행 취소 1회. 우클릭 = 이징 선택기.\n\n<b>단축키</b>\nESC 또는 ×로 닫기.");

            Add(d, "SapphireBezier", "Custom bezier",
"<b>What it does</b>\nA fully custom easing curve for a camera tween. The game can't play one natively, so Apply DECOMPOSES the tween into short Linear segments (default 10) sampling your curve — one undo reverts it.\n\n<b>How to use</b>\nDrag the two control points; baselines mark 0 and 1 (overshoot allowed). Needs an EARLIER keyframe of the same property to read the start value from — the duration-0 set partner of a pair works.",
"커스텀 베지어",
"<b>기능</b>\n카메라 트윈에 완전한 커스텀 이징 곡선을 적용합니다. 게임이 직접 재생할 수 없으므로, 적용 시 곡선을 샘플링한 짧은 Linear 구간들(기본 10개)로 분해합니다 — 실행 취소 1회로 되돌립니다.\n\n<b>사용법</b>\n두 컨트롤 포인트를 드래그하세요. 기준선은 0과 1입니다 (오버슈트 가능). 시작 값을 읽기 위해 같은 속성의 이전 키프레임이 필요합니다 — 쌍의 duration 0 설정 키프레임이면 충분합니다.");

            Add(d, "SapphirePopup", "Message box",
"<b>What it does</b>\nSapphire-styled version of the editor's popups; buttons proxy the game's own.",
"메시지 박스",
"<b>기능</b>\n에디터 팝업의 Sapphire 스타일 버전입니다. 버튼은 게임 자체 버튼을 그대로 사용합니다.");
            return d;
        }
    }
}
