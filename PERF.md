# Sapphire editor performance — audit and fixes

Covers the pass landed in `97205a8` (branch `perf/hot-path-optimizations`).

Method: full read of all per-frame paths, cross-checked against an `ilspycmd` decompile of
`Assembly-CSharp.dll` so every claim about a game API's cost was verified rather than assumed.

---

## The fact that reframes everything

**The game does no per-frame level scan in the editor.**

`scnEditor.Update` steady state is `OttoUpdate()`, a parent-chain walk, and `HandleMouseActions()`.
The expensive `ObjectsAtMouse()` is both frame-throttled *and* only reached from mouse-down. All 14
`events.FindAll(x => x.floor == n)` call sites in the game are edit- or click-triggered.

So Sapphire's per-frame O(n) loops were **pure addition**, not work amortized into something the game
already did — on a main thread already carrying `scrFloor.Update`/`LateUpdate` per *visible* tile
(~20k managed callbacks/frame zoomed out over a 10k-tile level; that part is game cost, but mod work
lands on top of it and gets blamed).

Also worth knowing: events are a flat `EventsArray<LevelEvent> : List<LevelEvent>`. **There is no
per-floor index anywhere in the game**, so "events at tile N" is inherently O(total events). Any
per-tile lookup the mod needs repeatedly should build its own dictionary and invalidate on edit.

---

## What was lagging, and what changed

### 1. Per-frame scans of the whole level

| Where | Was | Now |
|---|---|---|
| [EditorEventPanel.cs:84](Sapphire/Editor/EditorEventPanel.cs:84) | Clearing `_floor`/`_sig` on an eventless tile made `floorChanged` **and** the `_sig == 0` redraw request true again the next frame, so `FloorEvents` (list alloc + scan of every event in the level) re-ran **every frame** for as long as an empty tile stayed selected — the normal state while mapping | Latched with an `EmptySig` sentinel + `_empty` flag |
| [EditorFilterPicker.cs:97](Sapphire/Editor/EditorFilterPicker.cs:97) | `ed.events.Contains(_target)` every frame. `LevelEvent` has no `Equals` override, so `List.Contains` does a virtual dispatch per element — 10k–50k per frame on a decorated chart | Cadence check (15 frames) |
| [EditorEvents.cs:958](Sapphire/Editor/EditorEvents.cs:958) | Same `Contains` in CAM mode, where people park for whole sessions | Cadence check |
| [EditorToolbar.cs:340](Sapphire/Editor/EditorToolbar.cs:340) | Scanned all of `angleData` (one entry per tile; 20k–100k on big charts) for the midspin counter, then assigned the label **with no `!=` guard**, re-dirtying the TMP mesh every frame | Throttled scan + guarded write |
| [EditorEvents.cs:469](Sapphire/Editor/EditorEvents.cs:469) | Recounted the selected floor's events every frame purely to notice a change | Tile and total already imply a change and are O(1); the full recount runs on a cadence |

The `EditorEventPanel` one is worth calling out: the comment directly above it already said
*"scanning every frame is what dropped a big level from 120 to ~80fps."* The cost had been measured;
the empty-tile path silently reintroduced it.

### 2. Multi-megabyte allocations per repaint

Mono's Boehm GC puts anything over 8 KB in the large-object area, so these were the dominant
GC-stutter source.

- **[EditorEvents.cs:1790](Sapphire/Editor/EditorEvents.cs:1790)** — `new Color32[1792 * texH]` per marker
  repaint: **~0.96 MB** at default lane height, **4.3 MB** with the height grip maxed. Fired every 4
  frames while play-testing and **every frame** while wheel-panning or drag-scrubbing → roughly
  **58 MB/s** of garbage, plus an equal amount of memcpy and GPU upload.
- **[EditorGraph.cs:274](Sapphire/Editor/EditorGraph.cs:274)** — `new Color32[1400 * 380]` = **2.13 MB** per
  repaint; every 30 frames at idle and **every frame while panning** the plot.

