#!/usr/bin/env bash
# Refresh the compile-time reference from the published PrismLib release.
# The DLL the game actually runs is installed by PrismBootstrap, not this one.
set -euo pipefail
cd "$(dirname "$0")"
VER=$(curl -sS https://raw.githubusercontent.com/PrismMods/PrismLib/main/prismlib.json | sed -n 's/.*"version" *: *"\([^"]*\)".*/\1/p')
curl -sSLo PrismLib.dll "https://github.com/PrismMods/PrismLib/releases/download/v$VER/PrismLib.dll"
echo "PrismLib.dll now $VER"
