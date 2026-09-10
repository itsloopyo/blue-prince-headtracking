#!/usr/bin/env pwsh
#Requires -Version 5.1
<#
.SYNOPSIS
    Automated release workflow for Blue Prince Head Tracking mod.

.DESCRIPTION
    This script:
    1. Updates version in csproj and plugin source
    2. Builds the release configuration through the pixi task chain
    3. Commits all changes
    4. Creates and pushes a git tag to trigger CI release

.PARAMETER Version
    The version to release (e.g., "1.0.0", "1.2.3")

.EXAMPLE
    pixi run release 1.0.0

.NOTES
    Run via: pixi run release <version>
#>
param(
    [Parameter(Position=0)]
    [string]$Version = "",
    # Ship a release even when there are no user-facing commits since the
    # last tag (writes a maintenance changelog entry instead of aborting).
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# THIRD-PARTY-NOTICES.md names the cameraunlock-core commit compiled into the
# release ZIPs, and bumping the submodule does not touch it. Packaging refuses
# to ship that mismatch, so a bump with no notices edit stopped the release
# here, or in CI once the tag had already been pushed. Re-sync it and let this
# release carry the correction.
$noticesRoot = Split-Path -Parent $PSScriptRoot
& git -C $noticesRoot diff --quiet -- THIRD-PARTY-NOTICES.md
if ($LASTEXITCODE -ne 0) { throw "THIRD-PARTY-NOTICES.md has uncommitted edits. Commit or discard them, then re-run." }
& (Join-Path $noticesRoot 'cameraunlock-core\scripts\sync-core-notices.ps1') -Repo $noticesRoot
if ($LASTEXITCODE -ne 0) { throw "sync-core-notices.ps1 exited $LASTEXITCODE - fix THIRD-PARTY-NOTICES.md before releasing." }
& git -C $noticesRoot diff --quiet -- THIRD-PARTY-NOTICES.md
if ($LASTEXITCODE -ne 0) {
    & git -C $noticesRoot commit -q -m 'chore: record the cameraunlock-core commit this build compiles' -- THIRD-PARTY-NOTICES.md
    if ($LASTEXITCODE -ne 0) { throw "Could not commit the re-synced THIRD-PARTY-NOTICES.md." }
    Write-Host 'THIRD-PARTY-NOTICES.md re-synced to the pinned cameraunlock-core commit.' -ForegroundColor Yellow
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectDir = Split-Path -Parent $scriptDir
$csprojPath = Join-Path $projectDir "src\BluePrinceHeadTracking\BluePrinceHeadTracking.csproj"

Import-Module (Join-Path $projectDir "cameraunlock-core\powershell\ReleaseWorkflow.psm1") -Force

# Mirrors New-ChangelogFromCommits' insertion so a -Force maintenance entry
# lands in the same place with the same shape.
function Add-MaintenanceChangelogEntry {
    param([string]$Path, [string]$NewVersion)
    $date = Get-Date -Format 'yyyy-MM-dd'
    $entry = "## [$NewVersion] - $date`n`n### Changed`n`n- Maintenance release (no user-facing changes).`n`n"
    $changelog = Get-Content $Path -Raw
    if ($changelog -match '(?s)(# Changelog.*?)(## \[)') {
        $changelog = $changelog -replace '(?s)(# Changelog.*?\n\n)', "`$1$entry"
    } else {
        $changelog = $changelog -replace '(?s)(# Changelog.*?\n)', "`$1$entry"
    }
    $changelog = $changelog.TrimEnd() + "`n"
    [System.IO.File]::WriteAllText($Path, $changelog, [System.Text.UTF8Encoding]::new($false))
}

Write-Host "=== Blue Prince Head Tracking Release ===" -ForegroundColor Cyan
Write-Host ""

$currentVersion = Get-CsprojVersion $csprojPath

# If no version provided, show current and exit
if ([string]::IsNullOrWhiteSpace($Version)) {
    Write-Host "Current version: " -NoNewline -ForegroundColor Yellow
    Write-Host $currentVersion -ForegroundColor White
    Write-Host ""
    Write-Host "Usage: " -NoNewline -ForegroundColor Yellow
    Write-Host "pixi run release <major|minor|patch|nightly|X.Y.Z>" -ForegroundColor White
    Write-Host ""
    Write-Host "Example: " -NoNewline -ForegroundColor Yellow
    Write-Host "pixi run release patch" -ForegroundColor White
    exit 0
}

if ($Version -eq 'nightly') {
    & (Join-Path $PSScriptRoot 'release-nightly.ps1')
    exit $LASTEXITCODE
}

# Resolve major/minor/patch into a concrete version (or accept literal X.Y.Z)
try {
    $Version = Resolve-ReleaseVersion -Argument $Version -CurrentVersion $currentVersion
} catch {
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

$tagName = "v$Version"

# Releases are semver-only; a prerelease suffix would produce a tag the
# release workflow and the launcher manifest cannot both agree on.
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "Error: Resolved version '$Version' is not a bare X.Y.Z semver" -ForegroundColor Red
    exit 1
}

# Check if we're on main branch
$currentBranch = git rev-parse --abbrev-ref HEAD
if ($currentBranch -ne "main") {
    Write-Host "Error: Must be on 'main' branch to release (currently on '$currentBranch')" -ForegroundColor Red
    exit 1
}

# Check for uncommitted changes
$status = git status --porcelain
if ($status) {
    Write-Host "Error: Working directory has uncommitted changes" -ForegroundColor Red
    Write-Host $status -ForegroundColor Gray
    Write-Host "Please commit or stash changes before releasing" -ForegroundColor Yellow
    exit 1
}

# Check if tag already exists
$existingTag = git tag -l $tagName
if ($existingTag) {
    Write-Host "Error: Tag '$tagName' already exists" -ForegroundColor Red
    exit 1
}

Write-Host "Current version: $currentVersion" -ForegroundColor Gray
Write-Host "New version:     $Version" -ForegroundColor Green
Write-Host ""

# Step 1: generate CHANGELOG from commits since last tag. This is the gate
# that aborts when there are no user-facing commits, so run it BEFORE
# mutating any version files - a failure here then leaves a clean tree
# instead of stranding a half-applied version bump with no tag.
Write-Host "Generating CHANGELOG from commits..." -ForegroundColor Cyan
$changelogPath = Join-Path $projectDir "CHANGELOG.md"
# No 2>$null: with $ErrorActionPreference = 'Stop', redirecting a native
# command's stderr turns any line git writes there into a terminating error.
$hasExistingTags = git tag -l
if (-not $hasExistingTags) {
    # First release - write a basic changelog entry
    $date = Get-Date -Format 'yyyy-MM-dd'
    $firstEntry = "# Changelog`n`n## [$Version] - $date`n`nFirst release.`n"
    [System.IO.File]::WriteAllText($changelogPath, $firstEntry + "`r`n", [System.Text.UTF8Encoding]::new($false))
    Write-Host "  First release - wrote initial CHANGELOG entry" -ForegroundColor Gray
} else {
    try {
        $changelogArgs = @{
            ChangelogPath = $changelogPath
            Version = $Version
            ArtifactPaths = @(
                "src/BluePrinceHeadTracking/",
                "cameraunlock-core",
                "scripts/install.cmd",
                "scripts/uninstall.cmd"
            )
        }
        New-ChangelogFromCommits @changelogArgs
    } catch {
        if (-not $Force) {
            Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
            Write-Host "No user-facing changes to release. Re-run with -Force for a maintenance release." -ForegroundColor Yellow
            exit 1
        }
        Write-Host "No user-facing commits since last tag - writing maintenance entry (-Force)." -ForegroundColor Yellow
        Add-MaintenanceChangelogEntry -Path $changelogPath -NewVersion $Version
    }
}

# Step 2: Update version in csproj
Write-Host "Updating version to $Version..." -ForegroundColor Cyan
Set-CsprojVersion $csprojPath $Version

# Step 3: Update version in plugin source
$pluginPath = Join-Path $projectDir "src\BluePrinceHeadTracking\Core\HeadTrackingPlugin.cs"
$pluginContent = Get-Content $pluginPath -Raw
$pluginContent = $pluginContent -replace 'PluginVersion = "[^"]+"', "PluginVersion = `"$Version`""
[System.IO.File]::WriteAllText($pluginPath, $pluginContent, [System.Text.UTF8Encoding]::new($false))
Write-Host "  Updated HeadTrackingPlugin.cs" -ForegroundColor Gray

# Step 2b: Update MOD_VERSION in install.cmd CONFIG BLOCK so the state file
# written at install time records the correct version. Preserve CRLF line
# endings (.cmd files require CRLF on Windows).
$installCmdPath = Join-Path $projectDir "scripts\install.cmd"
if (Test-Path $installCmdPath) {
    $installCmdBytes = [System.IO.File]::ReadAllBytes($installCmdPath)
    $installCmdContent = [System.Text.Encoding]::UTF8.GetString($installCmdBytes)
    $updatedInstallCmd = $installCmdContent -replace 'set "MOD_VERSION=[^"]+"', "set `"MOD_VERSION=$Version`""
    if ($updatedInstallCmd -ne $installCmdContent) {
        [System.IO.File]::WriteAllText($installCmdPath, $updatedInstallCmd, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  Updated install.cmd MOD_VERSION" -ForegroundColor Gray
    }
}

# Step 2c: Update mod_info.version in the launcher manifest so
# launcher-manifest.json never drifts from the released package version.
$manifestPath = Join-Path $projectDir "launcher-manifest.json"
if (Test-Path $manifestPath) {
    $manifestContent = Get-Content $manifestPath -Raw
    # Only mod_info.version is semver-shaped; loader/strategy carry no semver.
    $updatedManifest = $manifestContent -replace '("mod_info":\s*\{[^}]*?"version":\s*")\d+\.\d+\.\d+(")', "`${1}$Version`${2}"
    if ($updatedManifest -ne $manifestContent) {
        [System.IO.File]::WriteAllText($manifestPath, $updatedManifest, [System.Text.UTF8Encoding]::new($false))
        Write-Host "  Updated launcher-manifest.json version" -ForegroundColor Gray
    }
}

# Step 2d: Keep the pixi workspace version in step with the canonical csproj
# version so `pixi task list` and the release never disagree.
$pixiTomlPath = Join-Path $projectDir "pixi.toml"
$pixiContent = Get-Content $pixiTomlPath -Raw
$updatedPixi = $pixiContent -replace '(?m)^version = "\d+\.\d+\.\d+"', "version = `"$Version`""
if ($updatedPixi -ne $pixiContent) {
    [System.IO.File]::WriteAllText($pixiTomlPath, $updatedPixi, [System.Text.UTF8Encoding]::new($false))
    Write-Host "  Updated pixi.toml version" -ForegroundColor Gray
}

# Step 4: Build and package
# `pixi run package`, never a bare `dotnet build`: the pixi chain is what runs
# restore -> setup-libs, so a release cut from a clean checkout compiles
# against the same references CI uses instead of whatever is stale in libs/.
# Packaging rather than only building, because everything the packager asserts -
# the vendored loader, the shared bundle, the manifest, the docs - is checked
# nowhere else, and steps 6 and 7 push a tag that cannot be taken back.
Write-Host "Building and packaging release..." -ForegroundColor Cyan
Push-Location $projectDir
pixi run package
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build or packaging failed!" -ForegroundColor Red
    Pop-Location
    exit 1
}

# The two content gates over what was just staged. build.yml runs these too, but
# it skips the release commit and does not run at all for a docs-only change, so
# a release that never passed through it would otherwise reach the tag ungated.
foreach ($gate in @("validate-manifest", "validate-notices")) {
    Write-Host "Running $gate..." -ForegroundColor Cyan
    pixi run $gate
    if ($LASTEXITCODE -ne 0) {
        Write-Host "$gate failed!" -ForegroundColor Red
        Pop-Location
        exit 1
    }
}
Pop-Location

# Step 5: Commit
Write-Host "Committing changes..." -ForegroundColor Cyan
git add $csprojPath
git add $pluginPath
git add $installCmdPath
git add $manifestPath
git add $pixiTomlPath
git add $changelogPath
git commit -m "Release v$Version"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Commit failed!" -ForegroundColor Red
    exit 1
}

# Step 6: Create tag
Write-Host "Creating tag $tagName..." -ForegroundColor Cyan
git tag -a $tagName -m "Release $tagName"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Tag creation failed!" -ForegroundColor Red
    exit 1
}

# Step 7: Push. A push that fails leaves the tag local and CI never runs, so
# report it here rather than printing the success banner over a silent failure.
Write-Host "Pushing to GitHub..." -ForegroundColor Cyan
git push origin main
if ($LASTEXITCODE -ne 0) {
    Write-Host "Push of main failed!" -ForegroundColor Red
    exit 1
}
git push origin $tagName
if ($LASTEXITCODE -ne 0) {
    Write-Host "Push of tag $tagName failed!" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "Release $tagName initiated!" -ForegroundColor Green
Write-Host ""
Write-Host "The GitHub Actions release workflow will now:" -ForegroundColor Yellow
Write-Host "  - Build and package the mod" -ForegroundColor White
Write-Host "  - Create GitHub release with artifacts" -ForegroundColor White
Write-Host ""
Write-Host "Watch progress at:" -ForegroundColor Yellow
Write-Host "  https://github.com/itsloopyo/blue-prince-headtracking/actions" -ForegroundColor Cyan
