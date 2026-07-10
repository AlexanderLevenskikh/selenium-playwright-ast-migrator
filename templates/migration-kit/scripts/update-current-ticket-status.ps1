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

function Write-JsonAtomic([string]$Path, $Value, [int]$Depth = 30) {
    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    $tempPath = "$Path.tmp-$([Guid]::NewGuid().ToString('N'))"
    try {
        $Value | ConvertTo-Json -Depth $Depth | Set-Content -Path $tempPath -Encoding UTF8
        Move-Item -Force -Path $tempPath -Destination $Path
    }
    finally {
        if (Test-Path $tempPath) { Remove-Item -Force -Path $tempPath -ErrorAction SilentlyContinue }
    }
}

function Merge-JsonState([string]$Path, $Changes) {
    $state = [ordered]@{}
    $existing = Read-JsonIfExists $Path
    if ($null -ne $existing) {
        foreach ($property in $existing.PSObject.Properties) { $state[$property.Name] = ConvertTo-HashtableRecursive $property.Value }
    }
    foreach ($key in $Changes.Keys) { $state[$key] = $Changes[$key] }
    Write-JsonAtomic $Path $state
    return $state
}

function Update-ActiveWaveStatus([string]$WorkspacePath, [string]$TicketStatus, [string]$TicketId) {
    $runsPath = Join-Path $WorkspacePath "runs"
    if (-not (Test-Path $runsPath)) { return }
    $waveStatusFile = Get-ChildItem -Path $runsPath -Recurse -File -Filter "wave-status.json" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]wave-[^\\/]+[\\/]wave-status\.json$' } |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -eq $waveStatusFile) { return }

    $waveRoot = Split-Path -Parent $waveStatusFile.FullName
    $generatedRoot = Join-Path $waveRoot "generated"
    $generatedFiles = @()
    if (Test-Path $generatedRoot) {
        $generatedFiles = @(Get-ChildItem -Path $generatedRoot -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -ne 'README.md' })
    }
    $existingWave = Read-JsonIfExists $waveStatusFile.FullName
    $currentWaveStatus = if ($null -ne $existingWave) { [string]$existingWave.status } else { "unknown" }
    if ($generatedFiles.Count -gt 0 -and $currentWaveStatus -eq "prepared") { $currentWaveStatus = "migrated" }
    $lifecycleStage = switch ($TicketStatus) {
        "READY" { "ticket-ready" }
        "IN_PROGRESS" { "ticket-in-progress" }
        "REVIEW_READY" { "ticket-review-ready" }
        "DONE" { "ticket-done-pending-final-gate" }
        "BLOCKED" { "ticket-blocked" }
    }
    [void](Merge-JsonState $waveStatusFile.FullName ([ordered]@{
        schemaVersion = "migration-wave-status/v2"
        status = $currentWaveStatus
        placeholderOnly = ($generatedFiles.Count -eq 0)
        generatedFileCount = $generatedFiles.Count
        lifecycleStage = $lifecycleStage
        currentTicketId = $TicketId
        currentTicketStatus = $TicketStatus
        updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }))
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

Write-JsonAtomic $statusPath $statusPayload 20
Add-Content -Path $ledgerPath -Encoding UTF8 -Value ($event | ConvertTo-Json -Depth 20 -Compress)

