param(
    [string]$Version = "0.0.0-preview.1",
    [string]$PackageId = "SeleniumPlaywrightMigrator",
    [string]$Configuration = "Release",
    [string]$Output = "artifacts/nuget"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "Migrator.Cli/Migrator.Cli.csproj"
$outDir = Join-Path $root $Output

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

Write-Host "Packing $PackageId $Version..."
dotnet pack $project `
    -c $Configuration `
    -o $outDir `
    /p:Version=$Version
if ($LASTEXITCODE -ne 0) {
    throw "dotnet pack failed with exit code $LASTEXITCODE"
}

Write-Host "Package output: $outDir"
$package = Get-ChildItem $outDir -Filter "$PackageId.$Version.nupkg" | Select-Object -First 1
if (-not $package) {
    throw "Expected package was not created: $PackageId.$Version.nupkg"
}

Write-Host $package.FullName
