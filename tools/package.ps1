# Copies only shipping content into dist/DeathrestUnbound and validates it.
param([string]$OutDir = 'dist')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root "$OutDir\DeathrestUnbound"

# Steam writes About/PublishedFileId.txt into this folder after the first
# successful Workshop upload, so later uploads update the same item instead
# of creating a new one. It is never checked into the repo, so a wipe-and-
# recopy here would silently delete it -- preserve it across repackages.
$publishedFileId = Join-Path $target 'About\PublishedFileId.txt'
$publishedFileIdBackup = $null
if (Test-Path $publishedFileId) { $publishedFileIdBackup = Get-Content $publishedFileId -Raw }

if (Test-Path $target) { Remove-Item -Recurse -Force $target }
New-Item -ItemType Directory -Force $target | Out-Null

$include = @('About', '1.6', 'Languages', 'Textures', 'Sounds', 'LoadFolders.xml', 'LICENSE')
foreach ($item in $include) {
    $src = Join-Path $root $item
    if (Test-Path $src) { Copy-Item $src -Destination $target -Recurse -Force }
}

if ($publishedFileIdBackup) { Set-Content -Path $publishedFileId -Value $publishedFileIdBackup -NoNewline }

if (-not (Test-Path (Join-Path $target '1.6\Assemblies\DeathrestUnbound.dll'))) { throw 'DeathrestUnbound.dll missing: build first.' }
$allowed = @('DeathrestUnbound.dll')
$bad = Get-ChildItem $target -Recurse -Filter *.dll | Where-Object { $allowed -notcontains $_.Name }
if ($bad) { throw "Unexpected DLLs in package: $(($bad | ForEach-Object Name) -join ', ')" }
$forbidden = Get-ChildItem $target -Recurse -Include *.pdb, *.cs, *.csproj, *.user, *.sln, *.slnx | Select-Object -First 1
if ($forbidden) { throw "Forbidden file in package: $($forbidden.FullName)" }
Write-Output "Packaged to $target"
