param(
    [string]$Workspace = "migration",
    [string]$RepoRoot = ".",
    [string]$RunId = ""
)

$ErrorActionPreference = "Stop"

function Read-TextIfExists([string]$Path) {
    if (Test-Path $Path) { return Get-Content -Raw -Path $Path }
    return ""
}

function Get-RelativePathCompat([string]$BasePath, [string]$FullPath) {
    $baseFull = [System.IO.Path]::GetFullPath($BasePath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $targetFull = [System.IO.Path]::GetFullPath($FullPath)
    try {
        return [System.IO.Path]::GetRelativePath($baseFull, $targetFull)
    }
    catch {
        return $targetFull
    }
}

function Add-Check($Checks, [string]$Name, [bool]$Passed, [string]$Detail, [string]$Path = "") {
    $Checks.Add([ordered]@{
        name = $Name
        passed = $Passed
        detail = $Detail
        path = $Path
    })
}

function Get-LatestRunId([string]$WorkspacePath, [string]$ExplicitRunId) {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitRunId)) { return $ExplicitRunId }

    $harnessRunPath = Join-Path $WorkspacePath "state/harness-run.json"
    if (Test-Path $harnessRunPath) {
        try {
            $run = Get-Content -Raw -Path $harnessRunPath | ConvertFrom-Json -ErrorAction Stop
            foreach ($candidate in @([string]$run.runId, [string]$run.latestRunId, [string]$run.activeRunId)) {
                if (-not [string]::IsNullOrWhiteSpace($candidate)) { return $candidate }
            }
        }
        catch { }
    }

    $agentStatePath = Join-Path $WorkspacePath "agent-state.md"
    if (Test-Path $agentStatePath) {
        $match = [regex]::Match((Get-Content -Raw -Path $agentStatePath), '(?im)^\s*Latest run:\s*(run-[A-Za-z0-9_.-]+)\s*$')
        if ($match.Success) { return $match.Groups[1].Value }
    }

    $runsDir = Join-Path $WorkspacePath "runs"
    if (Test-Path $runsDir) {
        $latest = Get-ChildItem -Path $runsDir -Directory -Filter "run-*" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($latest -ne $null) { return $latest.Name }
    }

    return ""
}

function Test-PlanSanitized([string]$PlanPath, [ref]$Detail) {
    if (-not (Test-Path $PlanPath)) {
        $Detail.Value = "Plan.md missing; nothing to sanitize yet"
        return $true
    }
    $text = Read-TextIfExists $PlanPath
    $badPatterns = @(
        '(?im)^\s*function\s+Set-Utf8NoBom\b',
        '(?im)^\s*Set-Content\b',
        '(?im)^\s*Add-Content\b',
        '(?im)^\s*Out-File\b',
        '(?im)^\s*New-Item\b',
        '(?im)^\s*cat\s*<<',
        '(?im)^\s*@"\s*$',
        '(?im)^\s*"@\s*$',
        '(?im)^\s*\$[A-Za-z0-9_]+\s*=\s*@"'
    )
    $hits = New-Object System.Collections.Generic.List[string]
    foreach ($pattern in $badPatterns) {
        if ($text -match $pattern) { $hits.Add($pattern) }
    }
    if ($hits.Count -gt 0) {
        $Detail.Value = "Plan.md contains raw shell/write payloads or helper code: " + ($hits -join ", ")
        return $false
    }
    $Detail.Value = "Plan.md is sanitized"
    return $true
}

function Test-DocumentationHonesty([string]$DocumentationPath, [string]$WorkspacePath, [string]$RunId, [ref]$Detail) {
    if (-not (Test-Path $DocumentationPath)) {
        $Detail.Value = "Documentation.md missing; nothing to cross-check yet"
        return $true
    }

    $text = Read-TextIfExists $DocumentationPath
    $finalGatePath = Join-Path $WorkspacePath "state/final-gate-result.json"
    $gateStatus = "UNKNOWN"
    $continuationStatus = "UNKNOWN"
    if (Test-Path $finalGatePath) {
        try {
            $gate = Get-Content -Raw -Path $finalGatePath | ConvertFrom-Json -ErrorAction Stop
            $gateStatus = [string]$gate.status
            $continuationStatus = [string]$gate.continuationStatus
        }
        catch { }
    }

    $problems = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($RunId) -and $text -notmatch [regex]::Escape($RunId)) {
        $problems.Add("Documentation.md does not mention latest run id $RunId")
    }

    $claimsComplete = $text -match '(?i)\b(final|complete|completed|done|green|success)\b'
    $explicitNotFinal = $text -match '(?i)\bNOT FINAL\b|\bBLOCKED_BY_GATE\b|\bnot runtime ready\b|\bgate blocked\b|\bverification failed\b'
    if (($gateStatus -eq "FAIL" -or $continuationStatus -match 'BLOCKED|CONTINUE_REQUIRED') -and $claimsComplete -and -not $explicitNotFinal) {
        $problems.Add("Documentation.md claims completion/success while final gate is $gateStatus / $continuationStatus")
    }

    if ($problems.Count -gt 0) {
        $Detail.Value = $problems -join "; "
        return $false
    }

    $Detail.Value = "Documentation.md is consistent with run/gate status"
    return $true
}

