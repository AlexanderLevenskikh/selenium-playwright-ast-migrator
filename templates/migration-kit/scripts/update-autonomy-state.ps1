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
    [string]$EvaluationPath = "",
    # Legacy parameters are accepted by the parser only so old agents fail with a precise
    # contract error instead of silently retaining authority over progress classification.
    [string]$CandidateFingerprint = "",
    [string]$Result = "",
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
        schemaVersion = "standard-migration-autonomy/v2"
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
        lastDecision = $null
        lastEvaluationSha256 = $null
        lastBeforeStateHash = $null
        lastAfterStateHash = $null
        currentStateHash = $null
        rollbackRequired = $false
        lastCheckpointReason = $null
        exhaustedCandidateFingerprints = @()
        visitedStateHashes = @()
        completedCycles = @()
        cycleHistory = @()
        stopReason = $null
    }
}

function Convert-ToMutableState($InputState) {
    $state = New-State
    if ($null -eq $InputState) { return $state }

    $sourceSchema = [string]$InputState.schemaVersion
    if ($sourceSchema -ne "standard-migration-autonomy/v1" -and $sourceSchema -ne "standard-migration-autonomy/v2") {
        throw "AUTONOMY_STATE_SCHEMA_INVALID: $sourceSchema"
    }

    foreach ($key in @($state.Keys)) {
        if ($null -ne $InputState.PSObject.Properties[$key]) { $state[$key] = $InputState.$key }
    }
    $state.schemaVersion = "standard-migration-autonomy/v2"
    $state.exhaustedCandidateFingerprints = @($state.exhaustedCandidateFingerprints)
    $state.visitedStateHashes = @($state.visitedStateHashes)
    $state.completedCycles = @($state.completedCycles)
    $state.cycleHistory = @($state.cycleHistory)
    return $state
}

function Write-State([string]$Path, $State) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $temp = "$Path.tmp"
    $State | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $Path -Force
}

