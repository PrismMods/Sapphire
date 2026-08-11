# Sapphire

ADOFAI editor-suite mod (UnityModManager) by QuartzTeam, split out of Bismuth in July 2026.
Standalone: carries its own copy of Bismuth's UI framework. Settings panel opens with Ctrl+E.

## Build & deploy

- `./deploy.sh` — builds with **xbuild** (Mono; NEVER `dotnet build`) and copies to
  `UMMMods/Sapphire`. The user reloads in-game (Ctrl+F10) and tests; iterate from their
  screenshots/reports.
- `./release.sh` — tester zip named with the git hash; `./release.sh <version>` bumps
  Info.json + VERSION.txt + `repository.json` (the UMM update feed). The USER decides
  versions; don't bump unprompted.
- The csproj uses an explicit `<Compile>` whitelist — every new .cs file must be added.
- **Public repo** (PrismMods/Sapphire). UMM auto-update is wired via `repository.json` +
  GitHub release assets — see the `update-feed` memory for the publish flow + landmines.

## Architecture

- `MainClass` — UMM entry; `SapphireTicker` (DDOL MonoBehaviour) drives all per-frame
  `Tick()`s (EditorEvents, EditorSkin, EditorChrome, Tweaks).
- `Util/EditorEvents.cs` — event timeline strip (bottom-docked), transport, mode cluster.
- `Util/EditorChrome.cs` — file chip/menu, panel rail, event dock (proxies the game's own
  buttons; never reimplements game logic).
- `Util/EditorSkin.cs` — dark reskin of the game's editor UI (reversible, guard-based).
- `Util/Tweaks.cs` — autoplay-pause key (transpiler target), Editor Mode, tile angle.
- `UI/` — panel framework carried from Bismuth (UICore/UIBuilder/TabRail/PageStack/Theme).
  Settings tab: `UI/Pages/PageEditor.cs`.
- Game types are researched from an IL dump: `monodis Assembly-CSharp.dll > /tmp/acs.il`,
  then grep. Verify field/method accessibility there before writing code.

## Charting domain (ADOFAI)

Fundamentals for any tile/pseudo/shape work. Exhaustive detail lives in memory
(`feature-backlog`, `game-il-facts`); this is the working subset.

- **Angles.** `angleData[i]` = each tile's ABSOLUTE facing, in CLOCKWISE degrees (the game
  uses `ClockwiseAngleToVector`). "Charter" is the RELATIVE hit angle: `turn = 180 − charter`,
  so `180` = straight, `90` = quarter, `0` = U-turn; `beat = charter / 180`. `999` = midspin.
- **Spin.** `scrFloor.isCCW` propagates tile→tile and FLIPS on every Twirl AND every `999`
  midspin. The editor's swirl marker icon is `FloorIcon.Swirl` (CCW) vs `SwirlCW` (CW) — a
  **blue** swirl means the resulting spin is CCW. Build turns with the tile's own spin:
  `sign = isCCW ? -1 : +1` (the `AppendRel` convention `dir + (ccw ? -(180-rel) : 180-rel)`).
  Hardcoding `sign=1` ignores an incoming Twirl and renders the swirl the wrong colour.
- **Midspin (`999`).** Spins in place; reverses spin AND heading. Relative/charter math is
  INVALID across a `999` — write ABSOLUTE facings. A midspin excursion returns to the
  ABSOLUTE baseline facing, e.g. `[θ, 999, 360−θ, 180]` (the `360−θ` cancels the tap).
- **Pseudo (동타).** Beat-neutral: its charters SUM to 180 (K tiles = K hits). Twirls sit ONE
  tile BEFORE their tile; add ALL Twirls AFTER the tiles exist, then ONE `RemakePath` — a
  twirl written mid-build corrupts the arbitrary-angle writes. Midspin pseudo ground truth is
  `EditorToolbar.ApplyMidspinPseudo` (keep the clicked tile + interleaved tap+`999`, facings
  step down), used by BOTH single-click and batch-Inline — NOT the `PseudoBuild` core (whose
  `999` branch is dead). `PseudoBuild` drives the shape library + angle pad (no midspins).
- **Tile API.** Tap at absolute facing: `CreateFloorWithCharOrAngle((float)abs,(char)163,false,false)`;
  midspin: `(999f,'!',false,true)`; Twirl: `ed.events.Add(new ADOFAI.LevelEvent(seq, ADOFAI.LevelEventType.Twirl))`.
  One undo: `using (new SaveStateScope(ed))` (nests). Rebuild once at the end: `RemakePath(true,true)`.
  Batch mutation: process seqIDs HIGH→LOW so inserts/replaces don't shift unprocessed lower tiles.
- **IL research (mac path).** The DLL lives at
  `…/A Dance of Fire and Ice/ADanceOfFireAndIce.app/Contents/Resources/Data/Managed/Assembly-CSharp.dll`.

## Conventions

- Comments: terse "why" comments only; no banners, no narration of the diff. Keep
  landmine/gotcha substance.
- A user's reference `.adofai`/level proves a CONSTRUCTION, not which code path is broken.
  Confirm the exact path the user triggers (single-click vs batch, uniform vs custom) before
  "fixing" — a reference decoded as gospel led to reverting a correct single-click twice.
- Docs: keep `docs/FEATURES.md` ⇄ `docs/FEATURES.ko.md` in sync; both carry a top nav header
  (`| English | [한국어](FEATURES.ko.md) |`). Korean prose ends in `-니다` (no fragment+period);
  fixed terms — 동타 (pseudo), 드르륵 (zip), 소용돌이 (twirl), 도형 (shape), 빠른 차팅
  (quick chart), 각 패드 (angle pad), 가감속 (ease). User-facing UI strings go through
  `Loc.T`; add EN→KO pairs in `Util/Loc.cs`.
- Credits: permission-based ports (named author + repo) live in README's Credits; acknowledge
  inspiration from mods whose author is unknown separately, and don't invent an author/URL.
- Mod UI glyphs: user fonts lack exotic glyphs (⚙ ▼ ❚). Use proven ones (▶ ← × ›) or draw
  icons procedurally (dots, bars).
- uGUI gotchas that have bitten: clicked Buttons stay selected and Space re-submits them
  (always deselect after click); `CanvasGroup.interactable=false` silently kills proxied
  Button clicks (fade with alpha 0 + blocksRaycasts false only); game code rewrites UI
  colors/text directly (use per-frame guards, not one-shot styling).
- Harmony: build against HarmonyX 2.10 but must run on native UMM's older Harmony — avoid
  2.10-only Patch overloads; attribute patches via PatchAll are safe.
