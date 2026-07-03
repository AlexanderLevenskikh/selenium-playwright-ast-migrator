param(
    [string]$Workspace = ".dogfood/migration",
    [string]$RepoRoot = ".",
    [switch]$Clean,
    [string[]]$AllowedRoots = @(
        ".dogfood/migration",
        "migration",
        "docs",
        "templates/migration-kit",
        "templates/opencode-team",
        "scripts",
        "Migrator.Tests",
        "Migrator.Cli",
        ".gitignore"
    ),
    [switch]$CheckWorkingTreeScope
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$Base, [string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }
    return [System.IO.Path]::GetFullPath((Join-Path $Base $Path))
}

function Assert-File([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "Missing expected file: $Path"
    }
}

function Invoke-Checked([string]$FileName, [string[]]$Arguments) {
    Write-Host "RUN: $FileName $($Arguments -join ' ')"
    & $FileName @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FileName $($Arguments -join ' ')"
    }
}

$repoRootPath = Resolve-FullPath (Get-Location).Path $RepoRoot
$workspacePath = Resolve-FullPath $repoRootPath $Workspace
$cliProject = Join-Path $repoRootPath "Migrator.Cli/Migrator.Cli.csproj"

if (-not (Test-Path $cliProject)) {
    throw "Migrator.Cli project was not found: $cliProject"
}

if ($Clean -and (Test-Path $workspacePath)) {
    Write-Host "clean: $workspacePath"
    Remove-Item -Recurse -Force -Path $workspacePath
}

Write-Host "Installing dogfood migration workspace: $workspacePath"
Invoke-Checked "dotnet" @(
    "run",
    "--project", $cliProject,
    "--",
    "kit", "init",
    "--workspace", $Workspace,
    "--with-team"
)

$requiredFiles = @(
    "harness/README.md",
    "state/harness-policy.json",
    "state/harness-run-template.json",
    "prompts/autopilot-loop-prompt.txt",
    "prompts/harness-review-prompt.txt",
    "scripts/new-harness-run.ps1",
    "scripts/write-harness-event.ps1",
    "scripts/check-harness-policy.ps1",
    "scripts/check-scope.ps1",
    "scripts/check-final-gate.ps1"
)
foreach ($relative in $requiredFiles) {
    Assert-File (Join-Path $workspacePath $relative)
}

$newRunScript = Join-Path $workspacePath "scripts/new-harness-run.ps1"
$eventScript = Join-Path $workspacePath "scripts/write-harness-event.ps1"
$policyScript = Join-Path $workspacePath "scripts/check-harness-policy.ps1"
$scopeScript = Join-Path $workspacePath "scripts/check-scope.ps1"

& powershell -NoProfile -ExecutionPolicy Bypass -File $newRunScript `
    -Workspace $Workspace `
    -TaskTitle "Harness dogfood smoke" `
    -Goal "Verify that the Migrator Agent Harness Kit installs, creates a run, writes events, and passes policy checks."
if ($LASTEXITCODE -ne 0) {
    throw "new-harness-run.ps1 failed with exit code $LASTEXITCODE"
}

& powershell -NoProfile -ExecutionPolicy Bypass -File $eventScript `
    -Workspace $Workspace `
    -Phase "dogfood" `
    -Action "dogfood-smoke-started" `
    -Status "started" `
    -Detail "Harness dogfood smoke started."
if ($LASTEXITCODE -ne 0) {
    throw "write-harness-event.ps1 failed with exit code $LASTEXITCODE"
}

$agentStateAfterRun = Get-Content -Raw -Path (Join-Path $workspacePath "agent-state.md")
$latestRunMatch = [regex]::Match($agentStateAfterRun, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
if (-not $latestRunMatch.Success) {
    throw "new-harness-run.ps1 did not write Latest run to agent-state.md"
}
$latestRunId = $latestRunMatch.Groups[1].Value
$latestRunDir = Join-Path $workspacePath "runs/$latestRunId"
foreach ($runArtifact in @("Prompt.md", "Plan.md", "Implement.md", "Documentation.md", "trace.jsonl")) {
    Assert-File (Join-Path $latestRunDir $runArtifact)
}

& powershell -NoProfile -ExecutionPolicy Bypass -File $policyScript `
    -Workspace $Workspace `
    -RepoRoot $repoRootPath `
    -AllowedRoots $AllowedRoots `
    -SkipGitStatus
if ($LASTEXITCODE -ne 0) {
    throw "check-harness-policy.ps1 failed with exit code $LASTEXITCODE"
}

if ($CheckWorkingTreeScope) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File $scopeScript `
        -RepoRoot $repoRootPath `
        -AllowedRoots $AllowedRoots
    if ($LASTEXITCODE -ne 0) {
        throw "check-scope.ps1 failed with exit code $LASTEXITCODE"
    }
} else {
    Write-Host "SCOPE_GUARD_SKIPPED: pass -CheckWorkingTreeScope to validate the current repository diff with check-scope.ps1"
}

$agentState = Get-Content -Raw -Path (Join-Path $workspacePath "agent-state.md")
$match = [regex]::Match($agentState, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
if (-not $match.Success) {
    throw "Could not resolve latest run id from agent-state.md"
}
$runId = $match.Groups[1].Value
$runDir = Join-Path $workspacePath "runs/$runId"
foreach ($file in @("Prompt.md", "Plan.md", "Implement.md", "Documentation.md", "trace.jsonl")) {
    Assert-File (Join-Path $runDir $file)
}

$evidenceDir = Join-Path $workspacePath "evidence"
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
$evidencePath = Join-Path $evidenceDir "harness-dogfood-smoke.md"
$evidence = @(
    "# Harness Dogfood Smoke Evidence",
    "",
    "Status: PASS",
    "Run: $runId",
    "Workspace: $Workspace",
    "Generated at UTC: $([DateTimeOffset]::UtcNow.ToString('o'))",
    "",
    "Checked files:",
    "",
    ($requiredFiles | ForEach-Object { "- $_" }),
    "",
    "Reports:",
    "",
    "- state/harness-policy-result.md",
    "- state/harness-events.jsonl",
    "- runs/$runId/trace.jsonl"
)
Set-Content -Path $evidencePath -Value ($evidence -join [Environment]::NewLine) -Encoding UTF8

& powershell -NoProfile -ExecutionPolicy Bypass -File $eventScript `
    -Workspace $Workspace `
    -RunId $runId `
    -Phase "dogfood" `
    -Action "dogfood-smoke-pass" `
    -Status "pass" `
    -Detail "Harness dogfood smoke passed."
if ($LASTEXITCODE -ne 0) {
    throw "write-harness-event.ps1 pass event failed with exit code $LASTEXITCODE"
}

Write-Host "HARNESS_DOGFOOD_PASS"
Write-Host "Evidence: $evidencePath"
