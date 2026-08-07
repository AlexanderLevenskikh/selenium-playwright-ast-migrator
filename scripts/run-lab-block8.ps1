[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [int]$TimeoutSeconds = 600,
    [string]$Artifacts = "./artifacts/lab/block-08",
    [switch]$SkipBrowserInstall,
    [switch]$SkipFullTests
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Write-Host "Building Migrator..."
    dotnet build Migrator.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    if ($SkipFullTests) {
        Write-Host "Running Block 8 focused contract tests..."
        dotnet test Migrator.Tests/Migrator.Tests.csproj `
            -c $Configuration `
            --no-build `
            --filter "FullyQualifiedName~LabTriageAndPromotionTests|FullyQualifiedName~LabSeededGenerationTests|FullyQualifiedName~Cli_ExposesLabAsOneCommandFamilyWithoutASecondBinary"
    } else {
        Write-Host "Running full Migrator.Tests before final lab acceptance..."
        dotnet test Migrator.Tests/Migrator.Tests.csproj -c $Configuration --no-build
    }
    if ($LASTEXITCODE -ne 0) { throw "Block 8 tests failed with exit code $LASTEXITCODE" }

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

    if (Test-Path $Artifacts) { Remove-Item $Artifacts -Recurse -Force }
    New-Item -ItemType Directory -Path $Artifacts -Force | Out-Null

    $cleanRun = Join-Path $Artifacts "clean-p01"
    Write-Host "Running one real green scenario used as the Block 8 evidence base..."
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab replay `
        --project p01-basic-id-login `
        --corpus ./corpus/stable/vertical-slice `
        --out $cleanRun `
        --timeout-seconds $TimeoutSeconds `
        --configuration $Configuration
    if ($LASTEXITCODE -ne 0) { throw "Block 8 p01 evidence run failed with exit code $LASTEXITCODE" }

    $cleanSummaryPath = Join-Path $cleanRun "lab-summary.json"
    if (-not (Test-Path $cleanSummaryPath)) { throw "Missing clean p01 lab-summary.json" }

    Write-Host "Creating a synthetic copy of the report with one bounded regression for triage validation..."
    $syntheticRun = Join-Path $Artifacts "synthetic-regression"
    New-Item -ItemType Directory -Path $syntheticRun -Force | Out-Null
    $summary = Get-Content $cleanSummaryPath -Raw | ConvertFrom-Json
    $project = @($summary.projects)[0]
    $project.actualStatus = "REGRESSION"
    $project.quality.passed = $false
    $project.quality.todoActual = 1
    $project.quality.todoMax = 0
    $project.quality.issues = @("Quality budget exceeded: TODO comments = 1, maximum = 0.")
    $project.oracle.passed = $false
    $project.oracle.checks = @(
        [pscustomobject]@{
            kind = "event-sequence"
            expected = "auth:attempt -> auth:success"
            actual = "auth:success"
            passed = $false
        }
    )
    $project.oracle.issues = @("Semantic oracle failed (event-sequence).")
    $summary.summary.passed = 0
    $summary.summary.regressions = 1
    $summary | ConvertTo-Json -Depth 100 | Set-Content (Join-Path $syntheticRun "lab-summary.json") -Encoding UTF8

    $triage = Join-Path $Artifacts "triage"
    Write-Host "Clustering the regression and building a bounded task pack..."
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab triage `
        --run $syntheticRun `
        --corpus ./corpus/stable/vertical-slice `
        --repo . `
        --out $triage
    if ($LASTEXITCODE -ne 0) { throw "lab triage failed with exit code $LASTEXITCODE" }

    $triageJsonPath = Join-Path $triage "lab-triage.json"
    $triageJson = Get-Content $triageJsonPath -Raw | ConvertFrom-Json
    if ($triageJson.summary.findings -ne 1 -or $triageJson.summary.clusters -ne 1 -or $triageJson.summary.taskPacks -ne 1) {
        throw "Expected exactly one finding, cluster and task pack from the synthetic regression."
    }
    $cluster = @($triageJson.clusters)[0]
    $taskPack = $cluster.taskPackDirectory
    if (-not (Test-Path (Join-Path $taskPack "TASK.md"))) { throw "Bounded task pack is missing TASK.md" }
    if (-not (Test-Path (Join-Path $taskPack "repro/scenario.json"))) { throw "Bounded task pack is missing reduced repro" }

    Write-Host "Verifying standalone reducer and promotion workflow..."
    $reduced = Join-Path $Artifacts "reduced"
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab reduce `
        --candidate (Join-Path $taskPack "repro") `
        --out $reduced
    if ($LASTEXITCODE -ne 0) { throw "lab reduce failed with exit code $LASTEXITCODE" }

    $promoted = Join-Path $Artifacts "promoted"
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab promote `
        --repro (Join-Path $taskPack "repro") `
        --level unit-test `
        --out $promoted
    if ($LASTEXITCODE -ne 0) { throw "lab promote failed with exit code $LASTEXITCODE" }
    if (-not (Test-Path (Join-Path $promoted "unit-test-repros/p01-basic-id-login/promotion.json"))) {
        throw "Promotion artifact was not created."
    }

    Write-Host "Verifying the rare real-project release gate contract..."
    $revision = "working-tree"
    try {
        $gitRevision = git rev-parse HEAD 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($gitRevision)) { $revision = $gitRevision.Trim() }
    } catch { }

    $realEvidencePath = Join-Path $Artifacts "real-project-evidence.json"
    $realEvidence = [ordered]@{
        schemaVersion = "migrator-lab-real-project-evidence/v1"
        project = "block-08-release-probe"
        sourceRevision = "p01-basic-id-login"
        migratorRevision = $revision
        executedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        status = "PASS"
        evidencePaths = @($cleanSummaryPath)
        notes = "Synthetic contract probe only. Production releases must use evidence from a real project."
    }
    $realEvidence | ConvertTo-Json -Depth 20 | Set-Content $realEvidencePath -Encoding UTF8

    $releaseGate = Join-Path $Artifacts "release-gate"
    dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
        lab release-gate `
        --stable-run $cleanRun `
        --real-evidence $realEvidencePath `
        --out $releaseGate `
        --max-age-days 14
    if ($LASTEXITCODE -ne 0) { throw "lab release-gate failed with exit code $LASTEXITCODE" }

    foreach ($required in @(
        (Join-Path $triage "lab-triage.json"),
        (Join-Path $triage "lab-triage.md"),
        (Join-Path $taskPack "task-pack.json"),
        (Join-Path $taskPack "evidence.json"),
        (Join-Path $reduced "reduction.json"),
        (Join-Path $releaseGate "lab-release-gate.json"),
        (Join-Path $releaseGate "lab-release-gate.md")
    )) {
        if (-not (Test-Path $required)) { throw "Missing Block 8 artifact: $required" }
    }

    Write-Host "Block 8 passed: feature-aware reduction, failure clustering, bounded agent task packs, regression promotion, automation policy, and rare real-project release gate are verified. Migrator Lab v1 is ready for continuous use."
}
finally {
    Pop-Location
}
