param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [string]$TaskTitle = "Migration harness batch",
    [string]$Goal = "Run one bounded artifact-only migration batch.",
    [string[]]$AllowedRoots = @($Workspace),
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function Get-WorkspacePath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Test-CanonicalRunDirectory([string]$RunPath) {
    if (-not (Test-Path $RunPath)) { return $false }
    foreach ($file in @("Prompt.md", "Plan.md", "Implement.md", "Documentation.md", "trace.jsonl")) {
        if (-not (Test-Path (Join-Path $RunPath $file))) { return $false }
    }
    return $true
}

function Test-DirectoryEmpty([string]$Path) {
    if (-not (Test-Path $Path)) { return $true }
    return $null -eq (Get-ChildItem -LiteralPath $Path -Force -ErrorAction SilentlyContinue | Select-Object -First 1)
}

function Get-NextRunId([string]$RunsPath) {
    if (-not (Test-Path $RunsPath)) { return "run-001" }
    $max = 0
    Get-ChildItem -Path $RunsPath -Directory -ErrorAction SilentlyContinue | ForEach-Object {
        $m = [regex]::Match($_.Name, '^run-(\d+)$')
        if ($m.Success -and (Test-CanonicalRunDirectory $_.FullName)) {
            $n = [int]$m.Groups[1].Value
            if ($n -gt $max) { $max = $n }
        }
    }
    $candidate = $max + 1
    while ($true) {
        $runId = "run-{0:D3}" -f $candidate
        $candidatePath = Join-Path $RunsPath $runId
        if (-not (Test-Path $candidatePath) -or (Test-DirectoryEmpty $candidatePath)) {
            return $runId
        }
        $candidate++
    }
}

function Get-ExistingRunIds([string]$RunsPath) {
    if (-not (Test-Path $RunsPath)) { return @() }
    return @(Get-ChildItem -Path $RunsPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^run-(\d+)$' } |
        ForEach-Object { $_.Name } |
        Sort-Object)
}

function Get-CanonicalRunIds([string]$RunsPath) {
    if (-not (Test-Path $RunsPath)) { return @() }
    return @(Get-ChildItem -Path $RunsPath -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^run-(\d+)$' -and (Test-CanonicalRunDirectory $_.FullName) } |
        ForEach-Object { $_.Name } |
        Sort-Object)
}

function Set-Utf8NoBom([string]$Path, [string]$Value) {
    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Value, $utf8)
}

$workspacePath = Get-WorkspacePath $Workspace
$runsPath = Join-Path $workspacePath "runs"
New-Item -ItemType Directory -Force -Path $runsPath | Out-Null

$existingRunIds = @(Get-ExistingRunIds $runsPath)
$canonicalRunIds = @(Get-CanonicalRunIds $runsPath)
$runIdSource = "explicit"
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $runIdSource = "auto-from-existing-run-directories"
    $RunId = Get-NextRunId $runsPath
}

if ($RunId -notmatch '^run-[0-9A-Za-z][0-9A-Za-z._-]*$') {
    throw "Invalid RunId '$RunId'. Expected run-001 style identifier."
}

$runPath = Join-Path $runsPath $RunId
if ((Test-Path $runPath) -and -not (Test-DirectoryEmpty $runPath) -and -not $Force) {
    throw "Run already exists: $runPath. Use -Force to overwrite run skeleton files."
}

New-Item -ItemType Directory -Force -Path $runPath | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $workspacePath "state") | Out-Null

$createdAt = [DateTimeOffset]::UtcNow.ToString("o")
$allowedRootsText = ($AllowedRoots -join ", ")

Set-Utf8NoBom (Join-Path $runPath "Prompt.md") @"
# $RunId Prompt

Task: $TaskTitle

Goal: $Goal

Mode: artifact-only
Allowed roots: $allowedRootsText
Status: CONTINUE_AUTONOMOUSLY

## Done when

