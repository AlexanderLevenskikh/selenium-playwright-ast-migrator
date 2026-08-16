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
        lastFinalGateSha256 = $null
        lastFinalGateRunPath = $null
        lastFinalGateTargetSha256 = $null
        lastFinalGateVerificationEvidenceSha256 = $null
        lastBeforeStateHash = $null
        lastAfterStateHash = $null
        lastClosedResidualIds = @()
        lastOpenedResidualIds = @()
        currentStateHash = $null
        currentResidualIds = @()
        exhaustedResidualIds = @()
        rollbackRequired = $false
        cycleInProgress = $false
        activeCycleBaselineStateHash = $null
        activeCycleResidualIds = @()
        lastGuardSha256 = $null
        lastGuardDecision = $null
        lastWorkspaceIdentitySha256 = $null
        lastCheckpointReason = $null
        exhaustedCandidateFingerprints = @()
        visitedStateHashes = @()
        completedCycles = @()
        cycleHistory = @()
        rebaselineHistory = @()
        proofLedgerRequired = $false
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
    $state.activeCycleResidualIds = @($state.activeCycleResidualIds)
    $state.lastClosedResidualIds = @($state.lastClosedResidualIds)
    $state.lastOpenedResidualIds = @($state.lastOpenedResidualIds)
    $state.currentResidualIds = @($state.currentResidualIds)
    $state.exhaustedResidualIds = @($state.exhaustedResidualIds)
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

function Convert-StateToCanonicalJson($State) {
    return ($State | ConvertTo-Json -Depth 32 -Compress)
}

function Get-TextSha256([string]$Text) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $hash = [System.Security.Cryptography.SHA256]::HashData($bytes)
    return [Convert]::ToHexString($hash).ToLowerInvariant()
}


function Get-FileSha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return "" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Normalize-PathIdentity([string]$Path) {
    $normalized = [IO.Path]::GetFullPath($Path).Replace('\', '/').TrimEnd('/')
    if ([IO.Path]::DirectorySeparatorChar -eq '\') {
        $normalized = $normalized.ToLowerInvariant()
    }
    return $normalized
}

function Get-PathIdentitySha256([string]$Path) {
    return Get-TextSha256 (Normalize-PathIdentity $Path)
}

function Convert-ToRelativePath([string]$BasePath, [string]$Path) {
    $fullBase = [IO.Path]::GetFullPath($BasePath)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $separatorChars = [char[]]@(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    )
    $fullBase = $fullBase.TrimEnd($separatorChars)
    $separator = [string][IO.Path]::DirectorySeparatorChar
    $baseWithSeparator = $fullBase
    if (-not $baseWithSeparator.EndsWith($separator, [StringComparison]::Ordinal)) {
        $baseWithSeparator += $separator
    }
    $baseUri = New-Object System.Uri -ArgumentList $baseWithSeparator
    $pathUri = New-Object System.Uri -ArgumentList $fullPath
    $relativeUri = $baseUri.MakeRelativeUri($pathUri).ToString()
    return ([Uri]::UnescapeDataString($relativeUri) -replace '\\', '/')
}

function Get-GeneratedCsTreeSha256([string]$GeneratedRoot) {
    if (-not (Test-Path -LiteralPath $GeneratedRoot -PathType Container)) { return "" }

    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $GeneratedRoot -Recurse -File -Filter "*.cs")) {
        $relative = Convert-ToRelativePath $GeneratedRoot $file.FullName
        $hash = Get-FileSha256 $file.FullName
        $entries.Add("$relative`t$hash")
    }
    $entries.Sort([StringComparer]::Ordinal)
    return Get-TextSha256 ($entries -join "`n")
}

function Get-FinalGateProofSha256($Gate) {
    $material = @(
        "standard-run-final-gate/v3",
        [string]$Gate.status,
        [string]$Gate.workspacePathSha256,
        [string]$Gate.runPathSha256,
        [string]$Gate.autonomyStateFileSha256,
        [string]$Gate.autonomyLedgerSequence,
        [string]$Gate.autonomyLedgerEntrySha256,
        [string]$Gate.autonomyLedgerStateSha256,
        [string]$Gate.autonomyInvocationId,
        [string]$Gate.autonomyCurrentStateHash,
        [string]$Gate.sourceSha256,
        [string]$Gate.configSha256,
        [string]$Gate.targetSha256,
        [string]$Gate.toolSha256,
        [string]$Gate.environmentSha256,
        [string]$Gate.orchestrationReportFileSha256,
        [string]$Gate.generatedReportFileSha256,
        [string]$Gate.runManifestFileSha256,
        [string]$Gate.projectVerifyReportFileSha256,
        [string]$Gate.verificationEvidenceFileSha256,
        [string]$Gate.verificationEvidenceSha256,
        [string]$Gate.generatedCsTreeSha256
    ) -join "`n"
    return Get-TextSha256 $material
}

