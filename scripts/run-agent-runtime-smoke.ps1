[CmdletBinding()]
param(
    [string]$Root = '.',
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/agent-runtime-smoke',
    [string]$CliDll
)

$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path $Root).Path
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $rootPath $Output }
if (-not $CliDll) {
    $CliDll = Join-Path $rootPath "Migrator.Cli/bin/$Configuration/net10.0/Migrator.Cli.dll"
    if (-not (Test-Path $CliDll)) {
        $CliDll = Get-ChildItem (Join-Path $rootPath 'Migrator.Cli/bin') -Filter Migrator.Cli.dll -Recurse -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $CliDll -or -not (Test-Path $CliDll)) { throw 'Migrator.Cli.dll was not found. Build Migrator.Cli or pass -CliDll.' }

function ConvertTo-NativeArgument([string]$Value) {
    if ($null -eq $Value -or $Value.Length -eq 0) { return '""' }
    if ($Value -notmatch '[\s"]') { return $Value }
    return '"' + $Value.Replace('\', '\\').Replace('"', '\"') + '"'
}

function Set-ProcessArguments($ProcessStartInfo, [string[]]$Arguments) {
    $argumentListProperty = $ProcessStartInfo.PSObject.Properties['ArgumentList']
    if ($argumentListProperty -and $null -ne $ProcessStartInfo.ArgumentList) {
        foreach ($argument in $Arguments) { $null = $ProcessStartInfo.ArgumentList.Add($argument) }
        return
    }
    $ProcessStartInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
}

function Invoke-Cli {
    param([string]$Name, [string[]]$Arguments, [int[]]$AllowedExitCodes = @(0))
    $stdoutPath = Join-Path $outputPath "$Name.stdout.log"
    $stderrPath = Join-Path $outputPath "$Name.stderr.log"
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = (Get-Command dotnet -ErrorAction Stop).Source
    $psi.WorkingDirectory = $rootPath
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    Set-ProcessArguments $psi (@($CliDll) + $Arguments)
    $process = New-Object Diagnostics.Process
    $process.StartInfo = $psi
    $sw = [Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) { throw "Failed to start $Name." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $sw.Stop()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    [IO.File]::WriteAllText($stdoutPath, $stdout)
    [IO.File]::WriteAllText($stderrPath, $stderr)
    if ($process.ExitCode -notin $AllowedExitCodes) { throw "$Name returned $($process.ExitCode).`n$stdout`n$stderr" }
    return [pscustomobject]@{ StdOut = $stdout; StdErr = $stderr; ExitCode = $process.ExitCode; DurationMs = $sw.Elapsed.TotalMilliseconds }
}

function Assert-Action([string]$RunPath, [string]$Expected) {
    $decision = Get-Content -Raw (Join-Path $RunPath 'agent-next-action.json') | ConvertFrom-Json
    if ($decision.action -ne $Expected) { throw "Expected next action $Expected, got $($decision.action)." }
}

Remove-Item $outputPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$scratchRoot = Join-Path $rootPath ("migration/.agent-runtime-smoke-{0}-{1}" -f $PID, [Guid]::NewGuid().ToString('N'))
$sourcePath = Join-Path $outputPath 'source'
$planPath = Join-Path $scratchRoot 'plan'
$runPath = Join-Path $scratchRoot 'runs/wave-001'
New-Item -ItemType Directory -Force -Path $sourcePath | Out-Null
Copy-Item (Join-Path $rootPath 'Migrator.Tests/ScenarioFixtures/ValidationHost/SeleniumSmoke/LoginTests.cs') $sourcePath
$total = [Diagnostics.Stopwatch]::StartNew()
$events = [Collections.Generic.List[object]]::new()

try {
    foreach ($step in @(
        @{ Name='01-plan'; Args=@('migration','plan','--strategy','wavefront','--input',$sourcePath,'--workspace',$scratchRoot,'--out',$planPath,'--wave-profile','manual','--max-wave-size','1','--max-wave-files','1','--max-wave-actions','20','--hard-wave-actions','40','--max-wave-complexity','100','--hard-wave-complexity','200','--smoke-wave-size','1','--format','json') },
        @{ Name='02-run-wave'; Args=@('migration','run-wave','--plan',$planPath,'--wave','wave-001','--workspace',$scratchRoot,'--out',$runPath,'--execution-profile','fast') }
    )) {
        $result = Invoke-Cli $step.Name $step.Args
        $events.Add([ordered]@{ name=$step.Name; durationMs=[Math]::Round($result.DurationMs,3) })
    }

    $result = Invoke-Cli '03-next-executor' @('migration','next-agent-action','--out',$runPath)
    Assert-Action $runPath 'RUN_ROLE'
    Invoke-Cli '04-executor-start' @('migration','record-agent-role','--out',$runPath,'--role','executor','--role-phase','execution','--role-status','STARTED') | Out-Null
    $duplicateStart = Invoke-Cli '04a-duplicate-start-rejected' @('migration','record-agent-role','--out',$runPath,'--role','executor','--role-phase','execution','--role-status','STARTED') @(3)
    if ($duplicateStart.StdErr -notmatch 'AGENT_ROLE_ALREADY_ACTIVE') { throw 'Duplicate executor start was not rejected by the runtime.' }
    Invoke-Cli '04b-active-role' @('migration','next-agent-action','--out',$runPath) | Out-Null
    Assert-Action $runPath 'WAIT_FOR_ROLE'
    Copy-Item (Join-Path $rootPath 'Migrator.Tests/ScenarioFixtures/ValidationHost/Artifacts/Valid.cs') (Join-Path $runPath 'generated/Smoke.cs') -Force
    Invoke-Cli '05-executor-complete' @('migration','record-agent-role','--out',$runPath,'--role','executor','--role-phase','execution','--role-status','COMPLETED','--role-evidence','generated/Smoke.cs') | Out-Null

    Invoke-Cli '06-next-validation' @('migration','next-agent-action','--out',$runPath) | Out-Null
    Assert-Action $runPath 'RUN_COMMAND'
    $successCommand = if ($env:OS -eq 'Windows_NT') { 'dotnet --version' } else { '/bin/true' }
    Invoke-Cli '07-validation' @('migration','validate','--out',$runPath,'--validation-command',$successCommand,'--checkpoint-on-pass','true','--validation-timeout-seconds','30') | Out-Null

    Invoke-Cli '08-next-review-bundle' @('migration','next-agent-action','--out',$runPath) | Out-Null
    Assert-Action $runPath 'RUN_COMMAND'
    Invoke-Cli '09-review-bundle' @('migration','build-review-bundle','--out',$runPath) | Out-Null

    Invoke-Cli '10-next-reviewer' @('migration','next-agent-action','--out',$runPath) | Out-Null
    Assert-Action $runPath 'RUN_ROLE'
    Invoke-Cli '11-reviewer-start' @('migration','record-agent-role','--out',$runPath,'--role','reviewer','--role-phase','final','--role-status','STARTED') | Out-Null
    $outsideEvidence = Join-Path $outputPath 'outside-review.json'
    '{}' | Set-Content -Encoding UTF8 $outsideEvidence
    $invalidEvidence = Invoke-Cli '11a-outside-evidence-rejected' @('migration','record-agent-role','--out',$runPath,'--role','reviewer','--role-phase','final','--role-status','COMPLETED','--role-evidence',$outsideEvidence) @(2)
    if ($invalidEvidence.StdErr -notmatch 'AGENT_ROLE_EVIDENCE_INVALID') { throw 'Evidence outside the wave directory was not rejected.' }
    [ordered]@{ schema='migration-agent-review-evidence/v1'; status='PASS'; input='review/review-bundle.json' } | ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $runPath 'review/final-review.json')
    Invoke-Cli '12-reviewer-complete' @('migration','record-agent-role','--out',$runPath,'--role','reviewer','--role-phase','final','--role-status','COMPLETED','--role-evidence','review/final-review.json') | Out-Null

    Invoke-Cli '13-next-sentinel' @('migration','next-agent-action','--out',$runPath) | Out-Null
    Assert-Action $runPath 'RUN_ROLE'
    Invoke-Cli '14-sentinel-start' @('migration','record-agent-role','--out',$runPath,'--role','sentinel','--role-phase','final','--role-status','STARTED') | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $runPath 'sentinel') | Out-Null
    [ordered]@{ schema='migration-agent-sentinel-evidence/v1'; status='PASS'; findings=@() } | ConvertTo-Json -Depth 4 | Set-Content -Encoding UTF8 (Join-Path $runPath 'sentinel/smoke-inspection.json')
    Invoke-Cli '15-sentinel-complete' @('migration','record-agent-role','--out',$runPath,'--role','sentinel','--role-phase','final','--role-status','COMPLETED','--role-evidence','sentinel/smoke-inspection.json') | Out-Null

    Invoke-Cli '16-final-handoff' @('migration','next-agent-action','--out',$runPath) | Out-Null
    Assert-Action $runPath 'FINAL_HANDOFF'
    Invoke-Cli '17-budget' @('migration','check-agent-budget','--out',$runPath) | Out-Null
    Invoke-Cli '18-performance' @('migration','agent-perf-report','--out',$runPath) | Out-Null

    $riskSmokeOutput = Join-Path $outputPath 'risk-routing'
    & (Join-Path $rootPath 'scripts/run-agent-risk-routing-smoke.ps1') -Root $rootPath -Configuration $Configuration -Output $riskSmokeOutput -BaselineRun $runPath -CliDll $CliDll
    if ($LASTEXITCODE -ne 0) { throw "Adaptive risk-routing smoke failed with exit code $LASTEXITCODE." }

    $budget = Get-Content -Raw (Join-Path $runPath 'agent-budget-result.json') | ConvertFrom-Json
    $performance = Get-Content -Raw (Join-Path $runPath 'agent-lifecycle-performance.json') | ConvertFrom-Json
    $roleEvents = @(Get-Content (Join-Path $runPath 'agent-role-events.jsonl') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($budget.status -ne 'PASS' -or $budget.totalRoleInvocations -ne 3) { throw 'Unexpected agent budget result.' }
    if ($performance.roleInvocationCount -ne 3 -or $performance.completedRoleCount -ne 3) { throw 'Unexpected agent lifecycle performance result.' }
    if ($roleEvents.Count -ne 6) { throw "Expected 6 hash-chained role events, found $($roleEvents.Count)." }
    $ledger = Get-Content -Raw (Join-Path $runPath 'agent-role-ledger-head.json') | ConvertFrom-Json
    if ($ledger.eventCount -ne 6 -or [string]::IsNullOrWhiteSpace([string]$ledger.headEventHash)) { throw 'Unexpected agent role ledger head.' }

    $total.Stop()
    $evidencePath = Join-Path $outputPath 'run-evidence'
    Copy-Item $runPath $evidencePath -Recurse -Force
    [ordered]@{
        schema = 'migration-agent-runtime-smoke/v1'
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        status = 'PASS'
        wallClockDurationMs = [Math]::Round($total.Elapsed.TotalMilliseconds, 3)
        roleInvocationCount = 3
        roleEventCount = 6
        finalAction = 'FINAL_HANDOFF'
        adaptiveRiskRouting = 'PASS'
        events = $events
    } | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $outputPath 'agent-runtime-smoke.json')
    Write-Host 'AGENT_RUNTIME_SMOKE_PASS'
    Write-Host "Report: $(Join-Path $outputPath 'agent-runtime-smoke.json')"
}
finally {
    Remove-Item $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
