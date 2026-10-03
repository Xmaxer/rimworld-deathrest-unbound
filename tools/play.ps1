# Deploys the mod and launches RimWorld interactively for play testing, fully isolated from your real game:
#   - a separate, persistent save-data profile (tools/.dev-profile): your real saves, settings and mod list are
#     never touched, and a test colony you save there survives re-launches;
#   - only Harmony, the DLCs, VEF + VRE Sanguophage (unless -NoVre), this mod and the dev harness are active;
#   - dev mode on, and all sound and music muted.
#
# Start a NEW colony (any scenario/storyteller) and the "playground" scenario builds a powered deathrest hall next
# to your colonists: three caskets with sanguophage owners, every deathrest building type (vanilla and VRE), a
# power plant with batteries, and hemogen packs. Loaded saves are left alone. -NoPlayground skips it.
param(
    [string]$RimWorldDir = 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld',
    [switch]$SkipDeploy,
    [switch]$NoPlayground,
    [switch]$NoVre,
    [switch]$ResetProfile
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'common.ps1')
$root = Split-Path -Parent $PSScriptRoot

if (Get-Process RimWorldWin64 -ErrorAction SilentlyContinue) { Write-Output 'RimWorld is already running.'; exit 2 }
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) {
    Write-Output 'WARNING: Steam is not running. Harmony and VRE are Workshop mods and may fail to load.'
}
if (-not $SkipDeploy) { & (Join-Path $PSScriptRoot 'deploy-dev.ps1') -RimWorldDir $RimWorldDir }

$profileDir = Join-Path $root 'tools\.dev-profile'
if ($ResetProfile -and (Test-Path $profileDir)) { Remove-Item -Recurse -Force $profileDir; Write-Output 'Dev profile reset.' }
$configDir = Join-Path $profileDir 'Config'
New-Item -ItemType Directory -Force $configDir | Out-Null
# The mod list is rewritten every launch so -NoVre always takes effect; settings and saves persist.
Write-ModsConfig (Join-Path $configDir 'ModsConfig.xml') $RimWorldDir (-not $NoVre)
$prefs = Join-Path $configDir 'Prefs.xml'
if (-not (Test-Path $prefs)) { Write-Prefs $prefs $true }
Set-Muted $prefs

$harnessDir = Join-Path $profileDir 'DeathrestHarness'
$request = Join-Path $harnessDir 'request.txt'
if ($NoPlayground) {
    Remove-Item -Force $request -ErrorAction SilentlyContinue
    Write-Output 'Playground disabled: new colonies start untouched.'
} else {
    New-Item -ItemType Directory -Force $harnessDir | Out-Null
    Set-Content -Encoding ASCII $request 'playground'
    Write-Output 'Playground requested: start a NEW colony to get the deathrest hall.'
}

Write-Output "Launching RimWorld with the isolated dev profile: $profileDir"
Start-Process -FilePath (Join-Path $RimWorldDir 'RimWorldWin64.exe') -ArgumentList "-savedatafolder=$profileDir"
