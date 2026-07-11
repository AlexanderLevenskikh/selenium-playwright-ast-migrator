[CmdletBinding()]
param(
    [string]$Root = '.',
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/test-layers/e2e-agent-recovery',
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
    $property = $ProcessStartInfo.PSObject.Properties['ArgumentList']
    if ($property -and $null -ne $ProcessStartInfo.ArgumentList) {
        foreach ($argument in $Arguments) { $null = $ProcessStartInfo.ArgumentList.Add($argument) }
    } else {
        $ProcessStartInfo.Arguments = (($Arguments | ForEach-Object { ConvertTo-NativeArgument $_ }) -join ' ')
    }
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
    if (-not $process.Start()) { throw "Failed to start $Name." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    [IO.File]::WriteAllText($stdoutPath, $stdout)
    [IO.File]::WriteAllText($stderrPath, $stderr)
    if ($process.ExitCode -notin $AllowedExitCodes) { throw "$Name returned $($process.ExitCode).`n$stdout`n$stderr" }
    return [pscustomobject]@{ StdOut=$stdout; StdErr=$stderr; ExitCode=$process.ExitCode }
}
function Read-Json([string]$Path) { Get-Content -Raw $Path | ConvertFrom-Json }
function Write-Json([string]$Path, $Value) { $Value | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 $Path }
function Assert-RecoveryStatus([string]$RunPath, [string]$Expected) {
    $plan = Read-Json (Join-Path $RunPath 'agent-recovery-plan.json')
    if ($plan.status -ne $Expected) { throw "Expected recovery status $Expected, got $($plan.status)." }
}

Remove-Item $outputPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$scratchRoot = Join-Path $rootPath ("migration/.agent-recovery-smoke-{0}-{1}" -f $PID, [Guid]::NewGuid().ToString('N'))
$sourcePath = Join-Path $outputPath 'source'
$planPath = Join-Path $scratchRoot 'plan'
$runPath = Join-Path $scratchRoot 'runs/wave-001'
New-Item -ItemType Directory -Force -Path $sourcePath | Out-Null
Copy-Item (Join-Path $rootPath 'Migrator.Tests/ScenarioFixtures/ValidationHost/SeleniumSmoke/LoginTests.cs') $sourcePath
$total = [Diagnostics.Stopwatch]::StartNew()

try {
    Invoke-Cli '01-plan' @('migration','plan','--strategy','wavefront','--input',$sourcePath,'--workspace',$scratchRoot,'--out',$planPath,'--wave-profile','manual','--max-wave-size','1','--max-wave-files','1','--max-wave-actions','20','--hard-wave-actions','40','--max-wave-complexity','100','--hard-wave-complexity','200','--smoke-wave-size','1','--format','json') | Out-Null
    Invoke-Cli '02-run-wave' @('migration','run-wave','--plan',$planPath,'--wave','wave-001','--workspace',$scratchRoot,'--out',$runPath,'--execution-profile','fast') | Out-Null
    Invoke-Cli '03-route-executor' @('migration','next-agent-action','--out',$runPath) | Out-Null
    Invoke-Cli '04-start-executor' @('migration','record-agent-role','--out',$runPath,'--role','executor','--role-phase','execution','--role-status','STARTED','--role-lease-seconds','120') | Out-Null
    if (-not (Test-Path (Join-Path $runPath 'agent-role-lease.json'))) { throw 'STARTED did not create a durable role lease.' }

    $beforeHeartbeat = Read-Json (Join-Path $runPath 'agent-role-lease.json')
    Start-Sleep -Milliseconds 20
    Invoke-Cli '05-heartbeat' @('migration','heartbeat-agent-role','--out',$runPath,'--role','executor','--role-phase','execution','--role-lease-seconds','180') | Out-Null
    $afterHeartbeat = Read-Json (Join-Path $runPath 'agent-role-lease.json')
    if ([DateTimeOffset]$afterHeartbeat.expiresAtUtc -le [DateTimeOffset]$beforeHeartbeat.expiresAtUtc) { throw 'Heartbeat did not extend the active lease.' }

    Start-Sleep -Milliseconds 1100
    Invoke-Cli '05b-heartbeat-after-start-threshold' @('migration','heartbeat-agent-role','--out',$runPath,'--role','executor','--role-phase','execution','--role-lease-seconds','180') | Out-Null
    Invoke-Cli '05c-plan-fresh-heartbeat' @('migration','plan-agent-recovery','--out',$runPath,'--recovery-stale-after-seconds','1') @(3) | Out-Null
    Assert-RecoveryStatus $runPath 'WAIT_FOR_ROLE'
    $oversized = Invoke-Cli '05d-reject-oversized-lease' @('migration','heartbeat-agent-role','--out',$runPath,'--role','executor','--role-phase','execution','--role-lease-seconds','7201') @(2)
    if ($oversized.StdErr -notmatch 'AGENT_ROLE_LEASE_SECONDS_INVALID') { throw 'Oversized lease was not rejected explicitly.' }

    $invalidLeasePath = Join-Path $outputPath 'invalid-lease-run'
    Copy-Item $runPath $invalidLeasePath -Recurse -Force
    $invalidLease = Read-Json (Join-Path $invalidLeasePath 'agent-role-lease.json')
    $invalidLease.expiresAtUtc = $invalidLease.heartbeatAtUtc
    Write-Json (Join-Path $invalidLeasePath 'agent-role-lease.json') $invalidLease
    Invoke-Cli '05e-block-invalid-lease' @('migration','plan-agent-recovery','--out',$invalidLeasePath) @(4) | Out-Null
    Assert-RecoveryStatus $invalidLeasePath 'BLOCKED'

    Invoke-Cli '06-wait-active' @('migration','next-agent-action','--out',$runPath) | Out-Null
    $waitDecision = Read-Json (Join-Path $runPath 'agent-next-action.json')
    if ($waitDecision.action -ne 'WAIT_FOR_ROLE' -or $waitDecision.trigger -ne 'active-lease') { throw 'Valid lease did not produce WAIT_FOR_ROLE.' }

    $expired = Read-Json (Join-Path $runPath 'agent-role-lease.json')
    $expired.acquiredAtUtc = '2000-01-01T00:00:00Z'
    $expired.heartbeatAtUtc = '2000-01-01T00:00:01Z'
    $expired.expiresAtUtc = '2000-01-01T00:00:02Z'
    Write-Json (Join-Path $runPath 'agent-role-lease.json') $expired

    Invoke-Cli '07-plan-stale' @('migration','plan-agent-recovery','--out',$runPath,'--recovery-stale-after-seconds','1') | Out-Null
    Assert-RecoveryStatus $runPath 'SAFE_REPAIR_AVAILABLE'
    Invoke-Cli '08-route-recovery' @('migration','next-agent-action','--out',$runPath) | Out-Null
    $recoverDecision = Read-Json (Join-Path $runPath 'agent-next-action.json')
    if ($recoverDecision.action -ne 'RUN_COMMAND' -or $recoverDecision.command -notmatch 'recover-agent-runtime') { throw 'Stale lease did not route deterministic recovery.' }

    Invoke-Cli '09-recover-stale' @('migration','recover-agent-runtime','--out',$runPath,'--recovery-stale-after-seconds','1') | Out-Null
    Assert-RecoveryStatus $runPath 'CLEAN'
    if (Test-Path (Join-Path $runPath 'agent-role-lease.json')) { throw 'Recovered stale lease remained active.' }
    $events = @(Get-Content (Join-Path $runPath 'agent-role-events.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
    if ($events.Count -ne 2 -or $events[-1].status -ne 'FAILED' -or $events[-1].reason -notmatch 'RECOVERED_STALE_ROLE_LEASE') { throw 'Stale role was not closed with append-only FAILED evidence.' }
    if (-not (Test-Path (Join-Path $runPath 'recovery/leases'))) { throw 'Recovered lease was not archived.' }

    Remove-Item (Join-Path $runPath 'agent-role-ledger-head.json') -Force
    Invoke-Cli '10-plan-head' @('migration','plan-agent-recovery','--out',$runPath) | Out-Null
    Assert-RecoveryStatus $runPath 'SAFE_REPAIR_AVAILABLE'
    Invoke-Cli '11-rebuild-head' @('migration','recover-agent-runtime','--out',$runPath) | Out-Null
    $head = Read-Json (Join-Path $runPath 'agent-role-ledger-head.json')
    if ($head.eventCount -ne 2 -or $head.headEventHash -ne $events[-1].eventHash) { throw 'Ledger head was not rebuilt from the valid journal.' }

    Set-Content -Encoding UTF8 (Join-Path $runPath 'agent-next-action.json.tmp-crash') '{"partial":true}'
    Invoke-Cli '12-plan-temp' @('migration','plan-agent-recovery','--out',$runPath) | Out-Null
    Assert-RecoveryStatus $runPath 'SAFE_REPAIR_AVAILABLE'
    Invoke-Cli '13-quarantine-temp' @('migration','recover-agent-runtime','--out',$runPath) | Out-Null
    if (-not (Get-ChildItem (Join-Path $runPath 'recovery/quarantine') -File -ErrorAction SilentlyContinue)) { throw 'Atomic temp artifact was not quarantined.' }

    $corruptPath = Join-Path $outputPath 'corrupt-run'
    Copy-Item $runPath $corruptPath -Recurse -Force
    $journalPath = Join-Path $corruptPath 'agent-role-events.jsonl'
    $journalBefore = [IO.File]::ReadAllText($journalPath)
    $corruptEvents = @($journalBefore -split "`r?`n" | Where-Object { $_ })
    $last = $corruptEvents[-1] | ConvertFrom-Json
    $last.eventHash = ('0' * 64)
    $corruptEvents[-1] = ($last | ConvertTo-Json -Compress -Depth 10)
    [IO.File]::WriteAllText($journalPath, (($corruptEvents -join [Environment]::NewLine) + [Environment]::NewLine))
    $corruptAfterMutation = [IO.File]::ReadAllText($journalPath)
    Invoke-Cli '14-block-corrupt' @('migration','plan-agent-recovery','--out',$corruptPath) @(4) | Out-Null
    Assert-RecoveryStatus $corruptPath 'BLOCKED'
    Invoke-Cli '15-refuse-corrupt-repair' @('migration','recover-agent-runtime','--out',$corruptPath) @(4) | Out-Null
    if ([IO.File]::ReadAllText($journalPath) -ne $corruptAfterMutation) { throw 'Malformed journal was rewritten by automatic recovery.' }

    $total.Stop()
    Copy-Item $runPath (Join-Path $outputPath 'run-evidence') -Recurse -Force
    [ordered]@{
        schemaVersion = 'migration-agent-recovery-smoke/v1'
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        status = 'PASS'
        wallClockDurationMs = [Math]::Round($total.Elapsed.TotalMilliseconds, 3)
        durableLease = 'PASS'
        heartbeat = 'PASS'
        latestHeartbeatFreshness = 'PASS'
        leaseBounds = 'PASS'
        invalidLeaseBlocked = 'PASS'
        staleRoleClosedAppendOnly = 'PASS'
        ledgerHeadRebuilt = 'PASS'
        atomicTempQuarantined = 'PASS'
        malformedJournalRewriteRefused = 'PASS'
    } | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 (Join-Path $outputPath 'agent-recovery-smoke.json')
    Write-Host 'AGENT_RECOVERY_SMOKE_PASS'
    Write-Host "Report: $(Join-Path $outputPath 'agent-recovery-smoke.json')"
}
finally {
    Remove-Item $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
