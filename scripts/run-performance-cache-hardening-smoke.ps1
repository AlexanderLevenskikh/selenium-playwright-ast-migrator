[CmdletBinding()]
param(
    [string]$Root = '.',
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/test-layers/e2e-performance-cache-hardening',
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
    $sw = [Diagnostics.Stopwatch]::StartNew()
    if (-not $process.Start()) { throw "Failed to start $Name." }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    if (-not $process.WaitForExit(60000)) {
        try { $process.Kill($true) } catch { try { $process.Kill() } catch { } }
        throw "$Name timed out after 60 seconds."
    }
    $process.WaitForExit()
    $sw.Stop()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    [IO.File]::WriteAllText($stdoutPath, $stdout)
    [IO.File]::WriteAllText($stderrPath, $stderr)
    if ($process.ExitCode -notin $AllowedExitCodes) { throw "$Name returned $($process.ExitCode).`n$stdout`n$stderr" }
    return [pscustomobject]@{ StdOut=$stdout; StdErr=$stderr; ExitCode=$process.ExitCode; DurationMs=$sw.Elapsed.TotalMilliseconds }
}

Remove-Item $outputPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$scratchRoot = Join-Path $rootPath ("migration/.performance-cache-smoke-{0}-{1}" -f $PID, [Guid]::NewGuid().ToString('N'))
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

    Copy-Item (Join-Path $rootPath 'Migrator.Tests/ScenarioFixtures/ValidationHost/Artifacts/Valid.cs') (Join-Path $runPath 'generated/Smoke.cs') -Force
    $successCommand = if ($env:OS -eq 'Windows_NT') { 'dotnet --version' } else { '/bin/true' }
    Invoke-Cli '03-validation' @('migration','validate','--out',$runPath,'--validation-command',$successCommand,'--checkpoint-on-pass','true','--validation-timeout-seconds','30') | Out-Null
    Invoke-Cli '04-performance-report' @('migration','perf-report','--out',$runPath) | Out-Null

    $sourceScope = (Get-Content -Raw (Join-Path $runPath 'run-context.json') | ConvertFrom-Json).sourceScopePath
    Invoke-Cli '05-record-scope-access' @('migration','record-role-scope-access','--out',$runPath,'--role','executor','--role-phase','execution','--scope-operation','read','--scope-path',$sourceScope) | Out-Null
    Invoke-Cli '06-scope-audit' @('migration','scope-audit','--out',$runPath) | Out-Null

    Invoke-Cli '07-cache-stats' @('migration','cache-stats','--workspace',$scratchRoot) | Out-Null
    Invoke-Cli '08-cache-verify' @('migration','cache-verify','--workspace',$scratchRoot) | Out-Null
    $cacheRoot = Join-Path $scratchRoot '.cache/validation'
    $originalCache = Get-ChildItem $cacheRoot -Filter *.json -File | Select-Object -First 1
    if (-not $originalCache) { throw 'Validation did not create a cache entry.' }
    $orphanCache = Join-Path $cacheRoot 'orphan-old-compatible.json'
    Copy-Item $originalCache.FullName $orphanCache
    (Get-Item $orphanCache).LastWriteTimeUtc = [DateTime]::UtcNow.AddDays(-60)

    Invoke-Cli '09-cache-prune-dry-run' @('migration','cache-prune','--workspace',$scratchRoot,'--cache-max-age-days','30','--cache-max-size-mb','2048','--cache-apply','false') | Out-Null
    $dryRun = Get-Content -Raw (Join-Path $scratchRoot 'cache-prune.json') | ConvertFrom-Json
    if (@($dryRun.candidates) -notcontains 'orphan-old-compatible.json') { throw 'Old unreferenced cache entry was not selected for prune.' }
    if (-not (Test-Path $orphanCache)) { throw 'Dry-run cache prune removed an entry.' }

    Invoke-Cli '10-cache-prune-apply' @('migration','cache-prune','--workspace',$scratchRoot,'--cache-max-age-days','30','--cache-max-size-mb','2048','--cache-apply','true') | Out-Null
    if (Test-Path $orphanCache) { throw 'Applied cache prune did not remove the old unreferenced entry.' }
    if (-not (Test-Path $originalCache.FullName)) { throw 'Cache prune removed the active referenced entry.' }

    $outside = Join-Path $outputPath 'outside-scope.txt'
    'outside' | Set-Content -Encoding UTF8 $outside
    $violation = Invoke-Cli '11-out-of-scope-rejected' @('migration','record-role-scope-access','--out',$runPath,'--role','reviewer','--role-phase','final','--scope-operation','read','--scope-path',$outside) @(2)
    if ($violation.StdErr -notmatch 'MIGRATION_ROLE_SCOPE_AUDIT_FAIL') { throw 'Out-of-scope role access was not rejected.' }

    $performance = Get-Content -Raw (Join-Path $runPath 'performance-report.json') | ConvertFrom-Json
    $stats = Get-Content -Raw (Join-Path $scratchRoot 'cache-stats.json') | ConvertFrom-Json
    $verify = Get-Content -Raw (Join-Path $scratchRoot 'cache-verify.json') | ConvertFrom-Json
    if ($performance.status -ne 'PASS') { throw 'Aggregated performance report is not PASS.' }
    if ($verify.status -ne 'PASS' -or $stats.compatibleCount -lt 1) { throw 'Cache stats/verify did not recognize the compatible validation entry.' }

    $total.Stop()
    Copy-Item $runPath (Join-Path $outputPath 'run-evidence') -Recurse -Force
    Copy-Item (Join-Path $scratchRoot 'cache-stats.json') $outputPath
    Copy-Item (Join-Path $scratchRoot 'cache-verify.json') $outputPath
    Copy-Item (Join-Path $scratchRoot 'cache-prune.json') $outputPath
    [ordered]@{
        schema='migration-performance-cache-hardening-smoke/v1'
        generatedAtUtc=[DateTimeOffset]::UtcNow.ToString('O')
        status='PASS'
        wallClockDurationMs=[Math]::Round($total.Elapsed.TotalMilliseconds,3)
        compatibleCacheEntries=$stats.compatibleCount
        performanceStatus=$performance.status
        scopeViolationRejected=$true
        activeCacheEntryProtected=$true
        events=$events
    } | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $outputPath 'performance-cache-hardening-smoke.json')
    Write-Host 'PERFORMANCE_CACHE_HARDENING_SMOKE_PASS'
    Write-Host "Report: $(Join-Path $outputPath 'performance-cache-hardening-smoke.json')"
}
finally {
    Remove-Item $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
