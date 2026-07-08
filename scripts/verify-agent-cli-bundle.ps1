param(
    [string]$BundleDirectory = "artifacts/agent-cli-bundle/tool",
    [switch]$RunHelp
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$bundleDir = if ([System.IO.Path]::IsPathRooted($BundleDirectory)) { $BundleDirectory } else { Join-Path $root $BundleDirectory }

if (-not (Test-Path $bundleDir)) {
    throw "Agent CLI bundle directory not found: $bundleDir"
}

$requiredFiles = @(
    "README_AGENT_TOOL.md",
    "MANIFEST.sha256",
    "manifest.json",
    "schemas/adapter-config.schema.json",
    "templates/migration-kit/README.md",
    "templates/migration-kit/prompts/kickoff-prompt.txt",
    "templates/migration-kit/agent-skills/skill-map.md",
    "templates/migration-kit/agent-skills/manifest.json",
    "templates/migration-kit/scripts/write-agent-skill-usage.ps1",
    "templates/migration-kit/scripts/write-agent-skill-usage.sh",
    "templates/migration-kit/scripts/record-agent-skill-profile.ps1",
    "templates/migration-kit/scripts/record-agent-skill-profile.sh",
    "scripts/install-migration-kit.ps1",
    "scripts/install-migration-kit.sh"
)

foreach ($relative in $requiredFiles) {
    $path = Join-Path $bundleDir $relative
    if (-not (Test-Path $path)) {
        throw "Bundle is missing required file: $relative"
    }
}

$dll = Join-Path $bundleDir "Migrator.Cli.dll"
$exe = Join-Path $bundleDir "migrator.exe"
$apphost = Join-Path $bundleDir "Migrator.Cli"
if (-not (Test-Path $dll) -and -not (Test-Path $exe) -and -not (Test-Path $apphost)) {
    throw "Bundle does not contain a runnable CLI entry point. Expected Migrator.Cli.dll, Migrator.Cli, or migrator.exe."
}

$manifestSha = Join-Path $bundleDir "MANIFEST.sha256"
$manifestLines = @(Get-Content $manifestSha | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($manifestLines.Count -eq 0) {
    throw "MANIFEST.sha256 is empty."
}

foreach ($line in $manifestLines) {
    if ($line -notmatch '^(?<hash>[a-fA-F0-9]{64})\s\s(?<path>.+)$') {
        throw "Invalid MANIFEST.sha256 line: $line"
    }

    $expectedHash = $Matches['hash'].ToLowerInvariant()
    $relativePath = $Matches['path']
    $filePath = Join-Path $bundleDir ($relativePath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
    if (-not (Test-Path $filePath)) {
        throw "Manifest references missing file: $relativePath"
    }

    $actualHash = (Get-FileHash -Algorithm SHA256 -Path $filePath).Hash.ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Checksum mismatch for $relativePath"
    }
}

$manifestJson = Get-Content (Join-Path $bundleDir "manifest.json") -Raw | ConvertFrom-Json
if ($manifestJson.schemaVersion -ne 1) {
    throw "Unexpected manifest.json schemaVersion: $($manifestJson.schemaVersion)"
}

if (-not $manifestJson.files -or $manifestJson.files.Count -eq 0) {
    throw "manifest.json does not list bundled files."
}

if ($RunHelp) {
    if (Test-Path $dll) {
        dotnet $dll --help | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Bundle CLI --help failed with exit code $LASTEXITCODE" }
        dotnet $dll kit --help | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Bundle CLI kit --help failed with exit code $LASTEXITCODE" }
    }
    elseif (Test-Path $exe) {
        & $exe --help | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Bundle CLI --help failed with exit code $LASTEXITCODE" }
        & $exe kit --help | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Bundle CLI kit --help failed with exit code $LASTEXITCODE" }
    }
    else {
        & $apphost --help | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Bundle CLI --help failed with exit code $LASTEXITCODE" }
        & $apphost kit --help | Out-Host
        if ($LASTEXITCODE -ne 0) { throw "Bundle CLI kit --help failed with exit code $LASTEXITCODE" }
    }
}

Write-Host "Agent CLI bundle verification passed: $bundleDir"
Write-Host "Files checked from checksum manifest: $($manifestLines.Count)"
