[CmdletBinding()]
param(
    [string]$Root = '.',
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/test-layers/e2e-standard-migration',
    [string]$CliDll = ''
)
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path $Root).Path
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $rootPath $Output }
if (Test-Path $outputPath) { Remove-Item -Recurse -Force $outputPath }
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
if ([string]::IsNullOrWhiteSpace($CliDll)) { $CliDll = Join-Path $rootPath "Migrator.Cli/bin/$Configuration/net10.0/Migrator.Cli.dll" }
if (-not (Test-Path $CliDll)) {
    $CliDll = Get-ChildItem (Join-Path $rootPath 'Migrator.Cli/bin') -Filter Migrator.Cli.dll -Recurse -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $CliDll -or -not (Test-Path $CliDll)) { throw 'Migrator.Cli.dll was not found.' }
$verifyProjectHelp = (& dotnet $CliDll verify-project --help 2>&1 | Out-String)
if ($LASTEXITCODE -ne 0 -or $verifyProjectHelp -notmatch [regex]::Escape('--run-manifest')) {
    throw "Migrator.Cli.dll does not expose verify-project --run-manifest. Rebuild Migrator.Cli from the current sources before running the standard smoke. CLI: $CliDll"
}
$sourceDir = Join-Path $outputPath 'source'
$runDir = Join-Path $outputPath 'run-001'
New-Item -ItemType Directory -Force -Path $sourceDir | Out-Null
@'
using NUnit.Framework;
using OpenQA.Selenium;

public class LoginTests
{
    [Test]
    public void ClicksSubmit()
    {
        var submit = WebDriver.FindElement(By.CssSelector("[data-test='submit-button']"));
        submit.Click();
    }
}
'@ | Set-Content -Encoding UTF8 (Join-Path $sourceDir 'LoginTests.cs')
$watch = [Diagnostics.Stopwatch]::StartNew()
& dotnet $CliDll run --input $sourceDir --out $runDir --format both
$exitCode = $LASTEXITCODE
$watch.Stop()
$reportPath = Join-Path $runDir 'orchestration-report.json'
$generatedReportPath = Join-Path $runDir 'generated/report.json'
$verifyReportPath = Join-Path $runDir 'verify/verify-report.json'
$syntaxErrors = $null
$todoComments = $null
if (Test-Path $verifyReportPath) {
    try {
        $verifyData = Get-Content -Raw $verifyReportPath | ConvertFrom-Json
        $syntaxErrors = $verifyData.summary.syntaxErrors
        $todoComments = $verifyData.summary.todoComments
    } catch {
        Write-Warning "Could not parse verify report: $($_.Exception.Message)"
    }
}
$partitionDirectories = @(Get-ChildItem -Path $runDir -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -match '^(wave|partition)-' })
$orchestrationPassed = $exitCode -eq 0 -and (Test-Path $reportPath) -and (Test-Path $generatedReportPath) -and $partitionDirectories.Count -eq 0
$runManifestPath = Join-Path $runDir 'run-manifest.json'
$verifyProjectDir = Join-Path $runDir 'verify-project'
$projectVerifyReportPath = Join-Path $verifyProjectDir 'project-verify-report.json'
$projectVerifyEvidencePath = Join-Path $verifyProjectDir 'verification-evidence.json'
$projectVerifyExitCode = $null
$finalGateExitCode = $null
$finalGateOutput = @()

if ($orchestrationPassed) {
    & dotnet $CliDll verify-project `
        --input $sourceDir `
        --run-manifest $runManifestPath `
        --out $verifyProjectDir `
        --format both
    $projectVerifyExitCode = $LASTEXITCODE
}

if ($orchestrationPassed -and $projectVerifyExitCode -eq 0) {
    $finalGateScript = Join-Path $rootPath 'templates/migration-kit/scripts/check-final-gate.ps1'
    $finalGateOutput = @(& powershell -NoProfile -ExecutionPolicy Bypass `
        -File $finalGateScript `
        -Workspace $outputPath `
        -Run $runDir `
        -RepoRoot $rootPath 2>&1)
    $finalGateExitCode = $LASTEXITCODE
    $finalGateOutput | ForEach-Object { Write-Host $_ }
}

$status = if (
    $orchestrationPassed `
    -and $projectVerifyExitCode -eq 0 `
    -and $finalGateExitCode -eq 0 `
    -and (Test-Path $runManifestPath) `
    -and (Test-Path $projectVerifyReportPath) `
    -and (Test-Path $projectVerifyEvidencePath)
) { 'PASS' } else { 'FAIL' }

$summary = [ordered]@{
    schemaVersion = 'standard-migration-smoke/v2'
    status = $status
    orchestrationExitCode = $exitCode
    exactProjectVerifyExitCode = $projectVerifyExitCode
    finalGateExitCode = $finalGateExitCode
    durationMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
    source = $sourceDir
    run = $runDir
    runManifest = $runManifestPath
    orchestrationReport = $reportPath
    generatedReport = $generatedReportPath
    verifyReport = $verifyReportPath
    projectVerifyReport = $projectVerifyReportPath
    projectVerifyEvidence = $projectVerifyEvidencePath
    syntaxErrors = $syntaxErrors
    todoComments = $todoComments
    hiddenPartitionDirectories = @($partitionDirectories | ForEach-Object { $_.FullName })
    finalGateOutput = @($finalGateOutput | ForEach-Object { [string]$_ })
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 (Join-Path $outputPath 'standard-migration-smoke.json')
if ($status -ne 'PASS') {
    throw "Standard migration smoke failed; orchestrationExit=$exitCode; exactProjectVerifyExit=$projectVerifyExitCode; finalGateExit=$finalGateExitCode; runManifest=$(Test-Path $runManifestPath); projectVerifyReport=$(Test-Path $projectVerifyReportPath); projectVerifyEvidence=$(Test-Path $projectVerifyEvidencePath); syntaxErrors=$syntaxErrors; TODOs=$todoComments; hiddenPartitions=$($partitionDirectories.Count)"
}
Write-Host 'STANDARD_MIGRATION_SMOKE_PASS'
Write-Host "Report: $(Join-Path $outputPath 'standard-migration-smoke.json')"
