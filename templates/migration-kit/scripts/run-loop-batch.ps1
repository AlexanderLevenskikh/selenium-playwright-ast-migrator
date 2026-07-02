param(
    [string]$Workspace = "{{WORKSPACE}}",
    [string]$Prompt = "prompts/loop-batch-prompt.txt"
)

$ErrorActionPreference = "Stop"
$promptPath = Join-Path $Workspace $Prompt
if (-not (Test-Path $promptPath)) {
    throw "Prompt not found: $promptPath"
}

$contractPath = Join-Path $Workspace "AGENT_CONTRACT.md"
if (-not (Test-Path $contractPath)) {
    throw "Agent contract not found: $contractPath"
}

$scopeGuard = Join-Path $Workspace "scripts/check-scope.ps1"
if (Test-Path $scopeGuard) {
    Write-Host "Preflight scope guard:"
    & $scopeGuard -RepoRoot "." -AllowedRoots @($Workspace)
    if ($LASTEXITCODE -ne 0) {
        throw "Scope guard failed before handing a prompt to the agent."
    }
}

Write-Host "Copy this prompt into your coding agent:"
Write-Host ""
Write-Host "--- BEGIN PROMPT ---"
Write-Host "Read $contractPath first. Before each major action, restate which contract rule allows it."
Write-Host ""
Get-Content -Path $promptPath
Write-Host "--- END PROMPT ---"