function Assert-FinalGateIdentity([string]$Code, $Actual, $Expected) {
    $actualText = [string]$Actual
    $expectedText = [string]$Expected
    if ([string]::IsNullOrWhiteSpace($actualText) -or
        [string]::IsNullOrWhiteSpace($expectedText) -or
        -not [string]::Equals($actualText, $expectedText, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Code`: expected '$expectedText', got '$actualText'"
    }
}

function Test-PathUnderRoot([string]$Root, [string]$Candidate) {
    $separatorChars = [char[]]@(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    )
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd($separatorChars) + [IO.Path]::DirectorySeparatorChar
    $candidateFull = [IO.Path]::GetFullPath($Candidate)
    return $candidateFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-FinalGateForCurrentState(
    [string]$FinalGatePath,
    [string]$WorkspaceFull,
    [string]$StatePath,
    $State,
    $Ledger
) {
    if ([string]::IsNullOrWhiteSpace($FinalGatePath)) {
        throw "AUTONOMY_COMPLETE_REQUIRES_FINAL_GATE"
    }

    $canonicalGatePath = [IO.Path]::GetFullPath((Join-Path $WorkspaceFull "state/final-gate-result.json"))
    $resolvedFinalGatePath = Resolve-FullPath $FinalGatePath
    if (-not [string]::Equals($resolvedFinalGatePath, $canonicalGatePath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "AUTONOMY_COMPLETE_FINAL_GATE_PATH_INVALID: expected $canonicalGatePath, got $resolvedFinalGatePath"
    }
    if (-not (Test-Path -LiteralPath $resolvedFinalGatePath -PathType Leaf)) {
        throw "AUTONOMY_COMPLETE_FINAL_GATE_NOT_FOUND: $resolvedFinalGatePath"
    }

    try { $gate = Get-Content -LiteralPath $resolvedFinalGatePath -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_COMPLETE_FINAL_GATE_INVALID_JSON: $($_.Exception.Message)" }

    if ([string]$gate.schemaVersion -ne "standard-run-final-gate/v3") {
        throw "AUTONOMY_COMPLETE_FINAL_GATE_SCHEMA_INVALID: $($gate.schemaVersion)"
    }
    if ([string]$gate.status -ne "PASS") {
        throw "AUTONOMY_COMPLETE_FINAL_GATE_NOT_PASS: $($gate.status)"
    }
    if (@($gate.failures).Count -ne 0) {
        throw "AUTONOMY_COMPLETE_FINAL_GATE_HAS_FAILURES"
    }

    $computedGateSha256 = Get-FinalGateProofSha256 $gate
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_FINAL_GATE_HASH_MISMATCH" $gate.finalGateSha256 $computedGateSha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_WORKSPACE_IDENTITY_MISMATCH" $gate.workspacePathSha256 (Get-PathIdentitySha256 $WorkspaceFull)

    if (-not (Test-Path -LiteralPath $StatePath -PathType Leaf)) {
        throw "AUTONOMY_COMPLETE_STATE_FILE_MISSING"
    }
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_STATE_STALE" $gate.autonomyStateFileSha256 (Get-FileSha256 $StatePath)

    if ($null -eq $Ledger) {
        throw "AUTONOMY_COMPLETE_LEDGER_MISSING"
    }
    if ([long]$gate.autonomyLedgerSequence -ne [long]$Ledger.Sequence) {
        throw "AUTONOMY_COMPLETE_LEDGER_SEQUENCE_STALE: expected $($Ledger.Sequence), got $($gate.autonomyLedgerSequence)"
    }
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_LEDGER_ENTRY_STALE" $gate.autonomyLedgerEntrySha256 $Ledger.EntrySha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_LEDGER_STATE_STALE" $gate.autonomyLedgerStateSha256 $Ledger.StateSha256

    if ([string]$gate.autonomyInvocationId -ne [string]$State.invocationId) {
        throw "AUTONOMY_COMPLETE_INVOCATION_MISMATCH: expected $($State.invocationId), got $($gate.autonomyInvocationId)"
    }
    if ([string]$gate.autonomyCurrentStateHash -ne [string]$State.currentStateHash) {
        throw "AUTONOMY_COMPLETE_CURRENT_STATE_MISMATCH: expected $($State.currentStateHash), got $($gate.autonomyCurrentStateHash)"
    }

    $runPath = [string]$gate.runPath
    if ([string]::IsNullOrWhiteSpace($runPath) -or -not (Test-Path -LiteralPath $runPath -PathType Container)) {
        throw "AUTONOMY_COMPLETE_RUN_NOT_FOUND: $runPath"
    }
    if (-not (Test-PathUnderRoot (Join-Path $WorkspaceFull "runs") $runPath)) {
        throw "AUTONOMY_COMPLETE_RUN_OUTSIDE_WORKSPACE: $runPath"
    }
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_RUN_PATH_MISMATCH" $gate.runPathSha256 (Get-PathIdentitySha256 $runPath)

    $reportPath = Join-Path $runPath "orchestration-report.json"
    $generatedReportPath = Join-Path $runPath "generated/report.json"
    $manifestPath = Join-Path $runPath "run-manifest.json"
    $generatedRoot = Join-Path $runPath "generated"
    $generatedHashPath = Join-Path $generatedRoot "target-tree.sha256"
    $verificationReportPath = Join-Path $runPath "verify-project/project-verify-report.json"
    $verificationEvidencePath = Join-Path $runPath "verify-project/verification-evidence.json"

    foreach ($pair in @(
        @("AUTONOMY_COMPLETE_ORCHESTRATION_REPORT_STALE", $gate.orchestrationReportFileSha256, $reportPath),
        @("AUTONOMY_COMPLETE_GENERATED_REPORT_STALE", $gate.generatedReportFileSha256, $generatedReportPath),
        @("AUTONOMY_COMPLETE_RUN_MANIFEST_STALE", $gate.runManifestFileSha256, $manifestPath),
        @("AUTONOMY_COMPLETE_PROJECT_VERIFY_REPORT_STALE", $gate.projectVerifyReportFileSha256, $verificationReportPath),
        @("AUTONOMY_COMPLETE_VERIFICATION_EVIDENCE_STALE", $gate.verificationEvidenceFileSha256, $verificationEvidencePath)
    )) {
        $code = [string]$pair[0]
        $expected = [string]$pair[1]
        $path = [string]$pair[2]
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "$code`: evidence file is missing: $path"
        }
        Assert-FinalGateIdentity $code $expected (Get-FileSha256 $path)
    }

    try { $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_COMPLETE_ORCHESTRATION_REPORT_INVALID_JSON: $($_.Exception.Message)" }
    if ([string]$report.Status -notmatch '^(passed|PASS)$') {
        throw "AUTONOMY_COMPLETE_ORCHESTRATION_NOT_PASS: $($report.Status)"
    }

    try { $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_COMPLETE_RUN_MANIFEST_INVALID_JSON: $($_.Exception.Message)" }
    if ([string]$manifest.SchemaVersion -ne "migrator-run-manifest/v2") {
        throw "AUTONOMY_COMPLETE_RUN_MANIFEST_SCHEMA_INVALID: $($manifest.SchemaVersion)"
    }
    if ([string]$manifest.Status -notmatch '^(passed|PASS)$') {
        throw "AUTONOMY_COMPLETE_RUN_MANIFEST_NOT_PASS: $($manifest.Status)"
    }

    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_SOURCE_IDENTITY_MISMATCH" $gate.sourceSha256 $manifest.SourceSha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_CONFIG_IDENTITY_MISMATCH" $gate.configSha256 $manifest.ConfigSha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_TARGET_IDENTITY_MISMATCH" $gate.targetSha256 $manifest.TargetSha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_TOOL_IDENTITY_MISMATCH" $gate.toolSha256 $manifest.Tool.IdentitySha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_ENVIRONMENT_IDENTITY_MISMATCH" $gate.environmentSha256 $manifest.Environment.IdentitySha256

    if ($null -eq $manifest.Verification) {
        throw "AUTONOMY_COMPLETE_INTERNAL_VERIFICATION_MISSING"
    }
    if ([string]$manifest.Verification.Kind -ne "generated-verify" -or
        [string]$manifest.Verification.Status -notmatch '^(passed|PASS)$' -or
        [int]$manifest.Verification.ExitCode -ne 0) {
        throw "AUTONOMY_COMPLETE_INTERNAL_VERIFICATION_NOT_PASS"
    }
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_INTERNAL_SOURCE_MISMATCH" $manifest.Verification.SourceSha256 $manifest.SourceSha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_INTERNAL_CONFIG_MISMATCH" $manifest.Verification.ConfigSha256 $manifest.ConfigSha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_INTERNAL_TARGET_MISMATCH" $manifest.Verification.TargetSha256 $manifest.TargetSha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_INTERNAL_TOOL_MISMATCH" $manifest.Verification.ToolSha256 $manifest.Tool.IdentitySha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_INTERNAL_ENVIRONMENT_MISMATCH" $manifest.Verification.EnvironmentSha256 $manifest.Environment.IdentitySha256

    try { $projectVerify = Get-Content -LiteralPath $verificationReportPath -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_COMPLETE_PROJECT_VERIFY_REPORT_INVALID_JSON: $($_.Exception.Message)" }
    if ([string]$projectVerify.Status -notmatch '^(passed|PASS)$' -or [int]$projectVerify.ExitCode -ne 0) {
        throw "AUTONOMY_COMPLETE_PROJECT_VERIFY_NOT_PASS: $($projectVerify.Status) exit=$($projectVerify.ExitCode)"
    }

    try { $projectEvidence = Get-Content -LiteralPath $verificationEvidencePath -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_COMPLETE_VERIFICATION_EVIDENCE_INVALID_JSON: $($_.Exception.Message)" }
    if ([string]$projectEvidence.SchemaVersion -ne "migrator-verification-evidence/v1") {
        throw "AUTONOMY_COMPLETE_VERIFICATION_EVIDENCE_SCHEMA_INVALID: $($projectEvidence.SchemaVersion)"
    }
    if ([string]$projectEvidence.Kind -ne "dotnet-build-exact-target" -or
        [string]$projectEvidence.Status -notmatch '^(passed|PASS)$' -or
        [int]$projectEvidence.ExitCode -ne 0) {
        throw "AUTONOMY_COMPLETE_VERIFICATION_EVIDENCE_NOT_PASS"
    }
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_PROJECT_SOURCE_MISMATCH" $projectEvidence.SourceSha256 $manifest.SourceSha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_PROJECT_CONFIG_MISMATCH" $projectEvidence.ConfigSha256 $manifest.ConfigSha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_PROJECT_TARGET_MISMATCH" $projectEvidence.TargetSha256 $manifest.TargetSha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_PROJECT_TOOL_MISMATCH" $projectEvidence.ToolSha256 $manifest.Tool.IdentitySha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_PROJECT_ENVIRONMENT_MISMATCH" $projectEvidence.EnvironmentSha256 $manifest.Environment.IdentitySha256
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_VERIFICATION_EVIDENCE_IDENTITY_MISMATCH" $gate.verificationEvidenceSha256 $projectEvidence.EvidenceSha256

    if (-not (Test-Path -LiteralPath $generatedHashPath -PathType Leaf)) {
        throw "AUTONOMY_COMPLETE_TARGET_TREE_MARKER_MISSING"
    }
    $targetMarker = (Get-Content -LiteralPath $generatedHashPath -Raw).Trim()
    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_TARGET_TREE_MARKER_MISMATCH" $targetMarker $manifest.TargetSha256

    $generatedRootFull = [IO.Path]::GetFullPath($generatedRoot).TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)) + [IO.Path]::DirectorySeparatorChar
    foreach ($targetFile in @($manifest.TargetFiles)) {
        $relative = ([string]$targetFile.RelativePath).Replace('\', '/').TrimStart('/')
        if ([string]::IsNullOrWhiteSpace($relative)) {
            throw "AUTONOMY_COMPLETE_TARGET_FILE_IDENTITY_INVALID"
        }
        $candidate = [IO.Path]::GetFullPath((Join-Path $generatedRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
        if (-not $candidate.StartsWith($generatedRootFull, [StringComparison]::OrdinalIgnoreCase)) {
            throw "AUTONOMY_COMPLETE_TARGET_FILE_ESCAPES_ROOT: $relative"
        }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "AUTONOMY_COMPLETE_TARGET_FILE_MISSING: $relative"
        }
        Assert-FinalGateIdentity "AUTONOMY_COMPLETE_TARGET_FILE_HASH_MISMATCH" (Get-FileSha256 $candidate) $targetFile.ContentSha256
    }

    Assert-FinalGateIdentity "AUTONOMY_COMPLETE_GENERATED_TREE_STALE" $gate.generatedCsTreeSha256 (Get-GeneratedCsTreeSha256 $generatedRoot)
    return $gate
}

function Write-Utf8TextAtomic([string]$Path, [string]$Text) {
    $directory = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $temp = "$Path.tmp"
    [System.IO.File]::WriteAllText($temp, $Text, [System.Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temp -Destination $Path -Force
}

function Get-LedgerEntrySha256(
    [long]$Sequence,
    [string]$PreviousEntrySha256,
    [string]$StateSha256,
    [string]$StateJsonBase64
) {
    $material = @(
        "standard-migration-autonomy-ledger-entry/v1",
        $Sequence.ToString([System.Globalization.CultureInfo]::InvariantCulture),
        $PreviousEntrySha256,
        $StateSha256,
        $StateJsonBase64
    ) -join "`n"
    return Get-TextSha256 $material
}

function Get-LedgerEntryPath([string]$EntriesPath, [long]$Sequence, [string]$EntrySha256) {
    return Join-Path $EntriesPath ("{0:D12}-{1}.json" -f $Sequence, $EntrySha256)
}

function Read-AutonomyLedger([string]$EntriesPath, [string]$AnchorPath) {
    $entryFiles = if (Test-Path -LiteralPath $EntriesPath -PathType Container) {
        @(Get-ChildItem -LiteralPath $EntriesPath -Filter "*.json" -File)
    } else { @() }

    if (-not (Test-Path -LiteralPath $AnchorPath -PathType Leaf)) {
        if ($entryFiles.Count -gt 0) {
            throw "AUTONOMY_LEDGER_ANCHOR_MISSING: immutable ledger entries exist but anchor.json is missing."
        }
        return $null
    }

    try { $anchor = Get-Content -LiteralPath $AnchorPath -Raw | ConvertFrom-Json }
    catch { throw "AUTONOMY_LEDGER_ANCHOR_INVALID_JSON: $($_.Exception.Message)" }

    if ([string]$anchor.schemaVersion -ne "standard-migration-autonomy-ledger-anchor/v1") {
        throw "AUTONOMY_LEDGER_ANCHOR_SCHEMA_INVALID: $($anchor.schemaVersion)"
    }

    $headSequence = [long]$anchor.sequence
    $headEntrySha256 = [string]$anchor.entrySha256
    $anchorStateSha256 = [string]$anchor.stateSha256
    if ($headSequence -lt 1 -or
        [string]::IsNullOrWhiteSpace($headEntrySha256) -or
        [string]::IsNullOrWhiteSpace($anchorStateSha256)) {
        throw "AUTONOMY_LEDGER_ANCHOR_IDENTITY_MISSING"
    }

    $expectedSequence = $headSequence
    $expectedEntrySha256 = $headEntrySha256
    $headStateJson = $null
    $headStateSha256 = $null
    $stateHashes = New-Object System.Collections.Generic.List[string]

    while ($expectedSequence -ge 1) {
        $entryPath = Get-LedgerEntryPath $EntriesPath $expectedSequence $expectedEntrySha256
        if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf)) {
            throw "AUTONOMY_LEDGER_ENTRY_NOT_FOUND: $entryPath"
        }

        try { $entry = Get-Content -LiteralPath $entryPath -Raw | ConvertFrom-Json }
        catch { throw "AUTONOMY_LEDGER_ENTRY_INVALID_JSON: $entryPath $($_.Exception.Message)" }

        if ([string]$entry.schemaVersion -ne "standard-migration-autonomy-ledger-entry/v1") {
            throw "AUTONOMY_LEDGER_ENTRY_SCHEMA_INVALID: $entryPath $($entry.schemaVersion)"
        }
        if ([long]$entry.sequence -ne $expectedSequence) {
            throw "AUTONOMY_LEDGER_SEQUENCE_INVALID: expected $expectedSequence, got $($entry.sequence)"
        }

        $entrySha256 = [string]$entry.entrySha256
        $stateSha256 = [string]$entry.stateSha256
        $stateJsonBase64 = [string]$entry.stateJsonBase64
        $previousEntrySha256 = [string]$entry.previousEntrySha256
        if ([string]::IsNullOrWhiteSpace($entrySha256) -or
            [string]::IsNullOrWhiteSpace($stateSha256) -or
            [string]::IsNullOrWhiteSpace($stateJsonBase64)) {
            throw "AUTONOMY_LEDGER_ENTRY_IDENTITY_MISSING: $entryPath"
        }

        $computedEntrySha256 = Get-LedgerEntrySha256 `
            $expectedSequence `
            $previousEntrySha256 `
            $stateSha256 `
            $stateJsonBase64
        if ($computedEntrySha256 -ne $entrySha256 -or $entrySha256 -ne $expectedEntrySha256) {
            throw "AUTONOMY_LEDGER_ENTRY_HASH_MISMATCH: $entryPath"
        }

        try {
            $stateJson = [System.Text.Encoding]::UTF8.GetString(
                [Convert]::FromBase64String($stateJsonBase64))
        }
        catch {
            throw "AUTONOMY_LEDGER_STATE_BASE64_INVALID: $entryPath"
        }

        $computedStateSha256 = Get-TextSha256 $stateJson
        if ($computedStateSha256 -ne $stateSha256) {
            throw "AUTONOMY_LEDGER_STATE_HASH_MISMATCH: $entryPath"
        }

        try { $null = $stateJson | ConvertFrom-Json }
        catch { throw "AUTONOMY_LEDGER_STATE_INVALID_JSON: $entryPath $($_.Exception.Message)" }

        $stateHashes.Add($stateSha256)
        if ($expectedSequence -eq $headSequence) {
            $headStateJson = $stateJson
            $headStateSha256 = $stateSha256
        }

        if ($expectedSequence -eq 1) {
            if (-not [string]::IsNullOrWhiteSpace($previousEntrySha256)) {
                throw "AUTONOMY_LEDGER_GENESIS_HAS_PREDECESSOR"
            }
        }
        elseif ([string]::IsNullOrWhiteSpace($previousEntrySha256)) {
            throw "AUTONOMY_LEDGER_CHAIN_BROKEN: sequence $expectedSequence has no predecessor."
        }

        $expectedEntrySha256 = $previousEntrySha256
        $expectedSequence--
    }

    if ($headStateSha256 -ne $anchorStateSha256) {
        throw "AUTONOMY_LEDGER_ANCHOR_STATE_MISMATCH"
    }

    return [pscustomobject]@{
        Sequence = $headSequence
        EntrySha256 = $headEntrySha256
        StateSha256 = $headStateSha256
        StateJson = $headStateJson
        StateHashes = @($stateHashes)
    }
}

function Write-AutonomyLedgerSnapshot([string]$EntriesPath, [string]$AnchorPath, $State) {
    New-Item -ItemType Directory -Force -Path $EntriesPath | Out-Null

    $previousSequence = 0L
    $previousEntrySha256 = ""
    if (Test-Path -LiteralPath $AnchorPath -PathType Leaf) {
        try { $previousAnchor = Get-Content -LiteralPath $AnchorPath -Raw | ConvertFrom-Json }
        catch { throw "AUTONOMY_LEDGER_ANCHOR_INVALID_JSON: $($_.Exception.Message)" }

        if ([string]$previousAnchor.schemaVersion -ne "standard-migration-autonomy-ledger-anchor/v1") {
            throw "AUTONOMY_LEDGER_ANCHOR_SCHEMA_INVALID: $($previousAnchor.schemaVersion)"
        }
        $previousSequence = [long]$previousAnchor.sequence
        $previousEntrySha256 = [string]$previousAnchor.entrySha256
    }

    $sequence = $previousSequence + 1
    $stateJson = Convert-StateToCanonicalJson $State
    $stateSha256 = Get-TextSha256 $stateJson
    $stateJsonBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($stateJson))
    $entrySha256 = Get-LedgerEntrySha256 $sequence $previousEntrySha256 $stateSha256 $stateJsonBase64

    $entry = [ordered]@{
        schemaVersion = "standard-migration-autonomy-ledger-entry/v1"
        sequence = $sequence
        previousEntrySha256 = if ([string]::IsNullOrWhiteSpace($previousEntrySha256)) { $null } else { $previousEntrySha256 }
        stateSha256 = $stateSha256
        stateJsonBase64 = $stateJsonBase64
        entrySha256 = $entrySha256
        recordedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }

    $entryPath = Get-LedgerEntryPath $EntriesPath $sequence $entrySha256
    if (Test-Path -LiteralPath $entryPath) {
        throw "AUTONOMY_LEDGER_ENTRY_ALREADY_EXISTS: $entryPath"
    }
    Write-Utf8TextAtomic $entryPath ($entry | ConvertTo-Json -Depth 8)

    $anchor = [ordered]@{
        schemaVersion = "standard-migration-autonomy-ledger-anchor/v1"
        sequence = $sequence
        entrySha256 = $entrySha256
        stateSha256 = $stateSha256
        updatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    }
    Write-Utf8TextAtomic $AnchorPath ($anchor | ConvertTo-Json -Depth 8)

    return [pscustomobject]@{
        Sequence = $sequence
        EntrySha256 = $entrySha256
        StateSha256 = $stateSha256
    }
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
        "BLOCKED_AUTONOMY_NOT_RUNNING",
        "BLOCKED_CANDIDATE_REQUIRED",
        "BLOCKED_CANDIDATE_INVALID",
        "BLOCKED_CANDIDATE_EXHAUSTED"
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
$ledgerRoot = Join-Path $workspaceFull "evidence/autonomy-ledger"
$ledgerEntriesPath = Join-Path $ledgerRoot "entries"
$ledgerAnchorPath = Join-Path $ledgerRoot "anchor.json"

$loaded = $null
$stateReadError = $null
$stateFilePresent = Test-Path -LiteralPath $statePath -PathType Leaf
if ($stateFilePresent) {
    try { $loaded = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json }
    catch { $stateReadError = $_.Exception.Message }
}

$ledger = Read-AutonomyLedger $ledgerEntriesPath $ledgerAnchorPath
if ($null -eq $ledger) {
    if ($null -ne $stateReadError) {
        throw "AUTONOMY_STATE_INVALID_JSON: $stateReadError"
    }

    $state = Convert-ToMutableState $loaded
    if ($stateFilePresent) {
        if ([bool]$state.proofLedgerRequired) {
            throw "AUTONOMY_LEDGER_REQUIRED_BUT_MISSING: protected workspace lost its autonomy proof ledger."
        }
        $state.proofLedgerRequired = $true
        $bootstrap = Write-AutonomyLedgerSnapshot $ledgerEntriesPath $ledgerAnchorPath $state
        Write-Host "AUTONOMY_LEDGER_BOOTSTRAPPED: sequence=$($bootstrap.Sequence) entry=$($bootstrap.EntrySha256)"
    }
}
else {
    try {
        $ledgerStateObject = $ledger.StateJson | ConvertFrom-Json
        $ledgerState = Convert-ToMutableState $ledgerStateObject
    }
    catch {
        throw "AUTONOMY_LEDGER_HEAD_STATE_INVALID: $($_.Exception.Message)"
    }

    $recoverMutableState = $false
    if (-not $stateFilePresent -or $null -ne $stateReadError) {
        $recoverMutableState = $true
    }
    else {
        $mutableLoaded = Convert-ToMutableState $loaded
        $mutableStateSha256 = Get-TextSha256 (Convert-StateToCanonicalJson $mutableLoaded)
        if ($mutableStateSha256 -eq [string]$ledger.StateSha256) {
            $state = $mutableLoaded
        }
        elseif (@($ledger.StateHashes) -contains $mutableStateSha256) {
            $recoverMutableState = $true
        }
        else {
            throw "AUTONOMY_STATE_LEDGER_MISMATCH: mutable autonomy-state.json is not an anchored ledger state."
        }
    }

    if ($recoverMutableState) {
        $state = $ledgerState
        Write-State $statePath $state
        Write-Host "AUTONOMY_STATE_RECOVERED_FROM_LEDGER: sequence=$($ledger.Sequence) entry=$($ledger.EntrySha256)"
    }
}

switch ($Action) {
    "StartInvocation" {
        if ([string]$state.status -eq "COMPLETE") {
            throw "AUTONOMY_COMPLETE_IS_TERMINAL: completed migration cannot start a new invocation."
        }
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
        $state.stopReason = $null
        # currentStateHash, visitedStateHashes, rollbackRequired, totalCyclesCompleted,
        # cycleHistory, and rebaselineHistory intentionally survive invocation boundaries.
        # A new invocation refreshes execution budget; it never erases accumulated proof.
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

        $guardResidualIds = @(
            $guard.CandidateResidualIds |
                Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
                ForEach-Object { [string]$_ } |
                Select-Object -Unique
        )
        if (@($state.currentResidualIds).Count -gt 0 -and $guardResidualIds.Count -eq 0) {
            throw "AUTONOMY_CYCLE_RESIDUAL_BINDING_REQUIRED"
        }
        foreach ($residualId in $guardResidualIds) {
            if (@($state.exhaustedResidualIds) -contains $residualId) {
                throw "AUTONOMY_CYCLE_RESIDUAL_ALREADY_EXHAUSTED: $residualId"
            }
        }

        $state.rollbackRequired = $false
        $state.cycleInProgress = $true
        $state.activeCycleBaselineStateHash = [string]$state.currentStateHash
        $state.activeCycleResidualIds = $guardResidualIds
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
        $state.activeCycleResidualIds = @()
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

        # Block 10 changes StateHash by adding residual inventory. A workspace created by
        # the immediately previous tool version still stores the legacy formula. That old
        # identity is accepted only here, at the explicit verified tool-upgrade boundary.
        $legacyBeforeHash = [string]$evidence.Before.LegacyStateHash
        $matchesAuthoritativeBefore = [string]$state.currentStateHash -eq $beforeHash
        $matchesLegacyBefore = -not [string]::IsNullOrWhiteSpace($legacyBeforeHash) -and
            [string]$state.currentStateHash -eq $legacyBeforeHash
        if (-not $matchesAuthoritativeBefore -and -not $matchesLegacyBefore) {
            throw "AUTONOMY_REBASELINE_BASELINE_MISMATCH: expected $($state.currentStateHash), got authoritative=$beforeHash legacy=$legacyBeforeHash"
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
            legacyBeforeStateHash = $legacyBeforeHash
            usedLegacyStateBridge = [bool]$matchesLegacyBefore
            improvements = @($evidence.Improvements)
            completedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        }
        $state.rebaselineHistory = @(@($state.rebaselineHistory) + @([pscustomobject]$record))
        $state.visitedStateHashes = @(
            @($state.visitedStateHashes) + @($beforeHash, $afterHash) |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Select-Object -Unique
        )
        $state.currentStateHash = $afterHash
        $state.currentResidualIds = @(
            $evidence.After.Residuals |
                Where-Object { [bool]$_.Actionable -and [bool]$_.ProgressBearing } |
                ForEach-Object { [string]$_.ResidualId } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Select-Object -Unique
        )
        # Residual IDs are tool-derived. A successful tool rebaseline invalidates exhaustion
        # recorded under the previous tool identity.
        $state.exhaustedResidualIds = @()
        $state.activeCycleResidualIds = @()
        $state.lastDecision = "REBASELINE_CONFIRMED"
        $state.lastBeforeStateHash = $beforeHash
        $state.lastAfterStateHash = $afterHash
        $state.lastRebaselineSha256 = [string]$evidence.RebaselineSha256
        Write-Host "AUTONOMY_REBASELINE_CONFIRMED"
    }    "ConfirmRollback" {
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
        $candidateResidualIds = @($evaluation.CandidateResidualIds | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Unique)
        $closedResidualIds = @($evaluation.ClosedResidualIds | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Unique)
        $openedResidualIds = @($evaluation.OpenedResidualIds | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Select-Object -Unique)
        $activeCycleResidualIds = @(
            $state.activeCycleResidualIds |
                Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
                ForEach-Object { [string]$_ } |
                Sort-Object -Unique
        )
        $evaluationResidualIds = @(
            $candidateResidualIds |
                ForEach-Object { [string]$_ } |
                Sort-Object -Unique
        )
        if (($activeCycleResidualIds -join "`n") -ne ($evaluationResidualIds -join "`n")) {
            throw "AUTONOMY_EVALUATION_CANDIDATE_BINDING_MISMATCH"
        }

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
            candidateResidualIds = $candidateResidualIds
            result = $result
            decision = $decision
            reason = [string]$evaluation.Reason
            evaluationSha256 = [string]$evaluation.EvaluationSha256
            startGuardSha256 = [string]$state.lastGuardSha256
            beforeStateHash = $beforeHash
            afterStateHash = $afterHash
            beforeDefects = $evaluation.Before.Defects
            afterDefects = $evaluation.After.Defects
            closedResidualIds = $closedResidualIds
            openedResidualIds = $openedResidualIds
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
        $state.lastClosedResidualIds = $closedResidualIds
        $state.lastOpenedResidualIds = $openedResidualIds
        $state.rollbackRequired = $rollbackRequired
        $state.cycleInProgress = $false
        $state.activeCycleBaselineStateHash = $null
        $state.activeCycleResidualIds = @()
        $state.lastCheckpointReason = $null

        $beforeResidualIds = @(
            $evaluation.Before.Residuals |
                Where-Object { [bool]$_.Actionable -and [bool]$_.ProgressBearing } |
                ForEach-Object { [string]$_.ResidualId } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Select-Object -Unique
        )
        $afterResidualIds = @(
            $evaluation.After.Residuals |
                Where-Object { [bool]$_.Actionable -and [bool]$_.ProgressBearing } |
                ForEach-Object { [string]$_.ResidualId } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                Select-Object -Unique
        )

        if ($decision -eq "ACCEPT") {
            $state.noProgressStreak = 0
            $state.currentStateHash = $afterHash
            $state.currentResidualIds = $afterResidualIds
            $state.exhaustedResidualIds = @(
                $state.exhaustedResidualIds |
                    Where-Object { $afterResidualIds -contains [string]$_ } |
                    Select-Object -Unique
            )
        }
        else {
            $state.currentStateHash = $beforeHash
            $state.currentResidualIds = $beforeResidualIds

            if ($decision -eq "REJECT_NO_PROGRESS") {
                $state.noProgressStreak = [int]$state.noProgressStreak + 1

                # Only a canonical Core residual binding can be exhausted. A human-readable
                # label/fingerprint is kept for history but cannot prove candidate-space exhaustion.
                if ($candidateResidualIds.Count -gt 0) {
                    $state.exhaustedResidualIds = @(
                        @($state.exhaustedResidualIds) + $candidateResidualIds |
                            Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
                            Select-Object -Unique
                    )
                    $state.exhaustedCandidateFingerprints = @(
                        @($state.exhaustedCandidateFingerprints) + @($fingerprint) |
                            Select-Object -Unique
                    )
                }
            }
            else {
                # A regression proves that this implementation attempt is bad, not that the
                # underlying residual candidate is impossible. Roll back and permit a different
                # bounded implementation of the same residual.
                $state.noProgressStreak = 0
            }
        }

        if ($decision -eq "REJECT_CYCLE") {
            $state.status = "STOPPED"
            $state.stopReason = "REMEDIATION_CYCLE_DETECTED"
        }
        elseif ($decision -eq "REJECT_NO_PROGRESS" -and $candidateResidualIds.Count -gt 0 -and $state.currentResidualIds.Count -gt 0) {
            $remainingResidualIds = @(
                $state.currentResidualIds |
                    Where-Object { @($state.exhaustedResidualIds) -notcontains [string]$_ }
            )
            if ($remainingResidualIds.Count -eq 0) {
                $state.status = "STOPPED"
                $state.stopReason = "REMEDIATION_RESIDUAL_CANDIDATES_EXHAUSTED"
            }
            else {
                $state.status = "RUNNING"
                $state.stopReason = $null
            }
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
    }    "Stop" {
        if ([string]::IsNullOrWhiteSpace($StopReason)) { throw "AUTONOMY_STOP_REASON_REQUIRED" }
        if ($Status -eq "COMPLETE" -and $StopReason -ne "SUCCESS") { throw "AUTONOMY_COMPLETE_REQUIRES_SUCCESS" }
        if ([bool]$state.cycleInProgress -and $Status -ne "RUNNING") {
            throw "AUTONOMY_TERMINAL_STOP_REQUIRES_RESOLVED_CYCLE: record the cycle, or restore the accepted baseline and use AbortCycle before STOPPED/BLOCKED/COMPLETE."
        }
        if ($Status -eq "COMPLETE" -and [bool]$state.rollbackRequired) {
            throw "AUTONOMY_COMPLETE_REQUIRES_CLEAN_TRANSACTION_STATE"
        }
        if ($Status -eq "COMPLETE") {
            $finalGate = Assert-FinalGateForCurrentState `
                -FinalGatePath $FinalGatePath `
                -WorkspaceFull $workspaceFull `
                -StatePath $statePath `
                -State $state `
                -Ledger $ledger

            # Carry the exact completion proof into the next protected ledger snapshot.
            $state.lastFinalGateSha256 = [string]$finalGate.finalGateSha256
            $state.lastFinalGateRunPath = [string]$finalGate.runPath
            $state.lastFinalGateTargetSha256 = [string]$finalGate.targetSha256
            $state.lastFinalGateVerificationEvidenceSha256 = [string]$finalGate.verificationEvidenceSha256
        }
        if ($state.mode -eq "continuous" -and $StopReason -eq "AUTONOMOUS_CYCLE_BUDGET_REACHED") {
            throw "AUTONOMY_CONTINUOUS_BUDGET_IS_CHECKPOINT_NOT_STOP"
        }
        $state.status = $Status
        $state.stopReason = $StopReason
    }
}

$state.proofLedgerRequired = $true
$ledgerCommit = Write-AutonomyLedgerSnapshot $ledgerEntriesPath $ledgerAnchorPath $state
Write-State $statePath $state
Write-Host "AUTONOMY_STATE_UPDATED"
Write-Host "Ledger sequence: $($ledgerCommit.Sequence)"
Write-Host "Ledger entry: $($ledgerCommit.EntrySha256)"
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
Write-Host "Current residual candidates: $(@($state.currentResidualIds).Count)"
Write-Host "Exhausted residual candidates: $(@($state.exhaustedResidualIds).Count)"
Write-Host "Checkpoint: $($state.lastCheckpointReason)"
Write-Host "Stop reason: $($state.stopReason)"
