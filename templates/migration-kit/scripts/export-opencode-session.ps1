param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [string]$InputPath = "",
    [string]$Content = "",
    [string]$Source = "opencode-session",
    [switch]$Append
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

function Read-TextOrEmpty([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }
    if (-not (Test-Path $Path)) { throw "InputPath does not exist: $Path" }
    return Get-Content -Raw -Path $Path
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = Read-LatestRunId $workspacePath
}
if ([string]::IsNullOrWhiteSpace($RunId)) {
    throw "RunId was not provided and could not be read from agent-state.md."
}

$runDir = Join-Path $workspacePath "runs/$RunId"
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$sessionPath = Join-Path $runDir "opencode-session-export.md"
$manifestPath = Join-Path $runDir "opencode-session-export.json"
$observationsPath = Join-Path $runDir "session-observations.jsonl"

$inputText = Read-TextOrEmpty $InputPath
if (-not [string]::IsNullOrWhiteSpace($Content)) {
    if (-not [string]::IsNullOrWhiteSpace($inputText)) {
        $inputText = $inputText.TrimEnd() + "`n`n" + $Content
    }
    else {
        $inputText = $Content
    }
}

$existing = ""
if ($Append -and (Test-Path $sessionPath)) {
    $existing = (Get-Content -Raw -Path $sessionPath).TrimEnd() + "`n`n---`n`n"
}

$exportStatus = "REAL_EXPORT"
$unavailableReason = ""
if ([string]::IsNullOrWhiteSpace($inputText)) {
    $exportStatus = "UNAVAILABLE_WITH_REASON"
    $unavailableReason = "Native OpenCode transcript was not provided to export-opencode-session. Use run reports, project verification, and session-observations.jsonl as forensic evidence; do not treat this file as a transcript."
    $inputText = "Transcript unavailable. Reason: $unavailableReason"
}

$timestamp = [DateTimeOffset]::UtcNow.ToString("o")
$body = @"
# OpenCode Session Export

- Run: `$RunId`
- Workspace: `$Workspace`
- Source: `$Source`
- Exported at UTC: `$timestamp`
- Export status: `$exportStatus`

## Transcript / observed session content

$inputText
"@

Set-Content -Path $sessionPath -Encoding UTF8 -Value ($existing + $body)

$manifest = [ordered]@{
    schemaVersion = 1
    runId = $RunId
    workspace = $Workspace
    source = $Source
    exportedAtUtc = $timestamp
    sessionExportPath = "runs/$RunId/opencode-session-export.md"
    observationsPath = "runs/$RunId/session-observations.jsonl"
    inputPath = $InputPath
    appended = [bool]$Append
    exportStatus = $exportStatus
    unavailableReason = $unavailableReason
}
$manifest | ConvertTo-Json -Depth 20 | Set-Content -Path $manifestPath -Encoding UTF8

if (-not (Test-Path $observationsPath)) {
    New-Item -ItemType File -Path $observationsPath | Out-Null
}

Write-Host "OPENCODE_SESSION_EXPORTED: $RunId $exportStatus $sessionPath"
