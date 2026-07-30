[CmdletBinding()]
param(
    [string]$Workspace = "migration",
    [string]$HandoffPath = "",
    [string]$AutonomyStatePath = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

function Get-SingleField([string]$Text, [string]$Name) {
    $pattern = "(?m)^" + [regex]::Escape($Name) + ":\s*(.+?)\s*$"
    $matches = [regex]::Matches($Text, $pattern)
    if ($matches.Count -ne 1) {
        throw "HANDOFF_FIELD_COUNT_INVALID: '$Name' must appear exactly once; found $($matches.Count)."
    }
    return $matches[0].Groups[1].Value.Trim()
}

$workspaceFull = Resolve-FullPath $Workspace
if ([string]::IsNullOrWhiteSpace($HandoffPath)) { $HandoffPath = Join-Path $workspaceFull "state/handoff.md" }
if ([string]::IsNullOrWhiteSpace($AutonomyStatePath)) { $AutonomyStatePath = Join-Path $workspaceFull "state/autonomy-state.json" }
$handoffFull = Resolve-FullPath $HandoffPath
$stateFull = Resolve-FullPath $AutonomyStatePath

if (-not (Test-Path -LiteralPath $handoffFull)) { throw "HANDOFF_MISSING: $handoffFull" }
if (-not (Test-Path -LiteralPath $stateFull)) { throw "AUTONOMY_STATE_MISSING: $stateFull" }

$text = [System.IO.File]::ReadAllText($handoffFull)
$requiredHeadings = @(
    "## Current status",
    "## Latest run evidence",
    "## What happened in this invocation",
    "## Remaining root-cause clusters",
    "## Autonomous next actions",
    "## Human decisions required",
    "## Required checks before accepting the handoff",
    "## What not to do"
)
foreach ($heading in $requiredHeadings) {
    $count = [regex]::Matches($text, "(?m)^" + [regex]::Escape($heading) + "\s*$").Count
    if ($count -ne 1) { throw "HANDOFF_SECTION_COUNT_INVALID: '$heading' must appear exactly once; found $count." }
}

$status = Get-SingleField $text "Status"
$stopReason = Get-SingleField $text "Stop reason"
$mode = Get-SingleField $text "Mode"
$invocationId = Get-SingleField $text "Invocation ID"
$cycleBudgetText = Get-SingleField $text "Cycle budget"
$cyclesCompletedText = Get-SingleField $text "Cycles completed"
$totalCyclesCompletedText = Get-SingleField $text "Total cycles completed"
$continuousBatchesText = Get-SingleField $text "Continuous batches completed"
$noProgressText = Get-SingleField $text "No-progress streak"
$generatedSyntax = Get-SingleField $text "Generated syntax"
$projectVerification = Get-SingleField $text "Project verification"
$runtimeVerification = Get-SingleField $text "Runtime verification"

$allowedStatuses = @("NOT_STARTED", "RUNNING", "COMPLETE", "STOPPED", "BLOCKED")
if ($allowedStatuses -notcontains $status) { throw "HANDOFF_STATUS_INVALID: $status" }
$allowedModes = @("standard", "continue", "continuous", "bounded")
if ($allowedModes -notcontains $mode) { throw "HANDOFF_MODE_INVALID: $mode" }

[int]$cycleBudget = 0
[int]$cyclesCompleted = 0
[int]$totalCyclesCompleted = 0
[int]$continuousBatches = 0
[int]$noProgressStreak = 0
if (-not [int]::TryParse($cycleBudgetText, [ref]$cycleBudget) -or $cycleBudget -lt 1 -or $cycleBudget -gt 20) {
    throw "HANDOFF_CYCLE_BUDGET_INVALID: $cycleBudgetText"
}
if (-not [int]::TryParse($cyclesCompletedText, [ref]$cyclesCompleted) -or $cyclesCompleted -lt 0 -or $cyclesCompleted -gt $cycleBudget) {
    throw "HANDOFF_CYCLES_COMPLETED_INVALID: $cyclesCompletedText"
}
if (-not [int]::TryParse($totalCyclesCompletedText, [ref]$totalCyclesCompleted) -or $totalCyclesCompleted -lt $cyclesCompleted) {
    throw "HANDOFF_TOTAL_CYCLES_INVALID: $totalCyclesCompletedText"
}
if (-not [int]::TryParse($continuousBatchesText, [ref]$continuousBatches) -or $continuousBatches -lt 0) {
    throw "HANDOFF_CONTINUOUS_BATCHES_INVALID: $continuousBatchesText"
}
if (-not [int]::TryParse($noProgressText, [ref]$noProgressStreak) -or $noProgressStreak -lt 0 -or $noProgressStreak -gt 2) {
    throw "HANDOFF_NO_PROGRESS_STREAK_INVALID: $noProgressText"
}

if ($status -eq "COMPLETE" -and $stopReason -ne "SUCCESS") {
    throw "HANDOFF_COMPLETE_CONTRADICTION: COMPLETE requires Stop reason: SUCCESS."
}
if ($mode -eq "continuous" -and $stopReason -eq "AUTONOMOUS_CYCLE_BUDGET_REACHED") {
    throw "HANDOFF_CONTINUOUS_BUDGET_CONTRADICTION: continuous mode must roll over the budget checkpoint instead of stopping."
}
if ($stopReason -eq "AUTONOMOUS_CYCLE_BUDGET_REACHED" -and $status -eq "COMPLETE") {
    throw "HANDOFF_BUDGET_CONTRADICTION: budget exhaustion is not completion."
}
if ($stopReason -eq "STOPPED_TWO_CONSECUTIVE_NO_PROGRESS" -and $noProgressStreak -ne 2) {
    throw "HANDOFF_NO_PROGRESS_CONTRADICTION: two-no-progress stop requires No-progress streak: 2."
}
if ($stopReason -eq "AUTONOMOUS_CYCLE_BUDGET_REACHED" -and $cyclesCompleted -ne $cycleBudget) {
    throw "HANDOFF_BUDGET_COUNT_CONTRADICTION: budget stop requires completed cycles to equal cycle budget."
}

$claimsCompile = $text -match '(?i)\b(code|generated code|project)\s+(compiles|compiled)\s+cleanly\b' -or $text -match '(?i)\bclean compilation\b'
if ($claimsCompile -and $projectVerification -ne "PASS") {
    throw "HANDOFF_VALIDATION_OVERCLAIM: clean compilation requires Project verification: PASS; current value is '$projectVerification'."
}

try {
    $state = Get-Content -LiteralPath $stateFull -Raw | ConvertFrom-Json
}
catch {
    throw "AUTONOMY_STATE_INVALID_JSON: $($_.Exception.Message)"
}

if ($state.schemaVersion -ne "standard-migration-autonomy/v1") { throw "AUTONOMY_STATE_SCHEMA_INVALID: $($state.schemaVersion)" }
if ([int]$state.cycleBudget -ne $cycleBudget) { throw "HANDOFF_STATE_MISMATCH: cycleBudget" }
if ([int]$state.cyclesCompleted -ne $cyclesCompleted) { throw "HANDOFF_STATE_MISMATCH: cyclesCompleted" }
if ([int]$state.totalCyclesCompleted -ne $totalCyclesCompleted) { throw "HANDOFF_STATE_MISMATCH: totalCyclesCompleted" }
if ([int]$state.completedBatches -ne $continuousBatches) { throw "HANDOFF_STATE_MISMATCH: completedBatches" }
if ([int]$state.noProgressStreak -ne $noProgressStreak) { throw "HANDOFF_STATE_MISMATCH: noProgressStreak" }
if ([string]$state.status -ne $status) { throw "HANDOFF_STATE_MISMATCH: status" }
if (([string]$state.stopReason) -ne $stopReason -and -not (($null -eq $state.stopReason) -and $stopReason -eq "NONE")) {
    throw "HANDOFF_STATE_MISMATCH: stopReason"
}
if (([string]$state.mode) -ne $mode) { throw "HANDOFF_STATE_MISMATCH: mode" }
if ($invocationId -ne "NONE" -and ([string]$state.invocationId) -ne $invocationId) { throw "HANDOFF_STATE_MISMATCH: invocationId" }

$completedCycles = @($state.completedCycles)
$cycleHistory = @($state.cycleHistory)
if ($completedCycles.Count -ne $cyclesCompleted) {
    throw "AUTONOMY_STATE_CYCLE_HISTORY_INVALID: expected $cyclesCompleted completed cycle record(s), found $($completedCycles.Count)."
}

if ($cycleHistory.Count -ne $totalCyclesCompleted) {
    throw "AUTONOMY_STATE_TOTAL_CYCLE_HISTORY_INVALID: expected $totalCyclesCompleted total cycle record(s), found $($cycleHistory.Count)."
}

$fingerprints = @()
foreach ($cycle in $cycleHistory) {
    $fingerprint = [string]$cycle.candidateFingerprint
    $result = [string]$cycle.result
    if ([string]::IsNullOrWhiteSpace($fingerprint)) { throw "AUTONOMY_STATE_FINGERPRINT_MISSING" }
    if (@("PROGRESS", "NO_PROGRESS", "BLOCKED") -notcontains $result) { throw "AUTONOMY_STATE_CYCLE_RESULT_INVALID: $result" }
    $fingerprints += $fingerprint
}

if ($stopReason -eq "STOPPED_TWO_CONSECUTIVE_NO_PROGRESS") {
    if ($cycleHistory.Count -lt 2) { throw "AUTONOMY_STATE_NO_PROGRESS_HISTORY_MISSING" }
    $last = $cycleHistory[$cycleHistory.Count - 1]
    $previous = $cycleHistory[$cycleHistory.Count - 2]
    if ([string]$last.result -ne "NO_PROGRESS" -or [string]$previous.result -ne "NO_PROGRESS") {
        throw "AUTONOMY_STATE_NO_PROGRESS_HISTORY_INVALID: last two cycles must be NO_PROGRESS."
    }
    if ([string]$last.candidateFingerprint -eq [string]$previous.candidateFingerprint) {
        throw "AUTONOMY_STATE_NO_PROGRESS_CANDIDATES_NOT_DISTINCT"
    }
}

Write-Host "HANDOFF_CONTRACT_PASS"
Write-Host "Status: $status"
Write-Host "Stop reason: $stopReason"
Write-Host "Mode: $mode"
Write-Host "Cycles in current batch: $cyclesCompleted/$cycleBudget"
Write-Host "Total cycles: $totalCyclesCompleted"
Write-Host "Continuous batches completed: $continuousBatches"
Write-Host "Validation dimensions: syntax=$generatedSyntax project=$projectVerification runtime=$runtimeVerification"
