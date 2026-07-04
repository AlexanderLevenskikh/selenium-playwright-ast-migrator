param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [string]$Runtime = "win-x64",
    [Parameter(Mandatory=$true)]
    [string]$ArchivePath,
    [Parameter(Mandatory=$true)]
    [string]$ChecksumsPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$npmRoot = Join-Path $repoRoot "npm"
$installerScript = Join-Path $npmRoot "scripts/install.js"
$wrapperScript = Join-Path $npmRoot "bin/selenium-pw-migrator.js"

if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
    throw "Node.js is required for npm wrapper smoke."
}

$resolvedArchive = (Resolve-Path $ArchivePath).Path
$resolvedChecksums = (Resolve-Path $ChecksumsPath).Path

$env:SELENIUM_PW_MIGRATOR_VERSION = $Version
$env:SELENIUM_PW_MIGRATOR_RUNTIME = $Runtime
$env:SELENIUM_PW_MIGRATOR_ARCHIVE_PATH = $resolvedArchive
$env:SELENIUM_PW_MIGRATOR_CHECKSUMS_PATH = $resolvedChecksums

try {
    node $installerScript
    node $wrapperScript --version
}
finally {
    Remove-Item Env:\SELENIUM_PW_MIGRATOR_VERSION -ErrorAction SilentlyContinue
    Remove-Item Env:\SELENIUM_PW_MIGRATOR_RUNTIME -ErrorAction SilentlyContinue
    Remove-Item Env:\SELENIUM_PW_MIGRATOR_ARCHIVE_PATH -ErrorAction SilentlyContinue
    Remove-Item Env:\SELENIUM_PW_MIGRATOR_CHECKSUMS_PATH -ErrorAction SilentlyContinue
}
