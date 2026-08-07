| English | [한국어](FEATURES.ko.md) |

# Features

Sapphire is an editor-suite mod for **A Dance of Fire and Ice** (UnityModManager).
It layers a modern, tool-rich charting/VFX environment on top of the game's built-in editor.

This page is a running summary of what Sapphire does. It will be updated as features land.

---

## Getting started

- **Master switch** — the power button (top-right) gates the entire suite. Off = vanilla editor.
- **Settings** — `Ctrl+E` opens the settings panel (skin accent, toggles, module options).
- **Manual** — the `?` button beside the active tool opens the in-editor instruction manual.

---

## Toolbar tools

An Adobe-style toolbar replaces the event palette. Digit keys
`1`–`0` pick the tools under the current selected category when nothing is selected; each cell has a hover tooltip.

| Tool | What it does |
|---|---|
| **Circular path** | Build arcs / circles / stars as midspin sequences. |
| **Free angle** | Free-rotate a tile by dragging (hold **Left-Alt** or arm the tool); right-click stays free for the tile menu. |
| **Pseudo (동타)** | Turn a tile into a beat-neutral pseudo (single-click or multi-select batch), with midspin/twirl constructions and retune. |
| **Camera path** | On-screen FreeCamera path overlay: click-inspect keyframes, ▶ preview, play-all with beat gaps. |
| **Zip** | Replace a tile with a zip: 360° total split evenly across N keys (min 4) |
| **VFX preview** | VFX only mode — hides all UI (including the game's) to preview visuals; persists into play mode, ESC to exit. |
| **Inspector** | Eyedropper: left-click captures a tile's events, right-click pastes onto tiles. The copy panel also works as a paste filter. |
| **Quick chart (Q)** | Toggle fast in-place charting mode (see below). |
| **Shape library** | Dockable palette of reusable shapes (see below). |

Other QoL tools:

- **Selective copy + mirror** panel (multi-select, top-right) — copy multiple tiles, mirror geometry.
- **Filter browser** — the game's ~300-entry `SetFilterAdvanced` dropdown as a searchable,
  category-organized browser (chip rides the game's event panel).
- **Event presets** — save named bundles of events (via the Inspector) and stamp them back.

---

## Timeline & camera

The bottom-docked **event timeline** puts events on a song-time axis with a playhead, scrub,
zoom, transport controls, and a mode cluster.

- **Transport / mode cluster** — play/scrub, plus a mode dropdown: **Normal / Cam / Deco / Filter**.
- **Camera keyframe timeline (CAM)** — a proper workspace for `MoveCamera`:
  - Property lanes: **Position / Rotation / Zoom**; tween bars from start+duration beats.
  - **Ease picker** — grid of eases with live-plotted curves (right-click a keyframe).
  - **Custom bezier** — two draggable control points; decomposed into Linear segments on apply.
  - **Graph editor** — AE-style value-over-time graph (tabs X/Y/Rotation/Zoom), diamonds you
    can select / value-drag / retime / right-click for eases; musical-beat X axis, own zoom/pan.
  - Keyframe **drag to retime**, inline inspector row, X·Y link toggle, split-on-retime.
- **Deco / Filter modes** — lanes per decoration tag / per active filter, for quick navigation.

---

## Quick chart mode (`Q`)

A fast, tool-less charting mode. While on, the timeline hides and these keys are live
(gated to the editor, not typing):

| Key | Action |
|---|---|
| `I` | Toggle a **twirl** on the selected tile(s). |
| `O` | **Set speed** — prompt (BPM or Multiplier) → SetSpeed event. |
| `[` / `]` | **Halve / double** the selected tile's SetSpeed value. |
| `Shift+P` | **Pause** — prompt beats → Pause event. |
| `Shift+L` | **Tile location** — prompt X/Y → PositionTrack event. |
| `Shift+G` | **Angle pad** — a floating field that appends a whole run of tiles. |

### Angle pad syntax

Space-separated **relative** angles (charter convention: `180` = straight, `90` = quarter turn):

- **Math** per token — `180-30`, `360/8`, `2*45`.
- **Twirl** — a trailing `t` twirls that tile: `30t 30t 180`.
- **Group repeat** — parenthesise and multiply: `(30t 150 180 180 180)*4`. Groups can nest.

Angle pads **are duplicable** (each keeps its own value as a scratch preset) and draggable.

---

## Shape library

A dockable, resizeable palette of reusable **shapes** — each a base angle subdivided into
key-count variants, inserted as a repeating pseudo.

- **Categories** — a collapsible rail: **Built-in** (90° / 120° / 240° / 270°) plus your own
  categories. `+ Category` / `+ Shape`; rename inline; `×` deletes (with a confirmation popup).
- **Variants** — each shape offers key-count variants (e.g. 2-key, 3-key). Per variant:
  - **Editable angles** — each tile's charter is a field; editing one rebalances the others so
    the run still sums to the base angle. `t` marks twirled tiles.
  - **Repeats (n)** — how many units to append (per-variant default).
  - **Preview** — a live path render: rounded ADOFAI-style tiles, red/blue swirl markers
    (blue when a tile's resulting angle ≥ 180°), planet at the start.
  - **Insert** — appends the run onto the selected tile (one undo). With no tile selected it
    does nothing and says so — there is no anchor to build from.
  - **Rotate** — mirrors the shape along its x-axis (the other of the two valid twirl parities).
- **Custom shapes** — `+ Shape` opens a form: name, category, and an **angle expression**
  (same `30t 30t 180` notation), or **From selection** to capture the selected editor tiles'
  angles + twirls. Custom shapes and categories **persist** across restarts.
- **Resizeable** — drag any edge/corner; the rail and preview columns reflow to the new size,
  and to docking.

---

## Level settings & decorations

The level-settings panel is Sapphire-native, rendered from the game's own property registry.

- **Artist permission status** — a colour-coded chip at the top of the Level tab shows the
  artist's approval status; click it to expand the full condition text. Hidden when no artist
  is set.
- **Decoration browser** — list grouped by tag (collapsible folders) or a thumbnail grid.
  `+ Decoration` picks the type (**image / text / object / particle / component**), `Duplicate`
  copies the selection, and `Delete` removes it. Selecting a row opens the **decoration
  inspector** in its own floating, resizable, dockable window (closing it also clears the
  selection) — the browser list itself never reflows.
- **Particle and object decorations are fully editable** — gradient colours (mode: single /
  two colours / gradient / two gradients / random, plus colour-stop and alpha-stop editors),
  float-pair and velocity-range fields, and a Play / Stop / Restart particle preview. Object
  decorations expose their full property set, including the `objectType`-dependent rows.
- **Decoration event target tags** — the event tree shows each decoration event's target tag:
  beside the number on every expanded row, and as a list of the group's distinct tags on a
  collapsed group's header (a tile with seven `MoveDecorations` events no longer renders as
  seven identical rows).

---

## Numeric fields

Every numeric field in Sapphire accepts **arithmetic**, the way the vanilla inspector does:
type `180*2`, `100/3`, `(1+2)*45` or `360/8` and the field evaluates on commit. Also supports
`^`, `%` and the constants `pi` / `tau` / `e`. Unlike vanilla, division is not integer division
— `10/4` is `2.5`, not `2`. Text fields (tags, image paths) are never evaluated.

---

## Updates

Sapphire checks its GitHub releases once per session and shows a toast when a newer build
exists. Clicking it downloads and installs over the current copy; the new version loads after
a game restart. Downloads are checked against the SHA-256 digest GitHub publishes for the
release asset, and archive entries that would escape the mods folder are rejected.

- **Settings → Updates** — installed version, live status, automatic-check and pre-release
  toggles, `Check now`, and a toast preview.
- **Plays nicely with other mods** — the toast parks top-right, the same corner Quartz uses.
  Sapphire detects another mod's update toast there and stacks below it instead of drawing on
  top, so both stay readable.

---

## Skin & chrome

- **Dark reskin** of the game's editor UI (reversible, guard-based) with a Sapphire-blue accent.
- **File chip / menu**, panel rail, event dock — modern chrome that proxies the game's own
  buttons (never reimplements game logic).
- **Layout overrides + on-screen drag editor** for game editor chrome.
- **Floating windows** — level-settings popup and dialogs are blocker-less, draggable, and
  remember their position (some also resize).
- **On-screen key hints** — a card in the bottom-right lists the keys live in the current
  context. It rides above the event timeline, sizes itself to its content, collapses to a
  header, and can be turned off (`Ctrl+E` → Editor).

---

## Playback & misc tweaks

- **Practice pitch** — `scnEditor.playbackSpeed` (the game's native lever; hitsounds follow).
- **Non-destructive pitch overlay**.
- **WASD** camera pan.
- **Autoplay-pause key**, **Editor Mode**, live **tile-angle readout**.

---

## Keybindings reference

| Key | Context | Action |
|---|---|---|
| `Ctrl+E` | anywhere | Open settings |
| `digits` | no tool/selection | Pick toolbar tool |
| `digits` | tile selected | Pick nth event of the dock category; `Enter` stamps |
| `,` / `.` | editor | Previous tool / saved slot (`Shift+.` saves slot) |
| `Q` | editor | Toggle Quick chart mode |
| `I` `O` `[` `]` | Quick chart | Swirl / set speed / halve / double |
| `Shift+P` `Shift+L` `Shift+G` | Quick chart | Pause / location / angle pad |
| `A` `N` | editor | Autoplay pan / no-fail toggle |
| `Esc` | popups | Close the focused Sapphire popup |

---

## For developers

- **Build:** `./deploy.sh` — builds with **xbuild** (Mono; never `dotnet build`) and copies to
  `UMMMods/Sapphire`. Reload in-game with `Ctrl+F10`.
- **Release:** `./release.sh [version]` — tester zip; version bumps are user-driven.
- The `.csproj` uses an explicit `<Compile>` whitelist — every new `.cs` file must be listed.
- Game types are researched from an IL dump (`monodis Assembly-CSharp.dll`), then grepped;
  verify field/method accessibility there before writing code.

See `CLAUDE.md` for architecture and conventions.
