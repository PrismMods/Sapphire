using System.Collections.Generic;
using UnityEngine;

namespace Sapphire
{
    /* Minimal localization: T(english) returns the Korean string when the GAME's language is
       Korean (RDString.language — follows the user's in-game setting), else the English source.
       Keys ARE the English strings, so unmapped text degrades gracefully. Long-form help topics
       are mapped as whole bodies. */
    internal static class Loc
    {
        internal static bool Korean
        {
            get
            {
                try
                {
                    var s = MainClass.Settings;
                    if (s != null && s.UiLanguage == 1) return false;      // forced English
                    if (s != null && s.UiLanguage == 2) return true;       // forced Korean
                    return RDString.language == SystemLanguage.Korean;      // auto: follow the game
                }
                catch { return false; }
            }
        }

        internal static string T(string en)
        {
            if (!Korean || en == null) return en;
            return Ko.TryGetValue(en, out var ko) ? ko : en;
        }

        private static readonly Dictionary<string, string> Ko = new Dictionary<string, string>
        {
            // ── toolbar tooltips / tool names ──
            ["Circular path"] = "원형 경로",
            ["Free angle"] = "자유 각도",
            ["Pseudo"] = "동타",
            ["Camera path"] = "카메라 경로",
            ["VFX preview (ESC exits)"] = "VFX 미리보기 (ESC로 종료)",
            ["Inspector (copy tile events)"] = "인스펙터 (타일 이벤트 복사)",
            ["Zip Tool"] = "드르륵",
            ["Instruction manual"] = "설명",
            ["Zip"] = "드르륵",
            ["Inspector"] = "인스펙터",
            ["Inspector · pick a tile"] = "인스펙터 · 타일을 선택하세요",

            // ── submenus ──
            ["Keys"] = "키 수",
            ["Midspin"] = "미드스핀",
            ["Angle"] = "각도",
            ["Custom"] = "커스텀",
            ["Beats"] = "박자",
            ["per-tile angles, e.g. 30 60 90"] = "타일별 각도, 예: 30 60 90",
            ["▶ Play all"] = "▶ 전체 재생",
            ["▶ Selected"] = "▶ 선택부터",
            ["Play Gaps"] = "박자 간격 재생",

            // ── curved path dialog ──
            ["Curved path"] = "곡선 경로",
            ["Pseudo (star)"] = "동타 (별)",
            ["Pseudo per round"] = "한 바퀴당 동타",
            ["Pseudo angle"] = "동타 각도",
            ["Reverse"] = "반전",
            ["Outer angle"] = "외각",
            ["Inner angle"] = "내각",
            ["Keep BPM"] = "BPM 유지",
            ["Pseudos as mid-spin tiles"] = "동타를 미드스핀 타일로",
            ["Circle degrees"] = "원 각도",
            ["Tile count"] = "타일 수",

            // ── batch dialog ──
            ["Pseudo interval"] = "동타 간격",
            ["Pseudo across {0} tiles"] = "타일 {0}개에 동타",
            ["Angled at {0}° (inline needs 90°)."] = "{0}° 각도 (인라인은 90°에서만 가능).",
            ["Set pseudos to {0}°"] = "동타를 {0}°로 변경",
            ["Level settings"] = "레벨 설정",
            ["On"] = "켬",
            // filter manager: keys/values the game leaves unmapped in its own tables
            ["targetType"] = "대상 유형",
            ["Foreground"] = "전경",
            ["Background"] = "배경",
            ["(no events on this tile)"] = "(이 타일에 이벤트 없음)",
            ["(settings unavailable)"] = "(설정을 불러올 수 없음)",
            ["Search events"] = "이벤트 검색",
            ["Favorites"] = "즐겨찾기",
            ["Style"] = "스타일",
            ["Upwards"] = "위로",
            ["Sideways"] = "옆으로",
            ["Inline"] = "인라인",
            ["Downwards"] = "아래로",
            ["Apply"] = "적용",
            ["Style: Inline (selection isn't straight)"] = "스타일: 인라인 (선택이 직선이 아님)",

            // ── tile menu ──
            ["Copy"] = "복사",
            ["Cut"] = "잘라내기",
            ["Paste"] = "붙여넣기",
            ["Delete"] = "삭제",
            ["Rotate CW"] = "시계 방향 회전",
            ["Rotate CCW"] = "반시계 방향 회전",

            // ── copy / mirror panel ──
            ["Copy options"] = "복사 옵션",
            ["Mirror"] = "반전",
            ["Horizontal"] = "좌우",
            ["Vertical"] = "상하",
            ["Preserve beats"] = "박자 유지",
            ["Events"] = "이벤트",
            ["All"] = "전체",
            ["None"] = "없음",
            ["Paste filter"] = "붙여넣기 필터",
            ["(no events)"] = "(이벤트 없음)",
            ["Gameplay"] = "게임플레이",
            ["Track"] = "트랙",
            ["Decorations"] = "장식",
            ["VFX"] = "시각 효과",
            ["Modifiers"] = "수정",
            ["Conveniences"] = "편의 기능",
            ["DLC"] = "DLC",
            ["Other"] = "그 외",

            // ── presets ──
            ["Presets"] = "프리셋",
            ["Preset"] = "프리셋",
            ["+ Save capture"] = "+ 캡처 저장",
            ["(none — capture a tile, then Save)"] = "(없음 — 타일을 캡처한 뒤 저장하세요)",

            // ── camera timeline ──
            ["Ease"] = "이징",
            ["Custom bezier"] = "커스텀 베지어",
            ["Graph"] = "그래프",
            ["Graph View"] = "그래프 뷰",
            ["Filters…"] = "필터 관리…",
            ["Filter manager"] = "필터 관리자",
            ["Filter manager (legacy)"] = "필터 관리자 (레거시)",
            ["Delete event"] = "이벤트 삭제",
            ["(no filter events on this tile)"] = "(이 타일에 필터 이벤트 없음)",
            ["Edited in the event panel"] = "이벤트 패널에서 편집하세요",
            ["Search filters"] = "필터 검색",
            ["All"] = "전체",
            ["Needs an earlier keyframe for the start value"] = "시작 값을 읽을 이전 키프레임이 필요합니다",
            ["This keyframe has no duration to decompose"] = "분해할 지속 시간이 없는 키프레임입니다",
            ["This keyframe animates nothing decomposable"] = "분해할 수 있는 속성이 없는 키프레임입니다",
            ["Segments"] = "구간 수",
            ["Cancel"] = "취소",
            ["Position"] = "위치",
            ["Rotation"] = "회전",
            ["Zoom"] = "확대",

            // ── magic shape panel ──
            ["Magic Shape"] = "마법진",
            ["Magic shape (multiply / create / rotate)"] = "마법진 (승수 / 생성 / 회전)",
            ["Multiply"] = "승수",
            ["Create"] = "생성",
            ["Rotate"] = "회전",
            ["Target BPM"] = "목표 BPM",
            ["Multiplier"] = "배수",
            ["BPM"] = "BPM",
            ["Write as"] = "기록 방식",
            ["Twirls"] = "회오리",
            ["Keep"] = "유지",
            ["Strip"] = "제거",
            ["Inner"] = "내각",
            ["Outer"] = "외각",
            ["Top icon"] = "상단 아이콘",
            ["Speed"] = "속도",
            ["Twirl"] = "회오리",
            ["Reshape angles instead"] = "각도 재구성 모드",
            ["Angle fix"] = "각도 보정",
            ["Off"] = "끔",
            ["Apply to selection"] = "선택 영역에 적용",
            ["Tiles"] = "타일",
            ["Sel"] = "선택",
            ["Vertices"] = "꼭짓점",
            ["Inverse direction"] = "반대 방향",
            ["Preview (ghost tiles)"] = "미리보기 (고스트 타일)",
            ["Create shape"] = "마법진 생성",
            ["Degrees"] = "각도(°)",
            ["Rotate range"] = "범위 회전",
            ["Applied"] = "적용됨",
            ["Shape created"] = "마법진 생성됨",
            ["Select at least two tiles"] = "타일을 2개 이상 선택하세요",
            ["Nothing selected"] = "선택된 타일이 없습니다",
            ["Range contains unsupported events: {0}"] = "범위에 지원되지 않는 이벤트가 있습니다: {0}",
            ["Angle exceeds 360° at tile {0} — set Angle fix"] = "타일 {0}에서 각도가 360°를 초과합니다 — 각도 보정을 설정하세요",
            ["Tile {0}: old levels need 15° multiples"] = "타일 {0}: 구버전 레벨은 15° 배수만 가능합니다",
            ["Needs a modern (mesh-floor) level"] = "신버전 (메시 타일) 레벨이 필요합니다",
            ["Vertices must be at least 2"] = "꼭짓점은 2개 이상이어야 합니다",
            ["Multiply failed — see log"] = "승수 적용 실패 — 로그를 확인하세요",
            ["Create failed — see log"] = "생성 실패 — 로그를 확인하세요",
            ["Rotate failed — see log"] = "회전 실패 — 로그를 확인하세요",

            // ── track tools panel ──
            ["Track Tools"] = "트랙 도구",
            ["Track tools (fades / explode / copies / generate)"] = "트랙 도구 (페이드 / 폭발 / 복제 / 생성)",
            ["Fade in"] = "페이드 인",
            ["Fade out"] = "페이드 아웃",
            ["Explode"] = "폭발",
            ["Size"] = "크기",
            ["Multi"] = "다중 트랙",
            ["Generate"] = "생성",
            ["Move window"] = "이동 범위 (상대)",
            ["Window (rel)"] = "범위 (상대)",
            ["Duration"] = "지속 시간",
            ["Duration follows BPM"] = "지속 시간 BPM 연동",
            ["X offset"] = "X 오프셋",
            ["Y offset"] = "Y 오프셋",
            ["Rotation"] = "회전",
            ["Scale"] = "크기",
            ["Opacity"] = "불투명도",
            ["Land scale"] = "도착 크기",
            ["Land parallax"] = "도착 시차",
            ["Angle offset"] = "각도 오프셋",
            ["Step angle"] = "단계별 각도",
            ["Track scale"] = "트랙 크기",
            ["Radius"] = "반지름",
            ["Planets"] = "행성",
            ["Copies"] = "복제",
            ["Animate"] = "애니메이션",
            ["Per tile"] = "타일별",
            ["One tile"] = "한 타일에",
            ["Tag"] = "태그",
            ["Fake planets"] = "가짜 행성",
            ["Planet events on first tile"] = "행성 이벤트를 첫 타일에",
            ["Depth 1st/step"] = "깊이 시작/증가",
            ["Parallax"] = "시차",
            ["Parallax affects planets"] = "시차를 행성에도 적용",
            ["Appear"] = "등장",
            ["Disappear"] = "소멸",
            ["Events per tile (off: first tile)"] = "이벤트 타일별 (끄면 첫 타일)",
            ["Copy # offset"] = "복제 번호 오프셋",
            ["Create copies"] = "복제 생성",
            ["Enter the copies' tag first"] = "먼저 복제 태그를 입력하세요",
            ["After tile"] = "타일 뒤에",
            ["End"] = "끝",
            ["Angles (T = twirl)"] = "각도 목록 (T = 회오리)",
            ["Repeat"] = "반복",
            ["Added {0} tiles"] = "타일 {0}개 추가됨",
            ["No angles parsed — e.g. 45 90T 135"] = "각도를 읽지 못했습니다 — 예: 45 90T 135",
            ["{0} failed — see log"] = "{0} 실패 — 로그를 확인하세요",
            ["Multi-track"] = "다중 트랙",
            ["Animate copies"] = "복제 애니메이션",

            // ── deco tools panel ──
            ["Deco Tools"] = "장식 도구",
            ["Deco tools (flipbook / video / 3D / lyrics)"] = "장식 도구 (플립북 / 영상 / 3D / 가사)",
            ["Flipbook"] = "플립북",
            ["Extract"] = "추출",
            ["3D stack"] = "3D 스택",
            ["Lyrics"] = "가사",
            ["Frame folder"] = "프레임 폴더",
            ["Event tag"] = "이벤트 태그",
            ["Frame window (off: all frames)"] = "프레임 범위 (끄면 전체)",
            ["Frames"] = "프레임",
            ["Start angle"] = "시작 각도",
            ["Angle per frame"] = "프레임당 각도",
            ["Create flipbook"] = "플립북 생성",
            ["{0} frames per tile"] = "타일당 프레임 {0}개",
            ["No frames found"] = "프레임을 찾지 못했습니다",
            ["Frame folder not found in the level folder"] = "레벨 폴더에서 프레임 폴더를 찾지 못했습니다",
            ["Video file (in the level folder)"] = "영상 파일 (레벨 폴더 안)",
            ["Format"] = "형식",
            ["Extract frames"] = "프레임 추출",
            ["Already extracting…"] = "이미 추출 중입니다…",
            ["Extracting frames…"] = "프레임 추출 중…",
            ["Extracted {0} frames to {1}"] = "{1} 폴더에 프레임 {0}개 추출됨",
            ["Video file not found"] = "영상 파일을 찾지 못했습니다",
            ["Frames land in a folder named after the video — use it in Flipbook."] = "프레임은 영상 이름의 폴더에 저장됩니다 — 플립북에서 사용하세요.",
            ["Image file"] = "이미지 파일",
            ["Image file not found"] = "이미지 파일을 찾지 못했습니다",
            ["Pos X from/to"] = "위치 X 시작/끝",
            ["Pos Y from/to"] = "위치 Y 시작/끝",
            ["Pivot X from/to"] = "피벗 X 시작/끝",
            ["Pivot Y from/to"] = "피벗 Y 시작/끝",
            ["Rotation from/to"] = "회전 시작/끝",
            ["Scale X from/to"] = "크기 X 시작/끝",
            ["Scale Y from/to"] = "크기 Y 시작/끝",
            ["Opacity from/to"] = "불투명도 시작/끝",
            ["Depth from/to"] = "깊이 시작/끝",
            ["Parallax X f/t"] = "시차 X 시작/끝",
            ["Parallax Y f/t"] = "시차 Y 시작/끝",
            ["Color from/to"] = "색상 시작/끝",
            ["Create stack"] = "스택 생성",
            ["Lyric text"] = "가사 텍스트",
            ["Split"] = "분할",
            ["Words"] = "단어별",
            ["Chars"] = "글자별",
            ["Generate"] = "생성",
            ["All parts"] = "전체",
            ["First only"] = "첫 부분만",
            ["As"] = "생성 방식",
            ["Game text"] = "게임 텍스트",
            ["PNG (font)"] = "PNG (폰트)",
            ["Color (hex)"] = "색상 (hex)",
            ["Spacing X/Y"] = "간격 X/Y",
            ["Part stagger (°)"] = "부분별 지연 (°)",
            ["Pivot X"] = "피벗 X",
            ["Pivot Y"] = "피벗 Y",
            ["PNG rendering"] = "PNG 렌더링",
            ["Font file"] = "폰트 파일",
            ["Stroke size"] = "외곽선 크기",
            ["Stroke color"] = "외곽선 색상",
            ["Shadow spread"] = "그림자 퍼짐",
            ["Shadow offset"] = "그림자 오프셋",
            ["Shadow density"] = "그림자 농도",
            ["Shadow color"] = "그림자 색상",
            ["Disappear animation"] = "소멸 애니메이션",
            ["After (beats)"] = "지연 (박자)",
            ["Generate lyrics"] = "가사 생성",
            ["Generated {0} lyric parts"] = "가사 {0}부분 생성됨",
            ["Enter a tag first"] = "먼저 태그를 입력하세요",
            ["Enter the lyric text"] = "가사 텍스트를 입력하세요",
            ["Save the level first"] = "먼저 레벨을 저장하세요",
            ["Font file (.ttf/.otf) not found"] = "폰트 파일(.ttf/.otf)을 찾지 못했습니다",
            ["Lyric contains characters invalid in file names"] = "가사에 파일 이름으로 쓸 수 없는 문자가 있습니다",

            // ── pitch bar ──
            ["Pitch"] = "피치",
            ["Reset"] = "초기화",

            // ── help mode chrome ──
            ["× Exit"] = "× 닫기",
            ["Help"] = "도움말",
            ["Help mode"] = "도움말 모드",
        };

        // Help topics: whole bodies mapped EN→KO (fallback = English). Kept beside the short
        // strings so the tables stay in one reviewable place.
        internal static string Body(string en) => T(en);
    }
}
