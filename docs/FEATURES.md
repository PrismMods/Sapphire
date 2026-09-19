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
(gated to the editor, not typing). **Every key below is rebindable** — settings ▸ Keybinds:

| Key | Action |
|---|---|
| `I` | Toggle a **twirl** on the selected tile(s). |
| `O` | **Set speed** — prompt (BPM or Multiplier) → SetSpeed event. |
| `[` / `]` | **Halve / double** the selected tile's SetSpeed value. |
| `Shift+P` | **Pause** — prompt beats → Pause event. |
| `Shift+L` | **Tile location** — prompt X/Y → PositionTrack event. |
| `Shift+R` | **Move tile** — prompt beats + X/Y → MoveTrack event on the selected tile. |
| `Shift+O` | **Hold** — prompt duration → Hold event. |
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

**Swirl button — invert the first tile's twirl, first repetition only.** It does *not* edit what
you typed. A twirl in the expression is part of the **shape**: `30t 30t 120t` is a unit whose three
twirls make it close, so rewriting it to `30 30t 120t` would change every repetition and the run
would stop being that shape. What the toggle says is narrower — the path already enters turning the
right way, so the leading twirl is redundant *this once*. That's a property of the entry, not of the
pattern, so it can only apply to the first pass. Lights up while it's on.

**Ghost preview** — the run the pad would place is drawn ahead of the selected tile at half
alpha, updating as you type. The **Hz tool** shows the same preview for its own run; when its
panel is open it takes over the ghosts, since two panels competing for them every frame would
just thrash. It walks the same spin rules the builder does, so it can't disagree
with what Place produces. Ghosts are chained only to each other rather than to the surrounding
real tiles (MSM's fake-floor preview edits its neighbours; that would leave the live track drawn
wrong for as long as a pad is open), so the seam at the anchor is a hair off. Capped at 200 tiles.

**`× n` repeat count** — lays the expression down *n* times. Hover it for **▲▼ nudge arrows**. Deliberately separate from the
`(…)*n` group syntax: baking the repetition into the text would make the `t` button flip the first
tile of *every* copy, whereas as a count the expression stays one **unit**, so flipping its leading
twirl costs only the first pass — which is the point when the path already enters turning the way
you want. The spin carries across repetitions, so the rest continue correctly from there.

The header also carries a **clear** button (bin icon — `×` already means close on that row) and
**`?`** for the pad's help page.

**Add to Shape Library** — the grey button beside Place saves the pad's expression (repeats
included) into the shape
library's **From Angle Pad** category with a repeat of 1, since a pad expression already
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
- **Note keyboard** — piano keys (`−`/`+` shift octave), as many octaves as the panel is wide enough for: widening the window adds whole octaves rather than padding empty space beside a fixed keyboard, and keys are never drawn narrower than they can be hit. Clicking a key sets the
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
- **Twirls are never stacked.** The game toggles a spin flag per event, so two Twirls on one
  floor cancel each other while still drawing a swirl marker — and the run's first twirl lands on
  the anchor tile, exactly where you are most likely to have put one. Every Sapphire build now
  *toggles* rather than appends: it removes the existing event instead of adding a second, which
  gives the same spin and leaves valid data.
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
It lives in the left sidebar as a tab next to the event palette — the tab strip above the
sidebar switches between them, and clicking the open tab collapses the sidebar to a vertical
edge rail carrying the tab names. Drop a window **on the rail** to add it as a tab (shape
library, Magic Shape, track tools, deco tools, Hz tool) — the rail lights up as you hover it —
and drag the tab itself off the rail to turn it back into a floating window (release it back
over the rail to change your mind). Dropping on a sidebar *body* still splits it the way docking
always did — but an **empty** sidebar has no body to split, so a drop there joins its rail. The
right sidebar works the same way; it just starts with no tabs on it.

- **Song waveform** — the timeline gives the song its own **AUDIO** track along the foot of the
  strip, aligned to the same time axis as the event lanes. It is built once per song and panned
  by UV, so zooming and scrubbing cost nothing; the level offset shifts it, so it lines up with
  what you actually hear. The **♪ AUDIO** button in the strip's right-hand column toggles the
  track (the Features tab holds the same switch); it dims while the song is still being read,
  and the track reserves no height when the song cannot be read at all.
