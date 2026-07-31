[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [int]$TimeoutSeconds = 600,
    [string]$Artifacts = "./artifacts/lab/block-05"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Write-Host "Building Migrator..."
    dotnet build Migrator.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    Write-Host "Running the full Migrator test suite..."
    dotnet test Migrator.Tests/Migrator.Tests.csproj -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { throw "Migrator tests failed with exit code $LASTEXITCODE" }

    if (Test-Path $Artifacts) { Remove-Item $Artifacts -Recurse -Force }
    $current = Join-Path $Artifacts "current"
    $baseline = Join-Path $Artifacts "baseline-main"
    $replay = Join-Path $Artifacts "replay-p15"
    $sameDiff = Join-Path $Artifacts "diff-same"
    $synthetic = Join-Path $Artifacts "synthetic-regression"
    $regressionDiff = Join-Path $Artifacts "diff-regression"

    Write-Host "Running the accepted vertical suite used as the current candidate..."
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab run `
        --suite vertical `
        --corpus ./corpus/stable/vertical-slice `
        --out $current `
        --timeout-seconds $TimeoutSeconds `
        --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "vertical lab run failed with exit code $LASTEXITCODE" }

    foreach ($report in @("lab-summary.json", "lab-summary.md", "lab-summary.html")) {
        if (-not (Test-Path (Join-Path $current $report))) { throw "Missing suite report: $report" }
    }

    Write-Host "Saving the normalized main baseline..."
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab baseline `
        --input $current `
        --out $baseline `
        --label main
    if ($LASTEXITCODE -ne 0) { throw "lab baseline failed with exit code $LASTEXITCODE" }

    Write-Host "Replaying one scenario through the full runtime pipeline..."
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab replay `
        --project p15-webdriverwait-visible `
        --corpus ./corpus/stable/vertical-slice `
        --out $replay `
        --timeout-seconds $TimeoutSeconds `
        --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "lab replay failed with exit code $LASTEXITCODE" }

    Write-Host "Comparing the baseline with the identical run..."
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab diff `
        --baseline $baseline `
        --current $current `
        --out $sameDiff `
        --duration-regression-percent 20
    if ($LASTEXITCODE -ne 0) { throw "same-run diff unexpectedly failed with exit code $LASTEXITCODE" }

    $same = Get-Content (Join-Path $sameDiff "lab-diff.json") -Raw | ConvertFrom-Json
    if ($same.summary.regressions -ne 0) { throw "Expected zero regressions for identical baseline/current run" }

    Write-Host "Creating a synthetic PR regression to prove the diff gate..."
    New-Item -ItemType Directory -Path $synthetic -Force | Out-Null
    $candidate = Get-Content (Join-Path $current "lab-summary.json") -Raw | ConvertFrom-Json
    $candidateProject = @($candidate.projects | Where-Object { $_.id -eq "p01-basic-id-login" })[0]
    $candidateProject.migration.todoComments = [int]$candidateProject.migration.todoComments + 1
    $candidate | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $synthetic "lab-summary.json") -Encoding utf8

    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab diff `
        --baseline $baseline `
        --current $synthetic `
        --out $regressionDiff `
        --duration-regression-percent 20
    $regressionExit = $LASTEXITCODE
    if ($regressionExit -ne 10) { throw "Synthetic regression diff must return exit code 10, got $regressionExit" }

    $regression = Get-Content (Join-Path $regressionDiff "lab-diff.json") -Raw | ConvertFrom-Json
    if ($regression.summary.regressions -lt 1) { throw "Synthetic diff did not report a regression" }

    foreach ($required in @(
        (Join-Path $baseline "lab-baseline.json"),
        (Join-Path $baseline "lab-baseline.md"),
        (Join-Path $replay "lab-summary.html"),
        (Join-Path $sameDiff "lab-diff.html"),
        (Join-Path $regressionDiff "lab-diff.html")
    )) {
        if (-not (Test-Path $required)) { throw "Missing Block 5 artifact: $required" }
    }

    Write-Host "Block 5 passed: HTML report, single-scenario replay, normalized baseline, clean diff, and regression exit code 10 are verified."
}
finally {
    Pop-Location
}
