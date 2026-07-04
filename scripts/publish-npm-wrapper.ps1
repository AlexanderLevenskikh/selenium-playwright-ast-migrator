param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [string]$PackagePath = "artifacts/npm/selenium-pw-migrator-$Version.tgz",
    [string]$Registry = "https://registry.npmjs.org/",
    [ValidateSet("public", "restricted")]
    [string]$Access = "public",
    [switch]$DryRun,
    [switch]$NoProvenance
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$resolvedPackagePath = if ([System.IO.Path]::IsPathRooted($PackagePath)) { $PackagePath } else { Join-Path $root $PackagePath }

if (-not (Test-Path $resolvedPackagePath)) {
    throw "npm wrapper package was not found: $resolvedPackagePath"
}

$expectedName = "selenium-pw-migrator-$Version.tgz"
if ([System.IO.Path]::GetFileName($resolvedPackagePath) -ne $expectedName) {
    throw "npm wrapper package name mismatch. Expected '$expectedName', actual '$([System.IO.Path]::GetFileName($resolvedPackagePath))'."
}

$args = @("publish", $resolvedPackagePath, "--registry", $Registry, "--access", $Access)
if ($DryRun) {
    $args += "--dry-run"
} elseif (-not $NoProvenance) {
    $args += "--provenance"
}

Write-Host "Publishing npm wrapper package: $resolvedPackagePath"
Write-Host "Registry: $Registry"
Write-Host "Dry run: $($DryRun.IsPresent)"

& npm @args
if ($LASTEXITCODE -ne 0) {
    throw "npm publish failed with exit code $LASTEXITCODE"
}