- **Audio window** — the `Audio` button beside the song offset opens it. Nothing is analysed
  until you press **Analyse**: a full decode and tempo pass on every level open is felt as the
  editor being slow to open, and most sessions never need one. Until then the window is just that
  button; afterwards it becomes `Re-analyse` and the rest appears. **Advanced** exposes the
  analysis parameters — window, step, smoothness λ and tempo bins — for a song that defeats the
  defaults. The waveform: drag the
  scrub anywhere on it, read the position in milliseconds, and press ▶ to audition from there.
  Under the waveform is a **tempo curve** for the whole song, on the same time axis and the same
  zoom, so acceleration, deceleration and base-BPM shifts are visible where they happen and the
  readout shows the tempo under the scrub. Every tempo is scored in every window and the line is then
  decoded as a **path** — the sequence maximising total score minus a cost that is *linear* in how
  far the tempo moves. Linear matters: a quadratic cost charges L·d² for one jump but only L·d²/N
  for N small steps summing to the same distance, so it rewards smearing every change into a slow
  ramp, and a song going 250→270 read as a wander through 256, 260 and 264. A linear cost charges
  the same total however a change is split, so the audio decides whether it is a step or a ramp.
  Neither is each window asked separately. Asking separately, even with a bias toward the
  previous answer, drifts: each step is only marginally better than staying put, nothing pulls
  the line back, and a song holding one tempo for three minutes wandered 14% off it. A global
  anchor then folds the result into one octave, since autocorrelation cannot tell a beat from a
  half-beat. PP BREAKER, which steps 250→270, resolves into plateaus at 250.0
  and 269.2; 初音ミクの激唱, which holds one tempo throughout, reads flat to within 0.9%; Second Revolution, charted at 240, reads flat at
  240.0 within half a percent across three minutes; Megantus — whose single
  global estimate was hopeless — resolves into a real curve dipping to 215 and peaking at 268
  before settling at 242, which is why one number could never describe it. On the two hardest
  maps to hand it holds up too: *Parallel Universe Shifter* traces a continuous journey from 95
  up to 131 and back down through 85 to 70, and *TremENDouS* opens at twice its charted base and
  climbs 33%, matching the accelerandi its own speed events spell out. The line is **shaded by
  confidence**: a tempo that is moving cannot pile onto one phase inside the analysis window, so
  dim means "moving or unclear here", not "wrong". The lane's vertical scale is fixed at a
  minimum of ±12% around the detected tempo, with a guide line at it — a steady song reads as a
  steady line rather than having its last half-percent stretched across the whole lane — and the
  readout gives the actual spread as a percentage.

  The window also reports a single global **tempo** — autocorrelation of the onset flux for the
  period, then a fine sweep that maximises how tightly the onsets fold onto one beat, which is
  what makes it precise enough for a chart. It never writes on its own: the buttons apply it, and only while the
  readout says *confident* or *steady*. Concentration alone was too strict a gate — it measures
  how peaked the onset histogram is, a property of the music's texture rather than of whether the
  tempo is right, so a song holding 250 BPM end to end was read as 249.90 and still called
  unsure. A reading also counts when it is **corroborated**: the global estimate and the tempo
  curve are different routes through the same envelope, so a curve that stays flat and lands on
  the tempo the global sweep found is two independent methods agreeing. Measured on 49 songs from one library and 157 from
  a second, held-out one, that gate fires for about a quarter of songs and lands within 0.1% of
  the charter's own BPM about three quarters of the time, within 0.5% nine times in ten; ungated
  it would be right barely a third of the time. Once the curve confirms the tempo is steady, the estimate is
  re-swept against **every onset in the song** rather than the first 30 s: a tempo 0.05% off has
  drifted only a sixteenth of a beat in 30 s and still folds tightly, but half a beat across four
  minutes, which pulls the two apart. It is then snapped to a round number when it is within
  0.04% of one — charters write round BPMs (83% of 89 distinct level BPMs are integers, another
  10% end in .5), so 249.96 becomes 250. The genuinely odd ones are left alone, and they are
  deliberate rather than sloppy: they cluster in old levels where a nudged BPM was how you filled
  a pause or absorbed an irregular intro before Pause events existed, and the nearest of them sits
  0.070% from an integer. Autocorrelation cannot tell a beat from a
  half-beat, so the multiple is a `÷4 ÷2 Use ×2 ×4` ladder plus `×1.5 ÷1.5` rather than a guess — powers of two
  are what charters actually use (78.5% of 31,220 speed multipliers across 429 published charts
  are exact powers of two, and every confident hit in the held-out set was one). Most of the rest
  are magic-shape compensation — ×3, ×1.5, ×4⁄3, ×6⁄5 — which hold the input tempo steady while
  the angles change, so they are not tempo changes either. A ratio that is neither is the signal
  that the base BPM really moved.
  `Click` turns on a **metronome** on the level's own grid — its BPM and offset, not the detected
  ones — so playing it over the song is the direct test of whether those two values are right:
  if the clicks sit on the music, they are. Bar downbeats are accented. The grid **follows the
  detected tempo** rather than repeating one interval — each beat's length is read from the tempo
  curve where that beat falls, and the grid is integrated forward from the offset rather than
  multiplied out from it, so a song that accelerates does not leave the click behind within a few
  bars. The curve is folded into the level's own octave first, so the click density stays
  comparable to the grid being verified even when the detection sits at half or double it. The clicks are
  *scheduled* against the audio clock rather than fired from the frame loop, because a frame
  boundary is up to 16 ms from where the beat actually falls, which is the same order as the
  offset error you would be listening for.
  Scroll to zoom about the cursor (down to 50 ms across the window), shift-scroll or drag with
  the right or middle button to pan, and `− / + / Fit` do the same from the button row; playback
  pulls the view along when it leaves the window, and dragging the scrub past an edge pans rather
  than stopping, so a zoomed-in scrub can still reach the rest of the song. `Music` and `Click`
  sliders set the two levels independently — judging whether a click sits on a transient is a
  balance problem before it is a timing one.
  Playback runs on Sapphire's own audio source, never the conductor's, so scrubbing cannot
  desync the editor's clock. The waveform carries the **beat grid** drawn from the level's own BPM and
  offset (bar lines brighter than beats), which is the quickest way to judge an offset: if the
  lines sit on the transients, it is right. `Suggest offset` lives here too. On a level with no
  offset yet it finds the song's first onset (marked on the waveform) and writes it; on one that
  already has an offset it gives a **correction** from two estimators at once: the nearest attack
  and the beat grid *measured from the audio* (every onset in the first 30 s votes on where the
  grid sits). Whether they agree is the tell — within 25 ms of each other means the attack was
  the right one and its precision is taken, otherwise the grid is the safer answer. Measured on
  115 levels and again on a held-out 317, the pair beats either alone on both, landing within
  30 ms 88% and 76% of the time —
  so a chart that starts three minutes into a long song is never dragged back to the song's
  opening sound. Detection is log-domain energy flux against a local median rather than an
  absolute level, which is what lets one rule serve both a song that starts at full tilt and one
  that opens on ambience. Measured against 115 published levels' own offsets: median error 9 ms,
  68% within 30 ms, and it always answers.
