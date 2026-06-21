param(
    [string]$Workspace = "{{WORKSPACE}}",
    [string]$Prompt = "prompts/loop-batch-prompt.txt"
)

$ErrorActionPreference = "Stop"
$promptPath = Join-Path $Workspace $Prompt
if (-not (Test-Path $promptPath)) {
    throw "Prompt not found: $promptPath"
}

Write-Host "Copy this prompt into your coding agent:"
Write-Host ""
Write-Host "--- BEGIN PROMPT ---"
Get-Content -Path $promptPath
Write-Host "--- END PROMPT ---"
