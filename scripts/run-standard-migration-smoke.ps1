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
$status = if ($exitCode -eq 0 -and (Test-Path $reportPath) -and (Test-Path $generatedReportPath) -and $partitionDirectories.Count -eq 0) { 'PASS' } else { 'FAIL' }
$summary = [ordered]@{
    schemaVersion = 'standard-migration-smoke/v1'
    status = $status
    exitCode = $exitCode
    durationMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
    source = $sourceDir
    run = $runDir
    orchestrationReport = $reportPath
    generatedReport = $generatedReportPath
    verifyReport = $verifyReportPath
    syntaxErrors = $syntaxErrors
    todoComments = $todoComments
    hiddenPartitionDirectories = @($partitionDirectories | ForEach-Object { $_.FullName })
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
}
$summary | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 (Join-Path $outputPath 'standard-migration-smoke.json')
if ($status -ne 'PASS') { throw "Standard migration smoke failed with exit code $exitCode; orchestration report: $(Test-Path $reportPath); generated report: $(Test-Path $generatedReportPath); verify report: $(Test-Path $verifyReportPath); syntax errors: $syntaxErrors; TODOs: $todoComments; hidden partitions: $($partitionDirectories.Count)" }
Write-Host 'STANDARD_MIGRATION_SMOKE_PASS'
Write-Host "Report: $(Join-Path $outputPath 'standard-migration-smoke.json')"
