#requires -Version 5.1
<#
.SYNOPSIS
    Обновляет вендоренный корпус тестов System.Text.Json.

.DESCRIPTION
    Корпус (JsonGoddess.GeneratorTests/Stj/corpus) - дословные копии файлов из
    dotnet/runtime, и правится он только этим скриптом.

    Вендоринг без зафиксированного коммита через год не даёт ответить, от какой
    версии эталона мы отстали, поэтому -Ref обязателен и пишется в PINNED.md
    рядом с файлами. Без -Ref скрипт берёт то, что уже записано, - то есть
    перекачивает ровно тот же коммит, а не «последний».

    После обновления прогнать JsonGoddess.GeneratorTests: отчёт
    stj-corpus-report.md пересчитается сам, и разница в нём и будет ответом на
    вопрос, что у эталона изменилось.

.PARAMETER Ref
    Тег или SHA коммита в dotnet/runtime. По умолчанию - записанный в PINNED.md.

.PARAMETER Add
    Имена дополнительных файлов из каталога TestClasses, которых в корпусе ещё
    нет.

.EXAMPLE
    ./eng/sync-stj-tests.ps1
    ./eng/sync-stj-tests.ps1 -Ref v11.0.0
    ./eng/sync-stj-tests.ps1 -Add TestClasses.Polymorphic.cs
#>
[CmdletBinding()]
param(
    [string] $Ref,
    [string[]] $Add = @()
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$stj = Join-Path $root 'JsonGoddess.GeneratorTests/Stj'
$corpus = Join-Path $stj 'corpus'
$pinned = Join-Path $stj 'PINNED.md'
$source = 'src/libraries/System.Text.Json/tests/Common/TestClasses'

if (-not (Test-Path $pinned)) {
    throw "Не найден $pinned - без него неизвестно, какой коммит вендорен."
}

# -Encoding обязательно: Windows PowerShell 5.1 без него читает файл в кодировке
# системы, и весь русский текст PINNED.md уезжает в мусор при первой же записи
$text = Get-Content -Raw -Path $pinned -Encoding UTF8

$refWasGiven = [bool] $Ref

if (-not $Ref) {
    $match = [regex]::Match($text, 'commit:\s*([0-9a-f]{40})')
    if (-not $match.Success) {
        throw "В $pinned нет строки 'commit: <sha>'; передайте -Ref явно."
    }

    $Ref = $match.Groups[1].Value
    Write-Host "Коммит не задан, беру записанный: $Ref"
}

if (-not (Test-Path $corpus)) {
    New-Item -ItemType Directory -Path $corpus | Out-Null
}

$names = @(Get-ChildItem -Path $corpus -Filter '*.cs' | ForEach-Object { $_.Name })
$names += $Add
$names = $names | Sort-Object -Unique

if ($names.Count -eq 0) {
    throw "Корпус пуст и -Add не задан: качать нечего."
}

# tag -> sha: в PINNED.md обязан лежать именно коммит, иначе «зафиксировано»
# означает «зафиксировано до следующего переноса тега»
$sha = $Ref
if ($Ref -notmatch '^[0-9a-f]{40}$') {
    $resolved = Invoke-RestMethod -Uri "https://api.github.com/repos/dotnet/runtime/commits/$Ref" -Headers @{ 'User-Agent' = 'JsonGoddess' }
    $sha = $resolved.sha
    Write-Host "$Ref разрешён в $sha"
}

foreach ($name in $names) {
    $uri = "https://raw.githubusercontent.com/dotnet/runtime/$sha/$source/$name"
    $target = Join-Path $corpus $name

    Write-Host "  $name"
    Invoke-WebRequest -Uri $uri -OutFile $target -UseBasicParsing
}

$updated = [regex]::Replace($text, '(?m)^(\s*commit:\s*).*$', "`${1}$sha")

# строку tag: трогаем только тогда, когда версию назвали явно: без -Ref мы
# перекачиваем уже записанный коммит, и подменять его же тег на SHA значило бы
# терять единственное человекочитаемое имя версии
if ($refWasGiven) {
    $updated = [regex]::Replace($updated, '(?m)^(\s*tag:\s*).*$', "`${1}$Ref")
}

if ($updated -ne $text) {
    # BOM'а в файле нет, и заводить его записью не надо: -Encoding utf8 в 5.1
    # пишет именно с BOM
    [System.IO.File]::WriteAllText($pinned, $updated, (New-Object System.Text.UTF8Encoding $false))
    Write-Host "PINNED.md обновлён: $Ref / $sha"
}

Write-Host ''
Write-Host "Файлов в корпусе: $($names.Count). Теперь: dotnet test JsonGoddess.GeneratorTests"
