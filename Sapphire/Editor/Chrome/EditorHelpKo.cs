using System.Collections.Generic;

namespace Sapphire
{
    /* Korean help translations, kept in ONE place so they're easy to edit without touching the
       help logic in EditorHelp.cs. Key = the topic key (matches EditorHelp.Build); value =
       (Korean title, Korean body). English text stays in EditorHelp.Build; a key missing here
       (or a null value) simply falls back to English. Use \n for a line break, \n\n for a
       paragraph break, and <b>…</b> for bold — the same rich text as the English bodies. */
    internal static class EditorHelpKo
    {
        // true + out title/body when this key has a Korean entry, else false (English fallback).
        internal static bool TryGet(string key, out string title, out string body)
        {
            if (Ko.TryGetValue(key, out var kv)) { title = kv.Key; body = kv.Value; return true; }
            title = null; body = null; return false;
        }

        private static readonly Dictionary<string, KeyValuePair<string, string>> Ko =
            new Dictionary<string, KeyValuePair<string, string>>
        {
            ["__intro"] = new KeyValuePair<string, string>("도움말 모드",
"<b>사용법</b>\n왼쪽 <b>목차</b>에서 모든 도구와 패널을 살펴볼 수 있습니다 — 항목을 클릭하면 설명이 여기에 표시됩니다.\n\n또는 Sapphire UI에 마우스를 올리면 강조선이 표시되고, 클릭하면 해당 기능의 설명으로 바로 이동합니다.\n\n<b>단축키</b>\nESC — 도움말 모드 종료."),

            ["AnglePad"] = new KeyValuePair<string, string>("각 패드",
"<b>기능</b>\n상대 각도(차터 규칙: 180 = 직진, 90 = 90도 회전, 0 = U턴)를 나열해 타일 묶음을 한 번에 추가합니다. 빠른 차팅 모드에서는 패드가 항상 하나 떠 있습니다.\n\n<b>문법</b>\n공백으로 구분된 각도이며, 값마다 수식을 쓸 수 있습니다 — 180-30, 360/8, 2*45. 끝에 <b>t</b>를 붙이면 그 타일이 소용돌이가 됩니다: 30t 30t 180. 괄호 그룹은 *로 반복하며(예: (30t 150 180)*4) 중첩할 수 있습니다.\n\n<b>버튼</b>\n소용돌이 — 첫 타일의 소용돌이를 반전합니다. 소용돌이는 회전 방향을 뒤집으므로 맨 앞의 것이 묶음 전체가 휘는 방향을 정합니다. 경로가 이미 원하는 방향으로 들어오면 빼면 됩니다. 첫 타일에 소용돌이가 있으면 버튼이 켜진 상태로 표시됩니다.\n+ — 패드를 복제하며 내용과 반복 횟수를 함께 가져갑니다(패드 하나가 임시 프리셋입니다).\n× n — 표현식을 n번 놓습니다. (…)*n과 일부러 분리했습니다. 횟수로 두면 표현식이 하나의 단위로 남아, 맨 앞 소용돌이를 빼는 효과가 첫 회차에만 적용되고 회전 방향은 다음 회차로 이어집니다.\n도형 라이브러리에 추가 — 반복까지 포함한 묶음을 '반복 없는 도형'에 저장합니다.\n배치 — 선택한 타일 뒤에 만들며, 되돌리기는 1회입니다.\n\n<b>단축키</b>\nShift+G — 새 패드. Enter — 배치(패드가 여러 개면 Enter로 선택 모드에 들어가 숫자 키로 고릅니다)."),

            ["ToolCircle"] = new KeyValuePair<string, string>("원형 경로",
"<b>기능</b>\n선택한 타일 뒤에 별/원을 생성합니다.\n\n<b>사용법</b>\n타일을 선택하고 도구를 연 뒤 라운드당 동타 수 / 간격 / 각도를 설정합니다. 공전 방향 반전, BPM 유지, 미드스핀 옵션 제공.\n\n<b>단축키</b>\n1 — 열기 (타일 미선택 시)."),

            ["ToolFreeAngle"] = new KeyValuePair<string, string>("자유 각도",
"<b>기능</b>\n다음 타일의 각도를 마우스로 자유롭게 지정합니다.\n\n<b>사용법</b>\n타일 하나를 선택한 상태에서 도구를 켜거나 왼쪽 Alt를 누르고 있으면 미리보기가 커서를 따라옵니다. 좌클릭으로 배치하고, 배치하지 않은 상태로 나가면 원래대로 돌아갑니다. <b>쉬프트</b>키를 눌른 상태로 자유 각도를 사용할 수 있습니다. \n\n<b>단축키</b>\n2 — 토글 (타일 미선택 시). 왼쪽 Alt — 누르는 동안 활성화."),

            ["ToolPseudo"] = new KeyValuePair<string, string>("동타",
"<b>기능</b>\n타일을 동타로 변환합니다.\n\n<b>사용법</b>\n단일 타일: 도구를 켠 상태에서 선택된 타일을 다시 클릭하면 변환됩니다. 다중 선택: 간격 + 스타일(위로 / 옆으로 / 인라인) 대화상자가 열리며, 기존 경로 위에 동타가 추가됩니다.\n\n<b>서브메뉴</b>\n키 수(버튼 또는 직접 입력), 각도 프리셋 + 자유 입력, 미드스핀 토글(탭+미드스핀 교차 구성), 타일별 커스텀 각도.\n\n<b>단축키</b>\n3 — 토글 (타일 미선택 시). 활성 중 숫자 키 = 동타 키 개수 설정."),

            ["ToolCamera"] = new KeyValuePair<string, string>("카메라 경로",
"<b>기능</b>\n모든 MoveCamera 키프레임을 표시합니다: 청록색 점(주황 = 플레이어 기준)이 점선으로 연결됩니다.\n\n<b>사용법</b>\n점을 클릭하면 상세 카드와 화면 영역 박스가 표시됩니다. 카드의 ▶는 실제 길이와 가감속으로 해당 이동을 미리 재생합니다.\n\n<b>서브메뉴</b>\n▶ 전체 재생 — 시퀀스 전체. ▶ 선택부터 — 선택한 키프레임부터. 박자 간격 — 이벤트 사이의 실제 박자 간격을 기다립니다.\n\n<b>단축키</b>\n9 — 토글 (타일 미선택 시)."),

            ["ToolVfx"] = new KeyValuePair<string, string>("VFX 미리보기",
"<b>기능</b>\nSapphire와 게임의 모든 UI를 숨겨 레벨만 깔끔하게 봅니다.\n\n<b>단축키</b>\n0 — 토글 (타일 미선택 시). ESC — 종료."),

            ["ToolInspector"] = new KeyValuePair<string, string>("인스펙터",
"<b>기능</b>\n이벤트 서식 복사 도구: 한 타일의 이벤트를 복사해 다른 타일에 붙여넣습니다.\n\n<b>사용법</b>\n도구를 켠 상태에서 선택된 타일을 다시 클릭하면 이벤트를 캡처합니다. 아무 타일이나 우클릭하면 붙여넣습니다. 표시되는 패널은 붙여넣기 필터입니다 — 원하지 않는 이벤트는 체크를 해제하세요.\n\n<b>단축키</b>\n8 — 토글 (타일 미선택 시)."),

            ["ToolZip"] = new KeyValuePair<string, string>("드르륵",
"<b>기능</b>\n타일을 드르륵(zip)으로 대체합니다.\n\n<b>사용법</b>\n도구를 켠 상태에서 선택된 타일을 다시 클릭합니다. 서브메뉴에서 키 수와 총 길이(박자, 기본 2박자 = 360°)를 설정합니다.\n\n<b>단축키</b>\n4 — 토글 (타일 미선택 시). 활성 중 숫자 4–8 = 키 수."),

            ["ToolMagic"] = new KeyValuePair<string, string>("마법진",
"<b>기능</b>\n마법진 도구 모음: 승수 — 선택 구간을 목표 BPM(또는 배수)에 맞춰 리타이밍하거나 각도를 재구성합니다. 생성 — 타일 범위를 N꼭짓점 도형으로 복제합니다 (고스트 미리보기). 회전 — 범위의 타일 각도를 회전합니다.\n\n<b>사용법</b>\n패널에서 탭을 고르고 범위를 설정한 뒤 (선택 = 현재 선택, 전체 = 레벨 전체) 적용합니다. 오류는 상태 줄에 표시됩니다.\n\n<b>단축키</b>\n5 — 토글 (타일 미선택 시).\n\n<b>크레딧</b>\nMagicShapeMultiply (tjwogud, JofoDuh) + MappingHelper (Sprout34)."),

            ["ToolTrack"] = new KeyValuePair<string, string>("트랙 도구",
"<b>기능</b>\n트랙 연출 생성기: 페이드 인/아웃 — 무작위 MoveTrack 애니메이션. 폭발 — 퍼져나가는 충격파. 크기 — 가감속 스케일 램프. 다중 트랙 — 트랙의 장식 복제 (가짜 행성 포함) 및 태그된 복제 애니메이션. 생성 — 각도 문자열로 타일 추가 (T = 회오리, 고스트 미리보기).\n\n<b>사용법</b>\n탭을 고르고 타일 범위를 설정한 뒤 무작위 행을 조정하고 (행 라벨 클릭 = 켜기/끄기) 적용합니다 — 실행 취소 1회로 묶입니다.\n\n<b>단축키</b>\n6 — 토글 (타일 미선택 시).\n\n<b>크레딧</b>\nMappingHelper (Sprout34)."),

            ["ToolDeco"] = new KeyValuePair<string, string>("장식 도구",
"<b>기능</b>\n장식 생성기: 플립북 — 이미지 시퀀스 폴더로 애니메이션 장식 생성. 추출 — 영상을 프레임 폴더로 변환. 3D 스택 — 색상 그라디언트와 함께 보간된 장식 복제 N개. 가사 — 텍스트를 게임 텍스트 또는 폰트 렌더링 PNG로 생성 (등장/소멸 애니메이션 포함).\n\n<b>사용법</b>\n먼저 레벨을 저장하세요 — 파일 경로는 레벨 폴더 기준입니다. 탭을 고르고 항목을 채운 뒤 적용합니다.\n\n<b>단축키</b>\n7 — 토글 (타일 미선택 시).\n\n<b>크레딧</b>\nMappingHelper (Sprout34)."),

            ["ToolBar"] = new KeyValuePair<string, string>("도구 모음",
"<b>기능</b>\nSapphire 도구 모음입니다. 기능별로 묶여 있습니다: 제작 (곡선 경로, 자유 각도, 동타, 집, 마법진) · 생성 (트랙 도구, 장식 도구) · 이벤트 (인스펙터) · 보기 (카메라 경로, VFX 미리보기). 도구에 마우스를 올리면 아래에 힌트가 표시됩니다. 도움말 모드에서 아이콘을 클릭하면 상세 설명을 볼 수 있습니다.\n\n<b>단축키</b>\n타일 미선택 시 숫자 1–0으로 도구 선택."),

            ["PseudoMenu"] = new KeyValuePair<string, string>("동타 서브메뉴",
"<b>기능</b>\n동타 도구의 설정입니다.\n\n<b>항목</b>\n키 수 — 입력 횟수 (버튼 또는 직접 입력). 미드스핀 — 탭+미드스핀 교차 구성 (경로가 정확히 복귀). 각도 — 프리셋 + 자유 입력. 커스텀 — 공백으로 구분한 타일별 각도 (키 수보다 우선)."),

            ["ZipMenu"] = new KeyValuePair<string, string>("집 서브메뉴",
"<b>기능</b>\n집 도구의 파라미터입니다.\n\n<b>키 수</b> — 입력 횟수, 최소 4.\n<b>박자</b> — 전체 길이. 2박자 = 360° (기본값). 타일당 각도 = 박자×180/N (2박자 8키 = 45°)."),

            ["CameraMenu"] = new KeyValuePair<string, string>("카메라 재생",
"<b>기능</b>\n카메라 키프레임 시퀀스를 오버레이에서 재생합니다.\n\n<b>버튼</b>\n▶ 전체 재생 — 첫 키프레임부터. ▶ 선택부터 — 선택한 키프레임부터. 박자 간격 — 다음 이벤트의 실제 시간까지 대기 (게임처럼 긴 트윈은 중간에 끊음)."),

            ["ToolLabel"] = new KeyValuePair<string, string>("현재 도구",
"<b>기능</b>\n선택된 도구를 표시합니다 (동타 키 수, 이벤트 도구 이름 등)."),

            ["Help"] = new KeyValuePair<string, string>("도움말 버튼",
"<b>기능</b>\n이 도움말 창을 엽니다."),

            ["FileChip"] = new KeyValuePair<string, string>("파일 메뉴",
"<b>기능</b>\n게임의 파일 바를 대체합니다.\n\n<b>참고</b>\n모든 항목은 게임 자체 버튼을 그대로 사용하므로 단축키도 정상 동작합니다."),

            ["SettingsChip"] = new KeyValuePair<string, string>("에디터 환경설정",
"<b>기능</b>\nADOFAI 에디터 환경설정 패널을 엽니다."),

            ["LevelSettingsChip"] = new KeyValuePair<string, string>("레벨 설정",
"<b>기능</b>\n레벨 설정(곡, 레벨, 트랙, 배경, 카메라 등)을 팝업으로 엽니다.\n\n<b>단축키</b>\nESC로 닫기."),

            ["GameSettingsChip"] = new KeyValuePair<string, string>("게임 설정",
"<b>기능</b>\n게임 자체 설정 화면(일시정지 메뉴의 설정)을 에디터에서 엽니다."),

            ["LeaveChip"] = new KeyValuePair<string, string>("에디터 나가기",
"<b>기능</b>\n에디터를 종료합니다."),

            ["HelpChip"] = new KeyValuePair<string, string>("도움말",
"<b>기능</b>\n이 도움말 모드를 엽니다."),

            ["EventDock"] = new KeyValuePair<string, string>("이벤트 팔레트",
"<b>기능</b>\n이벤트 팔레트를 지속 도구로 사용합니다: 이벤트를 고르면 타일에 반복해서 배치할 수 있습니다.\n\n<b>사용법</b>\n왼쪽 열은 카테고리 전환. 이벤트를 클릭하면 도구로 선택됩니다. 타일을 우클릭하면 빠르게 배치, 좌클릭은 먼저 타일 선택 → 같은 타일 재클릭 시 배치.\n\n<b>단축키</b>\n타일 선택 중: 숫자 1–9 = 현재 카테고리의 n번째 이벤트 선택, Enter = 선택된 타일에 배치. ESC = 도구 해제."),

            ["SapphireEditorChrome"] = new KeyValuePair<string, string>("에디터 크롬",
"<b>기능</b>\n파일 헤더 바와 이벤트 팔레트 — 게임 에디터 UI의 Sapphire 대체입니다. 개별 컨트롤을 클릭하면 상세 설명이 표시됩니다."),

            ["SapphireToolbar"] = new KeyValuePair<string, string>("도구 모음",
"<b>기능</b>\nSapphire 도구 모음과 서브메뉴입니다. 개별 도구 아이콘을 클릭하면 상세 설명이 표시됩니다.\n\n<b>단축키</b>\n타일 미선택 시 숫자 1–0으로 도구 선택."),

            ["SapphireEditorEvents"] = new KeyValuePair<string, string>("타임라인",
"<b>기능</b>\n실제 곡 시간 기준의 이벤트 타임라인: 카테고리별 마커, 재생 헤드, 확대, 트랜스포트(재생/되감기 · 시계 · BPM), 모드 클러스터(EDITOR / 난이도 / NO FAIL / AUTO).\n\n<b>사용법</b>\n마커 클릭 — 해당 타일로 이동하며 그 이벤트를 바로 엽니다. 빈 곳 클릭 — 재생 헤드 이동 (드래그로 이동도 가능). 확대 상태에서 휠 = 이동.\n\n<b>모드</b>\n확대 버튼 아래 모드 버튼으로 NORMAL / CAM / DECO / FILTER — CDF 작업 공간을 전환합니다. 도움말 모드에서 그 버튼을 클릭하면 상세 설명을, CAM 가이드에서 키프레임 편집을 볼 수 있습니다.\n\n<b>단축키</b>\n하단 중앙 화살표로 접기/펼치기. 스트립의 위쪽 가장자리를 드래그하면 레인 높이가 조절됩니다."),

            ["SapphireTimelineFold"] = new KeyValuePair<string, string>("타임라인 접기",
"<b>기능</b>\n타임라인을 접거나 다시 펼칩니다. 열려 있으면 ▼, 접혀 있으면 ▲."),

            ["SapphireEventTabs"] = new KeyValuePair<string, string>("이벤트 탭",
"<b>기능</b>\n선택된 타일의 이벤트를 아이콘 탭으로 표시합니다.\n\n<b>사용법</b>\n탭 클릭 = 해당 이벤트 열기, 우클릭 = 삭제. 같은 유형 이벤트가 여러 개면 번호 칩이 표시됩니다 — 번호를 클릭하면 해당 항목으로 바로 이동합니다."),

            ["SapphireCopyPanel"] = new KeyValuePair<string, string>("미러 · 선택 복사",
"<b>기능</b>\n타일을 2개 이상 선택하면 표시됩니다.\n\n<b>미러</b>\n선택을 반전하면서 장식/이벤트 좌표도 함께 반전합니다 (게임 내 단축기로의 반전은 이벤트의 좌표를 반전하지 않음). 박자 유지는 첫 타일에 소용돌이를 추가합니다.\n\n<b>복사</b>\n카테고리/유형별 체크박스로 복사에 포함할 항목을 고릅니다. 복사 후 평소처럼 붙여넣으세요.\n\n<b>인스펙터 모드</b>\n인스펙터 도구가 캡처를 들고 있는 동안 이 패널은 붙여넣기 필터가 됩니다."),

            ["SapphirePitch"] = new KeyValuePair<string, string>("연습 피치",
"<b>기능</b>\n연습 전용 재생 속도 — 곡과 히트사운드가 함께 변합니다. 저장되는 레벨 데이터는 건드리지 않습니다.\n\n<b>사용법</b>\n%를 입력하거나 ‹ ›로 ±10. 재생 시작 시 적용됩니다. 초기화로 원래 속도로 복귀."),

            ["SapphireMasterSwitch"] = new KeyValuePair<string, string>("마스터 스위치",
"<b>기능</b>\nSapphire 에디터 기능 전체를 켜고 끕니다. 끄면 기본 UI가 모두 복원되며, 다시 켤 수 있도록 스위치는 항상 표시됩니다."),

            ["SapphireLevelMenu"] = new KeyValuePair<string, string>("레벨 설정 팝업",
"<b>기능</b>\n게임의 레벨 설정 패널을 라벨 탭과 함께 넓게 표시합니다.\n\n<b>단축키</b>\nESC로 닫기."),

            ["SapphireCameraCard"] = new KeyValuePair<string, string>("카메라 키프레임 카드",
"<b>기능</b>\n선택한 카메라 키프레임의 상세 정보: 타일 번호, 기준(relativeTo), 오프셋, 확대, 회전, 길이, 가감속. ▶는 오버레이 박스로 이동을 미리 재생합니다."),

            ["SapphireTileMenu"] = new KeyValuePair<string, string>("타일 메뉴",
"<b>기능</b>\n타일 우클릭: 복사 / 잘라내기 / 붙여넣기 / 삭제 / 회전.\n\n<b>참고</b>\n이벤트 도구나 인스펙터가 활성화된 동안에는 우클릭이 해당 도구에 사용됩니다."),

            ["SapphirePresets"] = new KeyValuePair<string, string>("이벤트 프리셋",
"<b>기능</b>\n이름을 붙인 이벤트 묶음을 인스펙터 도구로 타일에 적용합니다.\n\n<b>사용법</b>\n타일을 캡처(인스펙터)한 뒤 '+ 캡처 저장'. 프리셋을 클릭하면 캡처로 불러와 평소처럼 타일에 배치할 수 있습니다. 행 우클릭 = 이름 변경, × = 삭제.\n\n<b>참고</b>\n프리셋은 게임을 껐다 켜도 유지됩니다."),

            ["SapphireEasePicker"] = new KeyValuePair<string, string>("가감속 선택기",
"<b>기능</b>\n곡선 미리보기를 보면서 가감속을 그래프를 고릅니다 — 모든 곡선은 게임의 실제 런타임 가감속으로 그려집니다 (오버슈트 포함).\n\n<b>사용법</b>\n타임라인 CAM 모드에서 키프레임을 우클릭하세요. 현재 가감속이 강조 표시되며, 셀을 클릭하면 적용됩니다 (실행 취소 가능). Custom 셀은 베지어 편집기를 엽니다. ESC 또는 바깥 클릭으로 닫기."),

            ["CamMode"] = new KeyValuePair<string, string>("타임라인 모드 메뉴",
"<b>기능</b>\n스트립을 CDF 작업 공간으로 전환합니다: NORMAL(카테고리별 전체 이벤트), CAM(카메라 키프레임), DECO(장식 이벤트 — 레인은 현재 보기에 나타나는 태그 상위 8개), FILTER(SetFilter 이벤트 — 필터별 레인).\n\n<b>데코 / 필터</b>\nCAM과 같은 막대 뷰입니다: 트윈은 길이 막대, 막대 전체 클릭 가능, 우클릭 = 가감속 선택기. 레인은 보기 범위를 따라갑니다 — 이동하면 다른 태그가 나타납니다."),

            ["Lane"] = new KeyValuePair<string, string>("CAM 모드 — 카메라 키프레임 작업 공간",
"<b>구성</b>\n속성별 레이어: 위치 / 회전 / 확대. 트윈은 길이 막대(머리·몸통·끝)로, duration 0 설정 키프레임은 얇은 선(틱)으로 표시됩니다. 같은 지점에서 시작하는 틱+막대가 한 쌍입니다.\n\n<b>선택</b>\n막대의 아무 곳이나 클릭하면(몸통 전체 클릭 가능) 키프레임이 흰색으로 강조되고, 타일이 선택되며, 스트립 상단에 인라인 편집 행이 열립니다.\n\n<b>시간 이동</b>\n레인 라벨(예: 확대)을 클릭하면 다이아몬드 서브 행이 펼쳐집니다. 키프레임 쌍은 옆으로 펼쳐져 각각 클릭할 수 있고, 여러 속성을 가진 이벤트의 한 속성만 옮기면 이벤트가 분리됩니다 (실행 취소 1회).\n\n<b>생성</b>\n빈 레인 공간을 우클릭하면 그 타일에 해당 속성의 설정 키프레임이 생성됩니다 (현재 값 유지).\n\n<b>기타</b>\n키프레임 우클릭 = 가감속 선택기. GRAPH 버튼(또는 인라인 행의 그래프 버튼) = AE 스타일 그래프 편집기. ESC = 선택 해제."),

            ["CamInspector"] = new KeyValuePair<string, string>("키프레임 속성값",
"<b>기능</b>\n선택된 키프레임의 편집 필드: 길이(박자), 위치 X/Y, 회전, 확대 — 그리고 가감속·그래프 버튼.\n\n<b>사용법</b>\n값을 입력하면 해당 속성이 설정되고 켜집니다. 필드를 비우면 속성이 꺼집니다 (패널의 켜기/끄기 토글과 동일). 모든 변경은 실행 취소 1회 단위입니다."),

            ["GraphBtn"] = new KeyValuePair<string, string>("GRAPH 버튼",
"<b>기능</b>\n현재 보기 범위로 AE 스타일 그래프 편집기를 엽니다 — 선택 없이도 사용 가능합니다. 키프레임이 선택되어 있으면 해당 트윈에 포커스됩니다."),

            ["SapphireGraph"] = new KeyValuePair<string, string>("그래프 편집기",
"<b>기능</b>\nAfter Effects 스타일의 카메라 속성 값 그래프: 곡 시간에 따른 속성 값을 각 트윈의 실제 가감속으로 그리고, 키프레임은 다이아몬드로 표시합니다.\n\n<b>사용법</b>\n타임라인 확대 버튼 아래 GRAPH 버튼(전체 보기) 또는 키프레임 인라인 행(해당 키프레임에 포커스)으로 엽니다. 탭: 위치(X·Y 겹쳐 보기), X, Y, 회전, 확대. 축에는 단위가 표시되고 가로축은 박자입니다. 플롯 위 휠 = 시간 확대, 왼쪽 여백 위 휠 = 값 축 확대. 빈 곳 드래그 = 가로·세로 이동 (세로 이동 시 값 축이 수동 스케일로 전환). 위치 탭의 X·Y 버튼은 좌표 쌍 연결 여부입니다: 연결(기본)이면 함께 이동해 이벤트가 유지되고, 해제하면 성분별로 분리되어 새 이벤트가 생깁니다. 다이아몬드 클릭 = 선택 (뷰가 포커스됨), 세로 드래그 = 값 변경, 가로 드래그 = 타일 이동 — 드래그당 실행 취소 1회. 우클릭 = 가감속 선택기.\n\n<b>창</b>\n그래프 뷰 헤더를 드래그해 창을 옮기고, 가장자리나 모서리를 드래그해 크기를 조절합니다.\n\n<b>단축키</b>\nESC 또는 ×로 닫기."),

            ["SapphireBezier"] = new KeyValuePair<string, string>("커스텀 베지어",
"<b>기능</b>\n카메라 트윈에 완전한 커스텀 가감속 곡선을 적용합니다. 게임이 직접 재생할 수 없으므로, 적용 시 곡선을 샘플링한 짧은 Linear 구간들(기본 10개)로 분해합니다 — 실행 취소 1회로 되돌립니다.\n\n<b>사용법</b>\n두 컨트롤 포인트를 드래그하세요. 기준선은 0과 1입니다 (오버슈트 가능). 시작 값을 읽기 위해 같은 속성의 이전 키프레임이 필요합니다 — 키프레임 쌍의 duration 0 설정 키프레임이면 충분합니다."),

            ["SapphireFilterPicker"] = new KeyValuePair<string, string>("필터 관리자",
"<b>기능</b>\n필터 이벤트를 한 곳에서 관리합니다: 300여 개 고급 필터의 검색·카테고리·카드 그리드, 활성 필터의 파라미터 편집기, 삭제 기능을 포함하는 메뉴입니다.\n\n<b>사용법</b>\nSetFilter/SetFilterAdvanced 이벤트를 열면 패널 옆에 '필터 관리…' 버튼이 나타납니다. 카드를 클릭하면 즉시 적용되며(실행 취소 가능) 관리자는 열린 채 유지됩니다. 오른쪽 열에서 활성 필터의 파라미터를 편집하고(빈 필드 = 재정의 없음) '이벤트 삭제'로 이벤트를 제거합니다(실행 취소 가능). 카드에 마우스를 올리면 하단 바에 파라미터가 표시됩니다.\n\n<b>단축키</b>\nESC 또는 ×로 닫기. 배경 클릭으로는 닫히지 않습니다."),

            ["SapphirePopup"] = new KeyValuePair<string, string>("메시지 박스",
"<b>기능</b>\n에디터 팝업의 Sapphire 스타일 버전입니다. 버튼은 게임 자체 버튼을 그대로 사용합니다."),
        };
    }
}
