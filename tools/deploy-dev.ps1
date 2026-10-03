# Builds, packages, and junctions the packaged mod plus the dev harness into RimWorld/Mods.
param(
    [string]$RimWorldDir = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [switch]$SkipBuild
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $SkipBuild) {
    dotnet build (Join-Path $root 'Source') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
}
& (Join-Path $PSScriptRoot 'package.ps1')
$mods = Join-Path $RimWorldDir 'Mods'

function Set-Junction([string]$name, [string]$target) {
    $link = Join-Path $mods $name
    if (Test-Path $link) {
        $item = Get-Item $link -Force
        if ($item.LinkType -ne 'Junction') { throw "$link exists and is not a junction; remove it manually." }
        return
    }
    New-Item -ItemType Junction -Path $link -Target $target | Out-Null
    Write-Output "Linked $link -> $target"
}

Set-Junction 'DeathrestUnbound' (Join-Path $root 'dist\DeathrestUnbound')
if (Test-Path (Join-Path $root 'tools\DevHarness\About\About.xml')) {
    Set-Junction 'DeathrestUnboundDevHarness' (Join-Path $root 'tools\DevHarness')
}
