<#
.SYNOPSIS
Detects repeated agent lifecycle dispatches before they turn into an infinite loop.

.DESCRIPTION
The guard records a normalized fingerprint of the current supervised/post-final dispatch.
If the same goal/stage/next-action is recorded repeatedly without a new concrete action,
it writes state/loop-guard.json and exits with code 2. Agents should stop and report
LOOP_GUARD_BLOCKED instead of printing the same Goal/Progress/Next Steps block again.
#>
param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [string]$TicketId = "",
    [string]$Goal = "",
    [string]$Stage = "",
    [string]$NextAction = "",
    [string]$Evidence = "",
    [int]$MaxRepeats = 3,
    [switch]$Reset
)

$ErrorActionPreference = "Stop"

function Read-JsonIfExists([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    $raw = Get-Content -Raw -Path $Path
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    return $raw | ConvertFrom-Json -ErrorAction Stop
}

function Normalize-LoopText([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return ([regex]::Replace($Value.Trim().ToLowerInvariant(), '\s+', ' '))
}

function Get-Sha256Text([string]$Value) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Value)
        $hash = $sha.ComputeHash($bytes)
        return ([System.BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)
if (-not (Test-Path $workspacePath)) { throw "Workspace not found: $workspacePath" }

$stateDir = Join-Path $workspacePath "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$guardPath = Join-Path $stateDir "loop-guard.json"
$ledgerPath = Join-Path $stateDir "loop-guard-ledger.jsonl"

if ($Reset) {
    if (Test-Path $guardPath) { Remove-Item -Path $guardPath -Force }
    Write-Host "LOOP_GUARD_RESET"
    exit 0
}

$fingerprintInput = "run=$RunId`nticket=$TicketId`ngoal=$(Normalize-LoopText $Goal)`nstage=$(Normalize-LoopText $Stage)`nnext=$(Normalize-LoopText $NextAction)"
$fingerprint = Get-Sha256Text $fingerprintInput
$previous = Read-JsonIfExists $guardPath

$repeatCount = 1
if ($null -ne $previous -and [string]$previous.fingerprint -eq $fingerprint) {
    $repeatCount = [int]$previous.repeatCount + 1
}

$status = if ($repeatCount -ge $MaxRepeats) { "BLOCKED_REPETITION" } else { "PASS" }
$payload = [ordered]@{
    schemaVersion = "loop-guard/v1"
    status = $status
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    runId = $RunId
    ticketId = $TicketId
    goal = $Goal
    stage = $Stage
    nextAction = $NextAction
    evidence = $Evidence
    fingerprint = $fingerprint
    repeatCount = $repeatCount
    maxRepeats = $MaxRepeats
    instruction = if ($status -eq "BLOCKED_REPETITION") { "Stop. Do not repeat the same Goal/Progress/Next Steps block. Execute a new concrete action only if one is available; otherwise report LOOP_GUARD_BLOCKED with this file as evidence." } else { "Continue with one concrete bounded action before the next user-facing handoff." }
}

$payload | ConvertTo-Json -Depth 20 | Set-Content -Path $guardPath -Encoding UTF8
Add-Content -Path $ledgerPath -Encoding UTF8 -Value ($payload | ConvertTo-Json -Compress -Depth 20)

if ($status -eq "BLOCKED_REPETITION") {
    Write-Host "LOOP_GUARD_BLOCKED: repeated dispatch fingerprint $repeatCount time(s)."
    Write-Host "Report: $guardPath"
    exit 2
}

Write-Host "LOOP_GUARD_PASS: repeatCount=$repeatCount maxRepeats=$MaxRepeats"
Write-Host "Report: $guardPath"
exit 0
