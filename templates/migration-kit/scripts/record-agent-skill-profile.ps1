<#
.SYNOPSIS
Record a named agent skill profile for the active migration run.

.DESCRIPTION
record-agent-skill-profile is a thin profile recorder over write-agent-skill-usage.
It expands a role/profile name such as orchestrator, executor-docs-first, watchdog,
final-handoff, or wave-manager into the corresponding migration/agent-skills usage evidence.
#>

param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [Parameter(Mandatory = $true)][string]$Profile,
    [string]$Trigger = "profile",
    [string]$Phase = "skills",
    [string]$Decision = "applied",
    [string]$Detail = "",
    [string]$EvidencePath = ""
)

$ErrorActionPreference = "Stop"

function Get-WorkspacePath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

$profiles = [ordered]@{
    "orchestrator" = @("plow-ahead", "efficient-frontier", "quick-recap")
    "supervised-task" = @("plow-ahead", "efficient-frontier", "quick-recap")
    "executor" = @("plow-ahead")
    "executor-docs-first" = @("plow-ahead", "read-the-damn-docs")
    "docs-first" = @("read-the-damn-docs")
    "wave" = @("efficient-frontier", "plow-ahead")
    "watchdog" = @("agent-watchdog")
    "reviewer" = @("agent-watchdog", "quick-recap")
    "plan-arbiter" = @("plan-arbiter")
    "final-handoff" = @("quick-recap")
    "wave-manager" = @("quality-profit-arbitration", "root-cause-prioritization", "adaptive-wave-sizing")
}

$normalizedProfile = $Profile.Trim().ToLowerInvariant()
if (-not $profiles.Contains($normalizedProfile)) {
    $allowed = ($profiles.Keys -join ", ")
    throw "Unknown agent skill profile '$Profile'. Allowed profiles: $allowed."
}

$workspacePath = Get-WorkspacePath $Workspace
if (-not (Test-Path $workspacePath)) {
    throw "Workspace not found: $workspacePath"
}

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$writer = Join-Path $scriptRoot "write-agent-skill-usage.ps1"
if (-not (Test-Path $writer)) {
    throw "Skill usage writer not found: $writer. Run kit update/bootstrap first."
}

$skills = @($profiles[$normalizedProfile])
$effectiveTrigger = if ($Trigger -eq "profile") { "profile:$normalizedProfile" } else { $Trigger }
$effectiveDetail = if ([string]::IsNullOrWhiteSpace($Detail)) {
    "Applied '$normalizedProfile' skill profile: $($skills -join ', ')."
} else {
    "Profile '$normalizedProfile'. $Detail"
}

$writerArgs = @("-Workspace", $Workspace, "-SkillName") + $skills + @(
    "-Trigger", $effectiveTrigger,
    "-Phase", $Phase,
    "-Decision", $Decision,
    "-Detail", $effectiveDetail,
    "-EvidencePath", $EvidencePath
)
if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    $writerArgs = @("-Workspace", $Workspace, "-RunId", $RunId, "-SkillName") + $skills + @(
        "-Trigger", $effectiveTrigger,
        "-Phase", $Phase,
        "-Decision", $Decision,
        "-Detail", $effectiveDetail,
        "-EvidencePath", $EvidencePath
    )
}

& $writer @writerArgs
if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "AGENT_SKILL_PROFILE_RECORDED: $normalizedProfile -> $($skills -join ',')"
