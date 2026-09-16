<#
    AutoFarmPost - сборка мода

    Запуск:
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -ProfilePath "C:\...\profiles\Default" -Install
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -ValheimPath "D:\SteamLibrary\steamapps\common\Valheim"

    Из интернета качается только один служебный пакет для компилятора.
    BepInEx и Jotunn берутся из вашего профиля модов.
#>
param(
    [string]$ValheimPath,
    [string]$ProfilePath,
    [switch]$Install,
    [switch]$NoPackage
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '1.0.0'
$zipName = "AutoFarmPost-$version.zip"

Write-Host ''
Write-Host '=== AutoFarmPost: сборка ===' -ForegroundColor Cyan

# --- 1. .NET SDK -----------------------------------------------------------
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host 'ОШИБКА: не найден .NET SDK.' -ForegroundColor Red
    Write-Host 'Скачайте его на https://dotnet.microsoft.com/download (кнопка "Download .NET SDK x64"),'
    Write-Host 'установите, закройте это окно, откройте новое и запустите скрипт заново.'
    exit 1
}

# --- 2. Папка с игрой ------------------------------------------------------
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
                foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
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
        Write-Host "ОШИБКА: в папке '$ValheimPath' нет Valheim." -ForegroundColor Red
        Write-Host 'Нужна папка, в которой лежит valheim.exe.'
        exit 1
    }
} else {
    $ValheimPath = Find-Valheim
    if (-not $ValheimPath) {
        Write-Host 'ОШИБКА: не нашёл Valheim автоматически.' -ForegroundColor Red
        Write-Host 'В Steam: правой кнопкой по Valheim -> Управление -> Посмотреть локальные файлы,'
        Write-Host 'скопируйте путь и запустите так:'
        Write-Host '   powershell -ExecutionPolicy Bypass -File .\build.ps1 -ValheimPath "<этот путь>"'
        exit 1
    }
}
Write-Host "Игра       : $ValheimPath"

# --- 3. Профиль модов ------------------------------------------------------
function Add-ProfileRoots([string]$start, $list) {
    if ([string]::IsNullOrWhiteSpace($start) -or -not (Test-Path $start)) { return }

    $direct = Join-Path $start 'profiles'
    if ((Test-Path $direct) -and -not $list.Contains($direct)) { $list.Add($direct) }

    foreach ($a in @(Get-ChildItem -LiteralPath $start -Directory -ErrorAction SilentlyContinue)) {
        $p1 = Join-Path $a.FullName 'profiles'
        if ((Test-Path $p1) -and -not $list.Contains($p1)) { $list.Add($p1) }

        foreach ($b in @(Get-ChildItem -LiteralPath $a.FullName -Directory -ErrorAction SilentlyContinue)) {
            $p2 = Join-Path $b.FullName 'profiles'
            if ((Test-Path $p2) -and -not $list.Contains($p2)) { $list.Add($p2) }
        }
    }
}

function Find-Profiles {
    $roots = New-Object System.Collections.Generic.List[string]
    $roots.Add((Join-Path $env:APPDATA 'Thunderstore Mod Manager'))
    $roots.Add((Join-Path $env:APPDATA 'r2modmanPlus-local'))
    foreach ($pattern in @(
        (Join-Path $env:APPDATA '*hunderstore*'),
        (Join-Path $env:LOCALAPPDATA '*hunderstore*'),
        (Join-Path $env:APPDATA '*r2modman*'),
        (Join-Path $env:LOCALAPPDATA '*r2modman*'))) {
        foreach ($item in @(Get-Item $pattern -ErrorAction SilentlyContinue)) {
            if ($item.PSIsContainer -and -not $roots.Contains($item.FullName)) { $roots.Add($item.FullName) }
        }
    }

    $profileRoots = New-Object System.Collections.Generic.List[string]
    foreach ($r in $roots) { Add-ProfileRoots $r $profileRoots }

    $profiles = New-Object System.Collections.Generic.List[object]
    foreach ($pr in $profileRoots) {
        foreach ($p in @(Get-ChildItem -LiteralPath $pr -Directory -ErrorAction SilentlyContinue)) {
            if (Test-Path (Join-Path $p.FullName 'BepInEx')) { $profiles.Add($p) }
        }
    }

    # BepInEx может быть установлен прямо в папку игры
    if (Test-Path (Join-Path $ValheimPath 'BepInEx')) {
        $profiles.Add((Get-Item -LiteralPath $ValheimPath))
    }

    # сначала профили Valheim, потом самые свежие
    return @($profiles | Sort-Object -Property @{ Expression = { if ($_.FullName -like '*Valheim*') { 0 } else { 1 } } }, @{ Expression = { $_.LastWriteTime }; Descending = $true })
}

