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

function Get-PowerShellExecutable {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) { return $pwsh.Source }

    try {
        $currentProcessPath = (Get-Process -Id $PID -ErrorAction Stop).Path
        if ($currentProcessPath -and (Test-Path $currentProcessPath)) { return $currentProcessPath }
    }
    catch { }

    $windowsPowerShell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($windowsPowerShell) { return $windowsPowerShell.Source }

    $windowsPowerShell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($windowsPowerShell) { return $windowsPowerShell.Source }

    throw 'PowerShell host executable was not found. Install PowerShell 7 or run this script from Windows PowerShell.'
}

function Get-RelativePathCompat([string]$BasePath, [string]$Path) {
    $method = [IO.Path].GetMethod('GetRelativePath', [Type[]]@([string], [string]))
    if ($method) {
        return [IO.Path]::GetRelativePath($BasePath, $Path)
    }

    $baseFull = [IO.Path]::GetFullPath($BasePath).TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)) + [IO.Path]::DirectorySeparatorChar
    $pathFull = [IO.Path]::GetFullPath($Path)
    $baseUri = [Uri]::new($baseFull)
    $pathUri = [Uri]::new($pathFull)
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/', [IO.Path]::DirectorySeparatorChar)
}

function ConvertTo-NativeArgument([string]$Value) {
    if ($null -eq $Value -or $Value.Length -eq 0) { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }

    # ProcessStartInfo.Arguments uses the standard CommandLineToArgvW-compatible
    # quoting rules on Windows, and .NET applies the same escaped form on Unix.
    $builder = New-Object Text.StringBuilder
    $null = $builder.Append('"')
    $backslashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') {
            $backslashes++
            continue
        }

        if ($character -eq '"') {
            $null = $builder.Append(('\' * (($backslashes * 2) + 1)))
            $null = $builder.Append('"')
            $backslashes = 0
            continue
        }

        if ($backslashes -gt 0) {
            $null = $builder.Append(('\' * $backslashes))
            $backslashes = 0
        }
        $null = $builder.Append($character)
    }

    if ($backslashes -gt 0) { $null = $builder.Append(('\' * ($backslashes * 2))) }
    $null = $builder.Append('"')
    return $builder.ToString()
}

function Set-ProcessArguments($ProcessStartInfo, [string[]]$Arguments) {
    $argumentListProperty = $ProcessStartInfo.PSObject.Properties['ArgumentList']
    if ($argumentListProperty -and $null -ne $ProcessStartInfo.ArgumentList) {
        foreach ($argument in $Arguments) { $null = $ProcessStartInfo.ArgumentList.Add($argument) }
        return
    }

    $ProcessStartInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
}

function Stop-ProcessTreeCompat([Diagnostics.Process]$Process) {
    try {
        $killTreeMethod = $Process.GetType().GetMethod('Kill', [Type[]]@([bool]))
        if ($killTreeMethod) {
            $null = $killTreeMethod.Invoke($Process, @($true))
        }
        else {
            $Process.Kill()
        }
    }
    catch { }
}

function Invoke-TrackedProcess {
    param(
        [string]$FileName,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$StdoutPath,
        [string]$StderrPath,
        [int]$TimeoutSeconds = 1200
    )

    $psi = [Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $FileName
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    Set-ProcessArguments $psi $Arguments

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $psi
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) { throw "Failed to start process: $FileName" }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    [long]$peakWorkingSetBytes = 0
    $timedOut = $false

    while (-not $process.WaitForExit(200)) {
        try { $peakWorkingSetBytes = [Math]::Max($peakWorkingSetBytes, [long]$process.PeakWorkingSet64) } catch { }
        if ($stopwatch.Elapsed.TotalSeconds -ge $TimeoutSeconds) {
            $timedOut = $true
            Stop-ProcessTreeCompat $process
            try { $process.WaitForExit() } catch { }
            break
        }
    }

    try { $peakWorkingSetBytes = [Math]::Max($peakWorkingSetBytes, [long]$process.PeakWorkingSet64) } catch { }
    $stopwatch.Stop()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    if ($timedOut) { $stderr += "`nProcess timed out after $TimeoutSeconds seconds." }
    [IO.File]::WriteAllText($StdoutPath, $stdout)
    [IO.File]::WriteAllText($StderrPath, $stderr)

    return [pscustomobject]@{
        ExitCode = if ($timedOut) { 124 } else { $process.ExitCode }
        TimedOut = $timedOut
        DurationMs = $stopwatch.Elapsed.TotalMilliseconds
        PeakWorkingSetBytes = $peakWorkingSetBytes
    }
}

function Get-TrxDurationMs($Result) {
    $durationText = [string]$Result.duration
    if ([string]::IsNullOrWhiteSpace($durationText)) { return 0.0 }
    $duration = [TimeSpan]::Zero
    if ([TimeSpan]::TryParse($durationText, [ref]$duration)) { return $duration.TotalMilliseconds }
    return 0.0
}

$trxPath = Join-Path $outputPath 'orchestrator-performance.trx'
$jsonPath = Join-Path $outputPath 'orchestrator-performance.json'
$mdPath = Join-Path $outputPath 'orchestrator-performance.md'
$smokeOutput = Join-Path $outputPath 'validation-host-smoke'
Remove-Item $trxPath -Force -ErrorAction SilentlyContinue

$project = Join-Path $rootPath 'Migrator.Tests/Migrator.Tests.csproj'
$testArgs = @(
    'test', $project, '-c', $Configuration,
    '--filter', 'FullyQualifiedName~Migrator.Tests.OrchestratorTests',
    '--results-directory', $outputPath,
    '--logger', 'trx;LogFileName=orchestrator-performance.trx',
    '--no-restore'
)
if ($NoBuild) { $testArgs += '--no-build' }

$testStdoutPath = Join-Path $outputPath 'orchestrator-performance.stdout.log'
$testStderrPath = Join-Path $outputPath 'orchestrator-performance.stderr.log'
$testProcess = Invoke-TrackedProcess -FileName 'dotnet' -Arguments $testArgs -WorkingDirectory $rootPath `
    -StdoutPath $testStdoutPath -StderrPath $testStderrPath -TimeoutSeconds 1200
$testExit = $testProcess.ExitCode
$orchestratorDurationMs = [double]$testProcess.DurationMs
$orchestratorPeakWorkingSetBytes = [long]$testProcess.PeakWorkingSetBytes

if (-not (Test-Path $trxPath)) {
    throw "Performance TRX was not created: $trxPath. See $testStdoutPath and $testStderrPath."
}

[xml]$trx = Get-Content -Raw $trxPath
$namespaceManager = New-Object Xml.XmlNamespaceManager($trx.NameTable)
$namespaceManager.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
$results = @($trx.SelectNodes('//t:UnitTestResult', $namespaceManager))
if ($results.Count -eq 0) {
    throw "No OrchestratorTests were discovered. Rebuild Migrator.Tests or remove -NoBuild."
}

$durations = @($results | ForEach-Object { Get-TrxDurationMs $_ })
$totalMeasurement = $durations | Measure-Object -Sum
$totalTestMs = if ($null -eq $totalMeasurement.Sum) { 0.0 } else { [double]$totalMeasurement.Sum }
$slowest = @($results | Sort-Object { Get-TrxDurationMs $_ } -Descending | Select-Object -First 10 | ForEach-Object {
    [ordered]@{ name = $_.testName; durationMs = [Math]::Round((Get-TrxDurationMs $_), 3); outcome = $_.outcome }
})

$cliDll = Join-Path $rootPath "Migrator.Cli/bin/$Configuration/net10.0/Migrator.Cli.dll"
if (-not (Test-Path $cliDll)) {
    $cliDll = Get-ChildItem (Join-Path $rootPath 'Migrator.Cli/bin') -Filter Migrator.Cli.dll -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1 -ExpandProperty FullName
}
if (-not $cliDll -or -not (Test-Path $cliDll)) {
    throw 'Migrator.Cli.dll was not found. Build Migrator.Cli or rerun without -NoBuild.'
}

$smokeScript = Join-Path $rootPath 'scripts/run-validation-host-smoke.ps1'
$smokeStdoutPath = Join-Path $outputPath 'validation-host-smoke.stdout.log'
$smokeStderrPath = Join-Path $outputPath 'validation-host-smoke.stderr.log'
$powerShellExecutable = Get-PowerShellExecutable
$smokeProcess = Invoke-TrackedProcess -FileName $powerShellExecutable -Arguments @(
    '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $smokeScript,
    '-Root', $rootPath, '-Configuration', $Configuration, '-Output', $smokeOutput, '-CliDll', $cliDll
) -WorkingDirectory $rootPath -StdoutPath $smokeStdoutPath -StderrPath $smokeStderrPath -TimeoutSeconds 300
$smokeExit = $smokeProcess.ExitCode
$validationHostPeakWorkingSetBytes = [long]$smokeProcess.PeakWorkingSetBytes
$smokeReportPath = Join-Path $smokeOutput 'validation-host-smoke.json'
$smokeReport = if (Test-Path $smokeReportPath) { Get-Content -Raw $smokeReportPath | ConvertFrom-Json } else { $null }
$smokeDurationMs = if ($null -ne $smokeReport -and $null -ne $smokeReport.wallClockDurationMs) { [double]$smokeReport.wallClockDurationMs } else { $null }
$smokeStdoutRelative = (Get-RelativePathCompat $outputPath $smokeStdoutPath).Replace('\', '/')
$smokeStderrRelative = (Get-RelativePathCompat $outputPath $smokeStderrPath).Replace('\', '/')
$smokeFailureSummary = $null
if ($smokeExit -ne 0 -or $null -eq $smokeReport -or $smokeReport.status -ne 'PASS') {
    $smokeFailureText = ''
    if (Test-Path $smokeStderrPath) { $smokeFailureText = (Get-Content -Raw $smokeStderrPath).Trim() }
    if ([string]::IsNullOrWhiteSpace($smokeFailureText) -and (Test-Path $smokeStdoutPath)) {
        $smokeFailureText = (Get-Content -Raw $smokeStdoutPath).Trim()
    }
    if (-not [string]::IsNullOrWhiteSpace($smokeFailureText)) {
        $smokeFailureSummary = if ($smokeFailureText.Length -gt 2000) { $smokeFailureText.Substring(0, 2000) } else { $smokeFailureText }
    }
}

$baselineDurationMs = $null
$regressionRatio = $null
if ($Baseline -and (Test-Path $Baseline)) {
    $baselineData = Get-Content -Raw $Baseline | ConvertFrom-Json
    $baselineDurationMs = [double]$baselineData.wallClockDurationMs
    if ($baselineDurationMs -gt 0) { $regressionRatio = $orchestratorDurationMs / $baselineDurationMs }
}

$budgetPath = Join-Path $rootPath 'Migrator.Tests/performance-budgets.json'
$budgets = Get-Content -Raw $budgetPath | ConvertFrom-Json
$orchestratorBudget = $budgets.scenarios.'orchestrator-baseline'
$validationHostBudget = $budgets.scenarios.'validation-host-smoke'
$budgetFindings = [Collections.Generic.List[object]]::new()
function Add-BudgetFinding([string]$Scenario, [double]$ActualMs, [long]$ActualPeakBytes, $Budget) {
    $softMs = [double]$Budget.softDurationSeconds * 1000
    $hardMs = [double]$Budget.hardDurationSeconds * 1000
    $softBytes = [double]$Budget.softPeakMemoryMb * 1MB
    $hardBytes = [double]$Budget.hardPeakMemoryMb * 1MB
    $durationLevel = if ($ActualMs -gt $hardMs) { 'HARD' } elseif ($ActualMs -gt $softMs) { 'SOFT' } else { 'PASS' }
    $memoryLevel = if ($ActualPeakBytes -gt $hardBytes) { 'HARD' } elseif ($ActualPeakBytes -gt $softBytes) { 'SOFT' } else { 'PASS' }
    $level = if ('HARD' -in @($durationLevel, $memoryLevel)) { 'HARD' } elseif ('SOFT' -in @($durationLevel, $memoryLevel)) { 'SOFT' } else { 'PASS' }
    $budgetFindings.Add([ordered]@{
        scenario = $Scenario
        level = $level
        durationLevel = $durationLevel
        memoryLevel = $memoryLevel
        actualDurationMs = [Math]::Round($ActualMs, 3)
        softDurationMs = $softMs
        hardDurationMs = $hardMs
        actualPeakMemoryMb = [Math]::Round($ActualPeakBytes / 1MB, 3)
        softPeakMemoryMb = [double]$Budget.softPeakMemoryMb
        hardPeakMemoryMb = [double]$Budget.hardPeakMemoryMb
    })
}
Add-BudgetFinding 'orchestrator-baseline' $orchestratorDurationMs $orchestratorPeakWorkingSetBytes $orchestratorBudget
if ($null -ne $smokeDurationMs) { Add-BudgetFinding 'validation-host-smoke' $smokeDurationMs $validationHostPeakWorkingSetBytes $validationHostBudget }

$hardBudgetFailure = @($budgetFindings | Where-Object level -eq 'HARD').Count -gt 0
$softBudgetWarning = @($budgetFindings | Where-Object level -eq 'SOFT').Count -gt 0
$status = if ($testExit -ne 0) {
    'TEST_FAIL'
} elseif ($smokeExit -ne 0 -or $null -eq $smokeReport -or $smokeReport.status -ne 'PASS') {
    'SMOKE_FAIL'
} elseif ($hardBudgetFailure) {
    'BUDGET_FAIL'
} elseif ($null -ne $regressionRatio -and $regressionRatio -gt $MaxRegressionRatio) {
    'REGRESSION'
} else {
    'PASS'
}

$report = [ordered]@{
    schema = 'migrator-performance-report/v2'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    status = $status
    configuration = $Configuration
    testCount = $results.Count
    testExitCode = $testExit
    wallClockDurationMs = [Math]::Round($orchestratorDurationMs, 3)
    orchestratorPeakWorkingSetBytes = $orchestratorPeakWorkingSetBytes
    summedTestDurationMs = [Math]::Round($totalTestMs, 3)
    validationHostSmokeExitCode = $smokeExit
    validationHostSmokeDurationMs = if ($null -eq $smokeDurationMs) { $null } else { [Math]::Round($smokeDurationMs, 3) }
    validationHostSmokePeakWorkingSetBytes = $validationHostPeakWorkingSetBytes
    validationHostSmokeReport = if ($null -ne $smokeReport) { (Get-RelativePathCompat $outputPath $smokeReportPath).Replace('\', '/') } else { $null }
    validationHostSmokeStdout = $smokeStdoutRelative
    validationHostSmokeStderr = $smokeStderrRelative
    validationHostSmokeFailure = $smokeFailureSummary
    baselineDurationMs = $baselineDurationMs
    regressionRatio = if ($null -eq $regressionRatio) { $null } else { [Math]::Round($regressionRatio, 4) }
    maxRegressionRatio = $MaxRegressionRatio
    softBudgetWarning = $softBudgetWarning
    budgets = $budgetFindings
    slowestTests = $slowest
}
$report | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 $jsonPath

$lines = @(
    '# Migrator orchestration and validation performance', '',
    "Status: **$status**", "Tests: $($results.Count)",
    "Orchestrator wall clock: $([Math]::Round($orchestratorDurationMs / 1000, 2)) s",
    "Orchestrator peak working set: $([Math]::Round($orchestratorPeakWorkingSetBytes / 1MB, 2)) MB",
    "Validation-host smoke: $(if ($null -eq $smokeDurationMs) { 'missing' } else { "$([Math]::Round($smokeDurationMs / 1000, 2)) s" })",
    "Summed test duration: $([Math]::Round($totalTestMs / 1000, 2)) s"
)
if ($null -ne $regressionRatio) { $lines += "Regression ratio: $([Math]::Round($regressionRatio, 3)) (limit $MaxRegressionRatio)" }
if ($null -ne $smokeFailureSummary) {
    $singleLineFailure = ($smokeFailureSummary -replace '[\r\n]+', ' ').Trim()
    $lines += ('Validation-host smoke failure: `{0}`' -f $singleLineFailure)
    $lines += ('Validation-host logs: `{0}`, `{1}`' -f $smokeStdoutRelative, $smokeStderrRelative)
}
$lines += @('', '## Performance budgets', '', '| Scenario | Status | Actual, ms | Soft, ms | Hard, ms | Peak, MB | Soft, MB | Hard, MB |', '|---|---|---:|---:|---:|---:|---:|---:|')
foreach ($item in $budgetFindings) { $lines += "| $($item.scenario) | $($item.level) | $($item.actualDurationMs) | $($item.softDurationMs) | $($item.hardDurationMs) | $($item.actualPeakMemoryMb) | $($item.softPeakMemoryMb) | $($item.hardPeakMemoryMb) |" }
$lines += @('', '## Slowest tests', '', '| Test | Duration, ms | Outcome |', '|---|---:|---|')
foreach ($item in $slowest) { $lines += "| $($item.name) | $($item.durationMs) | $($item.outcome) |" }
$lines | Set-Content -Encoding UTF8 $mdPath

Write-Host "PERFORMANCE_$status"
Write-Host "Report: $jsonPath"
if ($testExit -ne 0) { exit $testExit }
if ($smokeExit -ne 0) { exit $smokeExit }
if ($Enforce -and $status -in @('BUDGET_FAIL', 'REGRESSION', 'SMOKE_FAIL')) { exit 1 }
exit 0
