param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [string]$PackagePath = "artifacts/npm/selenium-pw-migrator-$Version.tgz",
    [string]$Registry = "https://registry.npmjs.org/",
    [ValidateSet("public", "restricted")]
    [string]$Access = "public",
    [string]$Tag = "preview",
    [switch]$DryRun,
    [switch]$Provenance,
    [switch]$NoProvenance
)

$ErrorActionPreference = "Stop"

if ($Provenance -and $NoProvenance) {
    throw "Use either -Provenance or -NoProvenance, not both. Provenance is disabled by default."
}

$root = Split-Path -Parent $PSScriptRoot
$resolvedPackagePath = if ([System.IO.Path]::IsPathRooted($PackagePath)) { $PackagePath } else { Join-Path $root $PackagePath }

if (-not (Test-Path $resolvedPackagePath)) {
    throw "npm wrapper package was not found: $resolvedPackagePath"
}

$expectedName = "selenium-pw-migrator-$Version.tgz"
if ([System.IO.Path]::GetFileName($resolvedPackagePath) -ne $expectedName) {
    throw "npm wrapper package name mismatch. Expected '$expectedName', actual '$([System.IO.Path]::GetFileName($resolvedPackagePath))'."
}

if ($Version.Contains('-') -and $Tag -eq 'latest') {
    throw "Prerelease versions must not be published with the latest dist-tag. Use -Tag preview."
}

$args = @("publish", $resolvedPackagePath, "--registry", $Registry, "--access", $Access, "--tag", $Tag)
if ($DryRun) {
    $args += "--dry-run"
} elseif ($Provenance) {
    $args += "--provenance"
}

Write-Host "Publishing npm wrapper package: $resolvedPackagePath"
Write-Host "Registry: $Registry"
Write-Host "Tag: $Tag"
Write-Host "Dry run: $($DryRun.IsPresent)"
Write-Host "Provenance: $($Provenance.IsPresent)"

& npm @args
if ($LASTEXITCODE -ne 0) {
    throw "npm publish failed with exit code $LASTEXITCODE"
}