$profileDir = $null
if ($ProfilePath) {
    if (-not (Test-Path $ProfilePath)) {
        Write-Host "ОШИБКА: папки '$ProfilePath' не существует." -ForegroundColor Red
        exit 1
    }
    if (-not (Test-Path (Join-Path $ProfilePath 'BepInEx'))) {
        Write-Host "Внимание: в '$ProfilePath' нет папки BepInEx. Проверьте, что это папка профиля." -ForegroundColor Yellow
    }
    $profileDir = $ProfilePath
} else {
    $found = Find-Profiles
    if ($found.Count -gt 0) {
        $profileDir = $found[0].FullName
        if ($found.Count -gt 1) {
            Write-Host "Профилей найдено: $($found.Count), беру самый свежий. Другой можно задать ключом -ProfilePath." -ForegroundColor DarkGray
        }
    }
}

if ($profileDir) {
    Write-Host "Профиль    : $profileDir"
} else {
    Write-Host 'Профиль    : не найден (сборке не мешает, но -Install работать не будет)' -ForegroundColor Yellow
}

# --- 4. BepInEx и Jotunn ---------------------------------------------------
$libs = Join-Path $root 'libs'
if (-not (Test-Path $libs)) { New-Item -ItemType Directory -Path $libs | Out-Null }

function Find-FileIn([string]$rootPath, [string]$fileName) {
    if ([string]::IsNullOrWhiteSpace($rootPath) -or -not (Test-Path $rootPath)) { return $null }

    $hits = Get-ChildItem -LiteralPath $rootPath -Filter $fileName -Recurse -File -ErrorAction SilentlyContinue
    if (-not $hits) { return $null }

    $ordered = @($hits | Sort-Object -Property @{ Expression = { if ($_.FullName -like '*Valheim*') { 0 } else { 1 } } }, @{ Expression = { $_.LastWriteTime }; Descending = $true })
    return $ordered[0].FullName
}

$needed = @('BepInEx.dll', '0Harmony.dll', 'Jotunn.dll')
$missing = @($needed | Where-Object { -not (Test-Path (Join-Path $libs $_)) })

