param(
    [ValidateSet("ProjectLocal", "ProjectDesktop", "Global")]
    [string]$Mode = "ProjectLocal",
    [string]$Target = "",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

$Source = Join-Path $PSScriptRoot "..\global\.config\opencode"
if ([string]::IsNullOrWhiteSpace($Target)) {
    if ($Mode -eq "Global") {
        $Target = Join-Path $HOME ".config\opencode"
    }
    elseif ($Mode -eq "ProjectDesktop") {
        $Target = Get-Location
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
elseif ($Mode -eq "ProjectDesktop") {
    Write-Host "ProjectDesktop mode is recommended for OpenCode Desktop when the project folder is opened directly."
}
else {
    Write-Host "ProjectLocal mode is recommended. Start OpenCode for migration sessions with this config only."
}

function Backup-PathIfExists([string]$PathToBackup, [string]$BackupRoot) {
    if (-not (Test-Path $PathToBackup)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
    $leaf = Split-Path -Leaf $PathToBackup
    $destination = Join-Path $BackupRoot $leaf
    Copy-Item -Path $PathToBackup -Destination $destination -Recurse -Force
    Write-Host "Backed up existing $leaf to $destination"
}

if ($Mode -eq "ProjectDesktop") {
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    $backupRoot = Join-Path $Target (".migration-kit\opencode-backups\" + (Get-Date -Format "yyyyMMdd-HHmmss"))
    if (-not $Force) {
        Backup-PathIfExists (Join-Path $Target "opencode.jsonc") $backupRoot
        Backup-PathIfExists (Join-Path $Target ".opencode\agents") $backupRoot
        Backup-PathIfExists (Join-Path $Target ".opencode\commands") $backupRoot
    }

    Copy-Item -Path (Join-Path $Source "opencode.jsonc") -Destination (Join-Path $Target "opencode.jsonc") -Force

    $ProjectOpenCode = Join-Path $Target ".opencode"
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectOpenCode "agents") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectOpenCode "commands") | Out-Null
    Copy-Item -Path (Join-Path $Source "agents\*") -Destination (Join-Path $ProjectOpenCode "agents") -Recurse -Force
    Copy-Item -Path (Join-Path $Source "commands\*") -Destination (Join-Path $ProjectOpenCode "commands") -Recurse -Force
}
else {
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Target -Recurse -Force
}

Write-Host ""
Write-Host "Done."
Write-Host ""
Write-Host "Next:"
Write-Host "1. Copy project-template\AGENTS.md to the root of your repository if needed."
if ($Mode -eq "ProjectDesktop") {
    Write-Host "2. Open this repository folder in OpenCode Desktop:"
    Write-Host "   $Target"
    Write-Host "3. Use /supervised-task with migration/prompts/kickoff-prompt.txt."
    Write-Host "4. Existing OpenCode project config is backed up unless -Force is used."
}
elseif ($Mode -eq "ProjectLocal") {
    Write-Host "2. Use this config only for migration sessions, for example:"
    Write-Host "   `$env:OPENCODE_CONFIG = `"$(Join-Path $Target "opencode.jsonc")`""
    Write-Host "   opencode"
}
else {
    Write-Host "2. In opencode, try:"
    Write-Host "   /supervised-task inspect the current repository and report the safest first task"
}
