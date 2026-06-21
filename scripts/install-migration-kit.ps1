param(
    [string]$Workspace = "migration",
    [string]$Source = "<SOURCE_SELENIUM_PROJECT_PATH>",
    [string]$Target = "<TARGET_PROJECT_OR_OUTPUT_PATH>",
    [string]$Config = "migration/profiles/adapter-config.json",
    [string]$Output = "migration/runs/run-001",
    [string]$ToolCommand = "selenium-pw-migrator",
    [switch]$Update,
    [switch]$Force,
    [switch]$Backup,
    [switch]$NoRootAgentFiles,
    [switch]$NoCodexFiles,
    [switch]$WithTeam,
    [switch]$WithLoopLibrary,
    [switch]$NoToolManifest
)

$ErrorActionPreference = "Stop"
$KitVersion = "0.3.0"

function Resolve-RepoRootFromScript {
    $scriptDir = Split-Path -Parent $PSCommandPath
    $candidate = Split-Path -Parent $scriptDir

    if ((Test-Path (Join-Path $candidate ".agent-loops")) -or
        (Test-Path (Join-Path $candidate "templates/migration-kit")) -or
        (Test-Path (Join-Path $candidate "Migrator.Cli"))) {
        return $candidate
    }

    # Agent bundle layout: tool/scripts/install-migration-kit.ps1
    $bundleCandidate = Split-Path -Parent $scriptDir
    if (Test-Path (Join-Path $bundleCandidate ".agent-loops")) {
        return $bundleCandidate
    }

    return $candidate
}

function Convert-ToAbsolutePath([string]$PathValue, [string]$BasePath) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $PathValue
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return $PathValue
    }

    return Join-Path $BasePath $PathValue
}

