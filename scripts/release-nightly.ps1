[CmdletBinding()]
param([switch]$AllowDirty)
$ErrorActionPreference = 'Stop'
$ProjectRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Import-Module (Join-Path $ProjectRoot 'cameraunlock-core\powershell\NightlyRelease.psm1') -Force

$csprojPath = Join-Path $ProjectRoot 'src\BluePrinceHeadTracking\BluePrinceHeadTracking.csproj'
$match = Select-String -Path $csprojPath -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
if (-not $match) {
    throw "Could not extract <Version> from $csprojPath"
}
$version = $match.Matches[0].Groups[1].Value

# -NoNexusZip: this mod is installer-only. Vortex ships no Blue Prince extension,
# and a BepInEx payload has to land at the game root, which a manager cannot reach
# - so the packager makes no Nexus ZIP and the default "missing Nexus ZIP is
# fatal" would fail every nightly.
Publish-NightlyBuild `
    -NoNexusZip `
    -ModId 'blue-prince' `
    -ModName 'BluePrinceHeadTracking' `
    -Version $version `
    -ProjectRoot $ProjectRoot `
    -BuildCommand 'pixi run build' `
    -AllowDirty:$AllowDirty
