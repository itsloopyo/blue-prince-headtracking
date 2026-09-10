#!/usr/bin/env pwsh
#Requires -Version 5.1
# Bump the vendored BepInEx 6 IL2CPP build to the latest published on
# builds.bepinex.dev. Manual: the dev runs this, reviews the diff, commits.
# The build task does not depend on it and CI never runs it.
#
# BepInEx 6 IL2CPP is bleeding edge and is only published on the BepInEx CI
# build server, never on GitHub releases, so Update-VendoredLoader's GitHub
# mode does not apply. We resolve the newest IL2CPP-win-x64 build off the
# project index and hand that URL to Update-VendoredLoader's DirectUrl mode,
# which owns the download, SHA-256, idempotency, LICENSE and README.

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ProgressPreference    = 'SilentlyContinue'

$scriptDir  = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir

Import-Module (Join-Path $projectDir 'cameraunlock-core/powershell/ModLoaderSetup.psm1') -Force

$out = Join-Path $projectDir 'vendor/bepinex'

Write-Host "Discovering latest BepInEx 6 IL2CPP build from builds.bepinex.dev..." -ForegroundColor Cyan
$idx = Invoke-WebRequest -Uri 'https://builds.bepinex.dev/projects/bepinex_be' -UseBasicParsing `
    -Headers @{ "User-Agent" = "CameraUnlock-HeadTracking" } -TimeoutSec 30
# Each build on the index is one <div class="artifact-item"> carrying its id, its
# build date and its download links, so the date is read from the same block as
# the link rather than paired up by position.
$items = [regex]::Matches(
    $idx.Content,
    '(?s)<div class="artifact-item">.*?(?=<div class="artifact-item">|\z)')
$candidates = foreach ($item in $items) {
    $link = [regex]::Match(
        $item.Value,
        'href="(/projects/bepinex_be/(\d+)/BepInEx-Unity\.IL2CPP-win-x64-6\.0\.0-be\.\d+(?:%2B|\+)([a-f0-9]+)\.zip)"')
    $date = [regex]::Match($item.Value, '<span class="build-date">([^<]+)</span>')
    if ($link.Success -and $date.Success) {
        [pscustomobject]@{
            Build  = [int]$link.Groups[2].Value
            Path   = $link.Groups[1].Value
            Commit = $link.Groups[3].Value
            Date   = ([datetime]$date.Groups[1].Value).ToUniversalTime()
        }
    }
}
if (-not $candidates) { throw "Could not find any IL2CPP-win-x64 builds on builds.bepinex.dev" }

# Minimum age. A loader published this morning goes into the installer ZIP every
# user extracts, so it has to have been in front of other people for a while
# first. Nothing newer than this is considered, however high its build number.
$MinimumAgeDays = 14
$cutoff = (Get-Date).ToUniversalTime().AddDays(-$MinimumAgeDays)
$eligible = $candidates | Where-Object { $_.Date -lt $cutoff }
if (-not $eligible) {
    $newest = $candidates | Sort-Object -Property Build -Descending | Select-Object -First 1
    throw ("No BepInEx IL2CPP build is at least $MinimumAgeDays days old. The newest is " +
        "$($newest.Build), published $($newest.Date.ToString('yyyy-MM-dd')).")
}
$best = $eligible | Sort-Object -Property Build -Descending | Select-Object -First 1
# @(...) around the pipeline: Set-StrictMode -Version Latest makes .Count a
# terminating error on $null and on a single object, and how many builds are
# newer than the winner depends entirely on BepInEx's release cadence.
$skipped = @($candidates | Where-Object { $_.Build -gt $best.Build }).Count
if ($skipped -gt 0) {
    Write-Host ("  skipping $skipped build(s) newer than $MinimumAgeDays days") -ForegroundColor Yellow
}

$meta = Update-VendoredLoader `
    -Name 'bepinex' `
    -OutputDir $out `
    -OutputFileName 'BepInEx_UnityIL2CPP_x64.zip' `
    -DirectUrl "https://builds.bepinex.dev$($best.Path)" `
    -LicenseUrl 'https://raw.githubusercontent.com/BepInEx/BepInEx/master/LICENSE'

# DirectUrl mode has no tag or commit to report, so the module leaves both out
# of the README. Re-insert them from the build index, plus the on-disk name
# install.cmd hardcodes (the module records the upstream asset name instead).
$readmePath = Join-Path $out 'README.md'
$lines = (Get-Content -LiteralPath $readmePath -Raw).TrimEnd("`r", "`n") -split "`r?`n" |
    Where-Object { $_ -notmatch '^- (Vendored as|Tag|Commit):' }
$patched = foreach ($line in $lines) {
    $line
    if ($line -match '^- Asset:') {
        "- Vendored as: ``BepInEx_UnityIL2CPP_x64.zip``"
        "- Tag: ``6.0.0-be.$($best.Build)``"
        "- Commit: ``$($best.Commit)``"
    }
}
# Explicit no-BOM encoder: Set-Content -Encoding UTF8 writes a BOM under Windows
# PowerShell 5.1, and this file is staged into the release ZIP as-is.
[System.IO.File]::WriteAllText(
    $readmePath, ($patched -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))

# The vendored loader is what runs; the csproj PackageReference is what the mod
# compiles against. They are independent versions with nothing else comparing
# them, so a bump that moves only the vendored side widens the gap silently.
$csprojPath = Join-Path $projectDir 'src/BluePrinceHeadTracking/BluePrinceHeadTracking.csproj'
$pinned = [regex]::Match(
    (Get-Content -LiteralPath $csprojPath -Raw),
    'Include="BepInEx\.Unity\.IL2CPP" Version="6\.0\.0-be\.(\d+)"')

Write-Host ""
Write-Host "vendor/bepinex at build $($best.Build), published $($best.Date.ToString('yyyy-MM-dd')) (sha $($meta.Sha256.Substring(0,12))...). Review and commit." -ForegroundColor Green
if ($pinned.Success -and [int]$pinned.Groups[1].Value -ne $best.Build) {
    Write-Warning ("The mod compiles against BepInEx 6.0.0-be.$($pinned.Groups[1].Value) and now ships " +
        "build $($best.Build). Bump the PackageReference in $csprojPath and the pinned package paths in " +
        "scripts/setup-libs.ps1 to match, or the plugin is built against a surface it does not run on.")
}
