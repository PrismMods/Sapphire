#!/bin/bash
set -e

MODS_DIR="$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice/UMMMods/Sapphire"

# Release: the Debug config sets <Optimize>false</Optimize>, and nothing in the source is
# gated on the DEBUG symbol, so shipping Debug bought us nothing but slower IL.
xbuild /p:Configuration=Release Sapphire.sln > /dev/null

mkdir -p "$MODS_DIR/Resources"
cp Sapphire/bin/Release/Sapphire.dll "$MODS_DIR/"
cp Info.json "$MODS_DIR/"
cp Sapphire/Resources/bismuth-fonts "$MODS_DIR/Resources/"

cmp -s Sapphire/bin/Release/Sapphire.dll "$MODS_DIR/Sapphire.dll" || { echo "ERROR: deployed dll does not match build output" >&2; exit 1; }

echo "Deployed $(grep -o '"Version": "[^"]*"' Info.json) to $MODS_DIR"
