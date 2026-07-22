param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [string]$Summary = "",
    [string[]]$SourceArtifacts = @(),
    [string[]]$MustPreserve = @("current ticket id", "failing tests", "scope contract", "known forbidden actions"),
    [string]$LastErrors = "",
    [switch]$NoOverwriteSummaries
)

$ErrorActionPreference = "Stop"

function Get-WorkspacePath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Set-Utf8NoBom([string]$Path, [string]$Value) {
    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Value, $utf8)
}

function Read-LatestRunId([string]$WorkspacePath) {
    $agentState = Join-Path $WorkspacePath "agent-state.md"
    if (Test-Path $agentState) {
        $text = Get-Content -Raw -Path $agentState
        $m = [regex]::Match($text, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    return ""
}

function Normalize-RelativePath([string]$WorkspacePath, [string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }
    $full = if ([System.IO.Path]::IsPathRooted($Path)) { [System.IO.Path]::GetFullPath($Path) } else { [System.IO.Path]::GetFullPath((Join-Path $WorkspacePath $Path)) }
    $workspaceUri = New-Object System.Uri(($WorkspacePath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar))
    $fileUri = New-Object System.Uri($full)
    return [System.Uri]::UnescapeDataString($workspaceUri.MakeRelativeUri($fileUri).ToString()).Replace('\\', '/')
}

$workspacePath = Get-WorkspacePath $Workspace
if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = Read-LatestRunId $workspacePath }
if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = "unknown-run" }

$memoryDir = Join-Path $workspacePath "state/memory"
New-Item -ItemType Directory -Force -Path $memoryDir | Out-Null
$createdAt = [DateTimeOffset]::UtcNow.ToString("o")

if ([string]::IsNullOrWhiteSpace($Summary)) {
    $Summary = "Compaction checkpoint for $RunId. Preserve current ticket, scope contract, active blockers, failing tests, and final-gate status before continuing."
}

$quickRecapPath = Join-Path $memoryDir "quick-recap.md"
$currentTicketPath = Join-Path $memoryDir "current-ticket.md"
$lastErrorsPath = Join-Path $memoryDir "last-errors.md"

if ((-not $NoOverwriteSummaries) -or (-not (Test-Path $quickRecapPath))) {
    Set-Utf8NoBom $quickRecapPath @"
# Quick Recap

Run: $RunId
Updated: $createdAt

$Summary

## Must preserve

$($MustPreserve | ForEach-Object { "- $_" } | Out-String)
"@
}

if ((-not $NoOverwriteSummaries) -or (-not (Test-Path $currentTicketPath))) {
    Set-Utf8NoBom $currentTicketPath @"
# Current Ticket

Run: $RunId
Updated: $createdAt

Summary: $Summary
"@
}

if ((-not $NoOverwriteSummaries) -or (-not (Test-Path $lastErrorsPath))) {
    $errorsText = if ([string]::IsNullOrWhiteSpace($LastErrors)) { "No errors captured in this compaction receipt." } else { $LastErrors }
    Set-Utf8NoBom $lastErrorsPath @"
# Last Errors

Run: $RunId
Updated: $createdAt

$errorsText
"@
}

$normalizedSources = @($SourceArtifacts | ForEach-Object { Normalize-RelativePath $workspacePath $_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($normalizedSources.Count -eq 0) {
    foreach ($candidate in @("runs/$RunId/events.jsonl", "runs/$RunId/evidence/index.json", "state/final-gate-result.json")) {
        if (Test-Path (Join-Path $workspacePath $candidate)) { $normalizedSources += $candidate }
    }
}

$receipt = [ordered]@{
    schemaVersion = 1
    runId = $RunId
    createdAtUtc = $createdAt
    sourceArtifacts = @($normalizedSources)
    summaryArtifacts = @("state/memory/quick-recap.md", "state/memory/current-ticket.md", "state/memory/last-errors.md")
    mustPreserve = @($MustPreserve)
    status = "PASS"
}

$receiptLine = $receipt | ConvertTo-Json -Compress -Depth 20
Add-Content -Path (Join-Path $memoryDir "compaction-receipts.jsonl") -Encoding UTF8 -Value $receiptLine
Write-Host "MEMORY_COMPACTION_RECEIPT_WRITTEN: state/memory/compaction-receipts.jsonl"