- **Right-click on a multi-selection** — right-clicking *inside* a selection of two or more
  tiles opens Sapphire's own menu (copy, cut, delete (safe), add twirls, repeat, duplicate, move)
  and leaves the selection intact; clicking outside it re-selects and gives the single-tile menu
  as before. The multi-selection verbs are not wired up yet and say so when clicked.
- **Timeline heights** — drag the strip's top edge to scale the event lanes (now up to 400 units
  rather than a fixed ceiling), and drag the **audio track's** own top edge to size just that
  track. They are separate grips because they hold different kinds of content and one control for
  both means neither can be set.
- **Panel animations** — windows, dropdowns, both context menus and the timeline strip fade and ease open over
  0.11 s by default (`Animation duration (s)`, 0–0.6 s, in `Ctrl+E` → Misc) instead of snapping in, with the arrival eased out and the exit eased in. `Ctrl+E` → Misc
  turns it off and restores the instant show/hide exactly. The motion is on alpha and
  `localScale`, never the rect, because the dock layout writes size and position every frame and
  would fight anything that touched them. A panel on its way out takes no part in the dock layout,
  so switching sidebar tabs no longer splits the sidebar between the outgoing and incoming panel
  for the length of the fade, and a closing dropdown stops catching clicks the instant it starts
  leaving rather than swallowing the next one.
  In the event panel, expanding a row slides the rows below it down and uncovers the settings
  as they move; collapsing slides them back up, and switching tiles fades the list in.
