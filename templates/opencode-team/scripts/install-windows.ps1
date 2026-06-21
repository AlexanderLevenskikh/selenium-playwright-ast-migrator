$ErrorActionPreference = "Stop"

$Source = Join-Path $PSScriptRoot "..\global\.config\opencode"
$Target = Join-Path $HOME ".config\opencode"

Write-Host "Installing OpenCode agent team template..."
Write-Host "Source: $Source"
Write-Host "Target: $Target"

New-Item -ItemType Directory -Force -Path $Target | Out-Null

Copy-Item -Path (Join-Path $Source "*") -Destination $Target -Recurse -Force

Write-Host ""
Write-Host "Done."
Write-Host ""
Write-Host "Next:"
Write-Host "1. Copy project-template\AGENTS.md to the root of your repository."
Write-Host "2. In opencode, try:"
Write-Host "   /supervised-task inspect the current repository and report the safest first task"
