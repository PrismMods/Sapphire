| English | [한국어](#한국어) |
| --- | --- |

# Changelog

## 1.0.0-a5

### Hz tool (new) — toolbar `♪` or `Shift+F`

- **Chart by frequency.** Pick a note and a shape; the tool solves the BPM the run needs (`shape BPM = angle × Hz ÷ 3`) and writes the SetSpeed that makes it real.
- **Duration is exact** and takes priority over pitch — the tile count is a whole number, so reachable frequencies are quantised and the error is shown in cents.
- **Exact pitch** pads the remainder with a Pause, so duration *and* pitch can both be exact; with **Perfect circles**, so can a closed loop.
- **Full circle** builds a regular N-gon. **Laps** re-trace it, **Separate circles** spaces them with PositionTrack offsets you set, and **Suggest** picks the most accurate sides/laps for your duration.
- **Note picker** with configurable **EDO and tuning reference** (12-EDO, A4 = 440 by default).

### Angle pad

- Always one open in quick chart, top-right, with a **repeat count**, **clear**, and its own help page.
- The **swirl button** inverts the first tile's twirl for the *first repetition only*, without touching what you typed — so a repeated shape still closes.
- **Add to Shape Library** saves the run under *Non-repeating shapes*.
- **Ghost tile preview** of the run, here and in the Hz tool.

### Charting fixes

- **Angles came out inverted** — a `30` tap landed as a `330` beat, because the anchor's spin was read with the wrong sign. This changes angle-pad output.
- Shapes placed on a **twirled tile now mirror**, keeping their charters and beat.
- **Twirls are never stacked**: two on one tile cancel each other, so placement toggles instead.
- New shapes default to **one repeat**.

### Elsewhere

- **Rebindable keybinds** for quick chart, the tool slots and the Hz tool (settings ▸ Editor ▸ Keybinds).
- **Panel layout** can survive a restart (off by default).
- Scrolling over a Sapphire window no longer zooms the editor.
- Error text is readable again, and long runs place faster.

---

## 1.0.0-a4

### Decorations

- **Decoration inspector is its own window.** It used to render inline under the browser list, which reflowed the list on every selection and buried a particle's 35+ properties entirely.
- **Particles and objects are fully editable.** New rows for float pairs, Vector2 ranges, min/max gradients (mode, colours, stop lists) and the particle playback transport.
- Typed property values are no longer written back as strings — that was destroying particle values on any edit.
- Properties the game marks hidden are now shown. It hides them because *it* edits them in dedicated panels; Sapphire hides those panels, so 18 rows across 6 event types had no UI anywhere.
- Switching gradient mode backfills the empty slot, so the level still loads afterwards.
- The **browse** button now appears on every file property, including a particle's image path.
- Decoration target tags are listed in the event tree — collapsed and per-row — so they're readable without opening two dropdowns.
- Duplicate re-syncs the browser and inspector to the copy.

### Level settings

- **Artist approval status.** The verified-artist list is fetched on opening the Level tab; the approval chip shows the current artist's status with the game's own strings.
- **Artist autocomplete.** Typing in the artist field lists matching verified artists with their approval badge — the game's dropdown, on Sapphire's field.

### Events and timeline

- Bulk edit across a selection, with confirmation dialogs on destructive actions.
- Event groups can be deleted, and groups are visually distinct from single events.
- **Drag the timeline away.** Pull the height grip past its floor to fold the strip; pull back up to restore it.
- The tile-angle readout sums the angles of a multi-tile selection.
- The filter manager opens on any tile, including one with no filter event yet.
- The timeline's event tooltip no longer prints on top of a window you're working in.

### Windows and layout

- **Event presets are a real window** — dockable, resizable, scrolling — with a *Pin event presets* setting that keeps them up when the Inspector tool is off.
- **Vertical resize on the Magic Shape, Track and Deco palettes**, with a scrolling body so a dragged height sticks.
- Width now relayouts live while you drag, instead of catching up on release.
- Docked panels leave room for the timeline chips above the strip.
- The pitch bar keeps the screen edge; the left dock ends above it instead of covering it.
- **Key hints are a plain text overlay** — no window, no plate, nothing clickable. Context-aware, bottom-right, above everything except the difficulty dropdown.

### Toolbar

- **Icon set redrawn** on one stroke weight: a plain circle for circular path, an angle with its measure arc, an accordion for zip, a pentagon for the shape library, a magic circle for magic shape, a jointed path for track tools, a proper almond eye for the VFX toggle, and a lightning bolt for quick chart.

### Elsewhere

- Settings panel reorganised into feature cards and subpages, and fully localised.
- The game's own notification bar (audio device, calibration, cloud) routes through the editor splash instead of sliding over the toolbar.
- Removed: editor UI repositioning, and the update toast's preview.

---

<a name="한국어"></a>

| [English](#changelog) | 한국어 |
| --- | --- |

# 변경 사항

## 1.0.0-a5

### Hz 도구 (신규) — 툴바 `♪` 또는 `Shift+F`

- **주파수로 차팅합니다.** 음과 도형을 고르면 필요한 BPM(`도형 BPM = 각도 × Hz ÷ 3`)을 계산하고, 그 값을 실제로 만드는 SetSpeed까지 작성합니다.
- **길이는 정확히 맞으며** 음높이보다 우선합니다. 타일 수가 정수라 낼 수 있는 주파수가 띄엄띄엄해지며, 오차는 센트로 표시됩니다.
- **정확한 음높이**는 남는 시간을 정지(Pause)로 채워 길이와 음높이를 동시에 정확하게 만듭니다. **완전히 닫힌 원**까지 켜면 셋 다 정확해집니다.
- **완전한 원**은 정N각형을 만듭니다. **바퀴 수**로 다시 그리고, **원 간격 벌리기**로 길 위치 오프셋만큼 떨어뜨리며, **추천**이 지정한 길이에 가장 정확한 변 수·바퀴 수를 찾아 줍니다.
- **음 건반**과 함께 **EDO·기준 음높이**를 바꿀 수 있습니다 (기본값 12-EDO, A4 = 440).

### 각 패드

- 빠른 차팅 중에는 항상 하나가 우측 상단에 떠 있으며, **반복 횟수**·**지우기**·전용 도움말이 있습니다.
- **소용돌이 버튼**은 첫 타일의 소용돌이를 *첫 회차에만* 반전하며, 입력한 표현식은 건드리지 않습니다. 그래서 반복되는 도형이 그대로 닫힙니다.
- **도형 라이브러리에 추가**로 묶음을 *반복 없는 도형*에 저장합니다.
- 묶음의 **미리보기 타일**이 표시됩니다. Hz 도구도 동일합니다.

### 차팅 관련 수정

- **각도가 뒤집혀 들어가던 문제** — 기준 타일의 회전 방향 부호를 잘못 읽어 `30` 탭이 `330` 박자로 들어갔습니다. 각 패드의 결과가 이번에 바뀝니다.
- **소용돌이가 걸린 타일**에 도형을 놓으면 이제 상하로 반전되어, 각도와 박자가 유지됩니다.
- **소용돌이를 겹쳐 쌓지 않습니다.** 한 타일에 두 개면 서로 상쇄되므로 이제 토글합니다.
- 새로 만든 도형의 기본 반복은 **1**입니다.

### 그 밖에

- 빠른 차팅·도구 슬롯·Hz 도구의 **단축키를 다시 지정**할 수 있습니다 (설정 ▸ 에디터 ▸ 단축키).
- **패널 배치**를 재시작 후에도 유지할 수 있습니다 (기본 꺼짐).
- Sapphire 창 위에서 스크롤해도 에디터가 확대되지 않습니다.
- 오류 문구가 잘 보이고, 긴 묶음 배치가 빨라졌습니다.

---

## 1.0.0-a4

### 장식

- **장식 인스펙터가 독립 창이 되었습니다.** 이전에는 목록 아래에 인라인으로 그려져 선택할 때마다 목록이 밀렸고, 입자의 35개가 넘는 속성은 아예 묻혔습니다.
- **입자와 오브젝트를 전부 편집할 수 있습니다.** 실수 쌍, Vector2 범위, 최소/최대 그라디언트(모드·색상·색상 지점 목록), 입자 재생 조작 행이 새로 추가되었습니다.
- 타입이 있는 속성값을 문자열로 되돌려 쓰지 않습니다. 이전에는 편집할 때마다 입자 값이 망가졌습니다.
- 게임이 숨김으로 표시한 속성도 표시됩니다. 게임은 전용 패널에서 편집하기 때문에 숨기지만 Sapphire는 그 패널을 가리므로, 6개 이벤트 종류에 걸친 18개 행에 UI가 전혀 없었습니다.
- 그라디언트 모드를 바꿀 때 빈 슬롯을 채우므로 레벨이 정상적으로 열립니다.
- **찾아보기** 버튼이 모든 파일 속성에 표시됩니다. 입자의 이미지 경로도 포함됩니다.
- 장식 대상 태그를 이벤트 트리에 표시합니다. 접었을 때와 각 행 모두에 나오므로 드롭다운을 두 번 열지 않아도 됩니다.
- 복제하면 브라우저와 인스펙터의 선택이 사본으로 옮겨갑니다.

### 레벨 설정

- **작곡가 허가 상태.** 레벨 탭을 열 때 인증된 작곡가 목록을 불러오고, 게임의 문구 그대로 현재 작곡가의 상태를 칩으로 보여줍니다.
- **작곡가 자동 완성.** 작곡가 칸에 입력하면 조건에 맞는 인증 작곡가가 허가 배지와 함께 나열됩니다. 게임의 드롭다운을 Sapphire의 입력 칸에 옮겨 온 것입니다.

### 이벤트와 타임라인

- 선택 영역을 한 번에 편집할 수 있고, 되돌릴 수 없는 동작에는 확인 창이 뜹니다.
- 이벤트 그룹을 삭제할 수 있고, 그룹과 단일 이벤트를 눈으로 구분할 수 있습니다.
- **타임라인을 드래그해 숨깁니다.** 높이 손잡이를 최소 높이 아래로 더 끌면 접히고, 다시 위로 끌면 돌아옵니다.
- 여러 타일을 선택하면 타일 각도 표시에 각도의 합이 함께 나옵니다.
- 필터 이벤트가 없는 타일에서도 필터 관리자가 열립니다.
- 창 위에 마우스가 있을 때는 타임라인의 이벤트 툴팁이 그려지지 않습니다.

### 창과 배치

- **이벤트 프리셋이 정식 창이 되었습니다.** 도킹·크기 조절·스크롤을 지원하며, *이벤트 프리셋 표시 고정* 설정을 켜면 인스펙터 도구를 꺼도 계속 떠 있습니다.
- **도형·트랙·장식 도구 창의 세로 크기 조절.** 내용이 스크롤되므로 조절한 높이가 그대로 유지됩니다.
- 크기를 조절하는 동안 내용의 가로 배치가 실시간으로 따라옵니다. 이전에는 손을 뗀 뒤에야 반영되었습니다.
- 도킹된 패널이 타임라인 위쪽 칩을 가리지 않습니다.
- 피치 바가 화면 왼쪽 끝에 붙고, 왼쪽 도크가 그 위에서 끝납니다.
- **단축키 도움말이 단순한 텍스트 오버레이가 되었습니다.** 창도 배경도 없고 클릭도 가로채지 않습니다. 상황에 맞춰 오른쪽 아래에 표시되며, 난이도 드롭다운을 제외한 모든 요소 위에 그려집니다.

### 툴바

- **아이콘을 같은 굵기로 다시 그렸습니다.** 원형 경로는 단순한 원, 자유 각도는 각도 호를 더한 기호, 드르륵은 촘촘한 톱니, 도형 라이브러리는 오각형, 매직 셰이프는 마법진, 트랙 도구는 꺾인 경로, VFX 전환은 제대로 된 눈 모양, 빠른 차팅은 번개입니다.

### 그 밖에

- 설정 패널을 기능 카드와 하위 페이지로 정리하고 전부 번역했습니다.
- 게임 자체 알림 바(오디오 장치·보정·클라우드)가 툴바 위를 지나가지 않고 에디터 알림으로 표시됩니다.
- 삭제: 에디터 UI 위치 조정 기능, 업데이트 토스트 미리보기.
