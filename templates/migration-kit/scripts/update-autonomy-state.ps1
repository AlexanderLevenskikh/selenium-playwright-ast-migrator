[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("StartInvocation", "StartCycle", "AbortCycle", "Rebaseline", "ConfirmRollback", "RecordCycle", "Stop")]
    [string]$Action,
    [string]$Workspace = "migration",
    [ValidateSet("standard", "continue", "continuous", "bounded")]
    [string]$Mode = "standard",
    [string]$InvocationId = "",
    [ValidateRange(1, 5)]
    [int]$CycleBudget = 5,
    [string]$GuardPath = "",
    [string]$EvaluationPath = "",
    [string]$RebaselinePath = "",
    [string]$FinalGatePath = "",
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
        schemaVersion = "standard-migration-autonomy/v3"
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
        lastRebaselineSha256 = $null
        lastBeforeStateHash = $null
        lastAfterStateHash = $null
        currentStateHash = $null
        rollbackRequired = $false
        cycleInProgress = $false
        activeCycleBaselineStateHash = $null
        lastGuardSha256 = $null
        lastGuardDecision = $null
        lastWorkspaceIdentitySha256 = $null
        lastCheckpointReason = $null
        exhaustedCandidateFingerprints = @()
        visitedStateHashes = @()
        completedCycles = @()
        cycleHistory = @()
        rebaselineHistory = @()
        stopReason = $null
    }
}

function Convert-ToMutableState($InputState) {
    $state = New-State
    if ($null -eq $InputState) { return $state }

    $sourceSchema = [string]$InputState.schemaVersion
    if ($sourceSchema -ne "standard-migration-autonomy/v1" -and $sourceSchema -ne "standard-migration-autonomy/v2" -and $sourceSchema -ne "standard-migration-autonomy/v3") {
        throw "AUTONOMY_STATE_SCHEMA_INVALID: $sourceSchema"
    }

    foreach ($key in @($state.Keys)) {
        if ($null -ne $InputState.PSObject.Properties[$key]) { $state[$key] = $InputState.$key }
    }
    $state.schemaVersion = "standard-migration-autonomy/v3"
    $state.exhaustedCandidateFingerprints = @($state.exhaustedCandidateFingerprints)
    if ($null -eq $state.PSObject.Properties["lastRebaselineSha256"]) {
        $state | Add-Member -NotePropertyName lastRebaselineSha256 -NotePropertyValue $null
    }
    if ($null -eq $state.PSObject.Properties["rebaselineHistory"]) {
        $state | Add-Member -NotePropertyName rebaselineHistory -NotePropertyValue @()
    }
    $state.visitedStateHashes = @($state.visitedStateHashes)
    $state.completedCycles = @($state.completedCycles)
    $state.cycleHistory = @($state.cycleHistory)
    $state.rebaselineHistory = @($state.rebaselineHistory)
    return $state
}

function Write-State([string]$Path, $State) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $temp = "$Path.tmp"
    $State | ConvertTo-Json -Depth 32 | Set-Content -LiteralPath $temp -Encoding UTF8
    Move-Item -LiteralPath $temp -Destination $Path -Force
}

