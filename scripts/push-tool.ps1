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

$effectiveApiKey = $ApiKey
if ([string]::IsNullOrWhiteSpace($effectiveApiKey)) {
    $effectiveApiKey = $env:NUGET_API_KEY
}

$isNuGetOrgSource = $Source -match 'nuget\.org'
if ($isNuGetOrgSource -and [string]::IsNullOrWhiteSpace($effectiveApiKey)) {
    throw @"
NUGET_API_KEY is required to publish to nuget.org.
For GitHub Actions Trusted Publishing, run NuGet/login@v1 before this script and pass the action output as NUGET_API_KEY.
For classic API key publishing, pass -ApiKey or set the NUGET_API_KEY environment variable.
"@
}

$args = @("nuget", "push", $package, "--source", $Source)

if (-not [string]::IsNullOrWhiteSpace($effectiveApiKey)) {
    $args += @("--api-key", $effectiveApiKey)
}

if ($SkipDuplicate) {
    $args += "--skip-duplicate"
}

Write-Host "Publishing $package to $Source..."
& dotnet @args
if ($LASTEXITCODE -ne 0) {
    throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE"
}
