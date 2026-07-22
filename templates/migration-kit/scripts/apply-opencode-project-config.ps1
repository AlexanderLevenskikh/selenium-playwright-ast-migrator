param(
    [string]$RepoRoot = ".",
    [string]$Workspace = "migration",
    [ValidateSet("LowNoise", "TrustedProject")]
    [string]$PermissionProfile = "LowNoise",
    [switch]$Force,
    [switch]$DryRun,
    [switch]$SkipProjectAgents
)

$ErrorActionPreference = "Stop"

function Get-FullPathCompat([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        throw "Path value is empty."
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $PathValue))
}

function Copy-DirectoryContents([string]$SourceDirectory, [string]$DestinationDirectory) {
    if (-not (Test-Path $SourceDirectory)) {
        throw "Source directory does not exist: $SourceDirectory"
    }

    if ($DryRun) {
        Write-Host "DRY RUN: copy directory contents from $SourceDirectory to $DestinationDirectory"
        return
    }

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
    $entries = Get-ChildItem -LiteralPath $SourceDirectory -Force
    foreach ($entry in $entries) {
        Copy-Item -LiteralPath $entry.FullName -Destination $DestinationDirectory -Recurse -Force
    }
}

function Backup-PathIfExists([string]$PathToBackup, [string]$BackupRoot) {
    if (-not (Test-Path $PathToBackup)) {
        return
    }

    if ($DryRun) {
        Write-Host "DRY RUN: backup $PathToBackup to $BackupRoot"
        return
    }

    New-Item -ItemType Directory -Force -Path $BackupRoot | Out-Null
    $leaf = Split-Path -Leaf $PathToBackup
    $destination = Join-Path $BackupRoot $leaf
    if (Test-Path $destination) {
        $destination = Join-Path $BackupRoot ($leaf + "." + [Guid]::NewGuid().ToString("N"))
    }
    Copy-Item -LiteralPath $PathToBackup -Destination $destination -Recurse -Force
    Write-Host "Backed up existing $leaf to $destination"
}

function Copy-FileSafe([string]$SourceFile, [string]$DestinationFile, [string]$BackupRoot, [bool]$Overwrite) {
    if (Test-Path $DestinationFile) {
        if (-not $Overwrite) {
            Write-Host "Keeping existing file: $DestinationFile"
            return
        }

        Backup-PathIfExists $DestinationFile $BackupRoot
    }

    if ($DryRun) {
        Write-Host "DRY RUN: copy file $SourceFile to $DestinationFile"
        return
    }

    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $DestinationFile) | Out-Null
    Copy-Item -LiteralPath $SourceFile -Destination $DestinationFile -Force
}

$repoRootFull = Get-FullPathCompat $RepoRoot
$workspaceFull = Get-FullPathCompat $Workspace
$sourceRoot = Join-Path $workspaceFull "opencode-team/global/.config/opencode"
$projectAgentsTemplate = Join-Path $workspaceFull "opencode-team/project-template/AGENTS.md"

if (-not (Test-Path $sourceRoot)) {
    throw "OpenCode team template was not found at '$sourceRoot'. Run 'selenium-pw-migrator kit bootstrap-opencode --workspace $Workspace --opencode-install none' first, or install the workspace with team templates."
}

$configFileName = if ($PermissionProfile -eq "TrustedProject") { "opencode.trusted-project.jsonc" } else { "opencode.jsonc" }
$configSource = Join-Path $sourceRoot $configFileName
if (-not (Test-Path $configSource)) {
    throw "OpenCode config profile was not found: $configSource"
}

$targetOpenCode = Join-Path $repoRootFull ".opencode"
$targetAgents = Join-Path $targetOpenCode "agents"
$targetCommands = Join-Path $targetOpenCode "commands"
$backupRoot = Join-Path $workspaceFull (".migration-kit/opencode-backups/" + (Get-Date -Format "yyyyMMdd-HHmmss"))

Write-Host "Applying OpenCode project config to repository root..."
Write-Host "Repo root:          $repoRootFull"
Write-Host "Workspace:          $workspaceFull"
Write-Host "Source:             $sourceRoot"
Write-Host "Permission profile: $PermissionProfile"
Write-Host "Force overwrite:    $Force"
Write-Host "Dry run:            $DryRun"
Write-Host ""

Backup-PathIfExists (Join-Path $repoRootFull "opencode.jsonc") $backupRoot
Backup-PathIfExists $targetAgents $backupRoot
Backup-PathIfExists $targetCommands $backupRoot

if (-not $DryRun) {
    New-Item -ItemType Directory -Force -Path $targetAgents | Out-Null
    New-Item -ItemType Directory -Force -Path $targetCommands | Out-Null
}

Copy-FileSafe $configSource (Join-Path $repoRootFull "opencode.jsonc") $backupRoot $true
Copy-DirectoryContents (Join-Path $sourceRoot "agents") $targetAgents
Copy-DirectoryContents (Join-Path $sourceRoot "commands") $targetCommands

if (-not $SkipProjectAgents -and (Test-Path $projectAgentsTemplate)) {
    Copy-FileSafe $projectAgentsTemplate (Join-Path $repoRootFull "AGENTS.md") $backupRoot ([bool]$Force)
}
elseif ($SkipProjectAgents) {
    Write-Host "Skipped root AGENTS.md installation."
}

Write-Host ""
Write-Host "OPENCODE_PROJECT_CONFIG_APPLIED"
Write-Host "Installed:"
Write-Host "  $(Join-Path $repoRootFull 'opencode.jsonc')"
Write-Host "  $targetAgents"
Write-Host "  $targetCommands"
Write-Host ""
Write-Host "Next: open this repository folder in OpenCode and run /supervised-task."
