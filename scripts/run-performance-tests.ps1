[CmdletBinding()]
param(
    [string]$Root = '.',
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/performance',
    [string]$Baseline,
    [double]$MaxRegressionRatio = 1.35,
    [switch]$Enforce,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path $Root).Path
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $rootPath $Output }
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$testArgs = @(
    'test', (Join-Path $rootPath 'Migrator.Tests/Migrator.Tests.csproj'),
    '--configuration', $Configuration,
    '--no-restore',
    '--filter', 'FullyQualifiedName~Migrator.Tests.StandardMigrationModeTests',
    '--verbosity', 'minimal'
)
if ($NoBuild) { $testArgs += '--no-build' }
$watch = [Diagnostics.Stopwatch]::StartNew()
& dotnet @testArgs
$testExit = $LASTEXITCODE
$watch.Stop()
$smokeOutput = Join-Path $outputPath 'standard-migration-smoke'
$smokeWatch = [Diagnostics.Stopwatch]::StartNew()
& (Join-Path $rootPath 'scripts/run-standard-migration-smoke.ps1') -Root $rootPath -Configuration $Configuration -Output $smokeOutput
$smokeExit = $LASTEXITCODE
$smokeWatch.Stop()
$currentDurationMs = $watch.Elapsed.TotalMilliseconds + $smokeWatch.Elapsed.TotalMilliseconds
$baselineDurationMs = $null
$regressionRatio = $null
if ($Baseline -and (Test-Path $Baseline)) {
    $baselineData = Get-Content -Raw $Baseline | ConvertFrom-Json
    if ($null -ne $baselineData.wallClockDurationMs) {
        $baselineDurationMs = [double]$baselineData.wallClockDurationMs
        if ($baselineDurationMs -gt 0) { $regressionRatio = $currentDurationMs / $baselineDurationMs }
    }
}
$status = if ($testExit -ne 0) { 'TEST_FAIL' } elseif ($smokeExit -ne 0) { 'SMOKE_FAIL' } elseif ($null -ne $regressionRatio -and $regressionRatio -gt $MaxRegressionRatio) { 'REGRESSION' } else { 'PASS' }
$report = [ordered]@{
    schema = 'migrator-performance-report/v3'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    status = $status
    configuration = $Configuration
    contractTestExitCode = $testExit
    contractTestDurationMs = [Math]::Round($watch.Elapsed.TotalMilliseconds, 3)
    wallClockDurationMs = [Math]::Round($currentDurationMs, 3)
    standardMigrationSmokeExitCode = $smokeExit
    standardMigrationSmokeDurationMs = [Math]::Round($smokeWatch.Elapsed.TotalMilliseconds, 3)
    baselineDurationMs = $baselineDurationMs
    regressionRatio = if ($null -eq $regressionRatio) { $null } else { [Math]::Round($regressionRatio, 4) }
    maxRegressionRatio = $MaxRegressionRatio
}
$jsonPath = Join-Path $outputPath 'performance-report.json'
$report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $jsonPath
@("# Migrator performance", "", "Status: **$status**", "Standard-mode contract tests: $([Math]::Round($watch.Elapsed.TotalSeconds, 2)) s", "Standard migration smoke: $([Math]::Round($smokeWatch.Elapsed.TotalSeconds, 2)) s") | Set-Content -Encoding UTF8 (Join-Path $outputPath 'performance-report.md')
Write-Host "PERFORMANCE_$status"
if ($testExit -ne 0) { exit $testExit }
if ($smokeExit -ne 0) { exit $smokeExit }
if ($Enforce -and $status -eq 'REGRESSION') { exit 1 }
exit 0