Both now reuse a static buffer, cleared with `Array.Clear`. `SetPixels32` demands an exact length
match, so EditorEvents reallocates only when the size genuinely changes. The clear is required —
the painters write marks over an assumed-transparent background.

### 3. Rebuild storms driven by drag input

- **[EditorFilterPicker.cs:92](Sapphire/Editor/EditorFilterPicker.cs:92)** — `ResizeHandle` is an
  `IDragHandler` writing `sizeDelta` on every drag frame, and any size change tore down and respawned
  the whole grid: ~300 filters × (3 GameObjects + `RoundedRectGraphic` + 2 TMP + 2 handlers + closures)
  ≈ **900 GameObjects per frame** for the duration of the drag. Now debounced — rebuilds once the drag
  settles.
- **[EditorCameraPath.cs:484](Sapphire/Editor/EditorCameraPath.cs:484)** — the rebuild signature folds in the
  camera view rect, so **any pan** destroyed the root and respawned every dot as a fresh
  `GameObject` + `SpriteRenderer` (0.4-unit spacing, 1500/leg cap). Dots are pooled now; only the path
  dots, since the selection box has its own lifetime.
- **[EditorEvents.cs:3124](Sapphire/Editor/EditorEvents.cs:3124)** — the strip's height grip cleared `_tlSig`
  on every pixel step, forcing a full `RebuildStructure`: `CalculateFloorEntryTimes`, 5 array allocs,
  an O(n×33) windowed tempo histogram, time-signature detection (`new double[nb]` up to 40,000),
  per-lane sorts, and a lane-label respawn — **10–30 ms per drag frame** on a big level. None of that
  depends on lane height. Throttled during the drag, with an exact rebuild on pointer-up.

### 4. uGUI

- **[RoundedRectGraphic.cs:79](Sapphire/UI/RoundedRectGraphic.cs:79)** — built the corner outline **twice,
  identically**, whenever `BorderWidth == 0` (`innerR == r` and the inset rect equals the rect), which
  is most shapes. It also re-ran all the trig for a plain colour change, because uGUI calls
  `OnPopulateMesh` again on any `Graphic.color` write. Outlines are now cached on geometry
  (size, origin, radius, border, fringe, segments) and the duplicate pass is gone. Applies across
  **145 instantiation sites** — shapes run 221–381 verts each.
- **[PanelKit.cs:181](Sapphire/UI/PanelKit.cs:181)** — the dock early-out keyed on `_dockChromeGo`, which
  `EnsureDockChrome` creates once and never destroys. After **any** panel had ever docked, the full
  layout path ran every frame forever, even with nothing docked. Now keys on the dock lists (plus an
  active header drag, so the drop indicator still works) and idles the chrome canvas.
- **`ClampFloating`** only recovers floating windows from a window resize, but walked every registered
  panel with two native `RectTransform.rect` reads apiece every frame. Now runs on an actual screen-size
  change.
- **`LayoutSide`** — `RemoveAll(p => ... p.DockSide != side ...)` captures `side`, so the compiler cannot
  cache the lambda: a display class **and** a `Predicate` allocated per call, twice per frame. Replaced
  with a manual reverse sweep. Divider components are cached instead of `GetComponent` per divider per
  frame.
- Tooltip and footer backdrops are no longer raycast targets. The tooltip one was also a bug — it sat
  over the `?` icon that opened it and stole that icon's pointer enter/exit.

### 5. Everything else

- **Shipped Debug builds.** `deploy.sh` and `release.sh` both copied `bin/Debug`, and the Debug config
  sets `<Optimize>false</Optimize>`. Nothing in the source is gated on the `DEBUG` symbol, so every
  deploy and every tester zip was unoptimized IL for no benefit. Both scripts now build and copy Release.
