#!/usr/bin/env bash
# AutoFarmPost - build script for Linux / macOS.
#   ./build.sh                            - autodetect game and mod profile
#   ./build.sh /path/to/Valheim           - explicit game folder
# BepInEx.dll, 0Harmony.dll and Jotunn.dll are copied from your mod profile into libs/.
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

mkdir -p "$root/libs"
find_lib() {
    local name="$1"
    [ -f "$root/libs/$name" ] && return 0
    local hit
    for base in \
        "$HOME/.config/r2modmanPlus-local/Valheim" \
        "$HOME/.config/Thunderstore Mod Manager/DataFolder/Valheim" \
        "$HOME/.config" \
        "$valheim/BepInEx"; do
        [ -d "$base" ] || continue
        hit="$(find "$base" -name "$name" -type f 2>/dev/null | head -n 1 || true)"
        if [ -n "$hit" ]; then
            cp "$hit" "$root/libs/"
            echo "  $name <- $hit"
            return 0
        fi
    done
    return 1
}

for lib in BepInEx.dll 0Harmony.dll Jotunn.dll; do
    if ! find_lib "$lib"; then
        echo "ERROR: $lib not found. Install BepInExPack Valheim + Jotunn in your mod profile," >&2
        echo "       or copy the three DLLs into $root/libs manually." >&2
        exit 1
    fi
done

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
