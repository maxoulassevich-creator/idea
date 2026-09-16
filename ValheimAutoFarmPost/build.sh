#!/usr/bin/env bash
# AutoFarmPost - build script for Linux / macOS (Proton or native install).
#   ./build.sh                       - autodetect the game
#   ./build.sh /path/to/Valheim      - explicit game folder
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
version="1.0.0"

if ! command -v dotnet >/dev/null 2>&1; then
    echo "ERROR: .NET SDK not found. Install it from https://dotnet.microsoft.com/download" >&2
    exit 1
fi

valheim="${1:-${VALHEIM_INSTALL:-}}"
if [ -z "$valheim" ]; then
    for candidate in \
        "$HOME/.steam/steam/steamapps/common/Valheim" \
        "$HOME/.local/share/Steam/steamapps/common/Valheim" \
        "$HOME/Library/Application Support/Steam/steamapps/common/Valheim"; do
        if [ -f "$candidate/valheim_Data/Managed/assembly_valheim.dll" ]; then
            valheim="$candidate"
            break
        fi
    done
fi

if [ ! -f "${valheim:-}/valheim_Data/Managed/assembly_valheim.dll" ]; then
    echo "ERROR: Valheim not found. Run: ./build.sh /path/to/Valheim" >&2
    exit 1
fi

echo "Valheim : $valheim"
dotnet build "$root/src/AutoFarmPost/AutoFarmPost.csproj" -c Release -v minimal -p:ValheimDir="$valheim"

dll="$root/src/AutoFarmPost/bin/Release/AutoFarmPost.dll"
rm -rf "$root/dist"
mkdir -p "$root/dist/package"
cp "$dll" "$root/dist/"
cp "$dll" "$root/package/manifest.json" "$root/package/icon.png" "$root/README.md" "$root/dist/package/"
(cd "$root/dist/package" && zip -qr "../AutoFarmPost-$version.zip" .)
rm -rf "$root/dist/package"

echo "Mod     : $root/dist/AutoFarmPost.dll"
echo "Package : $root/dist/AutoFarmPost-$version.zip"
