param(
    [string]$Version = "0.6.0-preview.1",
    [string]$PackageId = "SeleniumPlaywrightAstMigrator",
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
    /p:PackageId=$PackageId `
    /p:Version=$Version

Write-Host "Package output: $outDir"
Get-ChildItem $outDir -Filter "$PackageId.$Version.nupkg" | ForEach-Object { Write-Host $_.FullName }
