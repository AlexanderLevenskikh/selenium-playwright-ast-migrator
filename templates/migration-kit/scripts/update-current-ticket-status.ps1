<# Record a bounded repair-ticket status for the standard migration flow. #>
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
    [string]$Result = ""
)
$ErrorActionPreference = "Stop"
function Resolve-FullPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}
function Write-JsonAtomic([string]$Path, $Value) {
    $dir = Split-Path -Parent $Path
    if ($dir) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $tmp = "$Path.tmp-$([Guid]::NewGuid().ToString('N'))"
    try {
        $Value | ConvertTo-Json -Depth 20 | Set-Content -Path $tmp -Encoding UTF8
        Move-Item -Force -Path $tmp -Destination $Path
    } finally { if (Test-Path $tmp) { Remove-Item -Force $tmp -ErrorAction SilentlyContinue } }
}
$workspacePath = Resolve-FullPath $Workspace
if (-not (Test-Path $workspacePath)) { throw "Workspace not found: $workspacePath" }
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $latest = Get-ChildItem (Join-Path $workspacePath "runs") -Directory -Filter "run-*" -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -ne $latest) { $RunId = $latest.Name }
}
if ([string]::IsNullOrWhiteSpace($TicketId)) { $TicketId = "ticket-$([DateTimeOffset]::UtcNow.ToString('yyyyMMddHHmmss'))" }
$entry = [ordered]@{
    schemaVersion = "standard-repair-ticket-status/v1"
    ticketId = $TicketId
    runId = $RunId
    status = $Status
    actor = $Actor
    source = $Source
    summary = $Summary
    evidence = @($Evidence -split '[,;]' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() })
    result = $Result
    updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}
$stateDir = Join-Path $workspacePath "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
Write-JsonAtomic (Join-Path $stateDir "current-ticket-status.json") $entry
Add-Content -Path (Join-Path $stateDir "current-ticket-ledger.jsonl") -Encoding UTF8 -Value ($entry | ConvertTo-Json -Depth 20 -Compress)
if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    $ticketDir = Join-Path $workspacePath "runs/$RunId/tickets"
    New-Item -ItemType Directory -Force -Path $ticketDir | Out-Null
    Write-JsonAtomic (Join-Path $ticketDir "$TicketId.json") $entry
}
Write-Host "CURRENT_TICKET_STATUS_RECORDED: $TicketId -> $Status"
