<#
.SYNOPSIS
Update the selected current-ticket lifecycle state.

.DESCRIPTION
update-current-ticket-status records a machine-readable current-ticket lifecycle transition.
It keeps the latest status in migration/state/current-ticket-status.json, appends an
append-only line to migration/state/current-ticket-ledger.jsonl, and mirrors the status
under migration/runs/<run-id>/tickets/ when an active run can be inferred.

This script is intentionally small: it does not execute the ticket. It makes the
reviewer -> executor -> gate loop auditable so /supervised-task continue must finish
or explicitly block the selected current-ticket before starting a new wave.
#>
param(
    [string]$Workspace = "migration",
    [ValidateSet("READY", "IN_PROGRESS", "REVIEW_READY", "DONE", "BLOCKED")]
    [string]$Status = "IN_PROGRESS",
    [string]$RunId = "",
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

function ConvertTo-HashtableRecursive($Value) {
    if ($null -eq $Value) { return $null }
    if ($Value -is [System.Management.Automation.PSCustomObject]) {
        $hash = [ordered]@{}
        foreach ($property in $Value.PSObject.Properties) {
            $hash[$property.Name] = ConvertTo-HashtableRecursive $property.Value
        }
        return $hash
    }
    if ($Value -is [System.Collections.IEnumerable] -and $Value -isnot [string]) {
        $list = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Value) { [void]$list.Add((ConvertTo-HashtableRecursive $item)) }
        return $list.ToArray()
    }
    return $Value
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
    $runLedger = Join-Path $WorkspacePath "state/run-ledger.md"
    if (Test-Path $runLedger) {
        $text = Get-Content -Raw -Path $runLedger
        $matches = [regex]::Matches($text, 'run-[0-9A-Za-z_.-]+')
        if ($matches.Count -gt 0) { return $matches[$matches.Count - 1].Value }
    }
    $runsDir = Join-Path $WorkspacePath "runs"
    if (Test-Path $runsDir) {
        $latest = Get-ChildItem -Path $runsDir -Directory -Filter "run-*" -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc | Select-Object -Last 1
        if ($null -ne $latest) { return $latest.Name }
    }
    return ""
}

function Read-TicketMetadata([string]$TicketPath) {
    $metadata = [ordered]@{
        schemaVersion = "current-ticket-lifecycle/v1"
        ticketPath = "current-ticket.md"
        title = "Current ticket"
        ticketId = "current-ticket"
        source = "current-ticket.md"
    }
    if (-not (Test-Path $TicketPath)) { return $metadata }
    $lines = @(Get-Content -Path $TicketPath -ErrorAction SilentlyContinue)
    foreach ($line in $lines) {
        if ($line -match '^#\s+Current ticket:\s*(?<title>.+)$') { $metadata.title = $Matches['title'].Trim() }
        elseif ($line -match '^Task id:\s*`?(?<id>[^`\r\n]+)`?\s*$') { $metadata.ticketId = $Matches['id'].Trim() }
        elseif ($line -match '^Source:\s*`?(?<source>[^`\r\n]+)`?\s*$') { $metadata.source = $Matches['source'].Trim() }
        elseif ($line -match '^Schema:\s*`?(?<schema>[^`\r\n]+)`?\s*$') { $metadata.ticketSchema = $Matches['schema'].Trim() }
    }
    return $metadata
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)
if (-not (Test-Path $workspacePath)) { throw "Workspace not found: $workspacePath" }

if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = Read-LatestRunId $workspacePath }
$currentTicketPath = Join-Path $workspacePath "current-ticket.md"
$metadata = Read-TicketMetadata $currentTicketPath
if (-not [string]::IsNullOrWhiteSpace($TicketId)) { $metadata.ticketId = $TicketId }

$stateDir = Join-Path $workspacePath "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$ledgerPath = Join-Path $stateDir "current-ticket-ledger.jsonl"
$statusPath = Join-Path $stateDir "current-ticket-status.json"

$previous = Read-JsonIfExists $statusPath
$event = [pscustomobject][ordered]@{
    schemaVersion = "current-ticket-lifecycle/v1"
    event = "CURRENT_TICKET_STATUS_UPDATED"
    status = $Status
    previousStatus = if ($null -ne $previous) { [string]$previous.status } else { $null }
    runId = $RunId
    ticketId = [string]$metadata.ticketId
    title = [string]$metadata.title
    ticketPath = "current-ticket.md"
    source = $Source
    actor = $Actor
    summary = $Summary
    evidence = $Evidence
    result = $Result
    updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}

