#!/bin/bash
set -e

# Steam's install root differs per OS (and Linux has two in the wild — the real data dir and
# the legacy ~/.steam symlink to it); try each candidate and keep whichever actually exists.
# Must match Sapphire.csproj's AdofaiRoot search so build references and deploy target agree.
if [ -n "$ADOFAI_ROOT" ]; then
    GAME_DIR="$ADOFAI_ROOT"
else
    for candidate in \
        "$HOME/.local/share/Steam/steamapps/common/A Dance of Fire and Ice" \
        "$HOME/.steam/steam/steamapps/common/A Dance of Fire and Ice" \
        "$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice"; do
        if [ -d "$candidate" ]; then
            GAME_DIR="$candidate"
            break
        fi
    done
    GAME_DIR="${GAME_DIR:-$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice}"
fi

# Compile-time reference only — the copy the game runs is installed by PrismBootstrap. Fetched
# rather than committed so the checked-in tree never disagrees with the published release.
[ -f "$(dirname "$0")/lib/PrismLib.dll" ] || "$(dirname "$0")/lib/update-prismlib.sh"
[ -f "$(dirname "$0")/lib/PrismLib.UI.dll" ] || "$(dirname "$0")/lib/update-prismlib.sh"

# Two loader layouts in the wild: MelonLoader + UMMCompat reads UMMMods/, native UMM reads
# Mods/. Existence alone does not disambiguate — some installs carry a stale, empty UMMMods/
# alongside the Mods/ folder UMM is actually configured to read, so prefer whichever one
# actually HAS mods in it over whichever merely exists.
UMM_N=0; [ -d "$GAME_DIR/UMMMods" ] && UMM_N=$(find "$GAME_DIR/UMMMods" -mindepth 1 -maxdepth 1 -type d | wc -l)
MODS_N=0; [ -d "$GAME_DIR/Mods" ]    && MODS_N=$(find "$GAME_DIR/Mods"    -mindepth 1 -maxdepth 1 -type d | wc -l)
if [ "$UMM_N" -gt 0 ]; then
    MODS_DIR="$GAME_DIR/UMMMods/Sapphire"
elif [ "$MODS_N" -gt 0 ]; then
    MODS_DIR="$GAME_DIR/Mods/Sapphire"
elif [ -d "$GAME_DIR/UMMMods" ]; then
    MODS_DIR="$GAME_DIR/UMMMods/Sapphire"
elif [ -d "$GAME_DIR/Mods" ]; then
    MODS_DIR="$GAME_DIR/Mods/Sapphire"
else
    echo "ERROR: no UMMMods/ or Mods/ under $GAME_DIR (set ADOFAI_ROOT?)" >&2
    exit 1
fi

# Release: the Debug config sets <Optimize>false</Optimize>, and nothing in the source is
# gated on the DEBUG symbol, so shipping Debug bought us nothing but slower IL.
# Quiet on success, but a failed build must say why — with set -e and the output discarded,
# a compile error used to end the script with no message at all.
BUILD_LOG="${TMPDIR:-/tmp}/sapphire-build.log"
if ! xbuild /p:Configuration=Release "/p:AdofaiRoot=$GAME_DIR" Sapphire.sln > "$BUILD_LOG" 2>&1; then
    grep -E "error" "$BUILD_LOG" | sort -u >&2
    echo "BUILD FAILED (full log: $BUILD_LOG)" >&2
    exit 1
fi

mkdir -p "$MODS_DIR/Resources"
cp Sapphire/bin/Release/Sapphire.dll "$MODS_DIR/"
cp lib/PrismLib.UI.dll "$MODS_DIR/"
cp Info.json "$MODS_DIR/"
cp Sapphire/Resources/*.ttf "$MODS_DIR/Resources/"
cp Sapphire/Resources/*.txt "$MODS_DIR/Resources/"
# The font bundle was replaced by loose TTFs (Sept 2026); clear one left by an older deploy.
rm -f "$MODS_DIR/Resources/bismuth-fonts"

cmp -s Sapphire/bin/Release/Sapphire.dll "$MODS_DIR/Sapphire.dll" || { echo "ERROR: deployed dll does not match build output" >&2; exit 1; }

echo "Deployed $(grep -o '"Version": "[^"]*"' Info.json) to $MODS_DIR"
