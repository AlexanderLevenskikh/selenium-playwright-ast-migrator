[CmdletBinding()]
param(
    [string]$Root = '.',
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/test-layers/e2e-agent-risk-routing',
    [string]$BaselineRun,
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
if (-not $BaselineRun -or -not (Test-Path $BaselineRun)) { throw 'A completed low-risk -BaselineRun is required.' }

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
function Read-Json([string]$Path) { return Get-Content -Raw $Path | ConvertFrom-Json }
function Write-Json([string]$Path, $Value) { $Value | ConvertTo-Json -Depth 20 | Set-Content -Encoding UTF8 $Path }
function Assert-Action([string]$RunPath, [string]$Expected) {
    $decision = Read-Json (Join-Path $RunPath 'agent-next-action.json')
    if ($decision.action -ne $Expected) { throw "Expected $Expected, got $($decision.action)." }
}

Remove-Item $outputPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$backupPath = Join-Path $outputPath 'baseline-backup'
Copy-Item $BaselineRun $backupPath -Recurse -Force
$runPath = (Resolve-Path $BaselineRun).Path

function Restore-Baseline {
    Remove-Item $runPath -Recurse -Force
    Copy-Item $backupPath $runPath -Recurse -Force
}

try {
    # Low risk: the already completed baseline must stay compact and watchdog-free.
    Invoke-Cli '01-low-assess' @('migration','assess-agent-risk','--out',$runPath) | Out-Null
    $low = Read-Json (Join-Path $runPath 'agent-risk-assessment.json')
    if ($low.riskLevel -ne 'low' -or $low.adaptiveBudget.maxTotalRoleInvocations -ne 4 -or $low.adaptiveBudget.perRole.watchdog -ne 0) {
        throw 'Low-risk adaptive budget was not compact or watchdog-free.'
    }
    Copy-Item (Join-Path $runPath 'agent-risk-assessment.json') (Join-Path $outputPath 'low-risk-assessment.json') -Force

    # High risk: deterministic no-progress plus unresolved unmapped work must route watchdog.
    Restore-Baseline
    Write-Json (Join-Path $runPath 'no-progress-result.json') ([ordered]@{ schemaVersion='migration-no-progress-result/v1'; status='NO_PROGRESS_DETECTED'; signature='risk-smoke-high' })
    $highBundlePath = Join-Path $runPath 'review/review-bundle.json'
    $highBundle = Read-Json $highBundlePath
    $highBundle.riskFlags = @('no-progress-detected','remaining-unmapped')
    $highBundle.unmappedCount = 2
    Write-Json $highBundlePath $highBundle
    Invoke-Cli '02-high-next' @('migration','next-agent-action','--out',$runPath) | Out-Null
    Assert-Action $runPath 'RUN_ROLE'
    $highDecision = Read-Json (Join-Path $runPath 'agent-next-action.json')
    $highRisk = Read-Json (Join-Path $runPath 'agent-risk-assessment.json')
    if ($highDecision.role -ne 'watchdog' -or $highRisk.riskLevel -ne 'high' -or $highRisk.adaptiveBudget.perRole.watchdog -lt 1) {
        throw 'High-risk evidence did not enable and route the watchdog.'
    }
    Copy-Item (Join-Path $runPath 'agent-risk-assessment.json') (Join-Path $outputPath 'high-risk-assessment.json') -Force
    Copy-Item (Join-Path $runPath 'agent-next-action.json') (Join-Path $outputPath 'high-risk-next-action.json') -Force

    # Critical risk: assertion/gate weakening must stop automatic dispatch.
    Restore-Baseline
    $criticalBundlePath = Join-Path $runPath 'review/review-bundle.json'
    $criticalBundle = Read-Json $criticalBundlePath
    $criticalBundle.riskFlags = @('assertion-suppression')
    Write-Json $criticalBundlePath $criticalBundle
    Invoke-Cli '03-critical-next' @('migration','next-agent-action','--out',$runPath) @(4) | Out-Null
    Assert-Action $runPath 'HUMAN_REVIEW_REQUIRED'
    $criticalRisk = Read-Json (Join-Path $runPath 'agent-risk-assessment.json')
    if ($criticalRisk.riskLevel -ne 'critical' -or $criticalRisk.automaticContinuationAllowed -ne $false) {
        throw 'Critical risk did not stop automatic continuation.'
    }
    Copy-Item (Join-Path $runPath 'agent-risk-assessment.json') (Join-Path $outputPath 'critical-risk-assessment.json') -Force
    Copy-Item (Join-Path $runPath 'agent-next-action.json') (Join-Path $outputPath 'critical-risk-next-action.json') -Force

    # Stale dispatch: remove terminal history/evidence, route executor, then change risk evidence.
    Restore-Baseline
    Remove-Item (Join-Path $runPath 'agent-role-events.jsonl') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $runPath 'agent-role-ledger-head.json') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $runPath 'validation-result.json') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $runPath 'no-progress-result.json') -Force -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $runPath 'agent-next-action.json') -Force -ErrorAction SilentlyContinue
    Invoke-Cli '04-stale-route' @('migration','next-agent-action','--out',$runPath) | Out-Null
    Assert-Action $runPath 'RUN_ROLE'
    Write-Json (Join-Path $runPath 'no-progress-result.json') ([ordered]@{ schemaVersion='migration-no-progress-result/v1'; status='NO_PROGRESS_DETECTED'; signature='risk-changed-after-route' })
    $stale = Invoke-Cli '05-stale-start-rejected' @('migration','record-agent-role','--out',$runPath,'--role','executor','--role-phase','execution','--role-status','STARTED') @(3)
    if ($stale.StdErr -notmatch 'adaptive risk assessment changed after routing') { throw 'Stale risk-bound dispatch was not rejected.' }

    [ordered]@{
        schemaVersion = 'migration-agent-risk-routing-smoke/v1'
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        status = 'PASS'
        lowRisk = [ordered]@{ level=$low.riskLevel; score=$low.riskScore; maxRoles=$low.adaptiveBudget.maxTotalRoleInvocations; watchdog=$low.adaptiveBudget.perRole.watchdog }
        highRisk = [ordered]@{ level=$highRisk.riskLevel; score=$highRisk.riskScore; nextRole=$highDecision.role; watchdog=$highRisk.adaptiveBudget.perRole.watchdog }
        criticalRisk = [ordered]@{ level=$criticalRisk.riskLevel; score=$criticalRisk.riskScore; action='HUMAN_REVIEW_REQUIRED' }
        staleDispatchRejected = $true
    } | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 (Join-Path $outputPath 'agent-risk-routing-smoke.json')
}
finally {
    if (Test-Path $backupPath) { Restore-Baseline }
    Remove-Item $backupPath -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host 'AGENT_RISK_ROUTING_SMOKE_PASS'
Write-Host "Report: $(Join-Path $outputPath 'agent-risk-routing-smoke.json')"
exit 0
