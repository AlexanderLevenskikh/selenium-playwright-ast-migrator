[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [int]$TimeoutSeconds = 600,
    [string]$Artifacts = "./artifacts/lab/block-04",
    [switch]$SkipBrowserInstall
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

    if (-not $SkipBrowserInstall) {
        $playwrightScript = Join-Path $repoRoot "Migrator.Tests/bin/$Configuration/net10.0/playwright.ps1"
        if (-not (Test-Path $playwrightScript)) {
            throw "Playwright install script was not produced: $playwrightScript"
        }

        Write-Host "Ensuring Playwright Chromium is installed..."
        $powerShellHost = if ($PSVersionTable.PSEdition -eq "Core") {
            Join-Path $PSHOME "pwsh.exe"
        } else {
            Join-Path $PSHOME "powershell.exe"
        }
        & $powerShellHost -NoProfile -ExecutionPolicy Bypass -File $playwrightScript install chromium
        if ($LASTEXITCODE -ne 0) { throw "Playwright Chromium installation failed with exit code $LASTEXITCODE" }
    }

    if (Test-Path $Artifacts) {
        Remove-Item $Artifacts -Recurse -Force
    }

    Write-Host "Running project verification, Playwright runtime, quality budgets, and semantic oracle..."
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
    $labExitCode = $LASTEXITCODE

    $summaryPath = Join-Path $Artifacts "lab-summary.json"
    if (-not (Test-Path $summaryPath)) {
        throw "Missing suite report: $summaryPath"
    }

    $summary = Get-Content $summaryPath -Raw | ConvertFrom-Json
    if ($summary.summary.projects -ne 7) {
        throw "Expected 7 scenarios, got $($summary.summary.projects)"
    }

    $mismatches = @($summary.projects | Where-Object { $_.actualStatus -ne $_.expectedStatus })
    if ($labExitCode -ne 0 -or $mismatches.Count -gt 0) {
        Write-Host "Block 4 found unexpected scenario outcomes:" -ForegroundColor Yellow
        foreach ($project in $mismatches) {
            Write-Host "  $($project.id): expected $($project.expectedStatus), actual $($project.actualStatus)" -ForegroundColor Yellow
        }
        throw "Block 4 did not reach its final acceptance state; inspect $Artifacts/lab-summary.md"
    }

    foreach ($project in $summary.projects) {
        $projectVerifyReport = Join-Path $Artifacts "projects/$($project.id)/project-verify/project-verify-report.json"
        $runtimeReport = Join-Path $Artifacts "projects/$($project.id)/target/runtime-validation.json"
        $semanticReport = Join-Path $Artifacts "projects/$($project.id)/target/semantic-diff.json"
        $qualityReport = Join-Path $Artifacts "projects/$($project.id)/target/quality-evaluation.json"
        foreach ($required in @($projectVerifyReport, $runtimeReport, $semanticReport, $qualityReport)) {
            if (-not (Test-Path $required)) { throw "Missing Block 4 artifact: $required" }
        }
    }

    Write-Host "Block 4 passed: verify-project, target Playwright runtime, semantic oracle, and quality budgets matched all 7 scenario contracts."
}
finally {
    Pop-Location
}
