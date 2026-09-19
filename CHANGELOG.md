| English | [한국어](#한국어) |
| --- | --- |

# Changelog

## 1.0.0-a7

### Script (new) — Level settings → Script

- **Runtime logic that plays in the unmodded game.** Variables (`flag`, `state`, `counter`), key handlers (`on key Up:`), `if` / `else`, conditions, `when` clauses and per-tile judgments (`on miss at 40:`) compile into ordinary events. The game's only runtime memory is its key → events table, which a key event can rewrite, so the script becomes a state machine on it.
- A **code editor** with line numbers, colouring and errors as you type; **Compile** writes the events, **Remove** clears them. `run tag` uses events you tagged as templates.

### Level variables (new) — Level settings → Variables

- Named values (`bpm = 180`, `beat = 60/$bpm`) and **`$name` formulas in any number field**. The event stores the result, so the level plays anywhere; the formulas are saved inside the level for editing and survive undo, copy/paste and the tray.

### Event tray (new) — `Tray` in the event panel

- A clipboard you can see: drag rows — or the whole selection, any mix of types — in as **folders**, open them to **edit events in place**, drag a folder or one event onto a tile (**Shift replaces** that tile's events).

### Audio window (new)

- Waveform with the level's beat grid, a **tempo curve** for the whole song, a **metronome** that follows it, an **offset suggestion** and **chart analysis** that sorts every SetSpeed into subdivision, magic shape or real tempo change.

### Event panel

- Click **selects**; the `▸` button with the event's icon **expands**. One event opens expanded; several select the first. **↑ / ↓** move the selection, rows slide open and closed.
- **Copy** confirms with a notification; **Ctrl+V replaces** the selected tiles' events, **Ctrl+Shift+V adds** on top.
- **Tab / Shift+Tab** move between fields; digits typed into a field no longer switch palette tools.

### Quick chart

- **`U` edits the selected tile's event** — one opens directly, several show a numbered list previewing every parameter. Opens centred; Esc keeps the tile selected.
- **`Shift+R` move tile**, **`Shift+O` hold**. Pads are optional (one opens when the mode turns on).
- Angle pad: Enter no longer places the run twice; runs after a mid-track corner start from the right heading.

### Editor

- The **tile right-click menu** works (it never opened), including Copy / Cut with an event filter for a single tile.
- **Sidebar tab rails and tear-off docking**; tabs are easier to see. **Decorations** has its own window.
- **Timeline**: lanes size independently, an audio track, and a smooth height drag.
- **Hover tips** on icon-only buttons. **Animation duration** from instant to slow. **Remove toolbar shortcuts** (on by default) frees the digit keys. Keybinds can include **Alt**.
- **Play mode** locks the path and hides the placement rings.

### Fixes

- The colour wheel shows the picked colour and opens beside its field. PlaySound's sound has its dropdown back.
- The filter manager opens on tiles without a filter event. Presets switch on the first click. The autoplay tip no longer flashes.
- Sidebar resize grips no longer draw over popups. English help showed lookup ids instead of sentences.

---

## 1.0.0-a6

### Play mode (new)

- The playtest mirror of Editor mode (`Ctrl+E` → Features): **autoplay off, no-fail on, timeline hidden, and every Sapphire surface hidden except the pitch overlay**. No-fail is optional — turn it off to feel the misses.
- Autoplay and no-fail are set when you switch the mode on and when a playtest starts, not every frame, so flipping autoplay mid-run still works. Mutually exclusive with Editor mode, which wants autoplay *on*.

### Settings, rebuilt

- **Four tabs** instead of one: **Features** (what Sapphire adds), **Keybinds** (every key on one screen, including the fixed ones), **Updates**, **Misc**.
- **Updates** shows installed vs latest side by side, a real progress bar, an **Install** button, and the release's **notes in the panel**.
- **UI scale** and **accent colour** are finally reachable — both were live settings with no control since the split.

### Smaller download

- **5.9 MB → 1.7 MB.** The old font bundle carried 19 fonts to use one; Sapphire now ships the two Paperlogy weights it actually needs. Upgrading removes the old bundle for you.

### Looks

- A **symbol font** rides every panel font, so arrows, ▲▼, ●○, ⚙ and ✓ render properly instead of borrowing metrics from the game's CJK font.
- **Real padlock icons** on the Hz tool's lock rows.
- The four tool palettes (Magic Shape, Track, Deco, Hz) share **one width, label column and button geometry** — their columns line up when docked side by side.

---

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

## 1.0.0-a7

### 스크립트 (신규) — 레벨 설정 → 스크립트

- **모드 없는 게임에서도 동작하는 실행 중 로직입니다.** 변수(`flag`, `state`, `counter`), 키 처리(`on key Up:`), `if` / `else`, 조건, `when` 절, 타일별 판정(`on miss at 40:`)이 일반 이벤트로 컴파일됩니다. 게임이 실행 중에 기억하는 것은 키 → 이벤트 표뿐이고 키 이벤트가 이 표를 바꿀 수 있으므로, 스크립트는 그 위에서 동작하는 상태 기계가 됩니다.
- 줄 번호, 색 구분, 입력 중 오류 표시를 갖춘 **코드 편집기**를 제공합니다. **컴파일**은 이벤트를 기록하고 **제거**는 지웁니다. `run 태그`는 템플릿으로 태그해 둔 이벤트를 사용합니다.

### 레벨 변수 (신규) — 레벨 설정 → 변수

- 이름 붙은 값(`bpm = 180`, `beat = 60/$bpm`)과 **모든 숫자 입력란의 `$이름` 수식**을 지원합니다. 이벤트에는 계산 결과가 저장되므로 어디서나 플레이할 수 있고, 수식은 편집용으로 레벨 안에 함께 저장되어 되돌리기, 복사/붙여넣기, 트레이에서도 유지됩니다.

### 이벤트 트레이 (신규) — 이벤트 패널의 `트레이`

- 눈에 보이는 클립보드입니다. 행이나 선택 전체(종류가 섞여도 됩니다)를 **폴더**로 끌어다 넣고, 폴더를 열어 **그 자리에서 이벤트를 편집**하며, 폴더나 이벤트 하나를 타일로 끌어다 놓을 수 있습니다(**Shift를 누르면 교체**합니다).

### 오디오 창 (신규)

- 레벨의 박자 격자가 겹쳐진 파형, 곡 전체의 **템포 곡선**, 이를 따라가는 **메트로놈**, **오프셋 제안**, 그리고 모든 SetSpeed를 분할·마법진·실제 템포 변화로 분류하는 **채보 분석**을 제공합니다.

### 이벤트 패널

- 클릭하면 **선택**되고, 이벤트 아이콘이 있는 `▸` 버튼으로 **펼칩니다**. 이벤트가 하나면 펼쳐진 채로, 여러 개면 첫 이벤트가 선택된 채로 열립니다. **↑ / ↓**로 선택을 옮기며, 행은 부드럽게 열리고 닫힙니다.
- **복사**하면 알림으로 알려 주고, **Ctrl+V는 선택한 타일의 이벤트를 교체**하며 **Ctrl+Shift+V는 위에 추가**합니다.
- **Tab / Shift+Tab**으로 입력란을 이동하며, 입력란에 친 숫자가 더 이상 팔레트 도구를 바꾸지 않습니다.

### 빠른 차팅

- **`U`로 선택한 타일의 이벤트를 편집**합니다. 이벤트가 하나면 바로 열리고, 여러 개면 각 속성을 미리 보여 주는 번호 목록이 열립니다. 화면 가운데에 열리며, Esc로 닫아도 타일 선택이 유지됩니다.
- **`Shift+R`로 타일 이동**, **`Shift+O`로 홀드** 이벤트를 만듭니다. 패드는 선택 사항입니다(모드를 켤 때 하나 열립니다).
- 각도 패드: Enter가 더 이상 묶음을 두 번 배치하지 않으며, 트랙 중간 모서리 뒤의 묶음이 올바른 방향에서 시작합니다.

### 에디터

- **타일 우클릭 메뉴**가 동작합니다(한 번도 열리지 않았습니다). 단일 타일에서도 이벤트 필터로 복사 / 잘라내기를 할 수 있습니다.
- **사이드바 탭 레일과 떼어 내기 도킹**을 지원하며, 탭이 더 잘 보입니다. **장식**은 별도 창으로 열립니다.
- **타임라인**: 레인 높이를 따로 조절하고, 오디오 트랙이 생겼으며, 높이 조절이 부드러워졌습니다.
- 아이콘만 있는 버튼에 **호버 도움말**이 생겼습니다. **애니메이션 길이**를 즉시부터 느리게까지 조절합니다. **도구 모음 단축키 제거**(기본값 켜짐)로 숫자 키가 자유로워집니다. 단축키에 **Alt**를 넣을 수 있습니다.
- **플레이 모드**가 경로를 잠그고 배치 링을 숨깁니다.

### 수정

- 색상 휠이 고른 색을 보여 주며 입력란 옆에 열립니다. PlaySound의 소리 드롭다운이 돌아왔습니다.
- 필터 이벤트가 없는 타일에서도 필터 관리자가 열립니다. 프리셋이 한 번의 클릭으로 바뀝니다. 자동 재생 안내가 더 이상 깜빡이지 않습니다.
- 사이드바 크기 조절 손잡이가 더 이상 팝업 위에 그려지지 않습니다. 영어 도움말에 문장 대신 조회 ID가 보이던 문제를 고쳤습니다.

---

## 1.0.0-a6

### 플레이 모드 (신규)

- 에디터 모드의 반대편으로, 채보를 실제로 플레이하기 위한 모드입니다 (`Ctrl+E` → 기능). **자동 재생이 꺼지고, 노페일이 켜지며, 타임라인을 포함한 Sapphire UI가 음정 오버레이만 남기고 모두 숨겨집니다.** 노페일은 옵션이라 끄면 미스를 그대로 느낄 수 있습니다.
- 자동 재생과 노페일은 모드를 켤 때와 플레이를 시작할 때만 적용되므로 도중에 직접 바꾼 값은 유지됩니다. 자동 재생을 *켜는* 에디터 모드와는 서로 배타적입니다.

### 설정 개편

- 탭이 하나에서 **넷**으로 나뉘었습니다. **기능**(Sapphire가 더하는 것), **단축키**(고정 키를 포함한 모든 키를 한 화면에), **업데이트**, **기타**.
- **업데이트** 탭은 설치된 버전과 최신 버전을 나란히 보여 주고, 실제 진행 막대와 **설치** 버튼, 그리고 해당 릴리스의 **변경 사항**을 패널 안에 표시합니다.
- **UI 크기**와 **강조색**을 드디어 조절할 수 있습니다. 둘 다 분리 이후 조작 수단이 없던 설정이었습니다.

### 용량 감소

- **5.9 MB → 1.7 MB.** 기존 폰트 번들은 하나를 쓰려고 19개를 담고 있었습니다. 이제 실제로 필요한 Paperlogy 두 종만 포함하며, 업데이트하면 예전 번들은 자동으로 삭제됩니다.

### 모양

- **기호 폰트**가 모든 패널 폰트에 붙어, 화살표·▲▼·●○·⚙·✓가 게임 CJK 폰트에서 빌려온 어색한 크기 대신 제대로 표시됩니다.
- Hz 도구의 잠금 줄에 **실제 자물쇠 아이콘**이 들어갔습니다.
- 네 개의 도구 팔레트(Magic Shape·트랙·장식·Hz)가 **너비와 라벨 열, 버튼 형태를 공유**하여, 나란히 도킹했을 때 열이 정확히 맞습니다.

---

## 1.0.0-a5

### Hz 도구 (신규) — 툴바 `♪` 또는 `Shift+F`

- **주파수로 차팅합니다.** 음과 도형을 고르면 필요한 BPM(`도형 BPM = 각도 × Hz ÷ 3`)을 계산하고, 그 값을 실제로 만드는 SetSpeed까지 작성합니다.
- **길이는 정확히 맞으며** 음높이보다 우선합니다. 타일 수가 정수라 낼 수 있는 주파수가 띄엄띄엄해지며, 오차는 센트로 표시됩니다.
- **정확한 음높이**는 남는 시간을 정지(Pause)로 채워 길이와 음높이를 동시에 정확하게 만듭니다. **완전히 닫힌 원**까지 켜면 셋 다 정확해집니다.
- **완전한 원**은 정N각형을 만듭니다. **바퀴 수**로 다시 그리고, **원 간격 벌리기**로 길 위치 오프셋만큼 떨어뜨리며, **추천**이 지정한 길이에 가장 정확한 변 수·바퀴 수를 찾아 줍니다.
- **음 건반**과 함께 **EDO·기준 음높이**를 바꿀 수 있습니다 (기본값 12-EDO, A4 = 440).

### 각도 패드

- 빠른 차팅 중에는 항상 하나가 우측 상단에 떠 있으며, **반복 횟수**·**지우기**·전용 도움말이 있습니다.
- **소용돌이 버튼**은 첫 타일의 소용돌이를 *첫 회차에만* 반전하며, 입력한 표현식은 건드리지 않습니다. 그래서 반복되는 도형이 그대로 닫힙니다.
- **도형 라이브러리에 추가**로 묶음을 *반복 없는 도형*에 저장합니다.
- 묶음의 **미리보기 타일**이 표시됩니다. Hz 도구도 동일합니다.

### 차팅 관련 수정

- **각도가 뒤집혀 들어가던 문제** — 기준 타일의 회전 방향 부호를 잘못 읽어 `30` 탭이 `330` 박자로 들어갔습니다. 각도 패드의 결과가 이번에 바뀝니다.
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