if ($missing.Count -gt 0) {
    Write-Host 'Ищу BepInEx и Jotunn (несколько секунд)...'

    $searchRoots = New-Object System.Collections.Generic.List[string]
    if ($profileDir) { $searchRoots.Add($profileDir) }
    $searchRoots.Add((Join-Path $env:APPDATA 'Thunderstore Mod Manager'))
    $searchRoots.Add((Join-Path $env:APPDATA 'r2modmanPlus-local'))
    foreach ($pattern in @(
        (Join-Path $env:APPDATA '*hunderstore*'),
        (Join-Path $env:LOCALAPPDATA '*hunderstore*'),
        (Join-Path $env:APPDATA '*r2modman*'),
        (Join-Path $env:LOCALAPPDATA '*r2modman*'))) {
        foreach ($item in @(Get-Item $pattern -ErrorAction SilentlyContinue)) {
            if ($item.PSIsContainer -and -not $searchRoots.Contains($item.FullName)) { $searchRoots.Add($item.FullName) }
        }
    }
    $searchRoots.Add((Join-Path $ValheimPath 'BepInEx'))

    foreach ($file in $missing) {
        $hit = $null
        foreach ($r in $searchRoots) {
            $hit = Find-FileIn $r $file
            if ($hit) { break }
        }

        if (-not $hit) {
            Write-Host ''
            Write-Host "ОШИБКА: не нашёл $file." -ForegroundColor Red
            Write-Host 'В профиле модов должны стоять BepInExPack Valheim (denikson) и Jotunn (ValheimModding).'
            Write-Host 'Если они стоят, укажите папку профиля явно:'
            Write-Host '   (менеджер -> Settings -> Browse profile folder, скопировать путь)'
            Write-Host '   powershell -ExecutionPolicy Bypass -File .\build.ps1 -ProfilePath "<папка профиля>" -Install'
            Write-Host ''
            Write-Host "Или скопируйте BepInEx.dll, 0Harmony.dll и Jotunn.dll вручную в папку: $libs"
            exit 1
        }

        Copy-Item $hit $libs -Force
        Write-Host "  $file <- $hit" -ForegroundColor DarkGray
    }
}

Write-Host "Библиотеки : $libs"

# --- 5. Сборка -------------------------------------------------------------
$proj = Join-Path $root 'src\AutoFarmPost\AutoFarmPost.csproj'
Write-Host 'Собираю...'

& dotnet build $proj -c Release -v minimal -p:ValheimDir="$ValheimPath"
if ($LASTEXITCODE -ne 0) {
    Write-Host ''
    Write-Host 'ОШИБКА: сборка не прошла, сообщения выше.' -ForegroundColor Red
    Write-Host 'Раздел "Если сборка не проходит" в README.md подскажет, что делать.'
    exit 1
}

$dll = Join-Path $root 'src\AutoFarmPost\bin\Release\AutoFarmPost.dll'
if (-not (Test-Path $dll)) {
    Write-Host "ОШИБКА: файл $dll не появился." -ForegroundColor Red
    exit 1
}

$dist = Join-Path $root 'dist'
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory -Path $dist | Out-Null
Copy-Item $dll $dist

# --- 6. Пакет для менеджера модов -----------------------------------------
if (-not $NoPackage) {
    $staging = Join-Path $dist 'package'
    New-Item -ItemType Directory -Path $staging | Out-Null
    Copy-Item $dll $staging
    Copy-Item (Join-Path $root 'package\manifest.json') $staging
    Copy-Item (Join-Path $root 'package\icon.png') $staging
    Copy-Item (Join-Path $root 'README.md') $staging

    $zip = Join-Path $dist $zipName
    Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip -Force
    Remove-Item $staging -Recurse -Force
}

# --- 7. Установка в профиль (ключ -Install) --------------------------------
$installedTo = $null
if ($Install) {
    if (-not $profileDir) {
        Write-Host 'Не нашёл профиль: укажите -ProfilePath "<папка профиля>".' -ForegroundColor Yellow
    } else {
        $target = Join-Path (Join-Path $profileDir 'BepInEx\plugins') 'AutoFarmPost'
        if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
        Copy-Item $dll $target -Force
        $installedTo = $target
    }
}

# --- 8. Итог ---------------------------------------------------------------
Write-Host ''
Write-Host 'ГОТОВО' -ForegroundColor Green
Write-Host "  Мод   : $(Join-Path $dist 'AutoFarmPost.dll')"
if (-not $NoPackage) { Write-Host "  Пакет : $(Join-Path $dist $zipName)" }
if ($installedTo) {
    Write-Host "  Установлен в профиль: $installedTo" -ForegroundColor Green
    Write-Host '  Осталось нажать Modded в менеджере модов.'
} else {
    Write-Host ''
    Write-Host 'Дальше: менеджер модов -> Settings -> Import local mod -> выбрать zip из папки dist.'
    Write-Host 'Или запустите скрипт с ключом -Install, он сам положит мод в профиль.'
}
Write-Host ''
