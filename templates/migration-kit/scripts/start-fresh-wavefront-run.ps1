<#
.SYNOPSIS
Archive the current wavefront pilot and reset volatile orchestration state while preserving project memory.

.DESCRIPTION
Creates a forensic archive under migration/archive/, copies the existing plan, runs, current ticket,
and volatile state into it, then removes only those volatile artifacts. Project-scoped memory,
source-scope configuration, harness policy, adapter config, and migration-kit installation metadata
remain in place. The command is intended for an explicit `/supervised-task waves fresh` restart.
#>
param(
    [string]$Workspace = "migration",
    [string]$Label = "pilot",
    [switch]$KeepPlan
)

$ErrorActionPreference = "Stop"

function Copy-IfExists([string]$Source, [string]$DestinationRoot, [string]$WorkspaceRoot) {
    if (-not (Test-Path $Source)) { return $false }
    $workspaceFull = [System.IO.Path]::GetFullPath($WorkspaceRoot).TrimEnd('\', '/')
    $sourceFull = [System.IO.Path]::GetFullPath($Source)
    $prefix = $workspaceFull + [System.IO.Path]::DirectorySeparatorChar
    if (-not $sourceFull.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Archive source escapes workspace: $sourceFull"
    }
    $relative = $sourceFull.Substring($prefix.Length)
    $destination = Join-Path $DestinationRoot $relative
    $parent = Split-Path -Parent $destination
    if (-not [string]::IsNullOrWhiteSpace($parent)) { New-Item -ItemType Directory -Force -Path $parent | Out-Null }
    Copy-Item -Path $Source -Destination $destination -Recurse -Force
    return $true
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)
if (-not (Test-Path $workspacePath)) { throw "Workspace not found: $workspacePath" }

$safeLabel = (($Label.Trim().ToLowerInvariant() -replace '[^a-z0-9._-]+', '-') -replace '^-+|-+$', '')
if ([string]::IsNullOrWhiteSpace($safeLabel)) { $safeLabel = "pilot" }
$stamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss")
$archiveRoot = Join-Path $workspacePath "archive/$safeLabel-$stamp"
New-Item -ItemType Directory -Force -Path $archiveRoot | Out-Null

$copied = New-Object System.Collections.Generic.List[string]
$topLevelCandidates = @("runs", "plan", "current-ticket.md", "agent-state.md")
foreach ($relative in $topLevelCandidates) {
    $source = Join-Path $workspacePath $relative
    if (Copy-IfExists $source $archiveRoot $workspacePath) { [void]$copied.Add($relative) }
}

$statePath = Join-Path $workspacePath "state"
$volatileStateNames = @(
    "backlog",
    "harness-run.json",
    "final-gate-result.json", "final-gate-result.md",
    "continuation-decision.json", "continuation-decision.md",
    "task-slice-result.json",
    "current-ticket-status.json", "current-ticket-ledger.jsonl",
    "wave-progress-ledger.jsonl",
    "wave-quality-budget.json", "wave-quality-budget.md",
    "mapping-research-memory.json", "mapping-research-memory.md", "mapping-research-candidates.jsonl",
    "loop-guard.json", "loop-guard-ledger.jsonl",
    "sentinel-finding-ledger.jsonl",
    "harness-events.jsonl",
    "run-ledger.md"
)
foreach ($name in $volatileStateNames) {
    $source = Join-Path $statePath $name
    if (Copy-IfExists $source $archiveRoot $workspacePath) { [void]$copied.Add("state/$name") }
}

# Preserve a memory snapshot for forensics, but do not remove the live project memory.
$memoryPath = Join-Path $statePath "memory"
if (Copy-IfExists $memoryPath $archiveRoot $workspacePath) { [void]$copied.Add("state/memory (snapshot; live copy preserved)") }

$manifest = [ordered]@{
    schemaVersion = "wavefront-restart/v1"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    workspace = $workspacePath
    label = $safeLabel
    archiveRoot = $archiveRoot
    copiedArtifacts = $copied.ToArray()
    preservedLiveArtifacts = @(
        "state/memory/**",
        "state/source-scope.json",
        "state/harness-policy.json",
        "adapter-config.json",
        ".migration-kit/**"
    )
    nextAction = "/supervised-task waves"
}
$manifestPath = Join-Path $archiveRoot "archive-manifest.json"
$manifest | ConvertTo-Json -Depth 20 | Set-Content -Path $manifestPath -Encoding UTF8
if (-not (Test-Path $manifestPath)) { throw "Archive manifest was not created; refusing to reset state." }

if (Test-Path (Join-Path $workspacePath "runs")) { Remove-Item -Path (Join-Path $workspacePath "runs") -Recurse -Force }
if (-not $KeepPlan -and (Test-Path (Join-Path $workspacePath "plan"))) { Remove-Item -Path (Join-Path $workspacePath "plan") -Recurse -Force }
foreach ($relative in @("current-ticket.md", "agent-state.md")) {
    $path = Join-Path $workspacePath $relative
    if (Test-Path $path) { Remove-Item -Path $path -Force }
}
foreach ($name in $volatileStateNames) {
    $path = Join-Path $statePath $name
    if (Test-Path $path) { Remove-Item -Path $path -Recurse -Force }
}

New-Item -ItemType Directory -Force -Path $statePath | Out-Null
$receipt = [ordered]@{
    schemaVersion = "wavefront-restart/v1"
    restartedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    archiveRoot = $archiveRoot
    memoryPreserved = (Test-Path $memoryPath)
    planPreserved = [bool]$KeepPlan
    status = "READY_FOR_FRESH_WAVEFRONT"
    nextAction = "/supervised-task waves"
}
$receipt | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $statePath "wavefront-restart.json") -Encoding UTF8

Write-Host "WAVEFRONT_RESTART_READY"
Write-Host "Archive: $archiveRoot"
Write-Host "Memory preserved: $(Test-Path $memoryPath)"
Write-Host "Next: /supervised-task waves"
