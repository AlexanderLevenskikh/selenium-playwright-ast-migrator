<# Record a named skill profile for the active standard migration run. #>
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
$profiles = [ordered]@{
    "orchestrator" = @("plow-ahead", "efficient-frontier", "root-cause-prioritization", "quick-recap")
    "supervised-task" = @("plow-ahead", "efficient-frontier", "root-cause-prioritization", "quick-recap")
    "executor" = @("plow-ahead")
    "executor-docs-first" = @("plow-ahead", "read-the-damn-docs")
    "docs-first" = @("read-the-damn-docs")
    "watchdog" = @("agent-watchdog")
    "reviewer" = @("agent-watchdog", "quick-recap")
    "plan-arbiter" = @("plan-arbiter")
    "final-handoff" = @("quick-recap")
}
$normalizedProfile = $Profile.Trim().ToLowerInvariant()
if (-not $profiles.Contains($normalizedProfile)) {
    throw "Unknown agent skill profile '$Profile'. Allowed profiles: $($profiles.Keys -join ', ')."
}
$workspacePath = if ([IO.Path]::IsPathRooted($Workspace)) { [IO.Path]::GetFullPath($Workspace) } else { [IO.Path]::GetFullPath((Join-Path (Get-Location) $Workspace)) }
if (-not (Test-Path $workspacePath)) { throw "Workspace not found: $workspacePath" }
$writer = Join-Path (Split-Path -Parent $MyInvocation.MyCommand.Path) "write-agent-skill-usage.ps1"
if (-not (Test-Path $writer)) { throw "Skill usage writer not found: $writer" }
$skills = @($profiles[$normalizedProfile])
$effectiveTrigger = if ($Trigger -eq "profile") { "profile:$normalizedProfile" } else { $Trigger }
$effectiveDetail = if ([string]::IsNullOrWhiteSpace($Detail)) { "Applied '$normalizedProfile' skill profile: $($skills -join ', ')." } else { "Profile '$normalizedProfile'. $Detail" }
$argsList = @("-Workspace", $Workspace)
if (-not [string]::IsNullOrWhiteSpace($RunId)) { $argsList += @("-RunId", $RunId) }
$argsList += @("-SkillName") + $skills + @("-Trigger", $effectiveTrigger, "-Phase", $Phase, "-Decision", $Decision, "-Detail", $effectiveDetail, "-EvidencePath", $EvidencePath)
& $writer @argsList
if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "AGENT_SKILL_PROFILE_RECORDED: $normalizedProfile -> $($skills -join ',')"
