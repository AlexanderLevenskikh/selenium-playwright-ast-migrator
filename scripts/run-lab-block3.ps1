[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [int]$TimeoutSeconds = 600,
    [string]$Artifacts = "./artifacts/lab/block-03"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Write-Host "Building Migrator..."
    dotnet build Migrator.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    Write-Host "Running the full Migrator test suite..."
    dotnet test Migrator.Tests/Migrator.Tests.csproj `
        -c $Configuration `
        --no-build
    if ($LASTEXITCODE -ne 0) { throw "Migrator tests failed with exit code $LASTEXITCODE" }

    if (Test-Path $Artifacts) {
        Remove-Item $Artifacts -Recurse -Force
    }

    Write-Host "Running the seven-scenario vertical slice..."
    dotnet run --project Migrator.Cli `
        -c $Configuration `
        --no-build `
        -- `
        lab run `
        --suite vertical `
        --corpus ./corpus/stable/vertical-slice `
        --out $Artifacts `
        --timeout-seconds $TimeoutSeconds `
        --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "lab run failed with exit code $LASTEXITCODE; inspect $Artifacts/lab-summary.md" }

    $summaryPath = Join-Path $Artifacts "lab-summary.json"
    if (-not (Test-Path $summaryPath)) {
        throw "Missing suite report: $summaryPath"
    }

    $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
    if ($summary.summary.projects -ne 7) {
        throw "Expected 7 scenarios, got $($summary.summary.projects)"
    }
    if ($summary.summary.migratorFailures -ne 0 `
        -or $summary.summary.sourceInvalid -ne 0 `
        -or $summary.summary.infrastructureFailures -ne 0 `
        -or $summary.summary.regressions -ne 0) {
        throw "Block 3 has unexpected failures; inspect $Artifacts/lab-summary.md"
    }

    Write-Host "Block 3 passed: 7 source projects validated, existing migration run executed, suite statuses classified."
}
finally {
    Pop-Location
}
