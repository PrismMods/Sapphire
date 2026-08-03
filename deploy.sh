#!/bin/bash
set -e

GAME_DIR="${ADOFAI_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice}"

# Two loader layouts in the wild: MelonLoader + UMMCompat reads UMMMods/, native UMM reads
# Mods/. Pick whichever this machine actually has instead of assuming one.
if [ -d "$GAME_DIR/UMMMods" ]; then
    MODS_DIR="$GAME_DIR/UMMMods/Sapphire"
elif [ -d "$GAME_DIR/Mods" ]; then
    MODS_DIR="$GAME_DIR/Mods/Sapphire"
else
    echo "ERROR: no UMMMods/ or Mods/ under $GAME_DIR (set ADOFAI_ROOT?)" >&2
    exit 1
fi

# Release: the Debug config sets <Optimize>false</Optimize>, and nothing in the source is
# gated on the DEBUG symbol, so shipping Debug bought us nothing but slower IL.
xbuild /p:Configuration=Release Sapphire.sln > /dev/null

mkdir -p "$MODS_DIR/Resources"
cp Sapphire/bin/Release/Sapphire.dll "$MODS_DIR/"
cp Info.json "$MODS_DIR/"
cp Sapphire/Resources/bismuth-fonts "$MODS_DIR/Resources/"

cmp -s Sapphire/bin/Release/Sapphire.dll "$MODS_DIR/Sapphire.dll" || { echo "ERROR: deployed dll does not match build output" >&2; exit 1; }

echo "Deployed $(grep -o '"Version": "[^"]*"' Info.json) to $MODS_DIR"
