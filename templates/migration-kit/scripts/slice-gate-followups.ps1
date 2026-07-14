<#
.SYNOPSIS
Slice final-gate and sentinel diagnostics into bounded follow-up tickets.

.DESCRIPTION
slice-gate-followups is the bridge from process diagnostics to the next supervised-task action.
It reads state/final-gate-result.json, state/continuation-decision.json, state/wave-quality-budget.json,
and active-run sentinel findings, then writes a machine-readable backlog and a human-readable current ticket.
The generated tasks are migration-artifact tasks by default; anything that requires product-tree
writes is marked as a non-agent-executable blocker instead of silently broadening permissions.
#>
param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [string]$OutDir = "state/backlog",
    [switch]$DoNotOverwriteCurrentTicket
)

$ErrorActionPreference = "Stop"

function Read-LatestRunId([string]$WorkspacePath) {
    $agentState = Join-Path $WorkspacePath "agent-state.md"
    if (Test-Path $agentState) {
        $text = Get-Content -Raw -Path $agentState
        $m = [regex]::Match($text, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
        if ($m.Success) { return $m.Groups[1].Value }
    }

    $runsPath = Join-Path $WorkspacePath "runs"
    if (Test-Path $runsPath) {
        $latest = Get-ChildItem -Path $runsPath -Directory -Filter "run-*" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($latest -ne $null) { return $latest.Name }
    }

    return ""
}

function Read-JsonIfExists([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    try { return Get-Content -Raw -Path $Path | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
}

function Get-ObjectProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties | Where-Object { $_.Name.Equals($Name, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($property -eq $null) { return $null }
    return $property.Value
}

function Normalize-Token([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "unknown" }
    return (($Value.Trim().ToLowerInvariant() -replace '[^a-z0-9._-]+', '-') -replace '^-+|-+$', '')
}

function Get-SeverityRank([string]$Severity) {
    switch -Regex ($Severity) {
        '^(?i:critical)$' { return 100 }
        '^(?i:high)$' { return 80 }
        '^(?i:medium)$' { return 50 }
        '^(?i:low)$' { return 25 }
        default { return 10 }
    }
}

function Normalize-SentinelLifecycleStatus([string]$Status) {
    if ([string]::IsNullOrWhiteSpace($Status)) { return "OPEN" }
    $normalized = $Status.Trim().ToUpperInvariant().Replace("-", "_")
    switch ($normalized) {
        "VERIFIED" { return "VERIFIED" }
        "CLOSED" { return "CLOSED" }
        "NON_AGENT" { return "NON_AGENT_EXECUTABLE" }
        "NON_AGENT_EXECUTABLE" { return "NON_AGENT_EXECUTABLE" }
        "ACCEPTED" { return "ACCEPTED_RISK" }
        "ACCEPTED_RISK" { return "ACCEPTED_RISK" }
        "RESOLVED" { return "CLOSED" }
        "NON_BLOCKING" { return "ACCEPTED_RISK" }
        default { return $normalized }
    }
}

function Test-OpenStatus([string]$Status) {
    return -not ((Normalize-SentinelLifecycleStatus $Status) -match '^(VERIFIED|CLOSED|NON_AGENT_EXECUTABLE|ACCEPTED_RISK)$')
}

function Read-SentinelLifecycleStatuses([string]$WorkspacePath, [string]$RunId) {
    $map = @{}
    $paths = @()
    $stateLedger = Join-Path $WorkspacePath "state/sentinel-finding-ledger.jsonl"
    if (Test-Path $stateLedger) { $paths += $stateLedger }
    $runLedger = Join-Path $WorkspacePath "runs/$RunId/sentinel/sentinel-finding-lifecycle.jsonl"
    if (Test-Path $runLedger) { $paths += $runLedger }
    foreach ($path in ($paths | Sort-Object -Unique)) {
        foreach ($line in (Get-Content -Path $path -ErrorAction SilentlyContinue)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try { $entry = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
            $id = [string]$entry.findingId
            if ([string]::IsNullOrWhiteSpace($id)) { continue }
            $updatedAtUtc = [string]$entry.updatedAtUtc
            if (-not $map.ContainsKey($id) -or [string]::CompareOrdinal($updatedAtUtc, [string]$map[$id].updatedAtUtc) -ge 0) {
                $map[$id] = [pscustomobject]@{ status = (Normalize-SentinelLifecycleStatus ([string]$entry.status)); updatedAtUtc = $updatedAtUtc; ticketId = [string]$entry.ticketId }
            }
        }
    }
    return $map
}

function Get-TaskTemplateForCategory([string]$Category, [string]$Source) {
    $normalized = Normalize-Token $Category
    switch -Regex ($normalized) {
        'nested-migration-workspace' {
            return [pscustomobject][ordered]@{
                title = "Reconcile nested migration workspace evidence"
                allowedWriteScope = "migration/** only"
                objective = "Verify whether the nested workspace evidence is current. If the product-tree path no longer exists, update sentinel/final-gate evidence as STALE_GATE_EVIDENCE. If it exists, stop with a non-agent-executable cleanup request unless policy explicitly allows deleting that product-tree artifact."
                validation = "Run migration/scripts/check-final-gate.ps1 and confirm nested-migration-workspace no longer reports stale or unverified evidence."
            }
        }
        'state-contradiction|gate-ignored' {
            return [pscustomobject][ordered]@{
                title = "Reconcile final gate state with harness-run.json"
                allowedWriteScope = "migration/state/** and migration/runs/**"
                objective = "Re-run the final gate, verify harness-run.json reflects finalGateStatus/continuationStatus/latestChecks, and remove any stale healthy-state claim after a failed gate."
                validation = "Inspect migration/state/harness-run.json and migration/state/continuation-decision.json; gate failure must not leave CONTINUE_AUTONOMOUSLY as the active state."
            }
        }
        'scope-guard|harness-policy' {
            return [pscustomobject][ordered]@{
                title = "Classify scope guard failure with baseline evidence"
                allowedWriteScope = "migration/state/** and migration/runs/**"
                objective = "Use scope-baseline.json to distinguish pre-existing unchanged out-of-scope files from new/changed violations. Route product-tree cleanup as a blocker if it requires writes outside migration/**."
                validation = "Run migration/scripts/check-scope.ps1 and migration/scripts/check-harness-policy.ps1; pre-existing unchanged files should be WARN-style evidence, new files should remain FAIL."
            }
        }
        'prompt-doc-contradiction|plan' {
            return [pscustomobject][ordered]@{
                title = "Sanitize run Plan.md"
                allowedWriteScope = "migration/runs/**"
                objective = "Replace leaked shell/write payload text in Plan.md with a concise human plan. Keep raw commands only in trace/session evidence, not in planning artifacts."
                validation = "Run migration/scripts/check-final-gate.ps1 and confirm run-plan-sanitized passes."
            }
        }
        'missing-session-export|session' {
            return [pscustomobject][ordered]@{
                title = "Record honest session export status"
                allowedWriteScope = "migration/runs/**"
                objective = "Create or repair opencode-session-export.json/md so the run records REAL_EXPORT or UNAVAILABLE_WITH_REASON instead of an empty transcript template."
                validation = "Run migration/scripts/check-final-gate.ps1 and confirm session-export-honesty passes."
            }
        }
        'research-count-mismatch|memory|research' {
            return [pscustomobject][ordered]@{
                title = "Persist research memory for high-TODO wave"
                allowedWriteScope = "migration/state/memory/** and migration/runs/**/research/**"
                objective = "Summarize top TODO sources, syntax-fallback causes, unresolved symbols, and verify-project blockers into memory/research artifacts before the next wave."
                validation = "Run migration/scripts/check-final-gate.ps1 and confirm research/memory threshold evidence passes."
            }
        }
        'project-verify|verify|nu1008|cpm' {
            return [pscustomobject][ordered]@{
                title = "Repair verify-project evidence"
                allowedWriteScope = "migration/**"
                objective = "Re-run verify-project with CPM-compatible harness behavior and record project-verify-report evidence. If compilation remains impossible, record a precise NOT RUNTIME READY blocker."
                validation = "project-verify-report.md/json exists and either passes or explains the exact runtime blocker."
            }
        }
        'wave-quality-budget|blocked-by-wave-quality-budget|syntax-fallback|todo-budget|mapping-budget' {
            return [pscustomobject][ordered]@{
                title = "Repair wave scope or reduce the highest-value mapping gap"
                allowedWriteScope = "migration/state/**, migration/runs/**, migration/config-merge/**, migration/profiles/**, migration/current-ticket.md"
                objective = "Do not start the next wave. Inspect wave-quality-budget scopeIntegrity first. If the wave was contaminated by a full-project rerun, preserve that draft under migration/runs/<run-id>/full-project-rerun/**, restore exact wave-local output, and rerun the materialized wave wrapper. Otherwise run migration/scripts/collect-mapping-research-memory.ps1 (or migration/scripts/collect-mapping-research-memory.sh), summarize TODO causes, syntax-fallback clusters, unmapped targets, unresolved symbols, and verify blockers, then use its mapping-research-memory/v1 candidates to slice one bounded config/POM/recognizer improvement. Target-side Playwright POM/scaffold work under migration/** is agent-executable; original Selenium/product POM writes are forbidden."
                validation = "Run migration/scripts/evaluate-wave-quality-budget.ps1 and migration/scripts/check-final-gate.ps1; scopeIntegrity must be clean and budget evidence must either pass or name a concrete bounded remediation next action."
            }
        }
        default {
            return [pscustomobject][ordered]@{
                title = "Resolve $Category gate finding"
                allowedWriteScope = "migration/**"
                objective = "Investigate the finding from $Source, make the smallest migration-artifact fix, and record validation evidence. Do not broaden write scope."
                validation = "Re-run the affected guard/final-gate check and update the finding status or blocker evidence."
            }
        }
    }
}

function New-FollowupTask([string]$Kind, [string]$Category, [string]$Severity, [string]$Summary, [string]$Evidence, [string]$Source, [bool]$AgentExecutable, [string]$RunId, [int]$Index, [string]$FindingId = "") {
    $template = Get-TaskTemplateForCategory $Category $Source
    $rank = Get-SeverityRank $Severity
    if (-not $AgentExecutable) { $rank = [Math]::Min($rank, 20) }
    $id = "gate-followup-{0:D3}-{1}" -f $Index, (Normalize-Token $Category)
    return [pscustomobject][ordered]@{
        schemaVersion = "gate-followup-task/v1"
        id = $id
        runId = $RunId
        kind = $Kind
        category = $Category
        severity = $Severity
        priorityRank = $rank
        title = $template.title
        summary = $Summary
        evidence = $Evidence
        source = $Source
        findingId = $FindingId
        agentExecutable = [bool]$AgentExecutable
        allowedWriteScope = $template.allowedWriteScope
        objective = $template.objective
        validation = $template.validation
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)
if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = Read-LatestRunId $workspacePath }
if ([string]::IsNullOrWhiteSpace($RunId)) { throw "RunId was not provided and could not be inferred." }

$tasks = New-Object System.Collections.Generic.List[object]
$index = 0

# Sentinel findings first: these are the most actionable process diagnostics.
$sentinelLifecycleStatuses = Read-SentinelLifecycleStatuses $workspacePath $RunId
$sentinelPaths = New-Object System.Collections.Generic.List[string]
$seenSentinelFindingIds = @{}
$runSentinel = Join-Path $workspacePath "runs/$RunId/sentinel/sentinel-findings.jsonl"
$stateSentinel = Join-Path $workspacePath "state/sentinel-ledger.jsonl"
if (Test-Path $runSentinel) { $sentinelPaths.Add($runSentinel) }
if (Test-Path $stateSentinel) { $sentinelPaths.Add($stateSentinel) }
foreach ($path in ($sentinelPaths | Sort-Object -Unique)) {
    $lineNumber = 0
    foreach ($line in (Get-Content -Path $path -ErrorAction SilentlyContinue)) {
        $lineNumber += 1
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $entry = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        if ([string]$entry.runId -ne $RunId) { continue }
        $severity = [string]$entry.severity
        $findingId = [string]$entry.findingId
        if (-not [string]::IsNullOrWhiteSpace($findingId)) {
            if ($seenSentinelFindingIds.ContainsKey($findingId)) { continue }
            $seenSentinelFindingIds[$findingId] = $true
        }
        $status = Normalize-SentinelLifecycleStatus ([string]$entry.status)
        if (-not [string]::IsNullOrWhiteSpace($findingId) -and $sentinelLifecycleStatuses.ContainsKey($findingId)) { $status = [string]$sentinelLifecycleStatuses[$findingId].status }
        $agentExecutable = $true
        if ($null -ne $entry.agentExecutable) { $agentExecutable = [bool]$entry.agentExecutable }
        if (-not (Test-OpenStatus $status)) { continue }
        if (-not ($severity -match '^(?i:critical|high|medium)$')) { continue }
        $index += 1
        $summary = [string]$entry.summary
        $evidence = if ($entry.evidence) { (@($entry.evidence) -join "; ") } else { "${path}:$lineNumber" }
        [void]$tasks.Add((New-FollowupTask "sentinel-finding" ([string]$entry.category) $severity $summary $evidence ("sentinel:{0}:{1}" -f (Split-Path -Leaf $path), $lineNumber) $agentExecutable $RunId $index $findingId))
    }
}

# Final gate failed checks become fallback tasks when sentinel did not already describe them.
$finalGatePath = Join-Path $workspacePath "state/final-gate-result.json"
$finalGate = Read-JsonIfExists $finalGatePath
if ($null -ne $finalGate) {
    foreach ($check in @($finalGate.checks)) {
        if ($null -eq $check) { continue }
        $passed = [bool]$check.passed
        if ($passed) { continue }
        $name = [string]$check.name
        $detail = [string]$check.detail
        $alreadyCovered = $false
        foreach ($task in $tasks) {
            $combined = (([string]$task.category) + " " + ([string]$task.summary) + " " + ([string]$task.evidence)).ToLowerInvariant()
            if ($combined.Contains($name.ToLowerInvariant())) { $alreadyCovered = $true; break }
        }
        if ($alreadyCovered) { continue }
        $severity = if ($name -match '^(?i:guard-checksums|harness-policy|scope-guard|nested-migration-workspace|sentinel-open-critical-findings)$') { "high" } else { "medium" }
        $index += 1
        [void]$tasks.Add((New-FollowupTask "final-gate-check" $name $severity $detail $finalGatePath "final-gate-result.json" $true $RunId $index))
    }
}

$orderedTasks = @($tasks | Sort-Object @{ Expression = { -1 * [int]$_.priorityRank } }, @{ Expression = { [string]$_.id } })
$outPath = if ([System.IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $workspacePath $OutDir }
New-Item -ItemType Directory -Force -Path $outPath | Out-Null
$jsonlPath = Join-Path $outPath "gate-followup-tasks.jsonl"
$mdPath = Join-Path $outPath "gate-followup-backlog.md"
$summaryPath = Join-Path $outPath "gate-followup-summary.json"

Set-Content -Path $jsonlPath -Value "" -Encoding UTF8
foreach ($task in $orderedTasks) {
    Add-Content -Path $jsonlPath -Encoding UTF8 -Value ($task | ConvertTo-Json -Depth 20 -Compress)
}

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Gate follow-up backlog")
[void]$md.AppendLine()
[void]$md.AppendLine("Schema: ``gate-followup-slicer/v1``")
[void]$md.AppendLine("Run: ``$RunId``")
[void]$md.AppendLine("Generated at: $([DateTimeOffset]::UtcNow.ToString('o'))")
[void]$md.AppendLine()
if ($orderedTasks.Count -eq 0) {
    [void]$md.AppendLine("No open gate/sentinel follow-up tasks were found.")
}
else {
    foreach ($task in $orderedTasks) {
        [void]$md.AppendLine("## $($task.id): $($task.title)")
        [void]$md.AppendLine()
        [void]$md.AppendLine("- Category: ``$($task.category)``")
        [void]$md.AppendLine("- Severity: ``$($task.severity)``")
        [void]$md.AppendLine("- Agent-executable: ``$($task.agentExecutable)``")
        [void]$md.AppendLine("- Allowed writes: ``$($task.allowedWriteScope)``")
        [void]$md.AppendLine("- Source: ``$($task.source)``")
        [void]$md.AppendLine()
        [void]$md.AppendLine("Objective: $($task.objective)")
        [void]$md.AppendLine()
        [void]$md.AppendLine("Validation: $($task.validation)")
        [void]$md.AppendLine()
    }
}
Set-Content -Path $mdPath -Value $md.ToString() -Encoding UTF8

$summary = [pscustomobject][ordered]@{
    schemaVersion = "gate-followup-slicer/v1"
    runId = $RunId
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    taskCount = $orderedTasks.Count
    openAgentExecutableCount = @($orderedTasks | Where-Object { [bool]$_.agentExecutable }).Count
    tasksPath = ("state/backlog/gate-followup-tasks.jsonl")
    backlogPath = ("state/backlog/gate-followup-backlog.md")
}
$summary | ConvertTo-Json -Depth 20 | Set-Content -Path $summaryPath -Encoding UTF8

$currentTicketPath = Join-Path $workspacePath "current-ticket.md"
$selected = @($orderedTasks | Where-Object { [bool]$_.agentExecutable } | Select-Object -First 1)
if ($selected.Count -gt 0 -and (-not $DoNotOverwriteCurrentTicket -or -not (Test-Path $currentTicketPath))) {
    $task = $selected[0]
    $ticketLines = @(
        "# Current ticket: $($task.title)",
        "",
        "Schema: ``gate-followup-current-ticket/v1``",
        "Source: ``$($task.source)``",
        "Run: ``$RunId``",
        "Task id: ``$($task.id)``",
        "Finding id: ``$($task.findingId)``",
        "",
        "## Objective",
        "",
        [string]$task.objective,
        "",
        "## Constraints",
        "",
        "- Allowed writes: ``$($task.allowedWriteScope)``",
        "- Do not broaden scope or bypass final-gate/scope/harness-policy checks.",
        "- If the fix requires product-tree writes outside ``migration/**``, stop and mark the task as non-agent-executable with evidence.",
        "",
        "## Evidence",
        "",
        [string]$task.evidence,
        "",
        "## Validation",
        "",
        [string]$task.validation,
        "",
        "## Completion",
        "",
        "After the bounded fix, run:",
        "",
        '```powershell',
        "migration/scripts/check-final-gate.ps1 -Workspace migration -RepoRoot .",
        '```',
        "",
        "Then update ``migration/state/backlog/gate-followup-tasks.jsonl`` or add a sentinel finding status update if the blocker is resolved/non-agent-executable."
    )
    $ticket = $ticketLines -join [Environment]::NewLine
    Set-Content -Path $currentTicketPath -Value $ticket -Encoding UTF8

    # Start the current-ticket lifecycle immediately so /supervised-task continue
    # routes this ticket through reviewer/executor before another wave.
    $ticketStatusPath = Join-Path $workspacePath "state/current-ticket-status.json"
    $ticketLedgerPath = Join-Path $workspacePath "state/current-ticket-ledger.jsonl"
    $ticketEvent = [pscustomobject][ordered]@{
        schemaVersion = "current-ticket-lifecycle/v1"
        event = "CURRENT_TICKET_STATUS_UPDATED"
        status = "READY"
        previousStatus = $null
        runId = $RunId
        ticketId = [string]$task.id
        title = [string]$task.title
        ticketPath = "current-ticket.md"
        source = "slice-gate-followups"
        actor = "gate-followup-slicer"
        summary = "Selected the highest-priority agent-executable gate follow-up task."
        evidence = [string]$task.evidence
        result = "current-ticket-created"
        updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }
    $ticketStatus = [pscustomobject][ordered]@{
        schemaVersion = "current-ticket-lifecycle/v1"
        status = "READY"
        runId = $RunId
        ticketId = [string]$task.id
        title = [string]$task.title
        ticketPath = "current-ticket.md"
        ticketSchema = "gate-followup-current-ticket/v1"
        source = "slice-gate-followups"
        actor = "gate-followup-slicer"
        summary = "Selected the highest-priority agent-executable gate follow-up task."
        evidence = [string]$task.evidence
        result = "current-ticket-created"
        updatedAtUtc = $ticketEvent.updatedAtUtc
    }
    $ticketStatus | ConvertTo-Json -Depth 20 | Set-Content -Path $ticketStatusPath -Encoding UTF8
    Add-Content -Path $ticketLedgerPath -Encoding UTF8 -Value ($ticketEvent | ConvertTo-Json -Depth 20 -Compress)

    if (-not [string]::IsNullOrWhiteSpace([string]$task.findingId)) {
        $findingLifecycle = [pscustomobject][ordered]@{
            schemaVersion = "sentinel-finding-lifecycle/v1"
            event = "SENTINEL_FINDING_STATUS_UPDATED"
            findingId = [string]$task.findingId
            runId = $RunId
            category = [string]$task.category
            severity = [string]$task.severity
            status = "ASSIGNED"
            previousStatus = "OPEN"
            ticketId = [string]$task.id
            source = "slice-gate-followups"
            actor = "gate-followup-slicer"
            summary = "Assigned sentinel finding to current-ticket."
            evidence = [string]$task.evidence
            result = "current-ticket-created"
            updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        }
        $findingLine = $findingLifecycle | ConvertTo-Json -Depth 20 -Compress
        Add-Content -Path (Join-Path $workspacePath "state/sentinel-finding-ledger.jsonl") -Encoding UTF8 -Value $findingLine
        $runSentinelDir = Join-Path $workspacePath "runs/$RunId/sentinel"
        New-Item -ItemType Directory -Force -Path $runSentinelDir | Out-Null
        Add-Content -Path (Join-Path $runSentinelDir "sentinel-finding-lifecycle.jsonl") -Encoding UTF8 -Value $findingLine
    }
}

# Update continuation decision so the next supervised-task invocation has a concrete bridge action.
$continuationPath = Join-Path $workspacePath "state/continuation-decision.json"
$continuationMdPath = Join-Path $workspacePath "state/continuation-decision.md"
if ($orderedTasks.Count -gt 0) {
    $hasSelectedTask = $selected.Count -gt 0
    $nextAction = if ($hasSelectedTask) { "Run the current gate follow-up ticket from migration/current-ticket.md" } else { "Review gate follow-up backlog and request non-agent-executable cleanup for blocked tasks" }
    $decisionStatus = if ($hasSelectedTask) { "CONTINUE_REQUIRED" } else { "BLOCKED_NO_AGENT_EXECUTABLE_TASKS" }
    $currentTicketValue = if ($hasSelectedTask) { "current-ticket.md" } else { $null }
    $decision = [pscustomobject][ordered]@{
        status = $decisionStatus
        protocol = "Gate/sentinel diagnostics were sliced into bounded follow-up tasks. Execute the selected current-ticket before another wave or final handoff."
        nextAction = $nextAction
        source = "slice-gate-followups"
        evidence = "state/backlog/gate-followup-backlog.md"
        mustContinueBeforeUserMessage = $hasSelectedTask
        boundedAutoContinuation = $hasSelectedTask
        currentTicket = $currentTicketValue
    }
    $decision | ConvertTo-Json -Depth 20 | Set-Content -Path $continuationPath -Encoding UTF8
    $decisionMd = @"
# Harness Continuation Decision

Status: **$($decision.status)**

$($decision.protocol)

Next action: $($decision.nextAction)
Source: $($decision.source)
Evidence: $($decision.evidence)
"@
    Set-Content -Path $continuationMdPath -Value $decisionMd -Encoding UTF8
}

Write-Host "GATE_FOLLOWUP_TASKS_SLICED: $RunId tasks=$($orderedTasks.Count) currentTicket=$($selected.Count -gt 0)"
Write-Host "Backlog: $mdPath"
if ($selected.Count -gt 0) { Write-Host "Current ticket: $currentTicketPath" }

if ($orderedTasks.Count -eq 0) { exit 2 }
exit 0
