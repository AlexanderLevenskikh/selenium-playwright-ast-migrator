param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "artifacts/standalone/publish",
    [string]$ToolName = "selenium-pw-migrator",
    [string]$Version = "",
    [switch]$RunTests,
    [switch]$NoSelfContained
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path (Join-Path $root "Migrator.Cli") "Migrator.Cli.csproj"
$outputRoot = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
$publishDir = Join-Path $outputRoot $Runtime
$selfContainedValue = (-not $NoSelfContained).ToString().ToLowerInvariant()

Write-Host "Publishing Selenium Playwright Migrator standalone bundle"
Write-Host "Repo root:      $root"
Write-Host "Project:        $project"
Write-Host "Runtime:        $Runtime"
Write-Host "Configuration:  $Configuration"
Write-Host "Self-contained: $selfContainedValue"
Write-Host "Output:         $publishDir"
Write-Host ""

if (-not (Test-Path $project)) {
    throw "Migrator.Cli project was not found: $project"
}

if ($RunTests) {
    Write-Host "Running tests before standalone publish..."
    dotnet test (Join-Path $root "Migrator.sln") -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed with exit code $LASTEXITCODE"
    }
}

Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

$publishArgs = @(
    "publish",
    $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", $selfContainedValue,
    "/p:PublishSingleFile=false",
    "/p:DebugType=None",
    "/p:DebugSymbols=false",
    "-o", $publishDir
)

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $publishArgs += "/p:Version=$Version"
    $publishArgs += "/p:AssemblyInformationalVersion=$Version"
}

# Keep PublishSingleFile disabled on purpose.
# The migrator is Roslyn-heavy and depends on project-reference DLLs/resources next to the executable.
# This bundle still does not require a .NET SDK/runtime when --self-contained true is used.
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$publishedExe = Join-Path $publishDir "Migrator.Cli.exe"
$publishedAppHost = Join-Path $publishDir "Migrator.Cli"
$targetExe = Join-Path $publishDir "$ToolName.exe"
$targetAppHost = Join-Path $publishDir $ToolName
$entrypoint = "Migrator.Cli.dll"

if (Test-Path $publishedExe) {
    Copy-Item $publishedExe $targetExe -Force
    $entrypoint = "$ToolName.exe"
}
elseif (Test-Path $publishedAppHost) {
    Copy-Item $publishedAppHost $targetAppHost -Force
    $entrypoint = $ToolName
}
elseif (-not $NoSelfContained) {
    throw "Published executable was not found: $publishedExe or $publishedAppHost"
}

foreach ($rootFile in @("LICENSE", "SECURITY.md", "CONTRIBUTING.md", "CHANGELOG.md")) {
    $source = Join-Path $root $rootFile
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $publishDir $rootFile) -Force
    }
}

$readmePath = Join-Path $publishDir "README_STANDALONE.md"
$readmeLines = @(
    "# Selenium Playwright Migrator standalone bundle",
    "",
    "This directory contains a published CLI bundle for runtime '$Runtime'.",
    "",
    "## Run",
    "",
    "Windows:",
    "",
    '```powershell',
    ".\\$ToolName.exe --help",
    '```',
    "",
    "Linux/macOS:",
    "",
    '```bash',
    "./$ToolName --help",
    '```',
    "",
    "## Runtime model",
    "",
    "When published as self-contained, this bundle does not require the .NET SDK or .NET Runtime on the target machine.",
    "Keep the executable together with the DLL/resource files next to it. Do not copy only the executable out of this folder.",
    "",
    "PublishSingleFile is intentionally disabled because the migrator uses Roslyn and project-reference assemblies/resources.",
    "",
    "## Verify",
    "",
    '```bash',
    "./$ToolName --version",
    "./$ToolName --help",
    '```'
)
Set-Content -Path $readmePath -Value $readmeLines -Encoding UTF8

$manifest = [ordered]@{
    schemaVersion = "standalone-publish/v1"
    version = if ([string]::IsNullOrWhiteSpace($Version)) { $null } else { $Version }
    runtime = $Runtime
    configuration = $Configuration
    selfContained = (-not $NoSelfContained)
    publishSingleFile = $false
    entrypoint = $entrypoint
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path (Join-Path $publishDir "standalone-manifest.json") -Encoding UTF8

Write-Host "Standalone publish output: $publishDir"
Write-Host "Entrypoint: $entrypoint"