- **[SapphireLog.cs:22](Sapphire/Util/SapphireLog.cs:22)** — `File.AppendAllText` opens, writes and closes the
  file on every call, and several log sites sit inside per-tile/per-group loops in the batch pseudo-tools
  ([EditorToolbar.cs:2400](Sapphire/Editor/EditorToolbar.cs:2400), [:2472](Sapphire/Editor/EditorToolbar.cs:2472)).
  One batch op on a large selection meant hundreds of synchronous syscalls on the main thread. Now
  buffered, drained on a 45-frame cadence, before any log read, and on unload.
- **The perf census was itself a stutter.** A whole-scene `FindObjectsOfType<Canvas>` (the legacy
  *sorted* variant), `cv.name` marshalling a fresh string for every Canvas in the game, culture-sensitive
  `StartsWith`, and a `GetComponentsInChildren` array per canvas — every 900 frames, in release. A
  visible hitch every ~15 s. Now gated behind `DebugMode`, unsorted, ordinal, and allocation-free.
- Cached `Type.GetField` reflection in EditorLevelMenu; cached the `CanvasGroup` in EditorChrome and the
  interactive-content probe in EditorPopups (five subtree walks per frame while a popup was up);
  `Input.anyKeyDown` gates in front of the digit-hotkey blocks; ordinal `StartsWith` on name comparisons;
  `sqrMagnitude` instead of `Distance` in the per-tile cursor hit test; `HashSet` in EditorVfxPreview;
  `DedupEventSystem` latches instead of scanning forever; the profiler reads one timestamp per lap
  instead of two.

---

## Deliberately left alone

**The global `Graphic.color` / `TMP_Text.color` setter patches** ([EditorSkin.cs:513](Sapphire/Editor/EditorSkin.cs:513)).
These were the first suspect and the audit cleared them. Counting at the IL level: only **42 `.color =`
writes exist inside any `Update`/`LateUpdate` in the whole game, ~15 of them on a `Graphic`.** Every
high-multiplicity per-frame colour writer — `scrFloor`, `PlanetRenderer`, `EventIndicator` — targets
`SpriteRenderer` or the custom `FloorRenderer`, **not** `Graphic`, so per-tile and per-decoration work
contributes zero patched calls. Realistic load is 10–40 writes/frame in the editor at ~40 ns of wrapper
overhead ≈ **0.4–1.6 µs/frame, about 0.01% of a 16.6 ms frame**. `GetInstanceID()` is also pure managed
in this Unity build, not an ICall. The fast-path design is sound. The real risk here is *scope*, not
speed: the prefixes run during scene loads, in menus, and inside other mods' UI, so `InterceptActive`
is the only thing standing between the mod and 100% of the game's colour writes.

**`EditorCopyPanel.SyncTints` per-frame call** ([EditorCopyPanel.cs:85](Sapphire/Editor/EditorCopyPanel.cs:85)).
Looked redundant because every mutating callback also calls it — but `_on` is mutated at
[line 68](Sapphire/Editor/EditorCopyPanel.cs:68), inside the tick path, with no `SyncTints` of its own.
Removing the call would leave stale tints. Its cost was also predicated on the colour patch being
expensive, which it isn't.

---

## Known, still unfixed

Ordered roughly by value.

- **Nothing is pooled in the timeline or event panel.** [EditorEvents.cs:493](Sapphire/Editor/EditorEvents.cs:493)
  destroys and recreates up to 26 GameObjects including 13 `TextMeshProUGUI` on **every selected-tile
  change**, each calling `GetPreferredValues` (forces a text-generation pass). This is the visible hitch
  when arrow-keying through tiles. Same pattern for lane labels and `EditorEventPanel.BuildContent`
  (which rebuilds the whole tree, including `TMP_InputField`s, on every expand/collapse/commit).
- **`RenderMarkers` walks every event in every lane** ([EditorEvents.cs:1850](Sapphire/Editor/EditorEvents.cs:1850))
  to draw the ~50 on screen. The lists are already sorted — binary-search the bounds. In keyframe modes
  each iteration also hits `FieldInfo.GetValue` reflection.
