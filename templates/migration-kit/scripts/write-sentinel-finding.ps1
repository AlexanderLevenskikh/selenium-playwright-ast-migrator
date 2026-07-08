param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [Parameter(Mandatory = $true)][string]$Category,
    [ValidateSet("info", "low", "medium", "high", "critical")][string]$Severity = "medium",
    [Parameter(Mandatory = $true)][string]$Summary,
    [string]$Evidence = "",
    [string]$RecommendedAction = "",
    [bool]$AgentExecutable = $true,
    [string]$Status = "open",
    [string]$DataJson = ""
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

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = Read-LatestRunId $workspacePath
}
if ([string]::IsNullOrWhiteSpace($RunId)) {
    throw "RunId was not provided and could not be read from agent-state.md."
}

$data = $null
if (-not [string]::IsNullOrWhiteSpace($DataJson)) {
    try {
        $data = $DataJson | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "DataJson must be valid JSON. Error: $($_.Exception.Message)"
    }
}

$evidenceItems = @()
if (-not [string]::IsNullOrWhiteSpace($Evidence)) {
    $evidenceItems = @($Evidence -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

$finding = [ordered]@{
    schemaVersion = 1
    findingId = "sentinel-" + ([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())
    runId = $RunId
    category = $Category
    severity = $Severity
    status = $Status
    summary = $Summary
    evidence = $evidenceItems
    recommendedAction = $RecommendedAction
    agentExecutable = [bool]$AgentExecutable
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}
if ($null -ne $data) {
    $finding.data = $data
}

$runSentinelDir = Join-Path $workspacePath "runs/$RunId/sentinel"
New-Item -ItemType Directory -Force -Path $runSentinelDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $workspacePath "state") | Out-Null

$line = $finding | ConvertTo-Json -Compress -Depth 20
Add-Content -Path (Join-Path $runSentinelDir "sentinel-findings.jsonl") -Encoding UTF8 -Value $line
Add-Content -Path (Join-Path $workspacePath "state/sentinel-ledger.jsonl") -Encoding UTF8 -Value $line

$observationPath = Join-Path $workspacePath "runs/$RunId/session-observations.jsonl"
if (Test-Path (Split-Path -Parent $observationPath)) {
    Add-Content -Path $observationPath -Encoding UTF8 -Value $line
}

Write-Host "SENTINEL_FINDING_RECORDED: $RunId $Category $Severity"
