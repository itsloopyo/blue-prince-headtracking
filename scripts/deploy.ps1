#!/usr/bin/env pwsh
#Requires -Version 5.1
# Copy the built plugin and its CameraUnlock dependencies into the game's
# BepInEx/plugins folder, extracting the vendored loader first if BepInEx is not
# there yet. Development helper; install.cmd is what users run.

param(
    [string]$Configuration = 'Release',
    [string]$GamePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir   = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

Import-Module (Join-Path $projectRoot 'cameraunlock-core/powershell/GamePathDetection.psm1') -Force

if (-not $GamePath) {
    $GamePath = Find-GamePath -GameId 'blue-prince'
}
if (-not $GamePath) {
    throw "Blue Prince install not found. Set BLUE_PRINCE_PATH or pass -GamePath."
}

# The game holds the plugin DLLs open, so a copy over a running game fails - and
# a deploy that fails while the next step launches the game looks exactly like a
# mod that did not change. Refuse loudly instead.
if (Get-Process -Name 'BLUE PRINCE' -ErrorAction SilentlyContinue) {
    throw "Blue Prince is running. Close it before deploying, or the plugin DLLs cannot be overwritten."
}

$plugins = Join-Path $GamePath 'BepInEx/plugins'
if (-not (Test-Path (Join-Path $GamePath 'BepInEx/core'))) {
    Write-Host "BepInEx not present - extracting the vendored loader" -ForegroundColor Cyan
    Expand-Archive -Path (Join-Path $projectRoot 'vendor/bepinex/BepInEx_UnityIL2CPP_x64.zip') `
                   -DestinationPath $GamePath -Force
}
New-Item -ItemType Directory -Path $plugins -Force | Out-Null

$buildDir = Join-Path $projectRoot "src/BluePrinceHeadTracking/bin/$Configuration/net6.0"
foreach ($name in @('BluePrinceHeadTracking.dll', 'CameraUnlock.Core.dll', 'CameraUnlock.Core.Unity.dll')) {
    $src = Join-Path $buildDir $name
    if (-not (Test-Path $src)) { throw "Build output missing: $src. Run 'pixi run build' first." }
    Copy-Item $src $plugins -Force
}

Write-Host "Deployed to $plugins" -ForegroundColor Green
