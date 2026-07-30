[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("StartInvocation", "RecordCycle", "Stop")]
    [string]$Action,
    [string]$Workspace = "migration",
    [ValidateSet("standard", "continue", "continuous", "bounded")]
    [string]$Mode = "standard",
    [string]$InvocationId = "",
    [ValidateRange(1, 5)]
    [int]$CycleBudget = 5,
    [string]$CandidateFingerprint = "",
    [ValidateSet("PROGRESS", "NO_PROGRESS", "BLOCKED")]
    [string]$Result = "PROGRESS",
    [string]$MetricSummary = "",
    [ValidateSet("RUNNING", "COMPLETE", "STOPPED", "BLOCKED")]
    [string]$Status = "STOPPED",
    [string]$StopReason = ""
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

function New-State {
    return [ordered]@{
        schemaVersion = "standard-migration-autonomy/v1"
        invocationId = $null
        mode = "standard"
        status = "NOT_STARTED"
        cycleBudget = 5
        batchNumber = 1
        cyclesCompleted = 0
        totalCyclesCompleted = 0
        completedBatches = 0
        noProgressStreak = 0
        lastCandidateFingerprint = $null
        lastCycleResult = $null
        lastCheckpointReason = $null
        exhaustedCandidateFingerprints = @()
        completedCycles = @()
        cycleHistory = @()
        stopReason = $null
    }
}

function Convert-ToMutableState($InputState) {
    $state = New-State
    if ($null -eq $InputState) { return $state }
    foreach ($key in @($state.Keys)) {
        if ($null -ne $InputState.PSObject.Properties[$key]) { $state[$key] = $InputState.$key }
    }
    $state.exhaustedCandidateFingerprints = @($state.exhaustedCandidateFingerprints)
    $state.completedCycles = @($state.completedCycles)
    $state.cycleHistory = @($state.cycleHistory)
    return $state
}

function Write-State([string]$Path, $State) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $temp = "$Path.tmp"
    $State | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $Path -Force
}

