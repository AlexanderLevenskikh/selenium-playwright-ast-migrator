param(
    [Parameter(Mandatory = $true)] [string]$Source,
    [string]$Version = "0.0.0-preview.1",
    [string]$PackageId = "SeleniumPlaywrightMigrator",
    [string]$ApiKey = "",
    [string]$PackageDirectory = "artifacts/nuget",
    [switch]$SkipDuplicate
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$package = Join-Path $root "$PackageDirectory/$PackageId.$Version.nupkg"

if (-not (Test-Path $package)) {
    throw "Package not found: $package. Run scripts/pack-tool.ps1 first."
}

$args = @("nuget", "push", $package, "--source", $Source)

if (-not [string]::IsNullOrWhiteSpace($ApiKey)) {
    $args += @("--api-key", $ApiKey)
}

if ($SkipDuplicate) {
    $args += "--skip-duplicate"
}

Write-Host "Publishing $package to $Source..."
& dotnet @args
if ($LASTEXITCODE -ne 0) {
    throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE"
}