function Get-ObjectProperty($Object, [string[]]$Names) {
    if ($null -eq $Object) { return $null }
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties | Where-Object { $_.Name.Equals($name, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
        if ($property -ne $null) { Write-Output -NoEnumerate $property.Value; return }
    }
    return $null
}

function Test-RunAndWaveIdentity([string]$WorkspacePath, [string]$RunId, [ref]$Detail) {
    $problems = New-Object System.Collections.Generic.List[string]
    $runsDir = Join-Path $WorkspacePath "runs"
    if (-not (Test-Path $runsDir)) {
        $Detail.Value = "runs directory missing; identity check skipped"
        return $true
    }

    $boardFiles = @(Get-ChildItem -Path $runsDir -Recurse -File -Include "migration-board.md", "migration-board.json", "wave-status.json", "wave-quality-budget.json" -ErrorAction SilentlyContinue)
    foreach ($file in $boardFiles) {
        $relative = Get-RelativePathCompat $WorkspacePath $file.FullName
        $text = Read-TextIfExists $file.FullName
        $pathRun = [regex]::Match($relative, 'runs[\\/](run-[^\\/]+)')
        $pathWave = [regex]::Match($relative, 'runs[\\/](wave-[^\\/]+)')
        $expectedRun = if ($pathRun.Success) { $pathRun.Groups[1].Value } else { $RunId }
        $expectedWave = if ($pathWave.Success) { $pathWave.Groups[1].Value } else { "" }

        if ($file.Extension.Equals(".json", [StringComparison]::OrdinalIgnoreCase)) {
            try {
                $json = Get-Content -Raw -Path $file.FullName | ConvertFrom-Json -ErrorAction Stop
                $jsonRun = [string](Get-ObjectProperty $json @("runId", "activeRunId", "latestRunId"))
                $jsonWave = [string](Get-ObjectProperty $json @("waveId", "wave"))
                if (-not [string]::IsNullOrWhiteSpace($expectedRun) -and -not [string]::IsNullOrWhiteSpace($jsonRun) -and $jsonRun -ne $expectedRun) {
                    $problems.Add("${relative} runId mismatch: expected $expectedRun, found $jsonRun")
                }
                if (-not [string]::IsNullOrWhiteSpace($expectedWave) -and -not [string]::IsNullOrWhiteSpace($jsonWave) -and $jsonWave -ne $expectedWave) {
                    $problems.Add("${relative} waveId mismatch: expected $expectedWave, found $jsonWave")
                }
                if ($file.Name -eq "migration-board.json" -and [string]::IsNullOrWhiteSpace($jsonRun) -and [string]::IsNullOrWhiteSpace($jsonWave)) {
                    $problems.Add("${relative} missing runId/waveId identity")
                }
            }
            catch {
                $problems.Add("${relative} invalid JSON: $($_.Exception.Message)")
            }
        }
        else {
            if (-not [string]::IsNullOrWhiteSpace($expectedRun) -and $text -notmatch [regex]::Escape($expectedRun) -and $text -notmatch '(?i)run id|active run|latest run') {
                $problems.Add("${relative} missing run-id context")
            }
            if (-not [string]::IsNullOrWhiteSpace($expectedWave) -and $text -notmatch [regex]::Escape($expectedWave) -and $text -notmatch '(?i)wave id|wave') {
                $problems.Add("${relative} missing wave-id context")
            }
        }
    }

    if ($problems.Count -gt 0) {
        $Detail.Value = $problems -join "; "
        return $false
    }

    $Detail.Value = "run/wave identity is present on generated boards/status artifacts"
    return $true
}

function Test-SessionExportStatus([string]$WorkspacePath, [string]$RunId, [ref]$Detail) {
    if ([string]::IsNullOrWhiteSpace($RunId)) {
        $Detail.Value = "run id unknown; session export check skipped"
        return $true
    }
    $manifestPath = Join-Path $WorkspacePath "runs/$RunId/opencode-session-export.json"
    $markdownPath = Join-Path $WorkspacePath "runs/$RunId/opencode-session-export.md"
    if (-not (Test-Path $manifestPath) -and -not (Test-Path $markdownPath)) {
        $Detail.Value = "session export missing; strict presence is enforced by final gate options, not artifact hygiene"
        return $true
    }
    if (-not (Test-Path $manifestPath)) {
        $Detail.Value = "opencode-session-export.md exists without opencode-session-export.json status manifest"
        return $false
    }
    try {
        $manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json -ErrorAction Stop
        $status = [string]$manifest.exportStatus
        $reason = [string]$manifest.reason
        if ($status -eq "REAL_EXPORT") {
            if (-not (Test-Path $markdownPath)) {
                $Detail.Value = "REAL_EXPORT manifest exists but markdown export is missing"
                return $false
            }
            $text = Read-TextIfExists $markdownPath
            if ($text -match '(?i)template|placeholder|transcript unavailable') {
                $Detail.Value = "REAL_EXPORT markdown looks like template/unavailable text"
                return $false
            }
            $Detail.Value = "session export is REAL_EXPORT"
            return $true
        }
        if ($status -eq "UNAVAILABLE_WITH_REASON" -and -not [string]::IsNullOrWhiteSpace($reason)) {
            $Detail.Value = "session export unavailable with explicit reason"
            return $true
        }
        $Detail.Value = "session export status '$status' is not REAL_EXPORT or UNAVAILABLE_WITH_REASON"
        return $false
    }
    catch {
        $Detail.Value = "invalid session export manifest: $($_.Exception.Message)"
        return $false
    }
}

$workspacePath = [System.IO.Path]::GetFullPath($Workspace)
$repoRootPath = [System.IO.Path]::GetFullPath($RepoRoot)
$stateDir = Join-Path $workspacePath "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null

$latestRunId = Get-LatestRunId $workspacePath $RunId
$runRoot = if ([string]::IsNullOrWhiteSpace($latestRunId)) { "" } else { Join-Path $workspacePath "runs/$latestRunId" }

$checks = New-Object System.Collections.Generic.List[object]

$detail = ""
$planPath = if ([string]::IsNullOrWhiteSpace($runRoot)) { "" } else { Join-Path $runRoot "Plan.md" }
$planOk = if ([string]::IsNullOrWhiteSpace($planPath)) { $true } else { Test-PlanSanitized $planPath ([ref]$detail) }
Add-Check $checks "plan-artifact-sanitized" $planOk $detail $planPath

$detail = ""
$documentationPath = if ([string]::IsNullOrWhiteSpace($runRoot)) { "" } else { Join-Path $runRoot "Documentation.md" }
$docOk = if ([string]::IsNullOrWhiteSpace($documentationPath)) { $true } else { Test-DocumentationHonesty $documentationPath $workspacePath $latestRunId ([ref]$detail) }
Add-Check $checks "documentation-gate-consistency" $docOk $detail $documentationPath

$detail = ""
$identityOk = Test-RunAndWaveIdentity $workspacePath $latestRunId ([ref]$detail)
Add-Check $checks "run-wave-identity" $identityOk $detail (Join-Path $workspacePath "runs")

$detail = ""
$sessionOk = Test-SessionExportStatus $workspacePath $latestRunId ([ref]$detail)
$sessionManifestPath = if ([string]::IsNullOrWhiteSpace($runRoot)) { "" } else { Join-Path $runRoot "opencode-session-export.json" }
Add-Check $checks "session-export-status" $sessionOk $detail $sessionManifestPath

$status = if (@($checks | Where-Object { -not $_.passed }).Count -eq 0) { "PASS" } else { "FAIL" }
$report = [ordered]@{
    schemaVersion = "artifact-hygiene/v1"
    status = $status
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    workspace = $Workspace
    repoRoot = $RepoRoot
    runId = $latestRunId
    checks = $checks
    nextAction = $nextAction
}

$stateJson = Join-Path $stateDir "artifact-hygiene.json"
$stateMd = Join-Path $stateDir "artifact-hygiene.md"
$report | ConvertTo-Json -Depth 20 | Set-Content -Path $stateJson -Encoding UTF8

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Artifact Hygiene Report")
[void]$md.AppendLine()
[void]$md.AppendLine("Schema: ``artifact-hygiene/v1``")
[void]$md.AppendLine("Status: **$status**")
[void]$md.AppendLine("Run id: ``$latestRunId``")
[void]$md.AppendLine()
[void]$md.AppendLine("| Check | Status | Detail |")
[void]$md.AppendLine("|---|---:|---|")
foreach ($check in $checks) {
    $checkStatus = if ($check.passed) { "PASS" } else { "FAIL" }
    $safeDetail = ([string]$check.detail).Replace("|", "\\|").Replace("`r", " ").Replace("`n", " ")
    [void]$md.AppendLine("| $($check.name) | $checkStatus | $safeDetail |")
}
Set-Content -Path $stateMd -Value $md.ToString() -Encoding UTF8

if (-not [string]::IsNullOrWhiteSpace($runRoot) -and (Test-Path $runRoot)) {
    $runJson = Join-Path $runRoot "artifact-hygiene.json"
    $runMd = Join-Path $runRoot "artifact-hygiene.md"
    Copy-Item -Force -Path $stateJson -Destination $runJson
    Copy-Item -Force -Path $stateMd -Destination $runMd
}

if ($status -eq "PASS") {
    Write-Host "ARTIFACT_HYGIENE_PASS"
    Write-Host "Report: $stateMd"
    exit 0
}

Write-Host "ARTIFACT_HYGIENE_FAIL"
Write-Host "Report: $stateMd"
exit 1
