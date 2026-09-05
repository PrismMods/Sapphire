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
| **Hz tool (♪)** | Chart by frequency, with a note picker (see below). |

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
(gated to the editor, not typing). **Every key below is rebindable** — settings ▸ Editor ▸
Keybinds, or the Quick chart page:

| Key | Action |
|---|---|
| `I` | Toggle a **twirl** on the selected tile(s). |
| `O` | **Set speed** — prompt (BPM or Multiplier) → SetSpeed event. |
| `[` / `]` | **Halve / double** the selected tile's SetSpeed value. |
| `Shift+P` | **Pause** — prompt beats → Pause event. |
| `Shift+L` | **Tile location** — prompt X/Y → PositionTrack event. |
| `Shift+G` | **Angle pad** — a floating field that appends a whole run of tiles. |
| `Shift+K` | Toggle quick chart mode itself (works while the mode is off). |
| `Shift+F` | **Hz tool** — charting by frequency (see below); works with the mode off too. |

### Angle pad syntax

Space-separated **relative** angles (charter convention: `180` = straight, `90` = quarter turn):

- **Math** per token — `180-30`, `360/8`, `2*45`.
- **Twirl** — a trailing `t` twirls that tile: `30t 30t 180`.
- **Group repeat** — parenthesise and multiply: `(30t 150 180 180 180)*4`. Groups can nest.

