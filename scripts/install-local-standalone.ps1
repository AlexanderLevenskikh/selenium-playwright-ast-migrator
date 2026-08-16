<#
.SYNOPSIS
Builds and installs a local Windows standalone Selenium Playwright Migrator release.

.EXAMPLE
.\scripts\install-local-standalone.ps1 -Version "0.0.4-preview.3"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Version,

    [ValidateNotNullOrEmpty()]
    [string]$Runtime = "win-x64",

    [ValidateNotNullOrEmpty()]
    [string]$Configuration = "Release",

    [ValidateNotNullOrEmpty()]
    [string]$InstallDir = "$HOME/.selenium-pw-migrator",

    [switch]$RunTests
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$packageScript = Join-Path $PSScriptRoot "package-standalone.ps1"
$installScript = Join-Path $PSScriptRoot "install-standalone.ps1"

if (-not (Test-Path -LiteralPath $packageScript)) {
    throw "Standalone package script was not found: $packageScript"
}
if (-not (Test-Path -LiteralPath $installScript)) {
    throw "Standalone install script was not found: $installScript"
}
if (-not $Runtime.StartsWith("win-", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "install-local-standalone.ps1 currently installs the Windows .zip standalone. Runtime was: $Runtime"
}

Write-Host "== Package local standalone =="
Write-Host "Version:      $Version"
Write-Host "Runtime:      $Runtime"
Write-Host "Configuration:$Configuration"
Write-Host "InstallDir:   $InstallDir"

$packageParams = @{
    Version = $Version
    Runtimes = @($Runtime)
    Configuration = $Configuration
}
if ($RunTests) {
    $packageParams.RunTests = $true
}

& $packageScript @packageParams
if ($LASTEXITCODE -ne 0) {
    throw "package-standalone.ps1 failed with exit code $LASTEXITCODE"
}

$releaseDir = Join-Path $root "artifacts/release"
$archivePath = Join-Path $releaseDir "selenium-pw-migrator-$Version-$Runtime.zip"
$checksumsPath = Join-Path $releaseDir "checksums.sha256"

if (-not (Test-Path -LiteralPath $archivePath)) {
    throw "Fresh standalone archive was not found: $archivePath"
}
if (-not (Test-Path -LiteralPath $checksumsPath)) {
    throw "Standalone checksums file was not found: $checksumsPath"
}

Write-Host ""
Write-Host "== Install local standalone =="

$installParams = @{
    Version = $Version
    Runtime = $Runtime
    ArchivePath = $archivePath
    ChecksumsPath = $checksumsPath
    InstallDir = $InstallDir
    RemoveDotnetTool = $true
}

& $installScript @installParams
if ($LASTEXITCODE -ne 0) {
    throw "install-standalone.ps1 failed with exit code $LASTEXITCODE"
}

$exe = Join-Path (Join-Path $InstallDir "bin") "selenium-pw-migrator.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    throw "Installed executable was not found: $exe"
}

Write-Host ""
Write-Host "== Verify installed standalone =="

$versionOutput = (& $exe --version 2>&1 | Out-String).Trim()
if ($LASTEXITCODE -ne 0) {
    throw "Installed executable failed --version with exit code $LASTEXITCODE"
}
Write-Host $versionOutput

if ($versionOutput -notmatch [Regex]::Escape($Version)) {
    throw "Installed executable does not report requested version '$Version'."
}

$kitHelp = (& $exe kit --help 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "Installed executable failed 'kit --help' with exit code $LASTEXITCODE"
}
if ($kitHelp -notmatch [Regex]::Escape("--worktree")) {
    throw "Installed executable does not expose managed worktree support."
}

$remediationHelp = (& $exe remediation --help 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "Installed executable failed 'remediation --help' with exit code $LASTEXITCODE"
}
if ($remediationHelp -notmatch "guard" -or $remediationHelp -notmatch "evaluate" -or $remediationHelp -notmatch "rebaseline") {
    throw "Installed executable does not expose the expected remediation guard/evaluate/rebaseline commands."
}

Write-Host ""
Write-Host "LOCAL_STANDALONE_INSTALL_PASS"
Write-Host "Executable: $exe"
Write-Host "Version:    $Version"
Write-Host ""
Write-Host "PATH diagnostics:"
Write-Host "  Get-Command selenium-pw-migrator -All"
Write-Host "  where.exe selenium-pw-migrator"
