#!/usr/bin/env bash
# Valheim 1.0.21 (Unity 6 Mono) enforces member visibility at run time, while build.sh compiles against publicized
# references in which every game member looks public. This check compiles the mod against the original, non-publicized
# Valheim 0.221.4 assemblies instead, so any direct use of a private or protected game member shows up as a compile error.
# Run ./build.sh first (it fills libs/refs). Signature changes between 0.221.4 and 1.0.21 (CS1501) are ignored.
set -euo pipefail
cd "$(dirname "$0")/.."
PKGS=libs/pkgs
ORIG=libs/refs-original
[ -d libs/refs ] || { echo "run ./build.sh first"; exit 1; }
command -v dotnet > /dev/null || { echo "dotnet not found"; exit 1; }

file="$PKGS/original.nupkg"
[ -f "$file" ] || curl -fsSL -o "$file" https://api.nuget.org/v3-flatcontainer/coffeenova.valheim.managedreferences/1.2214.6/coffeenova.valheim.managedreferences.1.2214.6.nupkg
rm -rf "$PKGS/original" "$ORIG" && mkdir -p "$PKGS/original" "$ORIG"
unzip -q -o "$file" 'lib/*' -d "$PKGS/original"
cp libs/refs/*.dll "$ORIG"/
rm -f "$ORIG"/assembly_*-publicized.dll
cp "$PKGS"/original/lib/net46/assembly_*.dll "$ORIG"/

log=$(dotnet build src/Wizardry/Wizardry.csproj -c Release -p:RefsDir="$PWD/$ORIG" -p:OutDir="$PWD/libs/access-check/" --no-incremental 2>&1 || true)
bad=$(echo "$log" | grep -oE "[A-Za-z]+\.cs\([0-9]+,[0-9]+\): error CS[0-9]+: [^[]*" | grep -v "error CS1501" | sort -u || true)
if [ -n "$bad" ]; then
  echo "Non-public game members used directly (go through Access.cs instead):"
  echo "$bad"
  exit 1
fi
# the compiler must have run: either the build succeeded or it stopped only on the ignored signature changes
if ! echo "$log" | grep -qE "Build succeeded|error CS1501"; then
  echo "the build did not run:"
  echo "$log" | tail -20
  exit 1
fi
echo "access check passed"
