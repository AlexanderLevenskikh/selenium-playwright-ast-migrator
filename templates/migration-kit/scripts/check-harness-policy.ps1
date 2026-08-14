[CmdletBinding()]
param(
    [string]$Workspace = "migration",
    [string]$RepoRoot = ".",
    [string[]]$AllowedRoots = @(),
    [switch]$SkipGitStatus
)
$ErrorActionPreference = "Stop"
$workspaceFull = [IO.Path]::GetFullPath($Workspace).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$repoFull = [IO.Path]::GetFullPath($RepoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$repoPrefix = $repoFull + [IO.Path]::DirectorySeparatorChar
if (-not ($workspaceFull.Equals($repoFull, [StringComparison]::OrdinalIgnoreCase) -or
          $workspaceFull.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase))) {
    throw "WORKSPACE_SCOPE_VIOLATION: migration workspace must be inside the repository root"
}

$scopePath = Join-Path $workspaceFull "state/source-scope.json"
if (-not (Test-Path -LiteralPath $scopePath)) { throw "SOURCE_SCOPE_MISSING: $scopePath" }

$policyPath = Join-Path $workspaceFull "state/harness-policy.json"
if (-not (Test-Path -LiteralPath $policyPath)) { throw "HARNESS_POLICY_MISSING: $policyPath" }
try { $policy = Get-Content -LiteralPath $policyPath -Raw | ConvertFrom-Json }
catch { throw "HARNESS_POLICY_INVALID_JSON: $($_.Exception.Message)" }

if ($policy.schemaVersion -ne "standard-migration-policy/v2") { throw "HARNESS_POLICY_SCHEMA_INVALID: $($policy.schemaVersion)" }
if ([int]$policy.maxRemediationCyclesPerInvocation -ne 5) { throw "HARNESS_POLICY_CYCLE_BUDGET_INVALID" }
if ([int]$policy.maxRepairPassesPerRun -ne 5) { throw "HARNESS_POLICY_LEGACY_CYCLE_BUDGET_ALIAS_INVALID" }
if ([int]$policy.maxChangesPerCycle -ne 1) { throw "HARNESS_POLICY_CHANGE_LIMIT_INVALID" }
if ([int]$policy.noProgressStopThreshold -ne 2) { throw "HARNESS_POLICY_NO_PROGRESS_THRESHOLD_INVALID" }
if (-not [bool]$policy.continueStartsFreshBudget) { throw "HARNESS_POLICY_CONTINUE_BUDGET_INVALID" }
if (-not [bool]$policy.continuousAutoAdvanceAfterProgress) { throw "HARNESS_POLICY_CONTINUOUS_INVALID" }
if (-not [bool]$policy.continuousRollsOverCycleBudget) { throw "HARNESS_POLICY_CONTINUOUS_ROLLOVER_INVALID" }
if (-not [bool]$policy.requireDistinctNoProgressCandidates) { throw "HARNESS_POLICY_NO_PROGRESS_DISTINCT_INVALID" }
if (-not [bool]$policy.verificationDimensionsIndependent) { throw "HARNESS_POLICY_VALIDATION_DIMENSIONS_INVALID" }

$autonomyPath = Join-Path $workspaceFull "state/autonomy-state.json"
if (-not (Test-Path -LiteralPath $autonomyPath)) { throw "AUTONOMY_STATE_MISSING: $autonomyPath" }
try { $autonomy = Get-Content -LiteralPath $autonomyPath -Raw | ConvertFrom-Json }
catch { throw "AUTONOMY_STATE_INVALID_JSON: $($_.Exception.Message)" }
if ($autonomy.schemaVersion -ne "standard-migration-autonomy/v3") { throw "AUTONOMY_STATE_SCHEMA_INVALID: $($autonomy.schemaVersion)" }
if ([int]$autonomy.cycleBudget -lt 1 -or [int]$autonomy.cycleBudget -gt [int]$policy.maxRemediationCyclesPerInvocation) {
    throw "AUTONOMY_STATE_CYCLE_BUDGET_INVALID"
}
if ([bool]$autonomy.cycleInProgress -and [bool]$autonomy.rollbackRequired) { throw "AUTONOMY_STATE_TRANSACTION_FLAGS_CONFLICT" }
if ([bool]$autonomy.cycleInProgress -and [string]::IsNullOrWhiteSpace([string]$autonomy.activeCycleBaselineStateHash)) { throw "AUTONOMY_STATE_ACTIVE_BASELINE_MISSING" }

Write-Host "STANDARD_RUN_POLICY_PASS"
Write-Host "Remediation cycle budget: $($policy.maxRemediationCyclesPerInvocation)"
Write-Host "One bounded change per cycle: $($policy.maxChangesPerCycle)"
Write-Host "No-progress threshold: $($policy.noProgressStopThreshold) distinct candidates"
