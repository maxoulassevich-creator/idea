# Builds Wizardry.dll and the Thunderstore zip (dist\Wizardry-<version>.zip) on Windows.
# Needs the .NET SDK 8+ (https://dotnet.microsoft.com/download). Run: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
$Version = "1.0.1"
$Pkgs = "libs\pkgs"; $Refs = "libs\refs"
New-Item -ItemType Directory -Force -Path $Pkgs, $Refs, "dist" | Out-Null

function Fetch($url, $name) {
  $file = "$Pkgs\$name.zip"
  if (-not (Test-Path $file)) { Invoke-WebRequest -Uri $url -OutFile $file }
  if (Test-Path "$Pkgs\$name") { Remove-Item -Recurse -Force "$Pkgs\$name" }
  Expand-Archive -Path $file -DestinationPath "$Pkgs\$name"
}

Fetch "https://api.nuget.org/v3-flatcontainer/digitalroot.valheim.common.references/1.0.21/digitalroot.valheim.common.references.1.0.21.nupkg" "valheim"
Fetch "https://api.nuget.org/v3-flatcontainer/jotunnlib/2.30.2/jotunnlib.2.30.2.nupkg" "jotunn"
Fetch "https://api.nuget.org/v3-flatcontainer/harmonyx/2.10.2/harmonyx.2.10.2.nupkg" "harmony"
Fetch "https://nuget.bepinex.dev/v3/package/bepinex.baselib/5.4.20/bepinex.baselib.5.4.20.nupkg" "bepinex"
Fetch "https://nuget.bepinex.dev/v3/package/unityengine.modules/6000.0.58/unityengine.modules.6000.0.58.nupkg" "unity"

Copy-Item "$Pkgs\valheim\ref\net48\*.dll" $Refs
Copy-Item "$Pkgs\jotunn\lib\net462\Jotunn.dll" $Refs
Copy-Item "$Pkgs\harmony\lib\net45\0Harmony.dll" $Refs
Copy-Item "$Pkgs\bepinex\lib\net35\BepInEx.dll" $Refs
Copy-Item "$Pkgs\unity\lib\net45\UnityEngine*.dll" $Refs

dotnet build src\Wizardry\Wizardry.csproj -c Release "-p:RefsDir=$PWD\$Refs"

$Stage = "dist\stage"
if (Test-Path $Stage) { Remove-Item -Recurse -Force $Stage }
New-Item -ItemType Directory -Force -Path $Stage | Out-Null
Copy-Item src\Wizardry\bin\Release\Wizardry.dll, package\manifest.json, package\icon.png, package\README.md, CHANGELOG.md, "УСТАНОВКА.md" $Stage
Compress-Archive -Path "$Stage\*" -DestinationPath "dist\Wizardry-$Version.zip" -Force
Write-Host "dist\Wizardry-$Version.zip"
