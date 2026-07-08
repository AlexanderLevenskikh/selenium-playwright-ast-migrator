<#
.SYNOPSIS
Update a sentinel finding lifecycle status without rewriting the original finding.

.DESCRIPTION
update-sentinel-finding-status records sentinel finding lifecycle transitions in append-only ledgers.
The original sentinel-findings.jsonl remains forensic evidence; this script writes a separate
sentinel-finding-lifecycle/v1 event and updates latest status snapshots under migration/state
and migration/runs/<run-id>/sentinel/. Valid lifecycle states are OPEN, ASSIGNED,
FIX_ATTEMPTED, VERIFIED, CLOSED, BLOCKED, NON_AGENT_EXECUTABLE, and ACCEPTED_RISK.

Use this after a gate-followup ticket is assigned, after an executor attempts a fix, after
reviewer/final-gate verification, or when a finding is proven non-agent-executable.
#>
param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [Parameter(Mandatory = $true)][string]$FindingId,
    [ValidateSet("OPEN", "ASSIGNED", "FIX_ATTEMPTED", "VERIFIED", "CLOSED", "BLOCKED", "NON_AGENT_EXECUTABLE", "ACCEPTED_RISK")]
    [string]$Status = "ASSIGNED",
    [string]$TicketId = "",
    [string]$Actor = "orchestrator",
    [string]$Source = "manual",
    [string]$Summary = "",
    [string]$Evidence = "",
    [string]$Result = "",
    [switch]$NoContinuationUpdate
)

$ErrorActionPreference = "Stop"