- **Chart analysis** — the `Chart` button in the Audio window opens a reading of every speed
  change the level makes, classified against what the song is doing. A **subdivision** (a
  power-of-two ratio) and a **magic shape** (a `180/angle` ratio — odd angles with a compensating
  speed change that holds the input tempo) are not tempo changes; anything else is the base BPM
  genuinely moving. Each row is then checked against the audio tempo curve, and the rows worth
  your attention are the disagreements — a base change where the song is steady, or a steady
  chart where the song moves. `Showing: to check` filters to exactly those. It reads
  `scrFloor.speed` and `scrFloor.entryTime`, which the game has already folded every SetSpeed
  into, so it cannot drift from the game's own semantics.
- **Seed from the song** — `Seed` in the Audio window writes BPM *and* offset from the audio
  alone, for a level that has neither: the tempo estimate never looks at the chart, and feeding
  it back into the phase fold gives the grid too.
- **Colour wheel** — every colour value in Sapphire (level settings, event inspector,
  decorations, particles, and the accent colour in Ctrl+E) opens an HSV wheel with an SV square,
  RGB and HSV fields, a hex field and an alpha slider when the value carries one. Click the
  swatch beside the hex field to open it; the value commits when you release the drag, so a drag
  costs one undo step rather than one per frame.
- **Ctrl+Z from a Sapphire field** — the game refuses every editor hotkey while a text field has
  focus, which silently swallowed undo after any edit made in a Sapphire panel. Ctrl+Z and
  Ctrl+Shift+Z now drop the caret and reach the editor's own undo stack.
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
- **Event tree selection** — clicking a row selects it (Ctrl toggles, Shift extends); the
  `▸` button at its left — which also carries the event's icon — expands it. A tile with one event opens with it selected and
  expanded; with several, the first is selected and the tree stays collapsed.

---

## Numeric fields

Every numeric field in Sapphire accepts **arithmetic**, the way the vanilla inspector does:
type `180*2`, `100/3`, `(1+2)*45` or `360/8` and the field evaluates on commit. Also supports
`^`, `%` and the constants `pi` / `tau` / `e`. Unlike vanilla, division is not integer division
— `10/4` is `2.5`, not `2`. Text fields (tags, image paths) are never evaluated.

**Tab** / **Shift+Tab** commits the field and moves to the next / previous one in the same
window. While a field has the keyboard, digits and Enter never reach palette or toolbar
shortcuts.

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
  relaunch starts clean by default; `Ctrl+E` → Misc → **Keep panel layout after restart**
  turns that reset off (covers the shape library, level settings, Magic Shape, Track tools,
  Deco tools and the Hz tool).
- **On-screen key hints** — a card in the bottom-right lists the keys live in the current
  context. It rides above the event timeline, sizes itself to its content, collapses to a
  header, and can be turned off (`Ctrl+E` → Features).

---

## Playback & misc tweaks

- **Practice pitch** — `scnEditor.playbackSpeed` (the game's native lever; hitsounds follow).
- **Non-destructive pitch overlay**.
- **WASD** camera pan.
- **Autoplay-pause key**, live **tile-angle readout**.
- **Edit / Play mode** — one chip above the timeline, left of the difficulty chip; clicking it
  swaps the two. There is no third state, so the chip always reads the mode you are in.
  - **Edit** is the clean charting screen: Sapphire overlays and the key viewer stand down, and
    the game's difficulty / no-fail / autoplay icons and hit error meter hide. Starting a
    playtest turns autoplay on — uncheck **Autoplay in edit mode** (`Ctrl+E` → Features) if you
    would rather play it yourself.
  - **Play** is the playtest mirror: autoplay off, no-fail on (optional — turn it off to feel the
    misses), timeline hidden, every Sapphire surface hidden **except the pitch overlay**, and
    tile placement and deletion refused so a stray key cannot edit the level you are auditioning.
    The corner master switch hides too, so the mode chip (or `Ctrl+E`) is the way back.

  Autoplay and no-fail are asserted when you switch the mode on and when a playtest starts, not
  every frame, so flipping autoplay mid-run still works.

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
| `Shift+P` `Shift+L` `Shift+R` `Shift+O` `Shift+G` | Quick chart | Pause / location / move tile / hold / angle pad |

The quick-chart and tool-slot rows above are **defaults**; rebind them in settings ▸ Keybinds. Defaults avoid the editor's 17 tile-placement letters (`a b c d e h j m n q s t v w
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