$workspaceFull = Resolve-FullPath $Workspace
$statePath = Join-Path $workspaceFull "state/autonomy-state.json"
$loaded = $null
if (Test-Path -LiteralPath $statePath) {
    try { $loaded = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_STATE_INVALID_JSON: $($_.Exception.Message)" }
}
$state = Convert-ToMutableState $loaded
if ($state.schemaVersion -ne "standard-migration-autonomy/v1") { throw "AUTONOMY_STATE_SCHEMA_INVALID: $($state.schemaVersion)" }

switch ($Action) {
    "StartInvocation" {
        if ([string]::IsNullOrWhiteSpace($InvocationId)) { $InvocationId = [guid]::NewGuid().ToString("N") }
        $state.invocationId = $InvocationId
        $state.mode = $Mode
        $state.status = "RUNNING"
        $state.cycleBudget = $CycleBudget
        $state.batchNumber = 1
        $state.cyclesCompleted = 0
        $state.totalCyclesCompleted = 0
        $state.completedBatches = 0
        $state.noProgressStreak = 0
        $state.lastCandidateFingerprint = $null
        $state.lastCycleResult = $null
        $state.lastCheckpointReason = $null
        $state.completedCycles = @()
        $state.cycleHistory = @()
        $state.stopReason = $null
    }
    "RecordCycle" {
        if ($state.status -ne "RUNNING") { throw "AUTONOMY_STATE_NOT_RUNNING: start a new invocation first." }
        if ([string]::IsNullOrWhiteSpace($CandidateFingerprint)) { throw "AUTONOMY_CANDIDATE_FINGERPRINT_REQUIRED" }
        if ([int]$state.cyclesCompleted -ge [int]$state.cycleBudget) { throw "AUTONOMY_CYCLE_BUDGET_ALREADY_REACHED" }

        $exhausted = @($state.exhaustedCandidateFingerprints)
        if ($Result -eq "NO_PROGRESS" -and $exhausted -contains $CandidateFingerprint) {
            throw "AUTONOMY_CANDIDATE_ALREADY_EXHAUSTED: $CandidateFingerprint"
        }

        $record = [ordered]@{
            batchNumber = [int]$state.batchNumber
            cycleNumber = ([int]$state.cyclesCompleted + 1)
            totalCycleNumber = ([int]$state.totalCyclesCompleted + 1)
            candidateFingerprint = $CandidateFingerprint
            result = $Result
            metricSummary = $MetricSummary
            completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
        $state.completedCycles = @(@($state.completedCycles) + @([pscustomobject]$record))
        $state.cycleHistory = @(@($state.cycleHistory) + @([pscustomobject]$record))
        $state.cyclesCompleted = [int]$state.cyclesCompleted + 1
        $state.totalCyclesCompleted = [int]$state.totalCyclesCompleted + 1
        $state.lastCandidateFingerprint = $CandidateFingerprint
        $state.lastCycleResult = $Result
        $state.lastCheckpointReason = $null

        if ($Result -eq "PROGRESS") {
            $state.noProgressStreak = 0
        }
        elseif ($Result -eq "NO_PROGRESS") {
            $state.noProgressStreak = [int]$state.noProgressStreak + 1
            $state.exhaustedCandidateFingerprints = @($exhausted + @($CandidateFingerprint) | Select-Object -Unique)
        }
        else {
            $state.noProgressStreak = 0
            $state.exhaustedCandidateFingerprints = @($exhausted + @($CandidateFingerprint) | Select-Object -Unique)
        }

        if ([int]$state.noProgressStreak -ge 2) {
            $history = @($state.cycleHistory)
            $last = $history[$history.Count - 1]
            $previous = $history[$history.Count - 2]
            if ($last.result -ne "NO_PROGRESS" -or $previous.result -ne "NO_PROGRESS") {
                throw "AUTONOMY_NO_PROGRESS_STREAK_CORRUPT"
            }
            if ($last.candidateFingerprint -eq $previous.candidateFingerprint) {
                throw "AUTONOMY_NO_PROGRESS_CANDIDATES_NOT_DISTINCT"
            }
            $state.status = "STOPPED"
            $state.stopReason = "STOPPED_TWO_CONSECUTIVE_NO_PROGRESS"
        }
        elseif ([int]$state.cyclesCompleted -ge [int]$state.cycleBudget) {
            $state.lastCheckpointReason = "AUTONOMOUS_CYCLE_BUDGET_REACHED"
            if ($state.mode -eq "continuous") {
                $state.completedBatches = [int]$state.completedBatches + 1
                $state.batchNumber = [int]$state.batchNumber + 1
                $state.cyclesCompleted = 0
                $state.completedCycles = @()
                $state.status = "RUNNING"
                $state.stopReason = $null
                Write-Host "AUTONOMOUS_CYCLE_BUDGET_REACHED"
                Write-Host "AUTONOMY_CONTINUOUS_BATCH_ROLLOVER"
            }
            else {
                $state.status = "STOPPED"
                $state.stopReason = "AUTONOMOUS_CYCLE_BUDGET_REACHED"
            }
        }
        else {
            $state.status = "RUNNING"
            $state.stopReason = $null
        }
    }
    "Stop" {
        if ([string]::IsNullOrWhiteSpace($StopReason)) { throw "AUTONOMY_STOP_REASON_REQUIRED" }
        if ($Status -eq "COMPLETE" -and $StopReason -ne "SUCCESS") { throw "AUTONOMY_COMPLETE_REQUIRES_SUCCESS" }
        if ($state.mode -eq "continuous" -and $StopReason -eq "AUTONOMOUS_CYCLE_BUDGET_REACHED") {
            throw "AUTONOMY_CONTINUOUS_BUDGET_IS_CHECKPOINT_NOT_STOP"
        }
        $state.status = $Status
        $state.stopReason = $StopReason
    }
}

Write-State $statePath $state
Write-Host "AUTONOMY_STATE_UPDATED"
Write-Host "Invocation: $($state.invocationId)"
Write-Host "Mode: $($state.mode)"
Write-Host "Status: $($state.status)"
Write-Host "Batch: $($state.batchNumber)"
Write-Host "Cycles in batch: $($state.cyclesCompleted)/$($state.cycleBudget)"
Write-Host "Total cycles: $($state.totalCyclesCompleted)"
Write-Host "No-progress streak: $($state.noProgressStreak)"
Write-Host "Checkpoint: $($state.lastCheckpointReason)"
Write-Host "Stop reason: $($state.stopReason)"
