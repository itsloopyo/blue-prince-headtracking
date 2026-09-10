#!/usr/bin/env pwsh
#Requires -Version 5.1
# Packaging for Blue Prince Head Tracking.
#
# There is deliberately no Nexus ZIP stage and no NEXUS_MODS.md in this repo.
# The payload is a BepInEx 6 IL2CPP loader plus plugin DLLs, all of which have to
# land at the GAME ROOT, and Vortex only deploys into the single subtree its
# per-game extension names in queryModPath. Vortex ships no Blue Prince extension
# at all (checked against its bundledPlugins set), so there is no route by which a
# mod manager could put these files where the game loads them - it would report a
# successful install and nothing would load. Do not helpfully add one back: the
# release ZIP is an installer, and the README says so.
#
# The BepInEx 6 IL2CPP vendor zip is BepInEx_UnityIL2CPP_x64.zip rather than the
# regular BepInEx_win_x64.zip the shared bundler assumes, so vendor/bepinex is
# staged here rather than by Copy-SharedBundle.

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$ProgressPreference = 'SilentlyContinue'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir

Import-Module (Join-Path $projectDir "cameraunlock-core\powershell\ReleaseWorkflow.psm1") -Force

$csprojPath = Join-Path $projectDir "src\BluePrinceHeadTracking\BluePrinceHeadTracking.csproj"
$version = Get-CsprojVersion $csprojPath

$buildOutputDir = Join-Path $projectDir "src\BluePrinceHeadTracking\bin\Release\net6.0"
$scriptsDir = Join-Path $projectDir "scripts"
$releaseDir = Join-Path $projectDir "release"

$modDlls = @("BluePrinceHeadTracking.dll", "CameraUnlock.Core.dll", "CameraUnlock.Core.Unity.dll")

Write-Host "=== Blue Prince Head Tracking - Package Release ===" -ForegroundColor Magenta
Write-Host "Version: $version" -ForegroundColor Cyan

foreach ($dll in $modDlls) {
    $dllPath = Join-Path $buildOutputDir $dll
    if (-not (Test-Path $dllPath)) { throw "Required DLL not found: $dllPath" }
}

foreach ($script in @("install.cmd", "uninstall.cmd")) {
    $scriptPath = Join-Path $scriptsDir $script
    if (-not (Test-Path $scriptPath)) { throw "Required script not found: $scriptPath" }
}

if (-not (Test-Path $releaseDir)) { New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null }

# Vendoring is the install-time source of truth; refresh with `pixi run update-deps`.
$vendorBepDir = Join-Path $projectDir "vendor\bepinex"
$vendorBepZip = Join-Path $vendorBepDir "BepInEx_UnityIL2CPP_x64.zip"
if (-not (Test-Path $vendorBepZip)) {
    throw "Bundled BepInEx vendor zip missing: $vendorBepZip. Run 'pixi run update-deps' to refresh."
}

$stagingDir = Join-Path $releaseDir "staging-installer"
if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

foreach ($script in @("install.cmd", "uninstall.cmd")) {
    Copy-Item (Join-Path $scriptsDir $script) -Destination $stagingDir -Force
    Write-Host "  $script" -ForegroundColor Green
}

# Launcher manifest: the contract lopari ingests to deploy this package. Stamp
# mod_info.version with the real release version as it is staged.
$manifestPath = Join-Path $projectDir "launcher-manifest.json"
if (-not (Test-Path $manifestPath)) { throw "Required launcher manifest not found: $manifestPath" }
$manifestJson = Get-Content $manifestPath -Raw
# Scoped to mod_info: -replace is global, so an unanchored pattern also rewrites
# any version a dependencies[] or runtime_requirements[] entry pins.
$manifestJson = $manifestJson -replace '("mod_info":\s*\{[^}]*?"version":\s*")\d+\.\d+\.\d+(")', "`${1}$version`${2}"
# Asserted against the same scoped pattern the replace used. A bare substring
# search passes when some other block already carries the release version, and
# fails on a manifest that is merely formatted differently.
if ($manifestJson -notmatch ('"mod_info":\s*\{[^}]*?"version":\s*"' + [regex]::Escape($version) + '"')) {
    throw "Failed to stamp mod_info.version in launcher-manifest.json"
}
# WriteAllText with an explicit no-BOM encoder, never Set-Content -Encoding UTF8:
# under Windows PowerShell 5.1 that writes a BOM, and the launcher's JSON parser
# rejects a manifest whose first byte is EF.
[System.IO.File]::WriteAllText(
    (Join-Path $stagingDir "launcher-manifest.json"), $manifestJson, [System.Text.UTF8Encoding]::new($false))
Write-Host "  launcher-manifest.json (version $version)" -ForegroundColor Green

$pluginsDir = Join-Path $stagingDir "plugins"
New-Item -ItemType Directory -Path $pluginsDir -Force | Out-Null
foreach ($dll in $modDlls) {
    Copy-Item (Join-Path $buildOutputDir $dll) -Destination $pluginsDir -Force
    Write-Host "  plugins/$dll" -ForegroundColor Green
}

# Vendored BepInEx (LGPL-2.1, see THIRD-PARTY-NOTICES.md) as install-time source.
$stageVendorDir = Join-Path $stagingDir "vendor\bepinex"
New-Item -ItemType Directory -Path $stageVendorDir -Force | Out-Null
foreach ($vendorFile in @("BepInEx_UnityIL2CPP_x64.zip", "LICENSE", "README.md")) {
    $src = Join-Path $vendorBepDir $vendorFile
    if (-not (Test-Path $src)) {
        throw "Required vendor file missing: $src. Run 'pixi run update-deps' to refresh."
    }
    Copy-Item $src -Destination $stageVendorDir -Force
    Write-Host "  vendor/bepinex/$vendorFile" -ForegroundColor Green
}

# install.cmd and uninstall.cmd resolve the game through shared/find-game.ps1 on
# every run, so a ZIP without shared/ fails on startup for every user.
Copy-SharedBundle -StagingDir $stagingDir -CoreRoot (Join-Path $projectDir 'cameraunlock-core')

foreach ($doc in @("README.md", "LICENSE", "CHANGELOG.md", "THIRD-PARTY-NOTICES.md")) {
    $docPath = Join-Path $projectDir $doc
    if (-not (Test-Path $docPath)) {
        throw "Required document not found: $doc. Every published ZIP is a binary distribution and must carry it."
    }
    Copy-Item $docPath -Destination $stagingDir -Force
    Write-Host "  $doc" -ForegroundColor Green
}

$zipName = "BluePrinceHeadTracking-v$version-installer.zip"
$zipPath = Join-Path $releaseDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Push-Location $stagingDir
try {
    Compress-Archive -Path ".\*" -DestinationPath $zipPath -Force
} finally {
    Pop-Location
}
Remove-Item -Recurse -Force $stagingDir

$zipSize = (Get-Item $zipPath).Length / 1KB
Write-Host ""
Write-Host ("=== Package Complete: $zipPath ({0:N1} KB) ===" -f $zipSize) -ForegroundColor Magenta

Write-Output $zipPath
