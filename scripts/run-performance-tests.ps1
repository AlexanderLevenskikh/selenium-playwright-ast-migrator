[CmdletBinding()]
param(
    [string]$Root = '.',
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/performance',
    [string]$Baseline,
    [double]$MaxRegressionRatio = 1.35,
    [switch]$Enforce
)

$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path $Root).Path
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $rootPath $Output }
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

$trxPath = Join-Path $outputPath 'orchestrator-performance.trx'
$jsonPath = Join-Path $outputPath 'orchestrator-performance.json'
$mdPath = Join-Path $outputPath 'orchestrator-performance.md'
Remove-Item $trxPath -Force -ErrorAction SilentlyContinue

$project = Join-Path $rootPath 'Migrator.Tests/Migrator.Tests.csproj'
$sw = [Diagnostics.Stopwatch]::StartNew()
& dotnet test $project -c $Configuration --filter 'FullyQualifiedName~Migrator.Tests.OrchestratorTests' --results-directory $outputPath --logger 'trx;LogFileName=orchestrator-performance.trx' --no-restore
$testExit = $LASTEXITCODE
$sw.Stop()

if (-not (Test-Path $trxPath)) {
    throw "Performance TRX was not created: $trxPath"
}

[xml]$trx = Get-Content -Raw $trxPath
$ns = New-Object Xml.XmlNamespaceManager($trx.NameTable)
$ns.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
$results = @($trx.SelectNodes('//t:UnitTestResult', $ns))
$durations = @($results | ForEach-Object { [TimeSpan]::Parse($_.duration).TotalMilliseconds })
$totalTestMs = ($durations | Measure-Object -Sum).Sum
$slowest = @($results | Sort-Object { [TimeSpan]::Parse($_.duration) } -Descending | Select-Object -First 10 | ForEach-Object {
    [ordered]@{ name = $_.testName; durationMs = [Math]::Round(([TimeSpan]::Parse($_.duration).TotalMilliseconds), 3); outcome = $_.outcome }
})

$baselineDurationMs = $null
$regressionRatio = $null
if ($Baseline -and (Test-Path $Baseline)) {
    $baselineData = Get-Content -Raw $Baseline | ConvertFrom-Json
    $baselineDurationMs = [double]$baselineData.wallClockDurationMs
    if ($baselineDurationMs -gt 0) { $regressionRatio = $sw.Elapsed.TotalMilliseconds / $baselineDurationMs }
}

$status = if ($testExit -ne 0) { 'TEST_FAIL' } elseif ($regressionRatio -ne $null -and $regressionRatio -gt $MaxRegressionRatio) { 'REGRESSION' } else { 'PASS' }
$report = [ordered]@{
    schema = 'migrator-performance-report/v1'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    status = $status
    configuration = $Configuration
    testCount = $results.Count
    testExitCode = $testExit
    wallClockDurationMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 3)
    summedTestDurationMs = [Math]::Round([double]$totalTestMs, 3)
    baselineDurationMs = $baselineDurationMs
    regressionRatio = if ($regressionRatio -eq $null) { $null } else { [Math]::Round($regressionRatio, 4) }
    maxRegressionRatio = $MaxRegressionRatio
    slowestTests = $slowest
}
$report | ConvertTo-Json -Depth 6 | Set-Content -Encoding UTF8 $jsonPath

$lines = @(
    '# Migrator orchestration performance', '',
    "Status: **$status**", "Tests: $($results.Count)",
    "Wall clock: $([Math]::Round($sw.Elapsed.TotalSeconds, 2)) s",
    "Summed test duration: $([Math]::Round(([double]$totalTestMs / 1000), 2)) s"
)
if ($regressionRatio -ne $null) { $lines += "Regression ratio: $([Math]::Round($regressionRatio, 3)) (limit $MaxRegressionRatio)" }
$lines += @('', '## Slowest tests', '', '| Test | Duration, ms | Outcome |', '|---|---:|---|')
foreach ($item in $slowest) { $lines += "| $($item.name) | $($item.durationMs) | $($item.outcome) |" }
$lines | Set-Content -Encoding UTF8 $mdPath

Write-Host "PERFORMANCE_$status"
Write-Host "Report: $jsonPath"
if ($testExit -ne 0) { exit $testExit }
if ($Enforce -and $status -eq 'REGRESSION') { exit 1 }
exit 0
