#!/bin/bash
# Static cross-version check: resolves every game type/member a built mod dll references, every
# [HarmonyPatch(typeof(T), "name")] target + its by-name params, and literal-string reflection
# lookups, against the game build installed at <gameDir>. Misses guarded by Prepare()/null checks
# still print — read them against the source.
#   tools/refcheck/check.sh <mod.dll> [gameDir]
set -e
HERE=$(cd "$(dirname "$0")" && pwd)
OUT="${TMPDIR:-/tmp}/refcheck"
CECIL=$(ls -d "$(brew --prefix mono 2>/dev/null || echo /Library/Frameworks/Mono.framework/Versions/Current)"/lib/mono/gac/Mono.Cecil/0.11*/Mono.Cecil.dll | head -1)
mkdir -p "$OUT"
if [ ! -f "$OUT/RefCheck.exe" ] || [ "$HERE/RefCheck.cs" -nt "$OUT/RefCheck.exe" ]; then
    cp "$CECIL" "$OUT/"
    mcs -nowarn:618 -r:"$OUT/Mono.Cecil.dll" -out:"$OUT/RefCheck.exe" "$HERE/RefCheck.cs"
fi
G="${2:-$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice}"
MAN="$G/ADanceOfFireAndIce.app/Contents/Resources/Data/Managed"; [ -d "$MAN" ] || MAN="$G/ADanceOfFireAndIce_Data/Managed"
mono "$OUT/RefCheck.exe" "$1" "$MAN" "$G/UserLibs" "$G/MelonLoader/net35" "$G/UMMMods/PrismLib" "$G/Mods/PrismLib"
