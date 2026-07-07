param(
    [ValidateSet("ProjectLocal", "ProjectDesktop", "Global")]
    [string]$Mode = "ProjectLocal",
    [string]$Target = "",
    [switch]$Force,
    [ValidateSet("LowNoise", "TrustedProject")]
    [string]$PermissionProfile = "LowNoise"
)


<#
Example trusted-project install:
  .\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop -PermissionProfile TrustedProject -Force
#>

$ErrorActionPreference = "Stop"

$Source = Join-Path $PSScriptRoot "..\global\.config\opencode"
$ProjectAgentsTemplate = Join-Path $PSScriptRoot "..\project-template\AGENTS.md"
$ConfigFileName = if ($PermissionProfile -eq "TrustedProject") { "opencode.trusted-project.jsonc" } else { "opencode.jsonc" }

function Get-FullPathCompat([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "Path value is empty."
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $PathValue))
}

function Test-SamePath([string]$Left, [string]$Right) {
    $leftFull = Get-FullPathCompat $Left
    $rightFull = Get-FullPathCompat $Right
    return [string]::Equals(
        $leftFull.TrimEnd('\', '/'),
        $rightFull.TrimEnd('\', '/'),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-ProjectDesktopTargetFromScriptLocation() {
    # Expected installed-kit layout:
    #   <project-root>\migration\opencode-team\scripts\install-windows.ps1
    # In Desktop mode the target must be <project-root>, not the current shell directory
    # and never the user's global OpenCode config directory.
    $scriptsDir = Get-FullPathCompat $PSScriptRoot
    $opencodeTeamDir = Split-Path -Parent $scriptsDir
    $migrationDir = Split-Path -Parent $opencodeTeamDir
    $projectRoot = Split-Path -Parent $migrationDir

    if ((Split-Path -Leaf $scriptsDir) -ieq "scripts" -and
        (Split-Path -Leaf $opencodeTeamDir) -ieq "opencode-team" -and
        (Split-Path -Leaf $migrationDir) -ieq "migration" -and
        -not [string]::IsNullOrWhiteSpace($projectRoot)) {
        return $projectRoot
    }

    return ""
}

function Assert-ProjectDesktopTarget([string]$ProjectRoot) {
    $projectRootFull = Get-FullPathCompat $ProjectRoot
    $homeFull = Get-FullPathCompat $HOME
    $globalOpenCodeFull = Get-FullPathCompat (Join-Path $HOME ".config\opencode")

    if (Test-SamePath $projectRootFull $homeFull) {
        throw "ProjectDesktop cannot install into HOME ($homeFull). Run from the project root, use the installed migration\\opencode-team script, or pass -Target <project-root>."
    }

    if (Test-SamePath $projectRootFull $globalOpenCodeFull) {
        throw "ProjectDesktop cannot install into the global OpenCode config directory ($globalOpenCodeFull). Use -Mode Global for global install, or pass -Target <project-root>."
    }

    if ($projectRootFull -like (Join-Path $globalOpenCodeFull "*") ) {
        throw "ProjectDesktop cannot install under the global OpenCode config directory ($globalOpenCodeFull). Use -Mode Global for global install, or pass -Target <project-root>."
    }

    $migrationPath = Join-Path $projectRootFull "migration"
    if (-not (Test-Path $migrationPath)) {
        throw "ProjectDesktop target must be the repository root containing a migration directory. Target '$projectRootFull' does not contain '$migrationPath'. Pass -Target <project-root> explicitly if needed."
    }
}

if ([string]::IsNullOrWhiteSpace($Target)) {
    if ($Mode -eq "Global") {
        $Target = Join-Path $HOME ".config\opencode"
    }
    elseif ($Mode -eq "ProjectDesktop") {
        $inferredTarget = Get-ProjectDesktopTargetFromScriptLocation
        if (-not [string]::IsNullOrWhiteSpace($inferredTarget)) {
            $Target = $inferredTarget
        }
        else {
            $Target = (Get-Location).Path
        }
    }
    else {
        $Target = Join-Path (Get-Location).Path ".opencode-migrator"
    }
}

$Source = Get-FullPathCompat $Source
$Target = Get-FullPathCompat $Target

if (-not (Test-Path $Source)) {
    throw "OpenCode team source template was not found: $Source"
}

if ($Mode -eq "ProjectDesktop") {
    Assert-ProjectDesktopTarget $Target
}

Write-Host "Installing OpenCode agent team template..."
Write-Host "Mode:   $Mode"
Write-Host "Source: $Source"
Write-Host "Target: $Target"
Write-Host "Permission profile: $PermissionProfile"
Write-Host ""

if ($Mode -eq "Global") {
    Write-Warning "Global mode affects all OpenCode sessions for this user. Use it only if you want artifact-only migration behavior globally."
}
elseif ($Mode -eq "ProjectDesktop") {
    Write-Host "ProjectDesktop mode is recommended for OpenCode Desktop when the project folder is opened directly."
    Write-Host "It installs only into the project root, never into the user's global OpenCode config."
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

    Copy-Item -Path (Join-Path $Source $ConfigFileName) -Destination (Join-Path $Target "opencode.jsonc") -Force

    $ProjectOpenCode = Join-Path $Target ".opencode"
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectOpenCode "agents") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $ProjectOpenCode "commands") | Out-Null
    Copy-Item -Path (Join-Path $Source "agents\*") -Destination (Join-Path $ProjectOpenCode "agents") -Recurse -Force
    Copy-Item -Path (Join-Path $Source "commands\*") -Destination (Join-Path $ProjectOpenCode "commands") -Recurse -Force

    $TargetAgentsFile = Join-Path $Target "AGENTS.md"
    if ((Test-Path $ProjectAgentsTemplate) -and (-not (Test-Path $TargetAgentsFile))) {
        Copy-Item -Path $ProjectAgentsTemplate -Destination $TargetAgentsFile -Force
        Write-Host "Installed AGENTS.md into the project root."
    }
}

else {
    New-Item -ItemType Directory -Force -Path $Target | Out-Null
    Copy-Item -Path (Join-Path $Source "*") -Destination $Target -Recurse -Force
    # Copy selected permission profile into opencode.jsonc. Keep other profiles available as references.
    Copy-Item -Path (Join-Path $Source $ConfigFileName) -Destination (Join-Path $Target "opencode.jsonc") -Force
}

Write-Host ""
Write-Host "Done."
Write-Host ""
Write-Host "Next:"
Write-Host "1. AGENTS.md is copied automatically for ProjectDesktop when missing; existing files are preserved."
if ($PermissionProfile -eq "TrustedProject") {
    Write-Host "TrustedProject profile disables routine approval prompts inside this project; external directories remain blocked."
}
if ($Mode -eq "ProjectDesktop") {
    Write-Host "2. Open this repository folder in OpenCode Desktop:"
    Write-Host "   $Target"
    Write-Host "3. Use /supervised-task waves for a fresh wavefront start, or /supervised-task for existing workspace state."
    Write-Host "4. Existing OpenCode project config is backed up unless -Force is used."
}
elseif ($Mode -eq "ProjectLocal") {
    Write-Host "2. Use this config only for migration sessions, for example:"
    Write-Host "   `$env:OPENCODE_CONFIG = `"$(Join-Path $Target "opencode.jsonc")`""
    Write-Host "   opencode"
}
else {
    Write-Host "2. In opencode, try:"
    Write-Host "   /supervised-task waves"
}
