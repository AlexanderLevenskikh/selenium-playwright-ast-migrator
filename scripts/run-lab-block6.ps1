[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [int]$TimeoutSeconds = 600,
    [string]$Artifacts = "./artifacts/lab/block-06",
    [switch]$SkipFullTests
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Write-Host "Building Migrator..."
    dotnet build Migrator.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    if (-not $SkipFullTests) {
        Write-Host "Running the full Migrator test suite..."
        dotnet test Migrator.Tests/Migrator.Tests.csproj -c $Configuration --no-build
        if ($LASTEXITCODE -ne 0) { throw "Migrator tests failed with exit code $LASTEXITCODE" }
    }

    if (Test-Path $Artifacts) { Remove-Item $Artifacts -Recurse -Force }
    New-Item -ItemType Directory -Path $Artifacts -Force | Out-Null

    Write-Host "Validating all 30 READY stable scenarios..."
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab validate `
        --corpus ./corpus/stable/vertical-slice `
        --out (Join-Path $Artifacts "contracts") `
        --fail-on-planned
    if ($LASTEXITCODE -ne 0) { throw "lab validate failed with exit code $LASTEXITCODE" }

    $failures = [System.Collections.Generic.List[string]]::new()

    function Invoke-LabSuite {
        param(
            [Parameter(Mandatory=$true)][string]$Suite,
            [Parameter(Mandatory=$true)][int]$ExpectedCount
        )

        $out = Join-Path $Artifacts $Suite
        Write-Host "Running $Suite suite ($ExpectedCount scenarios)..."
        dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
            lab run `
            --suite $Suite `
            --corpus ./corpus/stable/vertical-slice `
            --out $out `
            --timeout-seconds $TimeoutSeconds `
            --configuration $Configuration
        $exitCode = $LASTEXITCODE

        $summaryPath = Join-Path $out "lab-summary.json"
        if (-not (Test-Path $summaryPath)) {
            $failures.Add("${Suite}: missing lab-summary.json (exit $exitCode)")
            return
        }

        $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
        $projects = @($summary.projects)
        if ($projects.Count -ne $ExpectedCount) {
            $failures.Add("${Suite}: expected $ExpectedCount scenarios, got $($projects.Count)")
        }

        $unexpected = @($projects | Where-Object { $_.actualStatus -ne $_.expectedStatus })
        foreach ($project in $unexpected) {
            $failures.Add("$Suite/$($project.id): expected $($project.expectedStatus), actual $($project.actualStatus)")
        }

        if ($exitCode -ne 0 -and $unexpected.Count -eq 0) {
            $failures.Add("${Suite}: exit code $exitCode despite matching scenario contracts")
        }
    }

    Invoke-LabSuite -Suite "smoke" -ExpectedCount 7
    Invoke-LabSuite -Suite "pr" -ExpectedCount 18
    Invoke-LabSuite -Suite "nightly" -ExpectedCount 30

    $featureOut = Join-Path $Artifacts "feature-waits"
    Write-Host "Checking feature-based selection for wait scenarios..."
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab run `
        --corpus ./corpus/stable/vertical-slice `
        --feature WebDriverWait,CustomWait `
        --out $featureOut `
        --timeout-seconds $TimeoutSeconds `
        --configuration $Configuration
    $featureExit = $LASTEXITCODE
    $featureSummaryPath = Join-Path $featureOut "lab-summary.json"
    if (-not (Test-Path $featureSummaryPath)) {
        $failures.Add("feature-waits: missing lab-summary.json (exit $featureExit)")
    }
    else {
        $featureSummary = Get-Content $featureSummaryPath -Raw | ConvertFrom-Json
        $actualIds = @($featureSummary.projects | ForEach-Object { $_.id } | Sort-Object)
        $expectedIds = @("p09-helper-extension-mapping", "p15-webdriverwait-visible", "p16-wait-disappear-negative", "p17-custom-wait-state")
        if (($actualIds -join ",") -ne ($expectedIds -join ",")) {
            $failures.Add("feature-waits: expected $($expectedIds -join ','), got $($actualIds -join ',')")
        }
        foreach ($project in @($featureSummary.projects | Where-Object { $_.actualStatus -ne $_.expectedStatus })) {
            $failures.Add("feature-waits/$($project.id): expected $($project.expectedStatus), actual $($project.actualStatus)")
        }
    }

    foreach ($required in @(
        ".\corpus\stable\vertical-slice\coverage-matrix.json",
        ".\docs\lab\STABLE_CORPUS_MATRIX.ru.md",
        (Join-Path $Artifacts "smoke/lab-summary.html"),
        (Join-Path $Artifacts "pr/lab-summary.html"),
        (Join-Path $Artifacts "nightly/lab-summary.html")
    )) {
        if (-not (Test-Path $required)) { $failures.Add("Missing Block 6 artifact: $required") }
    }

    if ($failures.Count -gt 0) {
        Write-Host "Block 6 found unexpected outcomes:" -ForegroundColor Yellow
        $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        throw "Block 6 did not reach its final acceptance state; inspect $Artifacts/nightly/lab-summary.md"
    }

    Write-Host "Block 6 passed: 30 READY stable scenarios, smoke/PR/nightly suites, feature selection, and expected negative contracts are verified."
}
finally {
    Pop-Location
}
