[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [int]$TimeoutSeconds = 600,
    [string]$Artifacts = "./artifacts/lab/block-07",
    [int]$Seed = 73001,
    [switch]$SkipBrowserInstall
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    Write-Host "Building Migrator..."
    dotnet build Migrator.sln -c $Configuration
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

    Write-Host "Running Block 7 generator/metamorphic contract tests..."
    dotnet test Migrator.Tests/Migrator.Tests.csproj `
        -c $Configuration `
        --no-build `
        --filter "FullyQualifiedName~LabSeededGenerationTests|FullyQualifiedName~SeleniumByAlias_FullPipeline_NormalizesToCanonicalByAndRenders|FullyQualifiedName~Host_OrdersBusinessEventsByBrowserSequenceWhenBeaconRequestsArriveOutOfOrder|FullyQualifiedName~PageCatalog_ContainsAllVerticalSliceRoutesAndClientEventLog|FullyQualifiedName~Cli_ExposesLabAsOneCommandFamilyWithoutASecondBinary"
    if ($LASTEXITCODE -ne 0) { throw "Block 7 contract tests failed with exit code $LASTEXITCODE" }

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

    $generatedA = Join-Path $Artifacts "generated-a"
    $generatedB = Join-Path $Artifacts "generated-b"
    $runA = Join-Path $Artifacts "run-a"
    $runB = Join-Path $Artifacts "run-b"
    $metaA = Join-Path $Artifacts "metamorphic-a"
    $metaB = Join-Path $Artifacts "metamorphic-b"
    $candidatesA = Join-Path $Artifacts "seed-candidates-a"
    $candidatesB = Join-Path $Artifacts "seed-candidates-b"
    $failures = [System.Collections.Generic.List[string]]::new()

    function Invoke-SeedGeneration {
        param([Parameter(Mandatory=$true)][string]$Out)
        dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
            lab generate `
            --corpus ./corpus/stable/vertical-slice `
            --base p01-basic-id-login `
            --seed $Seed `
            --count 6 `
            --out $Out `
            --force
        if ($LASTEXITCODE -ne 0) { throw "lab generate failed for $Out with exit code $LASTEXITCODE" }
    }

    Write-Host "Generating the same seed twice to prove deterministic project generation..."
    Invoke-SeedGeneration -Out $generatedA
    Invoke-SeedGeneration -Out $generatedB

    $manifestAPath = Join-Path $generatedA "generation-manifest.json"
    $manifestBPath = Join-Path $generatedB "generation-manifest.json"
    $manifestA = Get-Content $manifestAPath -Raw | ConvertFrom-Json
    $manifestB = Get-Content $manifestBPath -Raw | ConvertFrom-Json

    if ($manifestA.corpusFingerprint -ne $manifestB.corpusFingerprint) {
        $failures.Add("same seed produced different corpus fingerprints")
    }
    if (@($manifestA.variants).Count -ne 6 -or @($manifestB.variants).Count -ne 6) {
        $failures.Add("expected six pairwise variants in each generated corpus")
    }

    $variantHashesA = @($manifestA.variants | Sort-Object id | ForEach-Object { "$($_.id)|$($_.contentHash)" })
    $variantHashesB = @($manifestB.variants | Sort-Object id | ForEach-Object { "$($_.id)|$($_.contentHash)" })
    if (($variantHashesA -join "`n") -ne ($variantHashesB -join "`n")) {
        $failures.Add("same seed produced different generated project hashes")
    }

    foreach ($generated in @($generatedA, $generatedB)) {
        Write-Host "Validating generated corpus: $generated"
        dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
            lab validate `
            --corpus $generated `
            --out (Join-Path $generated "contract-validation") `
            --fail-on-planned
        if ($LASTEXITCODE -ne 0) { $failures.Add("generated corpus validation failed: $generated") }
    }

    function Invoke-GeneratedRun {
        param(
            [Parameter(Mandatory=$true)][string]$Corpus,
            [Parameter(Mandatory=$true)][string]$Out
        )
        dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
            lab run `
            --corpus $Corpus `
            --out $Out `
            --timeout-seconds $TimeoutSeconds `
            --configuration $Configuration | Out-Host
        $exitCode = $LASTEXITCODE
        return $exitCode
    }

    Write-Host "Running generated family A..."
    $runExitA = Invoke-GeneratedRun -Corpus $generatedA -Out $runA
    Write-Host "Running generated family B..."
    $runExitB = Invoke-GeneratedRun -Corpus $generatedB -Out $runB

    function Invoke-MetamorphicCheck {
        param(
            [Parameter(Mandatory=$true)][string]$Manifest,
            [Parameter(Mandatory=$true)][string]$Run,
            [Parameter(Mandatory=$true)][string]$Out,
            [Parameter(Mandatory=$true)][string]$Candidates
        )
        dotnet run --project Migrator.Cli -c $Configuration --no-build -- `
            lab metamorphic `
            --manifest $Manifest `
            --run $Run `
            --out $Out `
            --save-candidates $Candidates | Out-Host
        $exitCode = $LASTEXITCODE
        return $exitCode
    }

    Write-Host "Checking metamorphic invariants and saving useful failing seeds..."
    $metaExitA = Invoke-MetamorphicCheck -Manifest $manifestAPath -Run $runA -Out $metaA -Candidates $candidatesA
    $metaExitB = Invoke-MetamorphicCheck -Manifest $manifestBPath -Run $runB -Out $metaB -Candidates $candidatesB

    $summaryAPath = Join-Path $runA "lab-summary.json"
    $summaryBPath = Join-Path $runB "lab-summary.json"
    if (-not (Test-Path $summaryAPath) -or -not (Test-Path $summaryBPath)) {
        throw "Generated runs did not produce both lab-summary.json files."
    }

    $summaryA = Get-Content $summaryAPath -Raw | ConvertFrom-Json
    $summaryB = Get-Content $summaryBPath -Raw | ConvertFrom-Json

    function Get-OutcomeSignatures {
        param($Summary)
        return @($Summary.projects | Sort-Object id | ForEach-Object {
            $diagnostics = @($_.projectVerify.diagnosticCategories) -join ","
            "$($_.id)|$($_.actualStatus)|$($_.sourceTests.passed)/$($_.sourceTests.expectedPassed)|$($_.targetTests.passed)/$($_.targetTests.expectedPassed)|todo=$($_.quality.todoActual)|unmapped=$($_.quality.unmappedActual)|unsupported=$($_.quality.unsupportedActual)|warnings=$($_.quality.warningsActual)|quality=$($_.quality.passed)|oracle=$($_.oracle.passed)|diag=$diagnostics"
        })
    }

    $outcomesA = Get-OutcomeSignatures -Summary $summaryA
    $outcomesB = Get-OutcomeSignatures -Summary $summaryB
    if (($outcomesA -join "`n") -ne ($outcomesB -join "`n")) {
        $failures.Add("same seed produced different lab outcomes")
    }

    $sourceInvalid = @($summaryA.projects | Where-Object { $_.actualStatus -eq "SOURCE_INVALID" }).Count
    if ($sourceInvalid -gt 0) {
        $failures.Add("generator produced $sourceInvalid SOURCE_INVALID fixture(s); invalid fixtures must not dominate generated findings")
    }

    if ($runExitA -ne 0 -or $metaExitA -ne 0) {
        $failures.Add("generated family A found metamorphic regression(s); inspect $metaA and $candidatesA")
    }
    if ($runExitB -ne 0 -or $metaExitB -ne 0) {
        $failures.Add("generated family B reproduced metamorphic regression(s); inspect $metaB and $candidatesB")
    }

    foreach ($required in @(
        $manifestAPath,
        $manifestBPath,
        (Join-Path $generatedA "generation-manifest.md"),
        (Join-Path $runA "lab-summary.html"),
        (Join-Path $runB "lab-summary.html"),
        (Join-Path $metaA "lab-metamorphic.json"),
        (Join-Path $metaA "lab-metamorphic.md"),
        (Join-Path $metaB "lab-metamorphic.json"),
        (Join-Path $metaB "lab-metamorphic.md")
    )) {
        if (-not (Test-Path $required)) { $failures.Add("Missing Block 7 artifact: $required") }
    }

    if ($failures.Count -gt 0) {
        Write-Host "Block 7 found reproducible generated-corpus issues:" -ForegroundColor Yellow
        $failures | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
        throw "Block 7 did not reach final acceptance; generated failures are reproducible and useful seeds were saved under $Artifacts."
    }

    Write-Host "Block 7 passed: pairwise seeded generation is deterministic, generated fixtures are valid, repeated seed outcomes match, metamorphic invariants hold, and regression-candidate saving is verified."
}
finally {
    Pop-Location
}