function Read-Guard([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "AUTONOMY_CYCLE_GUARD_REQUIRED" }
    $full = Resolve-FullPath $Path
    if (-not (Test-Path -LiteralPath $full)) { throw "AUTONOMY_CYCLE_GUARD_NOT_FOUND: $full" }
    try { $guard = Get-Content -LiteralPath $full -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_CYCLE_GUARD_INVALID_JSON: $($_.Exception.Message)" }

    if ([string]$guard.SchemaVersion -ne "migrator-remediation-cycle-guard/v1") {
        throw "AUTONOMY_CYCLE_GUARD_SCHEMA_INVALID: $($guard.SchemaVersion)"
    }
    if ([string]::IsNullOrWhiteSpace([string]$guard.GuardSha256) -or [string]::IsNullOrWhiteSpace([string]$guard.AcceptedStateHash)) {
        throw "AUTONOMY_CYCLE_GUARD_IDENTITY_MISSING"
    }
    if (@(
        "READY_INITIAL_BASELINE",
        "READY",
        "ROLLBACK_CONFIRMED",
        "ABORT_CONFIRMED",
        "BLOCKED_BASELINE_MISMATCH",
        "BLOCKED_WORKSPACE_MISMATCH",
        "BLOCKED_AUTONOMY_NOT_RUNNING"
    ) -notcontains [string]$guard.Decision) {
        throw "AUTONOMY_CYCLE_GUARD_DECISION_INVALID: $($guard.Decision)"
    }
    return $guard
}

function Assert-GuardReadyToStartCycle($guard) {
    if (-not [bool]$guard.ReadyToStartCycle) {
        throw "AUTONOMY_CYCLE_GUARD_BLOCKED: $($guard.Decision) $($guard.Reason)"
    }
    if (@("READY_INITIAL_BASELINE", "READY", "ROLLBACK_CONFIRMED") -notcontains [string]$guard.Decision) {
        throw "AUTONOMY_CYCLE_GUARD_START_DECISION_INVALID: $($guard.Decision)"
    }
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

function Read-Rebaseline([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { throw "AUTONOMY_REBASELINE_EVIDENCE_REQUIRED" }
    $full = Resolve-FullPath $Path
    if (-not (Test-Path -LiteralPath $full)) { throw "AUTONOMY_REBASELINE_EVIDENCE_NOT_FOUND: $full" }
    try { $evidence = Get-Content -LiteralPath $full -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_REBASELINE_EVIDENCE_INVALID_JSON: $($_.Exception.Message)" }

    if ([string]$evidence.SchemaVersion -ne "migrator-remediation-rebaseline/v1") {
        throw "AUTONOMY_REBASELINE_EVIDENCE_SCHEMA_INVALID: $($evidence.SchemaVersion)"
    }
    if ([string]$evidence.Decision -ne "REBASELINE_CONFIRMED") {
        throw "AUTONOMY_REBASELINE_NOT_CONFIRMED: $($evidence.Decision) $($evidence.Reason)"
    }
    if ([string]::IsNullOrWhiteSpace([string]$evidence.RebaselineSha256)) {
        throw "AUTONOMY_REBASELINE_EVIDENCE_IDENTITY_MISSING"
    }
    if ([string]::IsNullOrWhiteSpace([string]$evidence.Before.StateHash) -or [string]::IsNullOrWhiteSpace([string]$evidence.After.StateHash)) {
        throw "AUTONOMY_REBASELINE_STATE_HASH_MISSING"
    }
    if ([string]::IsNullOrWhiteSpace([string]$evidence.Before.ToolSha256) -or [string]::IsNullOrWhiteSpace([string]$evidence.After.ToolSha256)) {
        throw "AUTONOMY_REBASELINE_TOOL_IDENTITY_MISSING"
    }
    if ([string]$evidence.Before.ToolSha256 -eq [string]$evidence.After.ToolSha256) {
        throw "AUTONOMY_REBASELINE_TOOL_IDENTITY_UNCHANGED"
    }
    return $evidence
}

function Apply-GuardIdentity($state, $guard) {
    $state.lastGuardSha256 = [string]$guard.GuardSha256
    $state.lastGuardDecision = [string]$guard.Decision
    $state.lastWorkspaceIdentitySha256 = [string]$guard.WorkspaceIdentitySha256
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
        if ([bool]$state.cycleInProgress) {
            throw "AUTONOMY_ACTIVE_CYCLE_MUST_BE_RESOLVED: record or roll back the active cycle before starting a new invocation."
        }
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
        $state.lastCheckpointReason = $null
        $state.completedCycles = @()
        $state.cycleHistory = @()
        $state.stopReason = $null
        # currentStateHash, visitedStateHashes, and rollbackRequired intentionally survive
        # a fresh invocation. `continue` refreshes budget, never transaction correctness.
    }
    "StartCycle" {
        if ($state.status -ne "RUNNING") { throw "AUTONOMY_STATE_NOT_RUNNING: start a new invocation first." }
        if ([bool]$state.cycleInProgress) { throw "AUTONOMY_CYCLE_ALREADY_IN_PROGRESS" }
        if ([int]$state.cyclesCompleted -ge [int]$state.cycleBudget) { throw "AUTONOMY_CYCLE_BUDGET_ALREADY_REACHED" }

        $guard = Read-Guard $GuardPath
        Assert-GuardReadyToStartCycle $guard
        $acceptedHash = [string]$guard.AcceptedStateHash
        if (-not [string]::IsNullOrWhiteSpace([string]$state.currentStateHash) -and [string]$state.currentStateHash -ne $acceptedHash) {
            throw "AUTONOMY_CYCLE_GUARD_BASELINE_MISMATCH: expected $($state.currentStateHash), got $acceptedHash"
        }

        if ([bool]$state.rollbackRequired) {
            if ([string]$guard.Decision -ne "ROLLBACK_CONFIRMED" -or -not [bool]$guard.RollbackConfirmed) {
                throw "AUTONOMY_ROLLBACK_NOT_CONFIRMED: rejected workspace state must be restored before another cycle."
            }
        }
        elseif ([string]$guard.Decision -eq "ROLLBACK_CONFIRMED") {
            throw "AUTONOMY_UNEXPECTED_ROLLBACK_CONFIRMATION"
        }

        if ([string]::IsNullOrWhiteSpace([string]$state.currentStateHash)) {
            if ([string]$guard.Decision -ne "READY_INITIAL_BASELINE") {
                throw "AUTONOMY_INITIAL_BASELINE_GUARD_REQUIRED"
            }
            $state.currentStateHash = $acceptedHash
            $state.visitedStateHashes = @(@($state.visitedStateHashes) + @($acceptedHash) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
        }

        $state.rollbackRequired = $false
        $state.cycleInProgress = $true
        $state.activeCycleBaselineStateHash = [string]$state.currentStateHash
        Apply-GuardIdentity $state $guard
        Write-Host "AUTONOMY_CYCLE_STARTED"
    }
    "AbortCycle" {
        if (-not [bool]$state.cycleInProgress) { throw "AUTONOMY_CYCLE_NOT_IN_PROGRESS" }
        if ([bool]$state.rollbackRequired) { throw "AUTONOMY_ABORT_CYCLE_INVALID_WITH_PENDING_ROLLBACK" }

        $guard = Read-Guard $GuardPath
        if ([string]$guard.Decision -ne "ABORT_CONFIRMED") {
            throw "AUTONOMY_ABORT_NOT_CONFIRMED: Core guard must prove the active cycle workspace was restored to its accepted baseline; got $($guard.Decision) $($guard.Reason)"
        }
        if ([bool]$guard.ReadyToStartCycle) {
            throw "AUTONOMY_ABORT_GUARD_MUST_NOT_START_NEW_CYCLE"
        }

        $acceptedHash = [string]$guard.AcceptedStateHash
        if ([string]$state.activeCycleBaselineStateHash -ne $acceptedHash) {
            throw "AUTONOMY_ABORT_BASELINE_MISMATCH: expected $($state.activeCycleBaselineStateHash), got $acceptedHash"
        }
        if ([string]$state.currentStateHash -ne $acceptedHash) {
            throw "AUTONOMY_ABORT_CURRENT_STATE_MISMATCH: expected $($state.currentStateHash), got $acceptedHash"
        }

        $state.cycleInProgress = $false
        $state.activeCycleBaselineStateHash = $null
        Apply-GuardIdentity $state $guard
        Write-Host "AUTONOMY_CYCLE_ABORTED"
    }
    "Rebaseline" {
        if ([bool]$state.cycleInProgress) { throw "AUTONOMY_REBASELINE_REQUIRES_NO_ACTIVE_CYCLE" }
        if ([bool]$state.rollbackRequired) { throw "AUTONOMY_REBASELINE_REQUIRES_CLEAN_TRANSACTION_STATE" }
        if ($state.status -eq "RUNNING") { throw "AUTONOMY_REBASELINE_REQUIRES_INVOCATION_BOUNDARY" }
        if ($state.status -eq "COMPLETE") { throw "AUTONOMY_REBASELINE_FORBIDDEN_AFTER_COMPLETE" }

        $evidence = Read-Rebaseline $RebaselinePath
        $beforeHash = [string]$evidence.Before.StateHash
        $afterHash = [string]$evidence.After.StateHash
        if ([string]::IsNullOrWhiteSpace([string]$state.currentStateHash)) {
            throw "AUTONOMY_REBASELINE_CURRENT_STATE_MISSING"
        }
        if ([string]$state.currentStateHash -ne $beforeHash) {
            throw "AUTONOMY_REBASELINE_BASELINE_MISMATCH: expected $($state.currentStateHash), got $beforeHash"
        }
        if ($beforeHash -eq $afterHash) {
            throw "AUTONOMY_REBASELINE_STATE_UNCHANGED"
        }

        $record = [ordered]@{
            beforeStateHash = $beforeHash
            afterStateHash = $afterHash
            beforeToolSha256 = [string]$evidence.Before.ToolSha256
            afterToolSha256 = [string]$evidence.After.ToolSha256
            reason = [string]$evidence.Reason
            rebaselineSha256 = [string]$evidence.RebaselineSha256
            improvements = @($evidence.Improvements)
            completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
        $state.rebaselineHistory = @(@($state.rebaselineHistory) + @([pscustomobject]$record))
        $state.visitedStateHashes = @(@($state.visitedStateHashes) + @($beforeHash, $afterHash) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
        $state.currentStateHash = $afterHash
        $state.lastDecision = "REBASELINE_CONFIRMED"
        $state.lastBeforeStateHash = $beforeHash
        $state.lastAfterStateHash = $afterHash
        $state.lastRebaselineSha256 = [string]$evidence.RebaselineSha256
        Write-Host "AUTONOMY_REBASELINE_CONFIRMED"
    }
    "ConfirmRollback" {
        if ([bool]$state.cycleInProgress) { throw "AUTONOMY_CANNOT_CONFIRM_ROLLBACK_DURING_ACTIVE_CYCLE" }
        if (-not [bool]$state.rollbackRequired) { throw "AUTONOMY_ROLLBACK_NOT_REQUIRED" }

        $guard = Read-Guard $GuardPath
        if ([string]$guard.Decision -ne "ROLLBACK_CONFIRMED" -or -not [bool]$guard.RollbackConfirmed) {
            throw "AUTONOMY_ROLLBACK_NOT_CONFIRMED: Core guard did not verify the accepted workspace state."
        }
        if ([string]$guard.AcceptedStateHash -ne [string]$state.currentStateHash) {
            throw "AUTONOMY_ROLLBACK_BASELINE_MISMATCH: expected $($state.currentStateHash), got $($guard.AcceptedStateHash)"
        }

        $state.rollbackRequired = $false
        Apply-GuardIdentity $state $guard
        Write-Host "AUTONOMY_ROLLBACK_CONFIRMED"
    }
    "RecordCycle" {
        if ($state.status -ne "RUNNING") { throw "AUTONOMY_STATE_NOT_RUNNING: start a new invocation first." }
        if (-not [bool]$state.cycleInProgress) { throw "AUTONOMY_CYCLE_NOT_STARTED: run StartCycle with a fresh Core guard before editing." }
        if ([bool]$state.rollbackRequired) { throw "AUTONOMY_PENDING_ROLLBACK_BLOCKS_RECORD" }
        if ([int]$state.cyclesCompleted -ge [int]$state.cycleBudget) { throw "AUTONOMY_CYCLE_BUDGET_ALREADY_REACHED" }
        if (-not [string]::IsNullOrWhiteSpace($CandidateFingerprint) -or -not [string]::IsNullOrWhiteSpace($Result) -or -not [string]::IsNullOrWhiteSpace($MetricSummary)) {
            throw "AUTONOMY_AGENT_PROGRESS_CLASSIFICATION_FORBIDDEN: use -EvaluationPath from `selenium-pw-migrator remediation evaluate`."
        }

        $evaluation = Read-Evaluation $EvaluationPath
        $beforeHash = [string]$evaluation.Before.StateHash
        $afterHash = [string]$evaluation.After.StateHash
        $decision = [string]$evaluation.Decision
        $fingerprint = [string]$evaluation.CandidateFingerprint

        if ([string]$state.activeCycleBaselineStateHash -ne $beforeHash) {
            throw "AUTONOMY_ACTIVE_CYCLE_BASELINE_MISMATCH: expected $($state.activeCycleBaselineStateHash), got $beforeHash"
        }
        if ([string]$state.currentStateHash -ne $beforeHash) {
            throw "AUTONOMY_EVALUATION_BASELINE_MISMATCH: expected $($state.currentStateHash), got $beforeHash"
        }

        $visited = @($state.visitedStateHashes)
        if ($visited -contains $afterHash -and $decision -ne "REJECT_CYCLE") {
            throw "AUTONOMY_EVALUATION_MISSED_CYCLE: Core evaluation must classify revisited state as REJECT_CYCLE."
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
            startGuardSha256 = [string]$state.lastGuardSha256
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
        $state.cycleInProgress = $false
        $state.activeCycleBaselineStateHash = $null
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
        if ([bool]$state.cycleInProgress -and $Status -ne "RUNNING") {
            throw "AUTONOMY_TERMINAL_STOP_REQUIRES_RESOLVED_CYCLE: record the cycle, or restore the accepted baseline and use AbortCycle before STOPPED/BLOCKED/COMPLETE."
        }
        if ($Status -eq "COMPLETE" -and [bool]$state.rollbackRequired) {
            throw "AUTONOMY_COMPLETE_REQUIRES_CLEAN_TRANSACTION_STATE"
        }
        if ($Status -eq "COMPLETE") {
            if ([string]::IsNullOrWhiteSpace($FinalGatePath)) {
                throw "AUTONOMY_COMPLETE_REQUIRES_FINAL_GATE"
            }

            $resolvedFinalGatePath = Resolve-FullPath $FinalGatePath
            if (-not (Test-Path -LiteralPath $resolvedFinalGatePath -PathType Leaf)) {
                throw "AUTONOMY_COMPLETE_FINAL_GATE_NOT_FOUND: $resolvedFinalGatePath"
            }

            $finalGate = Get-Content -LiteralPath $resolvedFinalGatePath -Raw | ConvertFrom-Json
            if ([string]$finalGate.schemaVersion -ne "standard-run-final-gate/v2") {
                throw "AUTONOMY_COMPLETE_FINAL_GATE_SCHEMA_INVALID: $($finalGate.schemaVersion)"
            }
            if ([string]$finalGate.status -ne "PASS") {
                throw "AUTONOMY_COMPLETE_FINAL_GATE_NOT_PASS: $($finalGate.status)"
            }
        }
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
Write-Host "Cycle in progress: $($state.cycleInProgress)"
Write-Host "Active baseline: $($state.activeCycleBaselineStateHash)"
Write-Host "Last decision: $($state.lastDecision)"
Write-Host "Rollback required: $($state.rollbackRequired)"
Write-Host "No-progress streak: $($state.noProgressStreak)"
Write-Host "Checkpoint: $($state.lastCheckpointReason)"
Write-Host "Stop reason: $($state.stopReason)"