$statusPayload = [pscustomobject][ordered]@{
    schemaVersion = "current-ticket-lifecycle/v1"
    status = $Status
    runId = $RunId
    ticketId = [string]$metadata.ticketId
    title = [string]$metadata.title
    ticketPath = "current-ticket.md"
    ticketSchema = if ($metadata.ticketSchema) { [string]$metadata.ticketSchema } else { $null }
    source = [string]$metadata.source
    actor = $Actor
    summary = $Summary
    evidence = $Evidence
    result = $Result
    previousStatus = if ($null -ne $previous) { [string]$previous.status } else { $null }
    updatedAtUtc = $event.updatedAtUtc
}

$statusPayload | ConvertTo-Json -Depth 20 | Set-Content -Path $statusPath -Encoding UTF8
Add-Content -Path $ledgerPath -Encoding UTF8 -Value ($event | ConvertTo-Json -Depth 20 -Compress)

if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    $ticketsDir = Join-Path $workspacePath ("runs/{0}/tickets" -f $RunId)
    New-Item -ItemType Directory -Force -Path $ticketsDir | Out-Null
    $safeTicketId = ([string]$metadata.ticketId) -replace '[^0-9A-Za-z_.-]', '-'
    if ([string]::IsNullOrWhiteSpace($safeTicketId)) { $safeTicketId = "current-ticket" }
    $runTicketJson = Join-Path $ticketsDir ("{0}.json" -f $safeTicketId)
    $runTicketMd = Join-Path $ticketsDir ("{0}.md" -f $safeTicketId)
    $statusPayload | ConvertTo-Json -Depth 20 | Set-Content -Path $runTicketJson -Encoding UTF8
    $md = @"
# Current ticket status: $Status

Schema: `current-ticket-lifecycle/v1`
Run: `$RunId`
Ticket: `$($metadata.ticketId)`
Updated at: $($event.updatedAtUtc)

Summary: $Summary

Evidence: $Evidence

Result: $Result
"@
    Set-Content -Path $runTicketMd -Encoding UTF8 -Value $md
}

if (-not $NoContinuationUpdate) {
    $continuationPath = Join-Path $workspacePath "state/continuation-decision.json"
    $continuationMdPath = Join-Path $workspacePath "state/continuation-decision.md"
    $decisionStatus = switch ($Status) {
        "READY" { "CONTINUE_REQUIRED" }
        "IN_PROGRESS" { "CONTINUE_REQUIRED" }
        "REVIEW_READY" { "CONTINUE_REQUIRED" }
        "DONE" { "CONTINUE_REQUIRED" }
        "BLOCKED" { "BLOCKED_CURRENT_TICKET" }
    }
    $nextAction = switch ($Status) {
        "READY" { "Route migration/current-ticket.md through migration-change-reviewer, then executor. Do not start another wave first." }
        "IN_PROGRESS" { "Finish the selected current-ticket, then run scope/harness/final gate checks." }
        "REVIEW_READY" { "Run watchdog/reviewer/scope/harness/final gate for the selected current-ticket." }
        "DONE" { "Run migration/scripts/check-final-gate.ps1 -Workspace migration -RepoRoot . and reconcile follow-up findings." }
        "BLOCKED" { "Report the concrete current-ticket blocker and do not start another wave." }
    }
    $decision = [pscustomobject][ordered]@{
        status = $decisionStatus
        protocol = "Current-ticket lifecycle is active. The selected ticket must be completed, blocked, or gate-validated before another wave can start."
        nextAction = $nextAction
        source = "update-current-ticket-status"
        evidence = "state/current-ticket-status.json"
        currentTicket = "current-ticket.md"
        currentTicketStatus = $Status
        mustContinueBeforeUserMessage = ($Status -ne "BLOCKED")
        boundedAutoContinuation = ($Status -ne "BLOCKED")
    }
    $decision | ConvertTo-Json -Depth 20 | Set-Content -Path $continuationPath -Encoding UTF8
    $decisionMd = @"
# Harness Continuation Decision

Status: **$($decision.status)**

$($decision.protocol)

Next action: $($decision.nextAction)
Source: $($decision.source)
Evidence: $($decision.evidence)
Current ticket status: $Status
"@
    Set-Content -Path $continuationMdPath -Encoding UTF8 -Value $decisionMd
}

Write-Host "CURRENT_TICKET_STATUS_UPDATED: ticket=$($metadata.ticketId) status=$Status run=$RunId"
