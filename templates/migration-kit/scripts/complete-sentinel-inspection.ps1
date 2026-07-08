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

function Count-Findings([string]$Path) {
    $counts = [ordered]@{
        total = 0
        openHighOrCriticalAgentExecutable = 0
    }

    if (-not (Test-Path $Path)) { return $counts }

    foreach ($line in (Get-Content -Path $Path -ErrorAction SilentlyContinue)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $entry = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        $counts.total += 1
        $severity = [string]$entry.severity
        $status = [string]$entry.status
        $agentExecutable = $true
        if ($null -ne $entry.agentExecutable) { $agentExecutable = [bool]$entry.agentExecutable }
        $isHigh = $severity -match '^(?i:high|critical)$'
        $isOpen = -not ($status -match '^(?i:closed|accepted|resolved|triaged|non-blocking)$')
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
$counts = Count-Findings $findingPath

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
