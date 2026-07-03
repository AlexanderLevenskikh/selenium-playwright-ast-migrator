param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [Parameter(Mandatory = $true)][string]$Phase,
    [Parameter(Mandatory = $true)][string]$Action,
    [string]$Status = "info",
    [string]$Detail = "",
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
        # Event writing must not break a dogfood/finalization flow only because optional metadata
        # was passed through a shell with stripped quotes (for example: {runId:run-001}).
        # Preserve the raw value as diagnostic metadata and keep the event append-only.
        Write-Warning "DataJson was not valid JSON; storing it as rawDataJson metadata. Received: $DataJson"
        $data = [ordered]@{
            rawDataJson = $DataJson
            dataJsonParseStatus = "invalid-json-preserved"
        }
    }
}

$event = [ordered]@{
    utc = [DateTimeOffset]::UtcNow.ToString("o")
    runId = $RunId
    phase = $Phase
    action = $Action
    status = $Status
    detail = $Detail
    data = $data
}

$line = $event | ConvertTo-Json -Compress -Depth 20
New-Item -ItemType Directory -Force -Path (Join-Path $workspacePath "state") | Out-Null
Add-Content -Path (Join-Path $workspacePath "state/harness-events.jsonl") -Encoding UTF8 -Value $line

$tracePath = Join-Path $workspacePath "runs/$RunId/trace.jsonl"
if (Test-Path (Split-Path -Parent $tracePath)) {
    Add-Content -Path $tracePath -Encoding UTF8 -Value $line
}

Write-Host "HARNESS_EVENT_RECORDED: $RunId $Phase/$Action $Status"
