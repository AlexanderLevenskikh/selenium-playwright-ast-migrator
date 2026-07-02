param(
    [ValidateSet("ProjectLocal", "Global")]
    [string]$Mode = "ProjectLocal",
    [string]$Target = ""
)

$ErrorActionPreference = "Stop"

$Source = Join-Path $PSScriptRoot "..\global\.config\opencode"
if ([string]::IsNullOrWhiteSpace($Target)) {
    if ($Mode -eq "Global") {
        $Target = Join-Path $HOME ".config\opencode"
    }
    else {
        $Target = Join-Path (Get-Location) ".opencode-migrator"
    }
}

Write-Host "Installing OpenCode agent team template..."
Write-Host "Mode:   $Mode"
Write-Host "Source: $Source"
Write-Host "Target: $Target"
Write-Host ""

if ($Mode -eq "Global") {
    Write-Warning "Global mode affects all OpenCode sessions for this user. Use it only if you want artifact-only migration behavior globally."
}
else {
    Write-Host "ProjectLocal mode is recommended. Start OpenCode for migration sessions with this config only."
}

New-Item -ItemType Directory -Force -Path $Target | Out-Null
Copy-Item -Path (Join-Path $Source "*") -Destination $Target -Recurse -Force

Write-Host ""
Write-Host "Done."
Write-Host ""
Write-Host "Next:"
Write-Host "1. Copy project-template\AGENTS.md to the root of your repository if needed."
if ($Mode -eq "ProjectLocal") {
    Write-Host "2. Use this config only for migration sessions, for example:"
    Write-Host "   `$env:OPENCODE_CONFIG = `"$(Join-Path $Target "opencode.jsonc")`""
    Write-Host "   opencode"
}
else {
    Write-Host "2. In opencode, try:"
    Write-Host "   /supervised-task inspect the current repository and report the safest first task"
}
