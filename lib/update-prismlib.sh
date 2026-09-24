#!/usr/bin/env bash
# Refresh the PrismLib DLLs this mod builds against.
#   PrismLib.dll     compile-time only — the running copy is installed by PrismBootstrap.
#   PrismLib.UI.dll  SHIPPED with the mod: the UI half needs no shared instance across mods, so
#                    it is an ordinary dependency and deploy/release copy it into the mod folder.
set -euo pipefail
cd "$(dirname "$0")"
FEED=https://raw.githubusercontent.com/PrismMods/PrismLib/main/prismlib.json
VER=$(curl -sS "$FEED" | sed -n 's/.*"version" *: *"\([^"]*\)".*/\1/p')
BASE="https://github.com/PrismMods/PrismLib/releases/download/v$VER"
curl -sSLo PrismLib.dll "$BASE/PrismLib.dll"
curl -sSLo PrismLib.UI.dll "$BASE/PrismLib.UI.dll"
echo "PrismLib + PrismLib.UI now $VER"
