[CmdletBinding()]
param(
    [string]$Root = '.',
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts/validation-host-smoke',
    [string]$CliDll
)

$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path $Root).Path

function Test-IsWindowsPlatform {
    if (Get-Variable IsWindows -ErrorAction SilentlyContinue) { return [bool]$IsWindows }
    return $env:OS -eq 'Windows_NT'
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

function Invoke-DotNetCliCaptured {
    param(
        [string]$CliPath,
        [string[]]$Arguments,
        [string]$WorkingDirectory,
        [string]$StdoutPath,
        [string]$StderrPath
    )

    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    $psi = New-Object Diagnostics.ProcessStartInfo
    $psi.FileName = $dotnetCommand.Source
    $psi.WorkingDirectory = $WorkingDirectory
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    Set-ProcessArguments $psi (@($CliPath) + $Arguments)

    $process = New-Object Diagnostics.Process
    $process.StartInfo = $psi
    if (-not $process.Start()) { throw 'Failed to start dotnet for validation-host smoke.' }

    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $process.WaitForExit()
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    [IO.File]::WriteAllText($StdoutPath, $stdout)
    [IO.File]::WriteAllText($StderrPath, $stderr)

    return [pscustomobject]@{ ExitCode = $process.ExitCode; StdOut = $stdout; StdErr = $stderr }
}

$isWindowsPlatform = Test-IsWindowsPlatform
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $rootPath $Output }
if (-not $CliDll) {
    $CliDll = Join-Path $rootPath "Migrator.Cli/bin/$Configuration/net10.0/Migrator.Cli.dll"
    if (-not (Test-Path $CliDll)) {
        $CliDll = Get-ChildItem (Join-Path $rootPath 'Migrator.Cli/bin') -Filter Migrator.Cli.dll -Recurse -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $CliDll -or -not (Test-Path $CliDll)) {
    throw 'Migrator.Cli.dll was not found. Build Migrator.Cli or pass -CliDll.'
}

Remove-Item $outputPath -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$scratchRoot = Join-Path $rootPath ("migration/.validation-host-smoke-{0}-{1}" -f $PID, [Guid]::NewGuid().ToString('N'))
$sourcePath = Join-Path $outputPath 'source'
$workspacePath = $scratchRoot
$planPath = Join-Path $workspacePath 'plan'
$runPath = Join-Path $workspacePath 'runs/wave-001'
New-Item -ItemType Directory -Force -Path $sourcePath | Out-Null
Copy-Item (Join-Path $rootPath 'Migrator.Tests/ScenarioFixtures/ValidationHost/SeleniumSmoke/LoginTests.cs') $sourcePath

$totalSw = [Diagnostics.Stopwatch]::StartNew()
$events = [Collections.Generic.List[object]]::new()
function Invoke-Cli {
    param([string]$Name, [string[]]$Arguments, [int[]]$ExpectedExitCodes = @(0))
    $stdoutPath = Join-Path $outputPath "$Name.stdout.log"
    $stderrPath = Join-Path $outputPath "$Name.stderr.log"
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $processResult = Invoke-DotNetCliCaptured -CliPath $CliDll -Arguments $Arguments -WorkingDirectory $rootPath `
        -StdoutPath $stdoutPath -StderrPath $stderrPath
    $exitCode = $processResult.ExitCode
    $sw.Stop()
    $events.Add([ordered]@{
        name = $Name
        exitCode = $exitCode
        durationMs = [Math]::Round($sw.Elapsed.TotalMilliseconds, 3)
        stdout = (Get-RelativePathCompat $outputPath $stdoutPath).Replace('\', '/')
        stderr = (Get-RelativePathCompat $outputPath $stderrPath).Replace('\', '/')
    })
    if ($ExpectedExitCodes -notcontains $exitCode) {
        $outText = if (Test-Path $stdoutPath) { Get-Content -Raw $stdoutPath } else { '' }
        $errText = if (Test-Path $stderrPath) { Get-Content -Raw $stderrPath } else { '' }
        throw "$Name returned $exitCode; expected $($ExpectedExitCodes -join ',').`n$outText`n$errText"
    }
    return @{ ExitCode = $exitCode; StdOut = $processResult.StdOut; StdErr = $processResult.StdErr }
}

try {
Invoke-Cli '01-plan' @(
    'migration', 'plan', '--strategy', 'wavefront', '--input', $sourcePath,
    '--workspace', $workspacePath, '--out', $planPath, '--wave-profile', 'manual',
    '--max-wave-size', '1', '--max-wave-files', '1', '--max-wave-actions', '20',
    '--hard-wave-actions', '40', '--max-wave-complexity', '100', '--hard-wave-complexity', '200',
    '--smoke-wave-size', '1', '--format', 'json'
) | Out-Null

Invoke-Cli '02-run-wave' @(
    'migration', 'run-wave', '--plan', $planPath, '--wave', 'wave-001',
    '--workspace', $workspacePath, '--out', $runPath, '--execution-profile', 'fast'
) | Out-Null

Copy-Item (Join-Path $rootPath 'Migrator.Tests/ScenarioFixtures/ValidationHost/Artifacts/Valid.cs') (Join-Path $runPath 'generated/Smoke.cs') -Force

$configResult = Invoke-Cli '03-configuration-required' @(
    'migration', 'validate', '--out', $runPath, '--checkpoint-on-pass', 'false'
) @(2)
if ($configResult.StdErr -notmatch 'VALIDATION_HOST_CONFIGURATION_REQUIRED') {
    throw 'Code changes without executable project evidence were not rejected.'
}

$successCommand = if ($isWindowsPlatform) { 'dotnet --version' } else { '/bin/true' }
$passResult = Invoke-Cli '04-pass' @(
    'migration', 'validate', '--out', $runPath, '--validation-command', $successCommand,
    '--checkpoint-on-pass', 'true', '--validation-timeout-seconds', '30'
)
if ($passResult.StdOut -notmatch 'MIGRATION_VALIDATION_HOST_PASS') { throw 'PASS marker missing.' }

$cacheResult = Invoke-Cli '05-cache-hit' @(
    'migration', 'validate', '--out', $runPath, '--validation-command', $successCommand,
    '--checkpoint-on-pass', 'true', '--validation-timeout-seconds', '30'
)
if ($cacheResult.StdOut -notmatch 'MIGRATION_VALIDATION_HOST_CACHE_HIT') { throw 'CACHE_HIT marker missing.' }
$cacheHostResult = Get-Content -Raw (Join-Path $runPath 'validation-host-result.json') | ConvertFrom-Json
if (@($cacheHostResult.checks).Count -ne 3 -or @($cacheHostResult.checks | Where-Object status -ne 'PASS').Count -ne 0) {
    throw 'Cache hit skipped or failed the cheap internal validation checks.'
}

$contractMissCommand = if ($isWindowsPlatform) { 'Write-Output contract-miss' } else { 'printf contract-miss' }
$contractMissResult = Invoke-Cli '06-contract-cache-miss' @(
    'migration', 'validate', '--out', $runPath, '--validation-command', $contractMissCommand,
    '--checkpoint-on-pass', 'false', '--validation-timeout-seconds', '30'
)
if ($contractMissResult.StdOut -notmatch 'MIGRATION_VALIDATION_HOST_PASS') {
    throw 'A changed validation contract was incorrectly served by the previous cache entry.'
}
if ($contractMissResult.StdOut -match 'MIGRATION_VALIDATION_HOST_CACHE_HIT') {
    throw 'Validation contract change did not invalidate the host cache.'
}

$timeoutCommand = if ($isWindowsPlatform) { 'Start-Sleep -Seconds 5' } else { 'sleep 5' }
$timeoutResult = Invoke-Cli '07-timeout-failure' @(
    'migration', 'validate', '--out', $runPath, '--validation-command', $timeoutCommand,
    '--checkpoint-on-pass', 'false', '--validation-timeout-seconds', '1', '--force-validation', 'true'
) @(1)
if ($timeoutResult.StdOut -notmatch 'MIGRATION_VALIDATION_HOST_FAIL') { throw 'Timeout FAIL marker missing.' }
$timeoutHostResult = Get-Content -Raw (Join-Path $runPath 'validation-host-result.json') | ConvertFrom-Json
$timedOutProcess = @($timeoutHostResult.processes | Where-Object timedOut -eq $true)
if ($timedOutProcess.Count -ne 1 -or $timedOutProcess[0].exitCode -ne 124) {
    throw 'Timed-out process evidence was not recorded with exit code 124.'
}

Copy-Item (Join-Path $rootPath 'Migrator.Tests/ScenarioFixtures/ValidationHost/Artifacts/Invalid.cs') (Join-Path $runPath 'generated/Smoke.cs') -Force
$syntaxResult = Invoke-Cli '08-syntax-failure' @(
    'migration', 'validate', '--out', $runPath, '--validation-command', $successCommand,
    '--checkpoint-on-pass', 'false', '--validation-timeout-seconds', '30'
) @(1)
if ($syntaxResult.StdOut -notmatch 'MIGRATION_VALIDATION_HOST_FAIL') { throw 'Syntax FAIL marker missing.' }

$hostResult = Get-Content -Raw (Join-Path $runPath 'validation-host-result.json') | ConvertFrom-Json
if ($hostResult.status -ne 'FAIL') { throw 'Final host result should describe the syntax failure.' }
if (($hostResult.checks | Where-Object id -eq 'generated-source-sanity').status -ne 'FAIL') {
    throw 'Generated source syntax failure was not recorded.'
}
$hostRunHistory = @(Get-ChildItem (Join-Path $runPath 'validation/host-runs') -Filter *.json -File)
if ($hostRunHistory.Count -ne 6) { throw "Expected 6 immutable validation host runs, found $($hostRunHistory.Count)." }
$processRunDirectories = @(Get-ChildItem (Join-Path $runPath 'validation/processes') -Directory)
if ($processRunDirectories.Count -ne 3) { throw "Expected independent PASS, contract-miss, and timeout process evidence, found $($processRunDirectories.Count) directories." }

$totalSw.Stop()
$evidencePath = Join-Path $outputPath 'run-evidence'
Copy-Item $runPath $evidencePath -Recurse -Force
$summary = [ordered]@{
    schema = 'migration-validation-host-smoke/v1'
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    status = 'PASS'
    wallClockDurationMs = [Math]::Round($totalSw.Elapsed.TotalMilliseconds, 3)
    cliDll = (Resolve-Path $CliDll).Path
    scenarios = @('configuration-required', 'executed-pass', 'exact-input-and-contract-cache-hit', 'validation-contract-cache-miss', 'process-timeout', 'generated-syntax-failure')
    events = $events
    resultPath = 'run-evidence/validation-host-result.json'
    immutableHostRunCount = $hostRunHistory.Count
    processEvidenceRunCount = $processRunDirectories.Count
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $outputPath 'validation-host-smoke.json')
Write-Host 'VALIDATION_HOST_SMOKE_PASS'
Write-Host "Report: $(Join-Path $outputPath 'validation-host-smoke.json')"
}
finally {
    Remove-Item $scratchRoot -Recurse -Force -ErrorAction SilentlyContinue
}
exit 0
