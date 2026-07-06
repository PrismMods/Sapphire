#!/bin/bash
# Build a shareable zip for testers (drop into UMMMods/ or install via UnityModManager).
#
# Usage:
#   ./release.sh              dev build: Sapphire-<version>-dev-<githash>.zip, no version bump
#   ./release.sh <version>    bump Info.json + VERSION.txt, build Sapphire-<version>.zip
#
# The repo is private, so there's no updater pipeline — send the zip directly.

set -e

if [ -n "$1" ]; then
    VERSION="$1"
    echo "$VERSION" > VERSION.txt
    jq --arg v "$VERSION" '.Version = $v' Info.json > Info.json.tmp && mv Info.json.tmp Info.json
    ZIP_NAME="Sapphire-$VERSION.zip"
else
    VERSION=$(cat VERSION.txt)
    HASH=$(git rev-parse --short HEAD 2>/dev/null || echo nogit)
    ZIP_NAME="Sapphire-$VERSION-dev-$HASH.zip"
fi

xbuild Sapphire.sln > /dev/null

# Stage the UMM payload (single Sapphire/ folder at the zip root).
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/Sapphire/Resources"
cp Sapphire/bin/Debug/Sapphire.dll "$STAGE/Sapphire/"
cp Info.json "$STAGE/Sapphire/"
cp Sapphire/Resources/bismuth-fonts "$STAGE/Sapphire/Resources/"

rm -f "$ZIP_NAME"
(cd "$STAGE" && zip -qr out.zip Sapphire)
mv "$STAGE/out.zip" "$ZIP_NAME"

echo "Built $ZIP_NAME"