function Read-JsonIfExists([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    $raw = Get-Content -Raw -Path $Path
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    return $raw | ConvertFrom-Json -ErrorAction Stop
}

function Read-LatestRunId([string]$WorkspacePath) {
    $harnessRunPath = Join-Path $WorkspacePath "state/harness-run.json"
    $harnessRun = Read-JsonIfExists $harnessRunPath
    if ($null -ne $harnessRun) {
        foreach ($name in @("activeRunId", "runId", "latestRunId")) {
            $value = [string]$harnessRun.$name
            if (-not [string]::IsNullOrWhiteSpace($value)) { return $value }
        }
    }
    $agentState = Join-Path $WorkspacePath "agent-state.md"
    if (Test-Path $agentState) {
        $text = Get-Content -Raw -Path $agentState
        $m = [regex]::Match($text, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    $runsDir = Join-Path $WorkspacePath "runs"
    if (Test-Path $runsDir) {
        $latest = Get-ChildItem -Path $runsDir -Directory -Filter "run-*" -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc | Select-Object -Last 1
        if ($null -ne $latest) { return $latest.Name }
    }
    return ""
}

function Read-SentinelFinding([string]$WorkspacePath, [string]$RunId, [string]$FindingId) {
    $paths = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($RunId)) {
        $runFindings = Join-Path $WorkspacePath "runs/$RunId/sentinel/sentinel-findings.jsonl"
        if (Test-Path $runFindings) { [void]$paths.Add($runFindings) }
    }
    $stateLedger = Join-Path $WorkspacePath "state/sentinel-ledger.jsonl"
    if (Test-Path $stateLedger) { [void]$paths.Add($stateLedger) }

    foreach ($path in ($paths | Sort-Object -Unique)) {
        foreach ($line in (Get-Content -Path $path -ErrorAction SilentlyContinue)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try { $entry = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
            if ([string]$entry.findingId -eq $FindingId) { return $entry }
        }
    }
    return $null
}

function Read-LatestLifecycleMap([string]$WorkspacePath, [string]$RunId) {
    $map = @{}
    $paths = New-Object System.Collections.Generic.List[string]
    $stateLedger = Join-Path $WorkspacePath "state/sentinel-finding-ledger.jsonl"
    if (Test-Path $stateLedger) { [void]$paths.Add($stateLedger) }
    if (-not [string]::IsNullOrWhiteSpace($RunId)) {
        $runLedger = Join-Path $WorkspacePath "runs/$RunId/sentinel/sentinel-finding-lifecycle.jsonl"
        if (Test-Path $runLedger) { [void]$paths.Add($runLedger) }
    }

    foreach ($path in ($paths | Sort-Object -Unique)) {
        foreach ($line in (Get-Content -Path $path -ErrorAction SilentlyContinue)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try { $entry = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
            $id = [string]$entry.findingId
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            $time = [string]$entry.updatedAtUtc
            if (-not $map.ContainsKey($id)) { $map[$id] = $entry; continue }
            $previousTime = [string]$map[$id].updatedAtUtc
            if ([string]::CompareOrdinal($time, $previousTime) -ge 0) { $map[$id] = $entry }
        }
    }
    return $map
}

function Assert-AllowedTransition([string]$PreviousStatus, [string]$NextStatus) {
    if ([string]::IsNullOrWhiteSpace($PreviousStatus)) { return }
    $terminal = @("CLOSED", "VERIFIED", "NON_AGENT_EXECUTABLE", "ACCEPTED_RISK")
    if (($terminal -contains $PreviousStatus) -and -not ($PreviousStatus -eq $NextStatus)) {
        throw "Sentinel finding lifecycle transition from terminal status $PreviousStatus to $NextStatus is not allowed. Reopen by recording a new finding instead."
    }

    $allowed = @{
        "OPEN" = @("ASSIGNED", "BLOCKED", "NON_AGENT_EXECUTABLE", "ACCEPTED_RISK", "CLOSED")
        "ASSIGNED" = @("FIX_ATTEMPTED", "BLOCKED", "NON_AGENT_EXECUTABLE", "ACCEPTED_RISK", "CLOSED")
        "FIX_ATTEMPTED" = @("VERIFIED", "ASSIGNED", "BLOCKED", "NON_AGENT_EXECUTABLE", "ACCEPTED_RISK")
        "BLOCKED" = @("ASSIGNED", "NON_AGENT_EXECUTABLE", "ACCEPTED_RISK", "CLOSED")
    }
    if ($allowed.ContainsKey($PreviousStatus) -and -not ($allowed[$PreviousStatus] -contains $NextStatus)) {
        throw "Sentinel finding lifecycle transition $PreviousStatus -> $NextStatus is not allowed."
    }
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)
if (-not (Test-Path $workspacePath)) { throw "Workspace not found: $workspacePath" }
if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = Read-LatestRunId $workspacePath }
if ([string]::IsNullOrWhiteSpace($RunId)) { throw "RunId was not provided and could not be inferred." }

$finding = Read-SentinelFinding $workspacePath $RunId $FindingId
if ($null -eq $finding) { throw "Sentinel finding not found: $FindingId" }

$latestMap = Read-LatestLifecycleMap $workspacePath $RunId
$previousStatus = if ($latestMap.ContainsKey($FindingId)) { [string]$latestMap[$FindingId].status } else { [string]$finding.status }
if ([string]::IsNullOrWhiteSpace($previousStatus)) { $previousStatus = "OPEN" }
$previousStatus = $previousStatus.ToUpperInvariant()
Assert-AllowedTransition $previousStatus $Status

$stateDir = Join-Path $workspacePath "state"
$runSentinelDir = Join-Path $workspacePath "runs/$RunId/sentinel"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
New-Item -ItemType Directory -Force -Path $runSentinelDir | Out-Null

$event = [pscustomobject][ordered]@{
    schemaVersion = "sentinel-finding-lifecycle/v1"
    event = "SENTINEL_FINDING_STATUS_UPDATED"
    findingId = $FindingId
    runId = $RunId
    category = [string]$finding.category
    severity = [string]$finding.severity
    status = $Status
    previousStatus = $previousStatus
    ticketId = $TicketId
    source = $Source
    actor = $Actor
    summary = $Summary
    evidence = $Evidence
    result = $Result
    updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}

$stateLedgerPath = Join-Path $stateDir "sentinel-finding-ledger.jsonl"
$runLedgerPath = Join-Path $runSentinelDir "sentinel-finding-lifecycle.jsonl"
$eventLine = $event | ConvertTo-Json -Depth 20 -Compress
Add-Content -Path $stateLedgerPath -Encoding UTF8 -Value $eventLine
Add-Content -Path $runLedgerPath -Encoding UTF8 -Value $eventLine

# Latest status snapshots are intentionally rewritten; the append-only ledger above remains the source of truth.
$latestMap[$FindingId] = $event
$allLatest = @($latestMap.Keys | Sort-Object | ForEach-Object { $latestMap[$_] })
$snapshot = [pscustomobject][ordered]@{
    schemaVersion = "sentinel-finding-lifecycle/v1"
    runId = $RunId
    updatedAtUtc = $event.updatedAtUtc
    statuses = $allLatest
}
$snapshot | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $stateDir "sentinel-finding-status.json") -Encoding UTF8
$snapshot | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runSentinelDir "sentinel-finding-status.json") -Encoding UTF8

if (-not $NoContinuationUpdate) {
    $continuationPath = Join-Path $stateDir "continuation-decision.json"
    $continuationMdPath = Join-Path $stateDir "continuation-decision.md"
    $isTerminal = $Status -match '^(VERIFIED|CLOSED|NON_AGENT_EXECUTABLE|ACCEPTED_RISK)$'
    $decision = [pscustomobject][ordered]@{
        status = if ($isTerminal) { "CONTINUE_REQUIRED" } else { "CONTINUE_REQUIRED" }
        protocol = "Sentinel finding lifecycle is active. High/critical findings must be VERIFIED/CLOSED, explicitly NON_AGENT_EXECUTABLE, or routed through a current-ticket before another migration wave or final handoff."
        nextAction = if ($isTerminal) { "Run migration/scripts/check-final-gate.ps1 -Workspace migration -RepoRoot . to verify no blocking sentinel findings remain." } else { "Continue the current-ticket/reviewer/executor loop for sentinel finding $FindingId. Do not start another wave." }
        source = "update-sentinel-finding-status"
        evidence = "state/sentinel-finding-status.json"
        findingId = $FindingId
        findingStatus = $Status
        ticketId = $TicketId
        mustContinueBeforeUserMessage = (-not $isTerminal)
        boundedAutoContinuation = (-not $isTerminal)
    }
    $decision | ConvertTo-Json -Depth 20 | Set-Content -Path $continuationPath -Encoding UTF8
    $decisionMd = @"
# Harness Continuation Decision

Status: **$($decision.status)**

$($decision.protocol)

Finding: `$FindingId`
Finding status: `$Status`
Next action: $($decision.nextAction)
Evidence: $($decision.evidence)
"@
    Set-Content -Path $continuationMdPath -Encoding UTF8 -Value $decisionMd
}

Write-Host "SENTINEL_FINDING_STATUS_UPDATED: finding=$FindingId status=$Status run=$RunId"