Angle pads **are duplicable** (each keeps its own value as a scratch preset) and draggable.
One pad is always on screen while the mode is on, spawning in the **top-right corner** (clear of
the toolbar's drop-down submenus and of the key hints below); `×` on the last pad clears it
instead of closing it.

**Add to Shape Library** — the grey button beside Place saves the pad's expression into the shape
library's **Non-repeating shapes** category with a repeat of 1, since a pad expression already
spells out the whole run (`(…)*n` groups are flattened before saving).

---

## Hz tool (♪ toolbar cell · `Shift+F`)

Chart by **frequency** instead of by angle. A tile of charter A° lasts `A/180` beats, so at B BPM:

```
H = 180·B / A     hits per minute        A = 180·B / H = 3·B / F
F = H / 60        hits per second (Hz)   H = F · 60
```

A 20 Hz buzz at 175 BPM is `3 × 175 / 20` = **26.25°** per tile.

There are **two BPMs**, and the difference is the whole tool:

- **Shape BPM** is the tempo the run has to play at, and it's an **output**: pick the note and
  the angle the shape needs, and `shape BPM = angle × Hz ÷ 3` follows. You don't know it in
  advance — the tool tells you. A 20 Hz buzz on a 16-sided circle needs 1050 BPM.
- **Base BPM** is what the chart is already at (level BPM × the anchor tile's speed), detected
  from the selection. Its **padlock pins it** so it stops mirroring the selection (typing a value
  pins it too — a base BPM the next click silently wiped would be a trap). It never feeds the pitch maths; it does two jobs — it's the denominator of
  the **SetSpeed** shown beneath it, and the clock the run's length is quoted in
  (`beats = tiles × base BPM ÷ (60 × Hz)`, i.e. tiles/Hz seconds in section beats).
- **Hz / angle / shape BPM are bound by one equation**, so two of the three are free. Each row
  has a **padlock**: the locked value is held and, with nothing locked, the shape BPM absorbs
  every edit. Lock the shape BPM (for "this is all the speed I have") and the angle moves
  instead. Hits-per-minute is Hz × 60 and tracks either.
- **Exact pitch (pad with a pause)** makes duration *and* pitch exact at once. They only
  conflict because the tile count is a whole number, so a **Pause** event absorbs the remainder:
  the run is deliberately floored short, plays at the target frequency to the cent, then holds
  for what's left of the duration you asked for. In circle mode you get all three — closed loop,
  exact pitch, exact duration. At 175 BPM over 4 beats, a 16-sided circle at 20 Hz is one closed
  lap of 16 tiles running 2.333 beats, plus a 1.667-beat pause. The pause rides the run's last
  tile, with the restoring SetSpeed forced onto that same tile so the hold is counted in section
  beats rather than the run's fast ones.
- **Duration (beats)** is exact, and takes priority over pitch. A run of N tiles at F Hz lasts
  N/F seconds whatever the geometry, so filling exactly the beats you asked for means
  `actual Hz = tiles × base BPM ÷ (60 × beats)` — and since the tile count is a whole number,
  only a *ladder* of frequencies is reachable for a given duration. **Actual Hz** shows the one
  you'll get, with the error in **cents** beside it (highlighted past ±5; a semitone is 100).
  Duration is never nudged to make the pitch come out.
- **Full circle** — close the run into a loop. A tile of charter A turns the heading by
  `180 − A`, so N of them close exactly when `N × (180 − A) = 360`: the run is a regular N-gon
  and the angle is pinned at `180 − 360/N`. Drive it from **Sides** and the tool reports the
  BPM (or Hz, per the padlock) that circle costs — a smooth many-sided circle needs tiles that
  each turn very little, which at buzz frequencies means a very fast section, which is exactly
  what the SetSpeed below can write. **Laps** re-traces the same polygon: 10 laps of a 16-gon is
  160 tiles over ten circles, *not* one 160-sided circle (that would be a different, much wider
  angle).
- **Perfect circles** (on by default) is what forces that closure. With it on the tile count must
  be a whole number of laps, which is a coarse ladder and makes the pitch pay — the error is
  shown in cents so you can see the cost. Switch it **off** and the run keeps the same curvature
  but may stop partway round: the tile count is free to be the one nearest ideal, so the pitch is
  as accurate as an integer count allows and the shape ends as an arc. At 175 BPM over 4 beats, a
  16-sided circle at 1 lap is 16 tiles and nearly an octave flat; the same curvature stopped at
  27 tiles (1.69 laps) is −27 cents. **Duration is exact either way** — this option only chooses
  which of the other two gives.
- **Suggest sides & laps** (closed loops only — with the constraint off the count is already
  pitch-optimal) finds the most accurate pair for the duration you set: the
  pitch error depends only on the *product* sides × laps, so it takes the product nearest the
  ideal tile count — the best any closed loop can do for that duration — then splits it into the
  factor pair whose side count is closest to the circle you asked for. A longer duration buys
  accuracy: at 175 BPM a 20 Hz circle is 7×1 and +36 cents over one beat, 9×3 and −27 cents over
  four. The duration is exact in both.
- **Write SetSpeed for this BPM** — the angle was solved for your base BPM, so if the anchor
  isn't already playing at it the run is the wrong frequency however right the geometry is.
  On by default. A floor's speed governs the traversal *out* of it, so the multiplier
  (shape ÷ base) rides the **anchor tile** — the one you had selected — and the restore rides the
  run's **last tile**, which covers the exit while all of the run stays fast. At the end of the
  track there's nothing after, so nothing is restored. No event is written when the two BPMs
  already match. The status line after Place says which happened.
- **Note keyboard** — two octaves of piano keys (`−`/`+` shift octave). Clicking a key sets the
  target frequency to that note's pitch, for charting a melody as a buzz. It is a note picker,
  not MIDI hardware input.
- **Tuning submenu** (`Tuning +`) — **EDO** (equal divisions of the octave, 1–72) plus the
  reference pitch that pins the grid: `f = ref · 2^((oct − refOct) + (step − refStep)/EDO)`.
  Defaults are 12-EDO with step 9 of octave 4 at 440 Hz, i.e. ordinary concert tuning, so the
  keyboard is a normal piano until you change something. Any other EDO swaps the piano for a
  strip of equal degrees (octave landmark tinted) and labels notes in `steps\edo` notation —
  letter names would be a lie outside 12. Wide divisions show one octave instead of two.
- **Place** appends the run onto the selected tile (one undo), following the path's own turn
  direction and any twirl already in effect; **To angle pad** hands it over as `(26.25)*16` so
  you can add twirls or a tail before committing.

---

## Shape library

A dockable, resizeable palette of reusable **shapes** — each a base angle subdivided into
key-count variants, inserted as a repeating pseudo.

- **Categories** — a collapsible rail: **Built-in** (90° / 120° / 240° / 270°) plus your own
  categories. `+ Category` / `+ Shape`; rename inline; `×` deletes (with a confirmation popup).
- **Variants** — each shape offers key-count variants (e.g. 2-key, 3-key). Per variant:
  - **Editable angles** — each tile's charter is a field; editing one rebalances the others so
    the run still sums to the base angle. `t` marks twirled tiles.
  - **Repeats (n)** — how many units to append. Built-ins keep their curated default (the
    count that closes the star); **anything you make defaults to 1** — placed exactly as written.
  - **Preview** — a live path render: rounded ADOFAI-style tiles, red/blue swirl markers
    (blue when a tile's resulting angle ≥ 180°), planet at the start.
  - **Insert** — appends the run onto the selected tile (one undo). With no tile selected it
    does nothing and says so — there is no anchor to build from.
  - **Rotate** — mirrors the shape along its x-axis (the other of the two valid twirl parities).
- **Twirled anchors mirror automatically** — insert onto a tile whose spin is counter-clockwise
  (it or an earlier tile carries a twirl) and the shape lands **vertically mirrored**, with the
  same charters and the same twirls as on a fresh track. Keeping the original orientation there
  would read every angle back as `360 − angle`, so a 30° grace would become a 330° beat.
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
- **Floating windows** — level-settings popup and dialogs are blocker-less, draggable, and
  remember their position (some also resize).
- **Panel layout memory** — open/closed state and position stick for the whole session. A
  relaunch starts clean by default; `Ctrl+E` → Editor → **Keep panel layout after restart**
  turns that reset off (covers the shape library, level settings, Magic Shape, Track tools,
  Deco tools and the Hz tool).
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
| `Shift+K` | editor | Toggle Quick chart mode (the toolbar `Q` cell does the same) |
| `Shift+F` | editor | Toggle the Hz tool |
| `I` `O` `[` `]` | Quick chart | Swirl / set speed / halve / double |
| `Shift+P` `Shift+L` `Shift+G` | Quick chart | Pause / location / angle pad |

The quick-chart and tool-slot rows above are **defaults**; rebind them in settings ▸ Editor ▸
Keybinds. Defaults avoid the editor's 17 tile-placement letters (`a b c d e h j m n q s t v w
x y z`), which the game binds with **and** without `Shift` — so a bind on one of those steals
tile placement.
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
