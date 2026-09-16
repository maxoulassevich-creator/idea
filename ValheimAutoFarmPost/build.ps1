<#
    AutoFarmPost - сборка мода

    Запуск:
        powershell -ExecutionPolicy Bypass -File .\build.ps1
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -Install
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -ValheimPath "D:\SteamLibrary\steamapps\common\Valheim"
        powershell -ExecutionPolicy Bypass -File .\build.ps1 -ProfilePath "C:\...\profiles\Default"

    Ничего из интернета не качается, кроме одного служебного пакета для компилятора.
    BepInEx и Jotunn берутся из вашего профиля модов.

    Результат:
        dist\AutoFarmPost.dll        - сам мод
        dist\AutoFarmPost-1.0.0.zip  - пакет для "Import local mod" в менеджере модов
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
$profileDir = $null
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

# --- 3. BepInEx и Jotunn из вашего профиля модов ---------------------------
$libs = Join-Path $root 'libs'
if (-not (Test-Path $libs)) { New-Item -ItemType Directory -Path $libs | Out-Null }

function Find-FileIn([string]$rootPath, [string]$fileName) {
    if ([string]::IsNullOrWhiteSpace($rootPath) -or -not (Test-Path $rootPath)) { return $null }

    $hits = Get-ChildItem -LiteralPath $rootPath -Filter $fileName -Recurse -File -ErrorAction SilentlyContinue
    if (-not $hits) { return $null }

    # сначала то, что лежит в папках Valheim, потом самое свежее
    $ordered = @($hits | Sort-Object -Property @{ Expression = { if ($_.FullName -like '*Valheim*') { 0 } else { 1 } } }, @{ Expression = { $_.LastWriteTime }; Descending = $true })
    return $ordered[0].FullName
}

$needed = @('BepInEx.dll', '0Harmony.dll', 'Jotunn.dll')
$haveAll = $true
foreach ($n in $needed) { if (-not (Test-Path (Join-Path $libs $n))) { $haveAll = $false } }

if (-not $haveAll) {
    Write-Host 'Ищу BepInEx и Jotunn в профилях модов (несколько секунд)...'

    $searchRoots = New-Object System.Collections.Generic.List[string]
    if ($ProfilePath -and (Test-Path $ProfilePath)) { $searchRoots.Add($ProfilePath) }

    $patterns = @(
        (Join-Path $env:APPDATA 'Thunderstore Mod Manager\DataFolder\Valheim'),
        (Join-Path $env:APPDATA 'r2modmanPlus-local\Valheim'),
        (Join-Path $env:APPDATA '*hunderstore*'),
        (Join-Path $env:LOCALAPPDATA '*hunderstore*'),
        (Join-Path $env:APPDATA '*r2modman*'),
        (Join-Path $env:LOCALAPPDATA '*r2modman*'),
        (Join-Path $ValheimPath 'BepInEx')
    )
    foreach ($pattern in $patterns) {
        foreach ($item in @(Get-Item $pattern -ErrorAction SilentlyContinue)) {
            if ($item.PSIsContainer -and -not $searchRoots.Contains($item.FullName)) {
                $searchRoots.Add($item.FullName)
            }
        }
    }

    $bepPath = $null
    foreach ($r in $searchRoots) {
        $bepPath = Find-FileIn $r 'BepInEx.dll'
        if ($bepPath) { break }
    }

    if (-not $bepPath) {
        Write-Host ''
        Write-Host 'ОШИБКА: не нашёл BepInEx.' -ForegroundColor Red
        Write-Host 'В менеджере модов в нужном профиле должны стоять BepInExPack Valheim и Jotunn.'
        Write-Host 'Если они стоят, укажите папку профиля явно:'
        Write-Host '   (менеджер -> Settings -> Browse profile folder, скопировать путь)'
        Write-Host '   powershell -ExecutionPolicy Bypass -File .\build.ps1 -ProfilePath "<папка профиля>"'
        Write-Host ''
        Write-Host 'Или скопируйте BepInEx.dll, 0Harmony.dll и Jotunn.dll вручную в папку:'
        Write-Host "   $libs"
        exit 1
    }

    $coreDir = Split-Path $bepPath -Parent
    $profileDir = Split-Path (Split-Path $coreDir -Parent) -Parent
    Write-Host "Профиль    : $profileDir"
    Copy-Item $bepPath $libs -Force

    $harmony = Join-Path $coreDir '0Harmony.dll'
    if (-not (Test-Path $harmony)) { $harmony = Find-FileIn $profileDir '0Harmony.dll' }
    if (-not $harmony) {
        Write-Host 'ОШИБКА: рядом с BepInEx.dll нет 0Harmony.dll. Переустановите BepInExPack Valheim.' -ForegroundColor Red
        exit 1
    }
    Copy-Item $harmony $libs -Force

    $jotunn = Find-FileIn $profileDir 'Jotunn.dll'
    if (-not $jotunn) {
        foreach ($r in $searchRoots) {
            $jotunn = Find-FileIn $r 'Jotunn.dll'
            if ($jotunn) { break }
        }
    }
    if (-not $jotunn) {
        Write-Host ''
        Write-Host 'ОШИБКА: не нашёл Jotunn.dll.' -ForegroundColor Red
        Write-Host 'Поставьте мод Jotunn (автор ValheimModding) в тот же профиль и запустите скрипт снова.'
        exit 1
    }
    Copy-Item $jotunn $libs -Force
    Write-Host "Jotunn     : $jotunn"
}

Write-Host "Библиотеки : $libs"

# --- 4. Сборка -------------------------------------------------------------
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

# --- 5. Пакет для менеджера модов -----------------------------------------
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

# --- 6. Установка в профиль (ключ -Install) --------------------------------
$installedTo = $null
if ($Install) {
    $base = $null
    if ($ProfilePath) { $base = $ProfilePath }
    elseif ($profileDir) { $base = $profileDir }

    if ($base) {
        $target = Join-Path (Join-Path $base 'BepInEx\plugins') 'AutoFarmPost'
        if (-not (Test-Path $target)) { New-Item -ItemType Directory -Path $target -Force | Out-Null }
        Copy-Item $dll $target -Force
        $installedTo = $target
    } else {
        Write-Host 'Не понял, в какой профиль ставить: укажите -ProfilePath "<папка профиля>".' -ForegroundColor Yellow
        Write-Host '(папка libs уже заполнена, поиск профиля в этот раз не выполнялся)' -ForegroundColor Yellow
    }
}

# --- 7. Итог ---------------------------------------------------------------
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