- **DECO/FILTER modes fold the view window into the structure signature**
  ([EditorEvents.cs:643](Sapphire/Editor/EditorEvents.cs:643)), so any pan or zoom re-runs the whole
  `RebuildStructure`. Throttled to 1/15 frames, but still a 10–30 ms hitch several times a second while
  panning a big level.
- **`EditorSkin`'s instance-ID `HashSet`s grow unboundedly.** `_ownedImages`/`_ownedTexts`/`_seen` are only
  cleared when the theme is turned off, but editor UI objects are destroyed and recreated constantly, so
  dead IDs accumulate for the whole session — memory growth plus slower `InterceptColor` lookups the
  longer you edit.
- `ResizeHandle` attaches **8 invisible full-edge raycast targets per panel** (~56 across all panels).
- The settings panel is **one Canvas with no sub-canvases**, so any dirty graphic rebatches everything;
  `ContentSizeFitter` under a `VerticalLayoutGroup` cascades a full-page layout pass per row height change.
  `EditorEvents` already uses sub-canvases correctly.
- `scnEditor.userIsEditingAnInputField` is a **property that does a `GetComponent<TMP_InputField>()`**;
  EditorToolbar reads it and then repeats the same `GetComponent` itself, up to 4× per frame.
- Tooltip strings and the transport clock are rebuilt every frame even though the TMP writes are guarded
  (pure allocation churn).
- `FieldInfo.GetValue` on a `bool` **boxes** every read — per frame in `EditorEvents.EditorPanelOpen`.
- **~1,800 of `UIBuilder.cs`'s 2,283 lines are dead** (only 14 members have callers). Not a runtime cost,
  but it makes the file hard to reason about.
- The 27-module ticker is **not hard-gated to the editor scene**. `scrController.instance` /
  `scrConductor.instance` / `RDConstants.data` are properties that degrade to `FindAnyObjectByType` /
  `Resources.Load` on *every access while null* — i.e. during scene transitions.

---

## Patterns worth copying

Already in the codebase and doing the right thing:

- [EditorUiLayout.cs:103](Sapphire/Editor/EditorUiLayout.cs:103) — `if (--_tickCooldown > 0) return;` (1 frame in 60).
- [EditorCopyPanel.cs:115](Sapphire/Editor/EditorCopyPanel.cs:115) — integer `LayoutSig()` instead of a string
  signature. Three other panels have now been converted to match.
- `PanelKit.SyncCanvasActive` — disables `Canvas` + `GraphicRaycaster` with equality guards.
- `PanelKit.TickFocus` — early-outs on `!Input.GetMouseButtonDown(0)` before touching anything.
- `TmpShadow.Apply` — six cached `_applied*` fields guard every redundant write.
- Most TMP `.text` and `Graphic.color` writes in `EditorEvents` are change-guarded.

---

## Verifying

The built-in profiler in `MainClass.SapphireTicker` is allocation-free and prints per-module timings
every ~900 frames. Before this pass, `EditorGraph`, `EditorCameraPath` and `EditorEvents` were the loud
entries. The UI census line (canvas / graphic / raycast-target counts) now requires `DebugMode`.

Note that both configurations compile clean, but these changes have **not been runtime-tested**. Worth
checking by hand: filter-grid resize now settles a few frames after you stop dragging rather than
tracking live; the timeline height grip refreshes periodically mid-drag with an exact rebuild on release;
camera-path dots are recycled rather than respawned.

### Build note

`./deploy.sh` will not run on every machine: [Sapphire.csproj:16](Sapphire/Sapphire.csproj:16) hardcodes
`/Users/preluminance/...` and references HarmonyX under `MelonLoader/net472/`, which a native-UMM install
does not have. That is pre-existing and unrelated to this pass. Overriding works:

```bash
GR="$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice"
xbuild /p:Configuration=Release Sapphire.sln \
  /p:GameRoot="$GR" \
  /p:AdofaiManaged="$GR/ADanceOfFireAndIce.app/Contents/Resources/Data/Managed"
```

HarmonyX 2.10 still has to come from somewhere the csproj can see — making the project machine-agnostic
would be a worthwhile separate change.