function Read-Evaluation([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "AUTONOMY_EVALUATION_REQUIRED" }
    $full = Resolve-FullPath $Path
    if (-not (Test-Path -LiteralPath $full)) { throw "AUTONOMY_EVALUATION_NOT_FOUND: $full" }
    try { $evaluation = Get-Content -LiteralPath $full -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_EVALUATION_INVALID_JSON: $($_.Exception.Message)" }

    if ([string]$evaluation.SchemaVersion -ne "migrator-remediation-evaluation/v1") {
        throw "AUTONOMY_EVALUATION_SCHEMA_INVALID: $($evaluation.SchemaVersion)"
    }
    if (@("ACCEPT", "REJECT_NO_PROGRESS", "REJECT_REGRESSION", "REJECT_CYCLE") -notcontains [string]$evaluation.Decision) {
        throw "AUTONOMY_EVALUATION_DECISION_INVALID: $($evaluation.Decision)"
    }
    foreach ($required in @("EvaluationSha256", "CandidateFingerprint")) {
        if ([string]::IsNullOrWhiteSpace([string]$evaluation.$required)) { throw "AUTONOMY_EVALUATION_FIELD_MISSING: $required" }
    }
    if ([string]::IsNullOrWhiteSpace([string]$evaluation.Before.StateHash) -or [string]::IsNullOrWhiteSpace([string]$evaluation.After.StateHash)) {
        throw "AUTONOMY_EVALUATION_STATE_HASH_MISSING"
    }
    return $evaluation
}

$workspaceFull = Resolve-FullPath $Workspace
$statePath = Join-Path $workspaceFull "state/autonomy-state.json"
$loaded = $null
if (Test-Path -LiteralPath $statePath) {
    try { $loaded = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_STATE_INVALID_JSON: $($_.Exception.Message)" }
}
$state = Convert-ToMutableState $loaded

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
        $state.lastDecision = $null
        $state.lastEvaluationSha256 = $null
        $state.lastBeforeStateHash = $null
        $state.lastAfterStateHash = $null
        $state.rollbackRequired = $false
        $state.lastCheckpointReason = $null
        $state.completedCycles = @()
        $state.cycleHistory = @()
        $state.stopReason = $null
        # currentStateHash and visitedStateHashes intentionally survive a fresh invocation.
        # A `continue` budget is fresh; logical state history is not.
    }
    "RecordCycle" {
        if ($state.status -ne "RUNNING") { throw "AUTONOMY_STATE_NOT_RUNNING: start a new invocation first." }
        if ([int]$state.cyclesCompleted -ge [int]$state.cycleBudget) { throw "AUTONOMY_CYCLE_BUDGET_ALREADY_REACHED" }
        if (-not [string]::IsNullOrWhiteSpace($CandidateFingerprint) -or -not [string]::IsNullOrWhiteSpace($Result) -or -not [string]::IsNullOrWhiteSpace($MetricSummary)) {
            throw "AUTONOMY_AGENT_PROGRESS_CLASSIFICATION_FORBIDDEN: use -EvaluationPath from `selenium-pw-migrator remediation evaluate`."
        }

        $evaluation = Read-Evaluation $EvaluationPath
        $beforeHash = [string]$evaluation.Before.StateHash
        $afterHash = [string]$evaluation.After.StateHash
        $decision = [string]$evaluation.Decision
        $fingerprint = [string]$evaluation.CandidateFingerprint

        if (-not [string]::IsNullOrWhiteSpace([string]$state.currentStateHash) -and [string]$state.currentStateHash -ne $beforeHash) {
            throw "AUTONOMY_EVALUATION_BASELINE_MISMATCH: expected $($state.currentStateHash), got $beforeHash"
        }

        $visited = @($state.visitedStateHashes)
        if ($visited -contains $afterHash -and $decision -ne "REJECT_CYCLE") {
            throw "AUTONOMY_EVALUATION_MISSED_CYCLE: Core evaluation must classify revisited state as REJECT_CYCLE."
        }

        if ([string]::IsNullOrWhiteSpace([string]$state.currentStateHash)) {
            $state.currentStateHash = $beforeHash
        }
        $state.visitedStateHashes = @($visited + @($beforeHash, $afterHash) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)

        $result = if ($decision -eq "ACCEPT") { "PROGRESS" } else { "NO_PROGRESS" }
        $rollbackRequired = [bool]$evaluation.RollbackRequired
        $record = [ordered]@{
            batchNumber = [int]$state.batchNumber
            cycleNumber = ([int]$state.cyclesCompleted + 1)
            totalCycleNumber = ([int]$state.totalCyclesCompleted + 1)
            candidateFingerprint = $fingerprint
            candidateLabel = [string]$evaluation.CandidateLabel
            result = $result
            decision = $decision
            reason = [string]$evaluation.Reason
            evaluationSha256 = [string]$evaluation.EvaluationSha256
            beforeStateHash = $beforeHash
            afterStateHash = $afterHash
            beforeDefects = $evaluation.Before.Defects
            afterDefects = $evaluation.After.Defects
            improvements = @($evaluation.Improvements)
            regressions = @($evaluation.Regressions)
            rollbackRequired = $rollbackRequired
            completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
        $state.completedCycles = @(@($state.completedCycles) + @([pscustomobject]$record))
        $state.cycleHistory = @(@($state.cycleHistory) + @([pscustomobject]$record))
        $state.cyclesCompleted = [int]$state.cyclesCompleted + 1
        $state.totalCyclesCompleted = [int]$state.totalCyclesCompleted + 1
        $state.lastCandidateFingerprint = $fingerprint
        $state.lastCycleResult = $result
        $state.lastDecision = $decision
        $state.lastEvaluationSha256 = [string]$evaluation.EvaluationSha256
        $state.lastBeforeStateHash = $beforeHash
        $state.lastAfterStateHash = $afterHash
        $state.rollbackRequired = $rollbackRequired
        $state.lastCheckpointReason = $null

        if ($decision -eq "ACCEPT") {
            $state.noProgressStreak = 0
            $state.currentStateHash = $afterHash
        }
        else {
            $state.noProgressStreak = [int]$state.noProgressStreak + 1
            $state.currentStateHash = $beforeHash
            $state.exhaustedCandidateFingerprints = @(@($state.exhaustedCandidateFingerprints) + @($fingerprint) | Select-Object -Unique)
        }

        if ($decision -eq "REJECT_CYCLE") {
            $state.status = "STOPPED"
            $state.stopReason = "REMEDIATION_CYCLE_DETECTED"
        }
        elseif ([int]$state.noProgressStreak -ge 2) {
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
Write-Host "Current state: $($state.currentStateHash)"
Write-Host "Last decision: $($state.lastDecision)"
Write-Host "Rollback required: $($state.rollbackRequired)"
Write-Host "No-progress streak: $($state.noProgressStreak)"
Write-Host "Checkpoint: $($state.lastCheckpointReason)"
Write-Host "Stop reason: $($state.stopReason)"