if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    $ticketsDir = Join-Path $workspacePath ("runs/{0}/tickets" -f $RunId)
    New-Item -ItemType Directory -Force -Path $ticketsDir | Out-Null
    $safeTicketId = ([string]$metadata.ticketId) -replace '[^0-9A-Za-z_.-]', '-'
    if ([string]::IsNullOrWhiteSpace($safeTicketId)) { $safeTicketId = "current-ticket" }
    $runTicketJson = Join-Path $ticketsDir ("{0}.json" -f $safeTicketId)
    $runTicketMd = Join-Path $ticketsDir ("{0}.md" -f $safeTicketId)
    Write-JsonAtomic $runTicketJson $statusPayload 20
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
        "READY" { "POST_FINAL_TASKS_READY" }
        "IN_PROGRESS" { "CONTINUE_REQUIRED" }
        "REVIEW_READY" { "CONTINUE_REQUIRED" }
        "DONE" { "CONTINUE_REQUIRED" }
        "BLOCKED" { "BLOCKED_CURRENT_TICKET" }
    }
    $nextActionCode = switch ($Status) {
        "READY" { "RUN_NEXT_BOUNDED_TASK" }
        "IN_PROGRESS" { "FINISH_CURRENT_TICKET" }
        "REVIEW_READY" { "REVIEW_CURRENT_TICKET" }
        "DONE" { "RUN_FINAL_GATE" }
        "BLOCKED" { $null }
    }
    $nextAction = switch ($Status) {
        "READY" { "Route migration/current-ticket.md through migration-change-reviewer, then executor. Do not start another wave first." }
        "IN_PROGRESS" { "Finish the selected current-ticket, then run scope/harness/final gate checks." }
        "REVIEW_READY" { "Run watchdog/reviewer/scope/harness/final gate for the selected current-ticket." }
        "DONE" { "Run migration/scripts/check-final-gate.ps1 -Workspace migration -RepoRoot . and reconcile follow-up findings." }
        "BLOCKED" { "Report the concrete current-ticket blocker and do not start another wave." }
    }
    $autoAllowed = $Status -ne "BLOCKED"
    $decision = [pscustomobject][ordered]@{
        status = $decisionStatus
        postFinalStage = switch ($Status) {
            "READY" { "TASKS_SLICED" }
            "IN_PROGRESS" { "TASK_IN_PROGRESS" }
            "REVIEW_READY" { "TASK_REVIEW_READY" }
            "DONE" { "TASK_COMPLETED_PENDING_GATE" }
            "BLOCKED" { "TASK_BLOCKED" }
        }
        protocol = "Current-ticket lifecycle is active. The selected ticket must be completed, blocked, or gate-validated before another wave can start."
        nextAction = $nextActionCode
        nextActionDetail = $nextAction
        source = "update-current-ticket-status"
        evidence = "state/current-ticket-status.json"
        currentTicket = "current-ticket.md"
        currentTicketId = [string]$metadata.ticketId
        currentTicketStatus = $Status
        mustContinueBeforeUserMessage = $autoAllowed
        boundedAutoContinuation = [ordered]@{
            allowed = $autoAllowed
            nextAction = $nextActionCode
            maxExecutorTasks = if ($Status -eq "READY") { 1 } else { 0 }
            requiresCurrentTicket = ($Status -in @("READY", "IN_PROGRESS", "REVIEW_READY"))
        }
        updatedAtUtc = $event.updatedAtUtc
    }
    Write-JsonAtomic $continuationPath $decision 20
    $decisionMd = @"
# Harness Continuation Decision

Status: **$($decision.status)**

$($decision.protocol)

Next action: $($decision.nextAction)
Detail: $($decision.nextActionDetail)
Source: $($decision.source)
Evidence: $($decision.evidence)
Current ticket: $($metadata.ticketId)
Current ticket status: $Status
"@
    Set-Content -Path $continuationMdPath -Encoding UTF8 -Value $decisionMd

    $taskSlicePath = Join-Path $workspacePath "state/task-slice-result.json"
    $taskSliceStatus = switch ($Status) {
        "READY" { "POST_FINAL_TASKS_READY" }
        "IN_PROGRESS" { "POST_FINAL_TASK_IN_PROGRESS" }
        "REVIEW_READY" { "POST_FINAL_TASK_REVIEW_READY" }
        "DONE" { "POST_FINAL_TASK_COMPLETED" }
        "BLOCKED" { "POST_FINAL_TASK_BLOCKED" }
    }
    [void](Merge-JsonState $taskSlicePath ([ordered]@{
        schemaVersion = "post-final-task-slice/v2"
        status = $taskSliceStatus
        selectedTicketId = [string]$metadata.ticketId
        selectedTicketTitle = [string]$metadata.title
        selectedTicketStatus = $Status
        currentTicket = "current-ticket.md"
        nextAction = $nextActionCode
        source = "update-current-ticket-status"
        updatedAtUtc = $event.updatedAtUtc
    }))

    $harnessRunPath = Join-Path $workspacePath "state/harness-run.json"
    if (Test-Path $harnessRunPath) {
        $harnessStatus = switch ($Status) {
            "READY" { "POST_FINAL_TASKS_READY" }
            "IN_PROGRESS" { "CURRENT_TICKET_IN_PROGRESS" }
            "REVIEW_READY" { "CURRENT_TICKET_REVIEW_READY" }
            "DONE" { "CURRENT_TICKET_DONE_PENDING_GATE" }
            "BLOCKED" { "BLOCKED_CURRENT_TICKET" }
        }
        [void](Merge-JsonState $harnessRunPath ([ordered]@{
            status = $harnessStatus
            currentTicketId = [string]$metadata.ticketId
            currentTicketStatus = $Status
            allowedNextAction = $nextActionCode
            continuationStatus = $decisionStatus
            continuationDecision = "state/continuation-decision.json"
            taskSliceResult = "state/task-slice-result.json"
            updatedAtUtc = $event.updatedAtUtc
        }))
    }

    Update-ActiveWaveStatus $workspacePath $Status ([string]$metadata.ticketId)
}

Write-Host "CURRENT_TICKET_STATUS_UPDATED: ticket=$($metadata.ticketId) status=$Status run=$RunId"
