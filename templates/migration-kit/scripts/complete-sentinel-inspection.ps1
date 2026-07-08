param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [ValidateSet("PASS", "FINDINGS", "BLOCKED", "ERROR")][string]$Status = "PASS",
    [string]$Summary = "Harness sentinel inspection completed.",
    [string]$ReportPath = ""
)

$ErrorActionPreference = "Stop"

function Read-LatestRunId([string]$WorkspacePath) {
    $agentState = Join-Path $WorkspacePath "agent-state.md"
    if (-not (Test-Path $agentState)) { return "" }
    $text = Get-Content -Raw -Path $agentState
    $m = [regex]::Match($text, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
    if ($m.Success) { return $m.Groups[1].Value }
    return ""
}

function Normalize-SentinelLifecycleStatus([string]$Status) {
    if ([string]::IsNullOrWhiteSpace($Status)) { return "OPEN" }
    $normalized = $Status.Trim().ToUpperInvariant().Replace("-", "_")
    switch ($normalized) {
        "CLOSED" { return "CLOSED" }
        "VERIFIED" { return "VERIFIED" }
        "NON_AGENT" { return "NON_AGENT_EXECUTABLE" }
        "NON_AGENT_EXECUTABLE" { return "NON_AGENT_EXECUTABLE" }
        "ACCEPTED" { return "ACCEPTED_RISK" }
        "ACCEPTED_RISK" { return "ACCEPTED_RISK" }
        "RESOLVED" { return "CLOSED" }
        "NON_BLOCKING" { return "ACCEPTED_RISK" }
        default { return $normalized }
    }
}

function Test-SentinelLifecycleTerminal([string]$Status) {
    return (Normalize-SentinelLifecycleStatus $Status) -match '^(VERIFIED|CLOSED|NON_AGENT_EXECUTABLE|ACCEPTED_RISK)$'
}

function Read-LifecycleStatuses([string]$WorkspacePath, [string]$RunId) {
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
                $map[$id] = [pscustomobject]@{ status = (Normalize-SentinelLifecycleStatus ([string]$entry.status)); updatedAtUtc = $updatedAtUtc }
            }
        }
    }
    return $map
}

function Count-Findings([string]$Path, [string]$WorkspacePath, [string]$RunId) {
    $counts = [ordered]@{
        total = 0
        openHighOrCriticalAgentExecutable = 0
    }

    if (-not (Test-Path $Path)) { return $counts }
    $lifecycleStatuses = Read-LifecycleStatuses $WorkspacePath $RunId

    foreach ($line in (Get-Content -Path $Path -ErrorAction SilentlyContinue)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $entry = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        $counts.total += 1
        $severity = [string]$entry.severity
        $id = [string]$entry.findingId
        $status = Normalize-SentinelLifecycleStatus ([string]$entry.status)
        if (-not [string]::IsNullOrWhiteSpace($id) -and $lifecycleStatuses.ContainsKey($id)) { $status = [string]$lifecycleStatuses[$id].status }
        $agentExecutable = $true
        if ($null -ne $entry.agentExecutable) { $agentExecutable = [bool]$entry.agentExecutable }
        $isHigh = $severity -match '^(?i:high|critical)$'
        $isOpen = -not (Test-SentinelLifecycleTerminal $status)
        if ($isHigh -and $isOpen -and $agentExecutable) {
            $counts.openHighOrCriticalAgentExecutable += 1
        }
    }
    return $counts
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = Read-LatestRunId $workspacePath
}
if ([string]::IsNullOrWhiteSpace($RunId)) {
    throw "RunId was not provided and could not be read from agent-state.md."
}

$runSentinelDir = Join-Path $workspacePath "runs/$RunId/sentinel"
New-Item -ItemType Directory -Force -Path $runSentinelDir | Out-Null

$reportFullPath = if ([string]::IsNullOrWhiteSpace($ReportPath)) { Join-Path $runSentinelDir "sentinel-report.md" } else { $ReportPath }
if (-not (Test-Path $reportFullPath)) {
    $report = @"
# Harness Sentinel Report

Status: $Status
Run: $RunId

## Scope inspected

No detailed report was supplied. This inspection marker records that sentinel completed a pass.

## Findings

$Summary
"@
    Set-Content -Path $reportFullPath -Encoding UTF8 -Value $report
}

$findingPath = Join-Path $runSentinelDir "sentinel-findings.jsonl"
$counts = Count-Findings $findingPath $workspacePath $RunId

$inspection = [ordered]@{
    schemaVersion = 1
    runId = $RunId
    status = $Status
    inspectedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    reportPath = "runs/$RunId/sentinel/sentinel-report.md"
    findingsPath = "runs/$RunId/sentinel/sentinel-findings.jsonl"
    summary = $Summary
    findingCount = $counts.total
    openHighOrCriticalAgentExecutableFindingCount = $counts.openHighOrCriticalAgentExecutable
}

$inspection | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runSentinelDir "sentinel-inspection.json") -Encoding UTF8
Write-Host "SENTINEL_INSPECTION_COMPLETED: $RunId $Status"
