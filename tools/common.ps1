# Shared helpers for smoke-test.ps1 and play.ps1 (dot-sourced).

# Writes an isolated ModsConfig.xml: Harmony, the DLCs, optionally VEF + VRE Sanguophage, this mod and the harness.
function Write-ModsConfig([string]$path, [string]$rimWorldDir, [bool]$withVre) {
    $version = (Get-Content (Join-Path $rimWorldDir 'Version.txt') -Raw).Trim()
    $vre = ''
    if ($withVre) {
        $vre = @"
    <li>oskarpotocki.vanillafactionsexpanded.core</li>
    <li>vanillaracesexpanded.sanguophage</li>
"@
    }
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<ModsConfigData>
  <version>$version</version>
  <activeMods>
    <li>brrainz.harmony</li>
    <li>ludeon.rimworld</li>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.ideology</li>
    <li>ludeon.rimworld.biotech</li>
    <li>ludeon.rimworld.odyssey</li>
$vre    <li>xmaxer.deathrestunbound</li>
    <li>xmaxer.deathrestunbound.devharness</li>
  </activeMods>
  <knownExpansions>
    <li>ludeon.rimworld.royalty</li>
    <li>ludeon.rimworld.ideology</li>
    <li>ludeon.rimworld.biotech</li>
    <li>ludeon.rimworld.odyssey</li>
  </knownExpansions>
</ModsConfigData>
"@
    Set-Content -Encoding UTF8 $path $xml
}

# Writes a Prefs.xml with dev mode on and all sound off.
function Write-Prefs([string]$path, [bool]$pauseOnError) {
    $xml = @"
<?xml version="1.0" encoding="utf-8"?>
<PlayerPrefs>
  <devMode>True</devMode>
  <runInBackground>True</runInBackground>
  <pauseOnError>$pauseOnError</pauseOnError>
  <pauseOnLoad>False</pauseOnLoad>
  <resetModsConfigOnCrash>False</resetModsConfigOnCrash>
</PlayerPrefs>
"@
    Set-Content -Encoding UTF8 $path $xml
    Set-Muted $path
}

# Sets every RimWorld volume (master, game, music, ambient, UI) to 0 in a Prefs.xml, keeping its other settings.
# Called on every launch so test runs and play tests are always silent, even with an older profile.
function Set-Muted([string]$path) {
    [xml]$doc = Get-Content $path -Raw
    $prefs = $doc.DocumentElement
    foreach ($name in 'volumeMaster', 'volumeGame', 'volumeMusic', 'volumeAmbient', 'volumeUI') {
        $node = $prefs.SelectSingleNode($name)
        if (-not $node) {
            $node = $doc.CreateElement($name)
            [void]$prefs.AppendChild($node)
        }
        $node.InnerText = '0'
    }
    $doc.Save($path)
}
