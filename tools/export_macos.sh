#!/bin/sh
# Exports the macOS app and re-signs it plain ad-hoc.
#
# The re-sign is load-bearing, not cosmetic: Godot 4.7's built-in signer stamps
# the hardened-runtime flag on the ad-hoc signature, and a hardened ad-hoc
# bundle carrying the .NET runtime is killed by macOS before main() ever runs —
# no output, no crash report, exit 137. Stripping the flag by re-signing is
# what makes the exported app launchable. Measured both ways on 4.7.1.
#
# Usage: GODOT=/path/to/Godot tools/export_macos.sh
set -e

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

if [ -z "$GODOT" ]; then
    echo "Set GODOT to the Godot 4.7 mono editor binary." >&2
    exit 1
fi

"$GODOT" --headless --path "$ROOT" --export-release "macOS" tmp/VoxelCAD.app
codesign --force --deep --sign - "$ROOT/tmp/VoxelCAD.app"

echo "Exported and re-signed: $ROOT/tmp/VoxelCAD.app"
