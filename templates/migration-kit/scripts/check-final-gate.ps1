param(
    [string]$Workspace = "migration",
    [string]$RepoRoot = ".",
    [string[]]$AllowedRoots = @($Workspace)
)

$ErrorActionPreference = "Stop"

function Add-Result($Results, [string]$Name, [bool]$Passed, [string]$Detail) {
    $Results.Add([ordered]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    })
}

function Read-TextIfExists([string]$Path) {
    if (Test-Path $Path) {
        return Get-Content -Raw -Path $Path
    }

    return ""
}

function Find-LatestFile([string]$Root, [string[]]$Names) {
    if (-not (Test-Path $Root)) {
        return $null
    }

    Get-ChildItem -Path $Root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $Names -contains $_.Name } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Find-RunIds([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    $matches = [regex]::Matches($Text, '(?i)\brun[-_][0-9A-Za-z][0-9A-Za-z._-]*\b')
    return @($matches | ForEach-Object { $_.Value.ToLowerInvariant() } | Sort-Object -Unique)
}

function Find-ExplicitLatestRunId([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    $match = [regex]::Match($Text, '(?im)^\s*(Latest run|Latest run id|Current run|Run id)\s*:\s*(run[-_][0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
    if ($match.Success) {
        return $match.Groups[2].Value.ToLowerInvariant()
    }

    return $null
}

function Test-ExplicitStatus([string]$Text, [string[]]$Statuses) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    foreach ($line in ($Text -split "\r?\n")) {
        $match = [regex]::Match($line, '^\s*(Runtime-ready|Runtime ready|Project verify|Final status|Status|Config validate|Config status)\s*:\s*(.+?)\s*$')
        if (-not $match.Success) {
            continue
        }

        $value = $match.Groups[2].Value.Trim()
        foreach ($status in $Statuses) {
            if ($value.Equals($status, [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }

    return $false
}

function Test-GuardChecksums([string]$WorkspacePath, [string[]]$GuardFiles, [ref]$Detail) {
    $checksumPath = Join-Path $WorkspacePath ".migration-kit/guard-checksums.json"
    if (-not (Test-Path $checksumPath)) {
        $Detail.Value = "missing $checksumPath"
        return $false
    }

    try {
        $json = Get-Content -Raw -Path $checksumPath | ConvertFrom-Json
        $expected = @{}
        foreach ($entry in @($json.files)) {
            if ($entry.path -and $entry.sha256) {
                $expected[$entry.path.ToString().Replace("\", "/")] = $entry.sha256.ToString().ToUpperInvariant()
            }
        }

        $mismatches = New-Object System.Collections.Generic.List[string]
        foreach ($guardFile in $GuardFiles) {
            $relative = $guardFile.Replace("\", "/")
            $fullPath = Join-Path $WorkspacePath $guardFile
            if (-not (Test-Path $fullPath)) {
                $mismatches.Add("$relative missing")
                continue
            }

            if (-not $expected.ContainsKey($relative)) {
                $mismatches.Add("$relative missing expected checksum")
                continue
            }

            $actual = (Get-FileHash -Algorithm SHA256 -Path $fullPath).Hash.ToUpperInvariant()
            if ($actual -ne $expected[$relative]) {
                $mismatches.Add("$relative checksum mismatch")
            }
        }

        $Detail.Value = if ($mismatches.Count -eq 0) { "guard checksums match" } else { $mismatches -join "; " }
        return $mismatches.Count -eq 0
    }
    catch {
        $Detail.Value = "checksum validation failed: $($_.Exception.Message)"
        return $false
    }
}

function Test-JsonStatusPassed([string]$Path) {
    try {
        $json = Get-Content -Raw -Path $Path | ConvertFrom-Json
        $text = ($json | ConvertTo-Json -Depth 100 -Compress)
        if ($text -match '(?i)"(status|result|outcome)"\s*:\s*"(passed|pass|success|succeeded|ok)"') {
            return $true
        }

        if ($text -match '(?i)"(errorCount|errors|failed|failureCount)"\s*:\s*0\b') {
            return $true
        }
    }
    catch {
        return $false
    }

    return $false
}

function Test-JsonDiagnosticsRecorded([string]$Path) {
    try {
        $json = Get-Content -Raw -Path $Path | ConvertFrom-Json
        $text = ($json | ConvertTo-Json -Depth 100 -Compress)
        return $text -match '(?i)diagnostic|error|warning|validation'
    }
    catch {
        return $false
    }
}

function Test-TextLooksPassed([string]$Text) {
    return $Text -match '(?i)\b(pass|passed|success|succeeded|ok)\b' -and $Text -notmatch '(?i)\b(fail|failed|error)\b'
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path $RepoRoot $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)
$results = New-Object System.Collections.Generic.List[object]

$checksumDetail = ""
$checksumOk = Test-GuardChecksums $workspacePath @("scripts/check-scope.ps1", "scripts/check-final-gate.ps1") ([ref]$checksumDetail)
Add-Result $results "guard-checksums" $checksumOk $checksumDetail

$scopeScript = Join-Path $workspacePath "scripts/check-scope.ps1"
if (Test-Path $scopeScript) {
    $scopeArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $scopeScript, "-RepoRoot", $RepoRoot, "-AllowedRoots") + @($AllowedRoots)

    & powershell @scopeArgs | Out-Host
    $scopeExitCode = $LASTEXITCODE
    Add-Result $results "scope-guard" ($scopeExitCode -eq 0) "check-scope.ps1 exit code $scopeExitCode"
}
else {
    Add-Result $results "scope-guard" $false "missing $scopeScript"
}

$agentStatePath = Join-Path $workspacePath "agent-state.md"
$currentTicketPath = Join-Path $workspacePath "current-ticket.md"
$runLedgerPath = Join-Path $workspacePath "state/run-ledger.md"
$stateFiles = @($agentStatePath, $currentTicketPath, $runLedgerPath)
$boardFile = Find-LatestFile -Root $workspacePath -Names @("migration-board.md", "migration-board.json")
if ($boardFile -ne $null) {
    $stateFiles += $boardFile.FullName
}

$missingStateFiles = @($stateFiles | Where-Object { -not (Test-Path $_) })
if ($boardFile -eq $null) {
    Add-Result $results "run-id-consistency" $false "missing migration-board.md or migration-board.json"
}
elseif ($missingStateFiles.Count -gt 0) {
    Add-Result $results "run-id-consistency" $false ("missing files: " + ($missingStateFiles -join ", "))
}
else {
    $agentStateText = Read-TextIfExists $agentStatePath
    $latestRunId = Find-ExplicitLatestRunId $agentStateText
    if ([string]::IsNullOrWhiteSpace($latestRunId)) {
        Add-Result $results "run-id-consistency" $false "agent-state.md missing explicit Latest run: run-* line"
    }
    else {
        $currentTicketIds = Find-RunIds (Read-TextIfExists $currentTicketPath)
        $boardIds = Find-RunIds (Read-TextIfExists $boardFile.FullName)
        $ledgerIds = Find-RunIds (Read-TextIfExists $runLedgerPath)
        $activeRunIds = @($currentTicketIds) + @($boardIds)
        $activeOtherIds = @($activeRunIds | Where-Object { $_ -ne $latestRunId } | Sort-Object -Unique)
        $problems = New-Object System.Collections.Generic.List[string]
        if (-not ($currentTicketIds -contains $latestRunId)) {
            $problems.Add("current-ticket.md missing $latestRunId")
        }

        if (-not ($boardIds -contains $latestRunId)) {
            $problems.Add("$($boardFile.Name) missing $latestRunId")
        }

        if (-not ($ledgerIds -contains $latestRunId)) {
            $problems.Add("run-ledger.md missing $latestRunId")
        }

        if ($activeOtherIds.Count -gt 0) {
            $problems.Add("active files contain other run ids: " + ($activeOtherIds -join ", "))
        }

        if ($problems.Count -eq 0) {
            Add-Result $results "run-id-consistency" $true "latest active run id: $latestRunId; ledger ids may be historical"
        }
        else {
            Add-Result $results "run-id-consistency" $false ("latest active run id: $latestRunId; " + ($problems -join "; "))
        }
    }
}

$dashboard = Find-LatestFile -Root $workspacePath -Names @("migration-quality-dashboard.json", "migration-board.json")
if ($dashboard -eq $null) {
    Add-Result $results "quality-dashboard" $false "missing migration-quality-dashboard.json or migration-board.json"
}
else {
    $dashboardText = Read-TextIfExists $dashboard.FullName
    $dangerousPatterns = @(
        "DANGEROUS_ASSERTION_SUPPRESSION",
        "ASSERTION_SUPPRESSION_BLOCKED",
        "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT"
    )
    $dangerousHits = @($dangerousPatterns | Where-Object { $dashboardText -match [regex]::Escape($_) })
    Add-Result $results "quality-dangerous-categories" ($dangerousHits.Count -eq 0) ("dashboard: $($dashboard.FullName); hits: " + ($dangerousHits -join ", "))

    $emptyTestHit = $dashboardText -match "EMPTY_TEST_AFTER_SUPPRESSION"
    $emptyLooksZero = $dashboardText -match 'EMPTY_TEST_AFTER_SUPPRESSION[^0-9]{0,120}0\b'
    $classificationFile = Join-Path $workspacePath "reports/empty-test-classification.md"
    Add-Result $results "empty-test-after-suppression" ((-not $emptyTestHit) -or $emptyLooksZero -or (Test-Path $classificationFile)) ("empty category present: $emptyTestHit; classification: $classificationFile")
}

$actualStatusTexts = @(
    Read-TextIfExists (Join-Path $workspacePath "agent-state.md")
    Read-TextIfExists (Join-Path $workspacePath "current-ticket.md")
    Read-TextIfExists (Join-Path $workspacePath "state/handoff.md")
    Read-TextIfExists (Join-Path $workspacePath "state/stop-policy-checklist.md")
) -join "`n"
$notRuntimeReady = Test-ExplicitStatus -Text $actualStatusTexts -Statuses @("NOT RUNTIME READY")
$configBlocked = Test-ExplicitStatus -Text $actualStatusTexts -Statuses @("NOT RUNTIME READY", "BLOCKED_BY_CONFIG", "BLOCKED_BY_DIAGNOSTICS")

$configReport = Find-LatestFile -Root $workspacePath -Names @("config-validate-report.json", "config-validate-report.md")
if ($configReport -eq $null) {
    Add-Result $results "config-validate" $false "missing config-validate report"
}
else {
    $configText = Read-TextIfExists $configReport.FullName
    $configPassed = ($configReport.Extension -eq ".json" -and (Test-JsonStatusPassed $configReport.FullName)) -or (Test-TextLooksPassed $configText)
    $diagnosticsRecorded = ($configReport.Extension -eq ".json" -and (Test-JsonDiagnosticsRecorded $configReport.FullName)) -or ($configText -match '(?i)diagnostic|error|warning|validation')
    Add-Result $results "config-validate" ($configPassed -or $configBlocked) ("report: $($configReport.FullName); passed: $configPassed; diagnostics recorded: $diagnosticsRecorded; explicit blocker status: $configBlocked")
}

$projectVerify = Find-LatestFile -Root $workspacePath -Names @("project-verify-report.json", "project-verify-report.md")
if ($projectVerify -eq $null) {
    Add-Result $results "project-verify-or-runtime-status" $notRuntimeReady "project verify missing; explicit NOT RUNTIME READY status: $notRuntimeReady"
}
else {
    $verifyText = Read-TextIfExists $projectVerify.FullName
    $verifyPassed = ($projectVerify.Extension -eq ".json" -and (Test-JsonStatusPassed $projectVerify.FullName)) -or (Test-TextLooksPassed $verifyText)
    Add-Result $results "project-verify-or-runtime-status" ($verifyPassed -or $notRuntimeReady) ("report: $($projectVerify.FullName); passed: $verifyPassed; explicit NOT RUNTIME READY status: $notRuntimeReady")
}

$passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
$report = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    status = if ($passed) { "PASS" } else { "FAIL" }
    workspace = $workspacePath
    checks = $results
}

$stateDir = Join-Path $workspacePath "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$jsonPath = Join-Path $stateDir "final-gate-result.json"
$mdPath = Join-Path $stateDir "final-gate-result.md"
$report | ConvertTo-Json -Depth 20 | Set-Content -Path $jsonPath -Encoding UTF8

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Final Gate Result")
[void]$md.AppendLine()
[void]$md.AppendLine("Status: **$($report.status)**")
[void]$md.AppendLine()
foreach ($check in $results) {
    $status = if ($check.passed) { "PASS" } else { "FAIL" }
    [void]$md.AppendLine("- ${status}: $($check.name) - $($check.detail)")
}
Set-Content -Path $mdPath -Value $md.ToString() -Encoding UTF8

Write-Host "FINAL_GATE_$($report.status)"
Write-Host "Report: $mdPath"

if ($passed) {
    exit 0
}

exit 1
