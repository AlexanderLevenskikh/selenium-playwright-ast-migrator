param(
    [string]$Version = "0.0.0-preview.1",
    [string[]]$Runtimes = @("win-x64", "linux-x64", "osx-x64", "osx-arm64"),
    [string]$Configuration = "Release",
    [string]$PublishOutput = "artifacts/standalone/publish",
    [string]$ReleaseOutput = "artifacts/release",
    [switch]$RunTests,
    [switch]$NoSelfContained
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $PSScriptRoot "publish-standalone.ps1"
$releaseDir = if ([System.IO.Path]::IsPathRooted($ReleaseOutput)) { $ReleaseOutput } else { Join-Path $root $ReleaseOutput }
$publishRoot = if ([System.IO.Path]::IsPathRooted($PublishOutput)) { $PublishOutput } else { Join-Path $root $PublishOutput }

if (-not (Test-Path $publishScript)) {
    throw "Standalone publish script was not found: $publishScript"
}

if ($RunTests) {
    Write-Host "Running tests before standalone packaging..."
    dotnet test (Join-Path $root "Migrator.sln") -c $Configuration --no-restore
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed with exit code $LASTEXITCODE"
    }
}

Remove-Item -Recurse -Force $releaseDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

$entries = @()

foreach ($runtime in $Runtimes) {
    Write-Host ""
    Write-Host "Packaging standalone runtime: $runtime"

    # Use hashtable splatting for script parameters.
    # Array splatting is fragile here: when a caller/session treats the array entries as
    # positional arguments, publish-standalone.ps1 receives values shifted like
    # Configuration=-Configuration and Runtime=Release. Hashtable splatting keeps the
    # parameter names explicit and prevents Release from being passed as RuntimeIdentifier.
    $publishParams = @{
        Configuration = $Configuration
        Runtime = $runtime
        Output = $PublishOutput
        Version = $Version
    }
    if ($NoSelfContained) {
        $publishParams.NoSelfContained = $true
    }

    & $publishScript @publishParams
    if ($LASTEXITCODE -ne 0) {
        throw "publish-standalone.ps1 failed for $runtime with exit code $LASTEXITCODE"
    }

    $publishDir = Join-Path $publishRoot $runtime
    if (-not (Test-Path $publishDir)) {
        throw "Expected publish directory was not created: $publishDir"
    }

    $archiveBaseName = "selenium-pw-migrator-$Version-$runtime"
    $archiveName = if ($runtime.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)) {
        "$archiveBaseName.zip"
    }
    else {
        "$archiveBaseName.tar.gz"
    }
    $archivePath = Join-Path $releaseDir $archiveName

    Remove-Item -Force $archivePath -ErrorAction SilentlyContinue

    if ($archiveName.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)) {
        Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $archivePath -Force
    }
    else {
        $tar = Get-Command tar -ErrorAction SilentlyContinue
        if (-not $tar) {
            throw "tar command was not found. It is required to create $archiveName."
        }

        Push-Location $publishDir
        try {
            & tar -czf $archivePath .
            if ($LASTEXITCODE -ne 0) {
                throw "tar failed with exit code $LASTEXITCODE"
            }
        }
        finally {
            Pop-Location
        }
    }

    $aliasName = if ($runtime.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)) {
        "selenium-pw-migrator-$runtime.zip"
    }
    else {
        "selenium-pw-migrator-$runtime.tar.gz"
    }
    $aliasPath = Join-Path $releaseDir $aliasName
    Copy-Item $archivePath $aliasPath -Force

    $hash = Get-FileHash -Algorithm SHA256 -Path $archivePath
    $aliasHash = Get-FileHash -Algorithm SHA256 -Path $aliasPath
    $entries += [ordered]@{
        runtime = $runtime
        file = $archiveName
        alias = $aliasName
        sha256 = $hash.Hash.ToLowerInvariant()
        aliasSha256 = $aliasHash.Hash.ToLowerInvariant()
        selfContained = (-not $NoSelfContained)
        publishSingleFile = $false
    }

    Write-Host "Created: $archivePath"
    Write-Host "Alias:   $aliasPath"
}

$checksumPath = Join-Path $releaseDir "checksums.sha256"
$checksumLines = foreach ($entry in $entries) {
    "$($entry.sha256)  $($entry.file)"
    "$($entry.aliasSha256)  $($entry.alias)"
}
Set-Content -Path $checksumPath -Value $checksumLines -Encoding ASCII

$manifest = [ordered]@{
    schemaVersion = "standalone-release/v1"
    version = $Version
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    selfContained = (-not $NoSelfContained)
    publishSingleFile = $false
    runtimes = $Runtimes
    artifacts = $entries
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -Path (Join-Path $releaseDir "standalone-release-manifest.json") -Encoding UTF8

Write-Host ""
Write-Host "Standalone release artifacts: $releaseDir"
Get-ChildItem $releaseDir | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Name)" }
