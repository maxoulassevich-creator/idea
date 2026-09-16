<#
    AutoFarmPost - build script

    Usage:
        powershell -ExecutionPolicy Bypass -File .\build.ps1
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -ValheimPath "D:\SteamLibrary\steamapps\common\Valheim"
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -DeployPath "C:\...\profiles\Default\BepInEx\plugins"

    Result:
        dist\AutoFarmPost.dll             - the mod itself
        dist\AutoFarmPost-<ver>.zip       - package for "Import local mod" in Thunderstore Mod Manager
#>
param(
    [string]$ValheimPath,
    [string]$DeployPath,
    [switch]$NoPackage
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '1.0.0'

Write-Host ''
Write-Host '=== AutoFarmPost build ===' -ForegroundColor Cyan

# --- 1. .NET SDK -----------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'ERROR: .NET SDK not found.' -ForegroundColor Red
    Write-Host 'Install it from https://dotnet.microsoft.com/download (button "Download .NET SDK x64"),'
    Write-Host 'close this window, open a new one and run the script again.'
    exit 1
}

# --- 2. Valheim folder -----------------------------------------------------
function Test-ValheimFolder([string]$path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $false }
    return Test-Path (Join-Path $path 'valheim_Data\Managed\assembly_valheim.dll')
}

function Find-Valheim {
    $candidates = New-Object System.Collections.Generic.List[string]

    if ($env:VALHEIM_INSTALL) { $candidates.Add($env:VALHEIM_INSTALL) }

    try {
        $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
        if ($steam) {
            $steam = $steam -replace '/', '\'
            $candidates.Add((Join-Path $steam 'steamapps\common\Valheim'))

            $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
            if (Test-Path $vdf) {
                $text = Get-Content $vdf -Raw
                foreach ($m in [regex]::Matches($text, '"path"\s+"([^"]+)"')) {
                    $lib = $m.Groups[1].Value -replace '\\\\', '\'
                    $candidates.Add((Join-Path $lib 'steamapps\common\Valheim'))
                }
            }
        }
    } catch { }

    $candidates.Add('C:\Program Files (x86)\Steam\steamapps\common\Valheim')
    $candidates.Add('C:\Program Files\Steam\steamapps\common\Valheim')
    $candidates.Add('C:\SteamLibrary\steamapps\common\Valheim')
    $candidates.Add('D:\Steam\steamapps\common\Valheim')
    $candidates.Add('D:\SteamLibrary\steamapps\common\Valheim')
    $candidates.Add('E:\SteamLibrary\steamapps\common\Valheim')

    foreach ($c in $candidates) { if (Test-ValheimFolder $c) { return $c } }
    return $null
}

if ($ValheimPath) {
    if (-not (Test-ValheimFolder $ValheimPath)) {
        Write-Host "ERROR: no Valheim in '$ValheimPath'." -ForegroundColor Red
        Write-Host 'Pass the folder that contains valheim.exe.'
        exit 1
    }
} else {
    $ValheimPath = Find-Valheim
    if (-not $ValheimPath) {
        Write-Host 'ERROR: Valheim not found automatically.' -ForegroundColor Red
        Write-Host 'In Steam: right click Valheim -> Manage -> Browse local files, copy the path and run:'
        Write-Host '   powershell -ExecutionPolicy Bypass -File .\build.ps1 -ValheimPath "<that path>"'
        exit 1
    }
}

Write-Host "Valheim : $ValheimPath"

# --- 3. Build --------------------------------------------------------------
$proj = Join-Path $root 'src\AutoFarmPost\AutoFarmPost.csproj'
Write-Host 'Building (first run downloads NuGet packages, 1-3 minutes)...'

& dotnet build $proj -c Release -v minimal -p:ValheimDir="$ValheimPath"
if ($LASTEXITCODE -ne 0) {
    Write-Host 'ERROR: build failed, see the messages above.' -ForegroundColor Red
    exit 1
}

$dll = Join-Path $root 'src\AutoFarmPost\bin\Release\AutoFarmPost.dll'
if (-not (Test-Path $dll)) {
    Write-Host "ERROR: $dll was not produced." -ForegroundColor Red
    exit 1
}

$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null
Copy-Item $dll $dist

# --- 4. Thunderstore-style package ----------------------------------------
if (-not $NoPackage) {
    $staging = Join-Path $dist 'package'
    New-Item -ItemType Directory -Path $staging | Out-Null

    Copy-Item $dll $staging
    Copy-Item (Join-Path $root 'package\manifest.json') $staging
    Copy-Item (Join-Path $root 'package\icon.png') $staging
    Copy-Item (Join-Path $root 'README.md') $staging

    $zip = Join-Path $dist "AutoFarmPost-$version.zip"
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
    Remove-Item $staging -Recurse -Force
    Write-Host "Package : $zip" -ForegroundColor Green
}

# --- 5. Optional direct copy into a profile -------------------------------
if ($DeployPath) {
    if (-not (Test-Path $DeployPath)) { New-Item -ItemType Directory -Path $DeployPath -Force | Out-Null }
    Copy-Item $dll $DeployPath -Force
    Write-Host "Copied to: $DeployPath" -ForegroundColor Green
}

Write-Host "Mod     : $(Join-Path $dist 'AutoFarmPost.dll')" -ForegroundColor Green
Write-Host 'Done.' -ForegroundColor Cyan
Write-Host ''