function Get-RelativePathCompat([string]$BasePath, [string]$FullPath) {
    $baseFull = [System.IO.Path]::GetFullPath($BasePath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $targetFull = [System.IO.Path]::GetFullPath($FullPath)
    $baseUri = [System.Uri]::new($baseFull)
    $targetUri = [System.Uri]::new($targetFull)
    return [System.Uri]::UnescapeDataString($baseUri.MakeRelativeUri($targetUri).ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function New-MigrationKitBackup([string]$WorkspacePath, [string]$ProjectRoot) {
    $timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $backupRoot = Join-Path $WorkspacePath ".migration-kit/backups/$timestamp"
    New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null

    if (Test-Path $WorkspacePath) {
        $workspaceBackup = Join-Path $backupRoot "workspace"
        New-Item -ItemType Directory -Force -Path $workspaceBackup | Out-Null
        Get-ChildItem -Path $WorkspacePath -Force | Where-Object { $_.Name -ne ".migration-kit" } | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $workspaceBackup -Recurse -Force
        }
    }

    foreach ($name in @(".agent-loops", ".agent-state")) {
        $path = Join-Path $ProjectRoot $name
        if (Test-Path $path) {
            Copy-Item -Path $path -Destination (Join-Path $backupRoot $name) -Recurse -Force
        }
    }

    Write-Host "backup: $backupRoot"
}

function Write-TextFileSafe(
    [string]$DestinationPath,
    [string]$Content,
    [switch]$ForceWrite,
    [switch]$UpdateMode,
    [switch]$NeverOverwrite
) {
    $destinationDir = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null

    if (-not (Test-Path $DestinationPath)) {
        Set-Content -Path $DestinationPath -Value $Content -Encoding UTF8
        Write-Host "write: $DestinationPath"
        return
    }

    $existing = Get-Content -Raw -Path $DestinationPath
    if ($existing -eq $Content) {
        Write-Host "unchanged: $DestinationPath"
        return
    }

    if ($ForceWrite -and -not $NeverOverwrite) {
        Set-Content -Path $DestinationPath -Value $Content -Encoding UTF8
        Write-Host "overwrite: $DestinationPath"
        return
    }

    if ($UpdateMode) {
        $updatesRoot = Join-Path $script:workspacePath ".migration-kit/updates/$(Get-Date -Format 'yyyyMMdd-HHmmss')"
        $relative = Get-RelativePathCompat -BasePath $script:workspacePath -FullPath $DestinationPath
        if ($relative.StartsWith("..")) {
            $relative = (Split-Path $DestinationPath -Leaf)
        }
        $updatePath = Join-Path $updatesRoot ($relative + ".new")
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $updatePath) | Out-Null
        Set-Content -Path $updatePath -Value $Content -Encoding UTF8
        Write-Host "conflict -> new: $updatePath"
        return
    }

    Write-Host "skip existing: $DestinationPath"
}

function Set-TemplatedFile(
    [string]$SourcePath,
    [string]$DestinationPath,
    [hashtable]$Tokens,
    [switch]$ForceWrite,
    [switch]$UpdateMode,
    [switch]$NeverOverwrite
) {
    $content = Get-Content -Raw -Path $SourcePath
    foreach ($key in $Tokens.Keys) {
        $content = $content.Replace("{{$key}}", [string]$Tokens[$key])
    }

    Write-TextFileSafe -DestinationPath $DestinationPath -Content $content -ForceWrite:$ForceWrite -UpdateMode:$UpdateMode -NeverOverwrite:$NeverOverwrite
}

function Test-WorkspaceMutableFile([string]$RelativePath) {
    $normalized = $RelativePath.Replace('\\', '/')
    return (
        $normalized -eq "agent-state.md" -or
        $normalized -eq "current-ticket.md" -or
        $normalized -eq "profiles/adapter-config.json" -or
        $normalized.StartsWith("runs/") -or
        $normalized.StartsWith("reports/") -or
        $normalized.StartsWith("logs/") -or
        $normalized.StartsWith("state/run-ledger.md") -or
        $normalized.StartsWith("state/decision-log.md") -or
        $normalized.StartsWith("state/handoff.md")
    )
}

function Copy-DirectoryContents(
    [string]$SourceDirectory,
    [string]$DestinationDirectory,
    [switch]$ForceCopy,
    [switch]$UpdateMode
) {
    if (-not (Test-Path $SourceDirectory)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null

    Get-ChildItem -Path $SourceDirectory -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($SourceDirectory.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        $destination = Join-Path $DestinationDirectory $relative
        $neverOverwrite = Test-WorkspaceMutableFile $relative
        Set-TemplatedFile -SourcePath $_.FullName -DestinationPath $destination -Tokens $script:tokens -ForceWrite:$ForceCopy -UpdateMode:$UpdateMode -NeverOverwrite:$neverOverwrite
    }
}

function Copy-RootAgentDirectorySafe([string]$SourceDirectory, [string]$DestinationDirectory, [switch]$ForceCopy, [switch]$UpdateMode) {
    if (-not (Test-Path $SourceDirectory)) {
        return
    }

    New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
    Get-ChildItem -Path $SourceDirectory -Recurse -File | ForEach-Object {
        $relative = $_.FullName.Substring($SourceDirectory.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        $destination = Join-Path $DestinationDirectory $relative
        $content = Get-Content -Raw -Path $_.FullName
        Write-TextFileSafe -DestinationPath $destination -Content $content -ForceWrite:$ForceCopy -UpdateMode:$UpdateMode
    }
}

function Write-KitVersionFile([string]$WorkspacePath, [hashtable]$Tokens, [string]$Version, [bool]$UpdateMode) {
    $metadataDir = Join-Path $WorkspacePath ".migration-kit"
    New-Item -ItemType Directory -Force -Path $metadataDir | Out-Null
    $versionPath = Join-Path $metadataDir "version.json"

    $previous = $null
    if (Test-Path $versionPath) {
        try { $previous = Get-Content -Raw -Path $versionPath | ConvertFrom-Json } catch { $previous = $null }
    }

    $installedAt = if ($previous -and $previous.installedAtUtc) { [string]$previous.installedAtUtc } else { (Get-Date).ToUniversalTime().ToString("o") }
    $payload = [ordered]@{
        kitVersion = $Version
        installedAtUtc = $installedAt
        updatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        updateMode = $UpdateMode
        workspace = [string]$Tokens["WORKSPACE"]
        source = [string]$Tokens["SOURCE"]
        target = [string]$Tokens["TARGET"]
        config = [string]$Tokens["CONFIG"]
        output = [string]$Tokens["OUTPUT"]
        toolCommand = [string]$Tokens["TOOL"]
    }

    $json = $payload | ConvertTo-Json -Depth 5
    Set-Content -Path $versionPath -Value $json -Encoding UTF8
    Write-Host "write: $versionPath"
}

$kitRoot = Resolve-RepoRootFromScript
$projectRoot = (Get-Location).Path
$script:workspacePath = Convert-ToAbsolutePath $Workspace $projectRoot
$configPath = Convert-ToAbsolutePath $Config $projectRoot
$outputPath = Convert-ToAbsolutePath $Output $projectRoot
$templateRoot = Join-Path $kitRoot "templates/migration-kit"
$agentLoopsSource = Join-Path $kitRoot ".agent-loops"
$agentStateSource = Join-Path $kitRoot ".agent-state"
$codexTemplateSource = Join-Path $kitRoot "templates/codex"
$teamTemplateSource = Join-Path $kitRoot "templates/opencode-team"
$loopLibrarySource = Join-Path $kitRoot "templates/loops-library"

if (-not (Test-Path $templateRoot)) {
    throw "Migration kit templates were not found: $templateRoot"
}

$script:tokens = @{
    "SOURCE" = $Source
    "TARGET" = $Target
    "CONFIG" = $Config
    "OUTPUT" = $Output
    "WORKSPACE" = $Workspace
    "TOOL" = $ToolCommand
    "KIT_VERSION" = $KitVersion
}

Write-Host "Installing Selenium -> Playwright migration kit"
Write-Host "Kit version:  $KitVersion"
Write-Host "Mode:         $(if ($Update) { 'update' } else { 'install' })"
Write-Host "Kit root:     $kitRoot"
Write-Host "Project root: $projectRoot"
Write-Host "Workspace:    $script:workspacePath"
Write-Host "Config:       $configPath"
Write-Host "Output:       $outputPath"
Write-Host ""

if ($Backup -and (Test-Path $script:workspacePath)) {
    New-MigrationKitBackup -WorkspacePath $script:workspacePath -ProjectRoot $projectRoot
}

New-Item -ItemType Directory -Force -Path $script:workspacePath | Out-Null
foreach ($dir in @("runs", "reports", "logs", "profiles", "prompts", "schemas", "state", "tickets", "evidence", "scripts", "codex", ".migration-kit")) {
    New-Item -ItemType Directory -Force -Path (Join-Path $script:workspacePath $dir) | Out-Null
}

Copy-DirectoryContents -SourceDirectory $templateRoot -DestinationDirectory $script:workspacePath -ForceCopy:$Force -UpdateMode:$Update

$schemaSource = Join-Path $kitRoot "schemas/adapter-config.schema.json"
if (Test-Path $schemaSource) {
    $schemaDest = Join-Path $script:workspacePath "schemas/adapter-config.schema.json"
    $schemaContent = Get-Content -Raw -Path $schemaSource
    Write-TextFileSafe -DestinationPath $schemaDest -Content $schemaContent -ForceWrite:$Force -UpdateMode:$Update
}

if (-not $NoCodexFiles -and (Test-Path $codexTemplateSource)) {
    Copy-DirectoryContents -SourceDirectory $codexTemplateSource -DestinationDirectory (Join-Path $script:workspacePath "codex") -ForceCopy:$Force -UpdateMode:$Update
}

if ($WithTeam -and (Test-Path $teamTemplateSource)) {
    Copy-DirectoryContents -SourceDirectory $teamTemplateSource -DestinationDirectory (Join-Path $script:workspacePath "opencode-team") -ForceCopy:$Force -UpdateMode:$Update

    $agentsTemplate = Join-Path $teamTemplateSource "project-template/AGENTS.md"
    if (Test-Path $agentsTemplate) {
        Set-TemplatedFile -SourcePath $agentsTemplate -DestinationPath (Join-Path $projectRoot "AGENTS.md") -Tokens $script:tokens -ForceWrite:$Force -UpdateMode:$Update
    }
}

if ($WithLoopLibrary -and (Test-Path $loopLibrarySource)) {
    Copy-DirectoryContents -SourceDirectory $loopLibrarySource -DestinationDirectory (Join-Path $script:workspacePath "loops-library") -ForceCopy:$Force -UpdateMode:$Update
}

if (-not $NoRootAgentFiles) {
    if (Test-Path $agentLoopsSource) {
        Copy-RootAgentDirectorySafe -SourceDirectory $agentLoopsSource -DestinationDirectory (Join-Path $projectRoot ".agent-loops") -ForceCopy:$Force -UpdateMode:$Update
    }

    if (Test-Path $agentStateSource) {
        Copy-RootAgentDirectorySafe -SourceDirectory $agentStateSource -DestinationDirectory (Join-Path $projectRoot ".agent-state") -ForceCopy:$Force -UpdateMode:$Update
    }
}

if (-not $NoToolManifest) {
    $toolManifest = Join-Path $projectRoot ".config/dotnet-tools.json"
    if (-not (Test-Path $toolManifest)) {
        Write-Host "Creating local dotnet tool manifest..."
        dotnet new tool-manifest | Out-Host
    }
    else {
        Write-Host "dotnet tool manifest already exists: $toolManifest"
    }
}

$quickStart = Join-Path $script:workspacePath "QUICKSTART.md"
$quickStartLines = @(
    "# Quickstart",
    "",
    "Workspace installed at: `$Workspace`",
    "Kit version: $KitVersion",
    "",
    "## Install/update",
    "",
    "Initial install:",
    "",
    "```powershell",
    ".\tool\scripts\install-migration-kit.ps1 -Workspace `"$Workspace`" -Source `"$Source`" -Config `"$Config`" -Output `"$Output`" -ToolCommand `"$ToolCommand`"",
    "```",
    "",
    "Safe update without overwriting project-owned files:",
    "",
    "```powershell",
    ".\tool\scripts\install-migration-kit.ps1 -Workspace `"$Workspace`" -Update -Backup",
    "```",
    "",
    "## 1. Validate the environment",
    "",
    "```powershell",
    "$ToolCommand --mode doctor --input `"$Source`" --config `"$Config`" --out `"$Output`" --format both",
    "```",
    "",
    "If you installed the migrator as a local dotnet tool, use:",
    "",
    "```powershell",
    "dotnet tool run selenium-pw-migrator -- --mode doctor --input `"$Source`" --config `"$Config`" --out `"$Output`" --format both",
    "```",
    "",
    "## 2. Give this prompt to your agent",
    "",
    "```text",
    "$(Join-Path $Workspace 'prompts/kickoff-prompt.txt')",
    "```",
    "",
    "## 3. Resume after interruptions",
    "",
    "```text",
    "$(Join-Path $Workspace 'prompts/resume-prompt.txt')",
    "```",
    "",
    "## 4. Run one stateful loop batch",
    "",
    "```text",
    "$(Join-Path $Workspace 'prompts/loop-batch-prompt.txt')",
    "```",
    "",
    "## 5. Ask for next ticket",
    "",
    "```text",
    "$(Join-Path $Workspace 'prompts/next-ticket-prompt.txt')",
    "```",
    "",
    "## Optional: Codex bounded ticket",
    "",
    "```text",
    "Read $(Join-Path $Workspace 'codex/CODEX.md') and $(Join-Path $Workspace 'codex/prompts/ticket-fix-prompt.txt').",
    "Fix only the current ticket.",
    "```",
    "",
    "## Optional: install OpenCode team files",
    "",
    "```powershell",
    ".\tool\scripts\install-migration-kit.ps1 -Workspace `"$Workspace`" -Update -Backup -WithTeam",
    "```",
    "",
    "OpenCode global template will be copied to:",
    "",
    "```text",
    "$(Join-Path $Workspace 'opencode-team')",
    "```"
)
Write-TextFileSafe -DestinationPath $quickStart -Content ($quickStartLines -join [Environment]::NewLine) -ForceWrite:$Force -UpdateMode:$Update

Write-KitVersionFile -WorkspacePath $script:workspacePath -Tokens $script:tokens -Version $KitVersion -UpdateMode:$Update

Write-Host ""
Write-Host "Migration kit installed."
Write-Host "Next files to open:"
Write-Host "  $quickStart"
Write-Host "  $(Join-Path $script:workspacePath 'prompts/kickoff-prompt.txt')"
Write-Host ""
Write-Host "Recommended next command:"
Write-Host "  $ToolCommand --mode doctor --input `"$Source`" --config `"$Config`" --out `"$Output`" --format both"
