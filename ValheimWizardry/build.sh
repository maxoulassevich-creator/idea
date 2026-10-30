#!/usr/bin/env bash
# Builds Wizardry.dll and the Thunderstore zip (dist/Wizardry-<version>.zip).
# Needs: .NET SDK 8+, curl, unzip, zip. The reference assemblies come from NuGet into libs/.
set -euo pipefail
cd "$(dirname "$0")"
VERSION=1.0.0
PKGS=libs/pkgs
REFS=libs/refs
mkdir -p "$PKGS" "$REFS" dist

fetch() { # url name
  local file="$PKGS/$2.nupkg"
  [ -f "$file" ] || curl -fsSL -o "$file" "$1"
  rm -rf "$PKGS/$2" && mkdir -p "$PKGS/$2" && unzip -q -o "$file" -d "$PKGS/$2"
}

fetch https://api.nuget.org/v3-flatcontainer/digitalroot.valheim.common.references/1.0.21/digitalroot.valheim.common.references.1.0.21.nupkg valheim
fetch https://api.nuget.org/v3-flatcontainer/jotunnlib/2.30.2/jotunnlib.2.30.2.nupkg jotunn
fetch https://api.nuget.org/v3-flatcontainer/harmonyx/2.10.2/harmonyx.2.10.2.nupkg harmony
fetch https://nuget.bepinex.dev/v3/package/bepinex.baselib/5.4.20/bepinex.baselib.5.4.20.nupkg bepinex
fetch https://nuget.bepinex.dev/v3/package/unityengine.modules/6000.0.58/unityengine.modules.6000.0.58.nupkg unity

cp "$PKGS"/valheim/ref/net48/*.dll "$REFS"/
cp "$PKGS"/jotunn/lib/net462/Jotunn.dll "$REFS"/
cp "$PKGS"/harmony/lib/net45/0Harmony.dll "$REFS"/
cp "$PKGS"/bepinex/lib/net35/BepInEx.dll "$REFS"/
cp "$PKGS"/unity/lib/net45/UnityEngine*.dll "$REFS"/

dotnet build src/Wizardry/Wizardry.csproj -c Release -p:RefsDir="$PWD/$REFS"

STAGE=dist/stage
rm -rf "$STAGE" && mkdir -p "$STAGE"
cp src/Wizardry/bin/Release/Wizardry.dll package/manifest.json package/icon.png package/README.md CHANGELOG.md "$STAGE"/
cp УСТАНОВКА.md "$STAGE"/
(cd "$STAGE" && zip -q -r "../Wizardry-$VERSION.zip" .)
echo "dist/Wizardry-$VERSION.zip"