- Scope guard passes.
- Harness policy check passes.
- Relevant migration/config/verify evidence is written under migration/**.
- Final gate passes before any FINAL claim.

## Non-goals

- Do not edit real product files.
- Do not edit guard scripts, checksums, permissions, or AGENTS.md.
- Do not reduce TODO by suppressing assertions or deleting behavior.
"@

Set-Utf8NoBom (Join-Path $runPath "Plan.md") @"
# $RunId Plan

## Milestones

1. Read contract, harness policy, current ticket, and handoff.
2. Run/check harness policy.
3. Perform one bounded migration/config/report improvement.
4. Record evidence.
5. Run scope guard.
6. Run relevant verification.
7. Update handoff and run ledger.
8. Run final gate only when claiming FINAL.
9. If final gate writes continuation-decision.json with CONTINUE_REQUIRED, execute one next bounded action before handoff. If it writes FINAL, stop for review unless the user explicitly requested continue.

## Validation commands

```powershell
.\migration\scripts\check-harness-policy.ps1 -Workspace migration -RepoRoot .
.\migration\scripts\check-scope.ps1 -RepoRoot . -AllowedRoots migration
.\migration\scripts\check-final-gate.ps1 -Workspace migration -RepoRoot .
```
"@

Set-Utf8NoBom (Join-Path $runPath "Implement.md") @"
# $RunId Implement

## Operating rules

- Continue autonomously inside the allowed lane.
- Keep changes scoped and reviewable.
- Prefer config/source-truth fixes over generated-code patching.
- Write blockers as artifacts instead of asking vague continuation questions.
- After a non-final final gate, read continuation-decision.json.
- If continuation-decision.json says CONTINUE_REQUIRED, continue with exactly one next bounded action before user-facing handoff. If it says FINAL, stop for review and include one recommended `/supervised-task continue ...` command.
- Update trace and state files after meaningful progress.

## Ask/stop triggers

- Any write outside allowed roots.
- Guard or permission changes.
- Dependency install/update.
- Network access.
- Git commit/push/reset/clean.
- Destructive delete/move.
"@

Set-Utf8NoBom (Join-Path $runPath "Documentation.md") @"
# $RunId Documentation

Created: $createdAt
Status: STARTED

## Progress

- Run skeleton created.

## Evidence

- Pending.

## Blockers

- None recorded yet.
"@

# Defensive pass: the harness run must never be marked created unless every
# canonical run artifact exists. This also makes the script robust across
# Windows PowerShell execution quirks and partially overwritten runs.
$requiredRunArtifacts = [ordered]@{
    "Prompt.md" = "# $RunId Prompt`n`nTask: $TaskTitle`n`nGoal: $Goal`n`nStatus: CONTINUE_AUTONOMOUSLY`n"
    "Plan.md" = "# $RunId Plan`n`n## Milestones`n`n- Read contract, policy, and handoff.`n- Execute one bounded batch.`n- Record evidence and run gates.`n"
    "Implement.md" = "# $RunId Implement`n`n## Operating rules`n`n- Continue autonomously inside the allowed lane.`n- Keep changes scoped and reviewable.`n- Update trace and state files after meaningful progress.`n"
    "Documentation.md" = "# $RunId Documentation`n`nCreated: $createdAt`nStatus: STARTED`n"
}
foreach ($entry in $requiredRunArtifacts.GetEnumerator()) {
    $artifactPath = Join-Path $runPath $entry.Key
    if (-not (Test-Path $artifactPath)) {
        Set-Utf8NoBom $artifactPath $entry.Value
    }
}
$missingRunArtifacts = @($requiredRunArtifacts.Keys | Where-Object { -not (Test-Path (Join-Path $runPath $_)) })
if ($missingRunArtifacts.Count -gt 0) {
    throw "new-harness-run.ps1 did not create required run artifacts: $($missingRunArtifacts -join ', ')"
}

$tracePath = Join-Path $runPath "trace.jsonl"
if (-not (Test-Path $tracePath) -or $Force) {
    Set-Utf8NoBom $tracePath ""
}

$runState = [ordered]@{
    schemaVersion = 1
    runId = $RunId
    status = "CONTINUE_AUTONOMOUSLY"
    mode = "artifact-only"
    createdAtUtc = $createdAt
    taskTitle = $TaskTitle
    goal = $Goal
    allowedRoots = @($AllowedRoots)
    runIdSource = $runIdSource
    existingRunIdsBeforeCreate = @($existingRunIds)
    canonicalRunIdsBeforeCreate = @($canonicalRunIds)
    files = [ordered]@{
        prompt = "runs/$RunId/Prompt.md"
        plan = "runs/$RunId/Plan.md"
        implement = "runs/$RunId/Implement.md"
        documentation = "runs/$RunId/Documentation.md"
        trace = "runs/$RunId/trace.jsonl"
    }
    latestChecks = [ordered]@{
        scope = "UNKNOWN"
        harnessPolicy = "UNKNOWN"
        finalGate = "UNKNOWN"
    }
}

Set-Utf8NoBom (Join-Path $workspacePath "state/harness-run.json") ($runState | ConvertTo-Json -Depth 10)

Set-Utf8NoBom (Join-Path $workspacePath "agent-state.md") @"
# Agent State

Status: CONTINUE_AUTONOMOUSLY
Latest run: $RunId
Mode: artifact-only
Allowed roots: $allowedRootsText
Updated: $createdAt

## Next action

Read migration/prompts/autopilot-loop-prompt.txt and continue the latest run.
"@

Set-Utf8NoBom (Join-Path $workspacePath "current-ticket.md") @"
# Current Ticket

Run id: $RunId
Status: CONTINUE_AUTONOMOUSLY
Task: $TaskTitle
Goal: $Goal
Allowed roots: $allowedRootsText

## Acceptance

- Scope guard PASS.
- Harness policy PASS.
- Relevant evidence exists under migration/**.
- Final gate PASS before FINAL.
"@

$handoffPath = Join-Path $workspacePath "state/handoff.md"
Set-Utf8NoBom $handoffPath @"
# Handoff

Status: CONTINUE_AUTONOMOUSLY
Latest run: $RunId
Updated: $createdAt

## Resume from

- migration/runs/$RunId/Prompt.md
- migration/runs/$RunId/Plan.md
- migration/runs/$RunId/Implement.md
- migration/runs/$RunId/Documentation.md

## Next autonomous step

Run/check harness policy, then execute the first bounded batch step.
"@

$ledgerPath = Join-Path $workspacePath "state/run-ledger.md"
if (-not (Test-Path $ledgerPath)) {
    Set-Utf8NoBom $ledgerPath "# Run Ledger`n`n"
}
Add-Content -Path $ledgerPath -Encoding UTF8 -Value "- $createdAt - $RunId - $TaskTitle - CONTINUE_AUTONOMOUSLY"

$event = [ordered]@{
    utc = $createdAt
    runId = $RunId
    phase = "bootstrap"
    action = "new-harness-run"
    status = "created"
    detail = "$Goal; runIdSource=$runIdSource; existingRunIdsBeforeCreate=$($existingRunIds -join ','); canonicalRunIdsBeforeCreate=$($canonicalRunIds -join ',')"
}
$eventLine = $event | ConvertTo-Json -Compress -Depth 10
Add-Content -Path (Join-Path $workspacePath "state/harness-events.jsonl") -Encoding UTF8 -Value $eventLine
Add-Content -Path $tracePath -Encoding UTF8 -Value $eventLine

Write-Host "HARNESS_RUN_CREATED: $RunId"
Write-Host "Run path: $runPath"
