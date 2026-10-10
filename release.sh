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
    # Keep the UMM auto-update feed in sync. DownloadUrl assumes the release is published as
    # tag v<version> with the zip attached (see the publish steps below). raw.githubusercontent
    # serves repository.json publicly; UMM polls it and compares Version.
    URL="https://github.com/PrismMods/Sapphire/releases/download/v$VERSION/$ZIP_NAME"
    jq --arg v "$VERSION" --arg u "$URL" \
        '.Releases[0].Version = $v | .Releases[0].DownloadUrl = $u' repository.json > repository.json.tmp \
        && mv repository.json.tmp repository.json
    echo "Updated repository.json -> $VERSION ($URL)"
    echo "Publish: git add -A && git commit && git push; git tag v$VERSION && git push origin v$VERSION;"
    echo "         gh release create v$VERSION $ZIP_NAME --prerelease --title 'Sapphire $VERSION'"
else
    VERSION=$(cat VERSION.txt)
    HASH=$(git rev-parse --short HEAD 2>/dev/null || echo nogit)
    ZIP_NAME="Sapphire-$VERSION-dev-$HASH.zip"
fi

# Release config: Debug sets <Optimize>false</Optimize> and no source is gated on DEBUG.
# ADOFAI_ROOT, if set, overrides Sapphire.csproj's own per-OS Steam-path autodetection
# (passing it through empty is harmless — the csproj only applies its default when unset).
BUILD_LOG="${TMPDIR:-/tmp}/sapphire-build.log"
if ! xbuild /p:Configuration=Release "/p:AdofaiRoot=$ADOFAI_ROOT" Sapphire.sln > "$BUILD_LOG" 2>&1; then
    grep -E "error" "$BUILD_LOG" | sort -u >&2
    echo "BUILD FAILED (full log: $BUILD_LOG)" >&2
    exit 1
fi

# Stage the UMM payload (single Sapphire/ folder at the zip root).
STAGE=$(mktemp -d)
trap 'rm -rf "$STAGE"' EXIT
mkdir -p "$STAGE/Sapphire/Resources"
cp Sapphire/bin/Release/Sapphire.dll "$STAGE/Sapphire/"
# Ships with the mod: PrismLib.UI is an ordinary dependency (the UI half needs no shared
# instance). Leaving it out makes the released build fail to load its update toast.
cp lib/PrismLib.UI.dll "$STAGE/Sapphire/"
cp Info.json "$STAGE/Sapphire/"
cp Sapphire/Resources/*.ttf "$STAGE/Sapphire/Resources/"
cp Sapphire/Resources/*.txt "$STAGE/Sapphire/Resources/"

rm -f "$ZIP_NAME"
(cd "$STAGE" && zip -qr out.zip Sapphire)
mv "$STAGE/out.zip" "$ZIP_NAME"

echo "Built $ZIP_NAME"
