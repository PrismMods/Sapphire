# Sapphire

ADOFAI editor-suite mod (UnityModManager) by QuartzTeam, split out of Bismuth in July 2026.
Standalone: carries its own copy of Bismuth's UI framework. Settings panel opens with Ctrl+E.

## Build & deploy

- `./deploy.sh` — builds with **xbuild** (Mono; NEVER `dotnet build`) and copies to
  `UMMMods/Sapphire`. The user reloads in-game (Ctrl+F10) and tests; iterate from their
  screenshots/reports.
- `./release.sh` — tester zip named with the git hash; `./release.sh <version>` bumps
  Info.json + VERSION.txt. The USER decides versions; don't bump unprompted.
- The csproj uses an explicit `<Compile>` whitelist — every new .cs file must be added.
- Private repo (QuartzTeam/Sapphire), no updater pipeline; builds are shared as zips.

## Architecture

- `MainClass` — UMM entry; `SapphireTicker` (DDOL MonoBehaviour) drives all per-frame
  `Tick()`s (EditorEvents, EditorSkin, EditorChrome, EditorUiLayout, Tweaks).
- `Util/EditorEvents.cs` — event timeline strip (bottom-docked), transport, mode cluster.
- `Util/EditorChrome.cs` — file chip/menu, panel rail, event dock (proxies the game's own
  buttons; never reimplements game logic).
- `Util/EditorSkin.cs` — dark reskin of the game's editor UI (reversible, guard-based).
- `Util/EditorUiLayout.cs` + `UI/EditorUiEditor.cs` — wrapper-based layout overrides +
  on-screen drag editor for game editor chrome.
- `Util/Tweaks.cs` — autoplay-pause key (transpiler target), Editor Mode, tile angle.
- `UI/` — panel framework carried from Bismuth (UICore/UIBuilder/TabRail/PageStack/Theme).
  Settings tab: `UI/Pages/PageEditor.cs`.
- Game types are researched from an IL dump: `monodis Assembly-CSharp.dll > /tmp/acs.il`,
  then grep. Verify field/method accessibility there before writing code.

## Conventions

- Comments: terse "why" comments only; no banners, no narration of the diff. Keep
  landmine/gotcha substance.
- Mod UI glyphs: user fonts lack exotic glyphs (⚙ ▼ ❚). Use proven ones (▶ ← × ›) or draw
  icons procedurally (dots, bars).
- uGUI gotchas that have bitten: clicked Buttons stay selected and Space re-submits them
  (always deselect after click); `CanvasGroup.interactable=false` silently kills proxied
  Button clicks (fade with alpha 0 + blocksRaycasts false only); game code rewrites UI
  colors/text directly (use per-frame guards, not one-shot styling).
- Harmony: build against HarmonyX 2.10 but must run on native UMM's older Harmony — avoid
  2.10-only Patch overloads; attribute patches via PatchAll are safe.
