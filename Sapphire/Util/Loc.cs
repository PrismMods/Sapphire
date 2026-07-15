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

            // ── batch dialog ──
            ["Pseudo interval"] = "동타 간격",
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
