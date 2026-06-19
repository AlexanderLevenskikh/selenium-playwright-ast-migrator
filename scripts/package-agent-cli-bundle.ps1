param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Output = "artifacts\agent-cli-bundle",
    [switch]$FrameworkDependent
)

$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir

$ProjectPath = Join-Path $RepoRoot "Migrator.Cli\Migrator.Cli.csproj"
$OutputRoot = if ([System.IO.Path]::IsPathRooted($Output)) {
    $Output
} else {
    Join-Path $RepoRoot $Output
}

$ToolDir = Join-Path $OutputRoot "tool"
$DocsDir = Join-Path $ToolDir "docs"
$SchemasDir = Join-Path $ToolDir "schemas"
$TemplatesDir = Join-Path $ToolDir "templates"
$TempPublishDir = Join-Path $RepoRoot "artifacts\.tmp-agent-cli-publish"

Write-Host "Packaging AST Migrator CLI bundle"
Write-Host "Repo root: $RepoRoot"
Write-Host "Project:   $ProjectPath"
Write-Host "Runtime:   $Runtime"
Write-Host "Output:    $OutputRoot"
Write-Host ""

if (-not (Test-Path $ProjectPath)) {
    throw "Migrator.Cli project was not found: $ProjectPath"
}

Remove-Item -Recurse -Force $TempPublishDir -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force $ToolDir -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Force $TempPublishDir | Out-Null
New-Item -ItemType Directory -Force $ToolDir | Out-Null
New-Item -ItemType Directory -Force $DocsDir | Out-Null
New-Item -ItemType Directory -Force $SchemasDir | Out-Null
New-Item -ItemType Directory -Force $TemplatesDir | Out-Null

$selfContainedValue = if ($FrameworkDependent) { "false" } else { "true" }

Write-Host "Publishing CLI..."

dotnet publish $ProjectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContainedValue `
    /p:PublishSingleFile=false `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    -o $TempPublishDir

Write-Host ""
Write-Host "Copying full publish output to tool folder..."

# Important: copy the whole publish directory, not only the exe.
# Roslyn-based tools need real assembly files next to the executable.
Copy-Item -Path (Join-Path $TempPublishDir "*") -Destination $ToolDir -Recurse -Force

$PublishedExe = Join-Path $ToolDir "Migrator.Cli.exe"
$FriendlyExe = Join-Path $ToolDir "migrator.exe"

if (Test-Path $PublishedExe) {
    Copy-Item $PublishedExe $FriendlyExe -Force
} elseif (-not $FrameworkDependent) {
    throw "Published executable was not found: $PublishedExe"
}

Write-Host "Copying docs/schema/templates..."

$SchemaPath = Join-Path $RepoRoot "schemas\adapter-config.schema.json"
if (Test-Path $SchemaPath) {
    Copy-Item $SchemaPath $SchemasDir -Force
}

$DocsSource = Join-Path $RepoRoot "docs"
if (Test-Path $DocsSource) {
    Copy-Item -Path (Join-Path $DocsSource "*") -Destination $DocsDir -Recurse -Force
}

$ReadmePath = Join-Path $RepoRoot "README.md"
if (Test-Path $ReadmePath) {
    Copy-Item $ReadmePath (Join-Path $ToolDir "README.md") -Force
}

$AgentsPath = Join-Path $RepoRoot "AGENTS.md"
if (Test-Path $AgentsPath) {
    Copy-Item $AgentsPath (Join-Path $ToolDir "AGENTS.md") -Force
}

$FirstPromptTemplate = Join-Path $RepoRoot "FIRST_AGENT_PROMPT_TEMPLATE.md"
if (Test-Path $FirstPromptTemplate) {
    Copy-Item $FirstPromptTemplate $TemplatesDir -Force
}

$RunMigratorTemplate = Join-Path $RepoRoot "run-migrator-template.ps1"
if (Test-Path $RunMigratorTemplate) {
    Copy-Item $RunMigratorTemplate $TemplatesDir -Force
}

$AgentReadme = Join-Path $ToolDir "README_AGENT_TOOL.md"

# Keep this generated README intentionally simple ASCII.
# This avoids PowerShell parsing/encoding issues on Windows consoles.
$AgentReadmeLines = @(
    '# AST Migrator Agent CLI Bundle',
    '',
    'This folder is intended to be given to a migration agent instead of the migrator source code.',
    '',
    'Use the migrator as a black-box CLI tool.',
    '',
    'Executable:',
    '  .\migrator.exe --help',
    '  .\Migrator.Cli.exe --help',
    '',
    'Do not move migrator.exe out of this folder.',
    'The executable depends on the DLL files published next to it.',
    '',
    'Important rule:',
    '  The migration agent must not edit migrator C# source code.',
    '',
    'If a core migrator limitation is found, create a ticket in:',
    '  migration\migrator-tickets.md',
    '',
    'Included files:',
    '  migrator.exe',
    '  Migrator.Cli.exe',
    '  required DLL dependencies',
    '  schemas\adapter-config.schema.json',
    '  docs\',
    '  templates\',
    '',
    'Windows PowerShell execution policy:',
    '  Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass',
    '',
    'This affects only the current PowerShell session.'
)

Set-Content -Path $AgentReadme -Value $AgentReadmeLines -Encoding UTF8

Remove-Item -Recurse -Force $TempPublishDir -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Bundle created successfully:"
Write-Host "  $ToolDir"
Write-Host ""
Write-Host "Tool folder contents:"
Get-ChildItem $ToolDir |
    Select-Object -First 30 Name, Length |
    Format-Table -AutoSize

Write-Host ""
Write-Host "IMPORTANT:"
Write-Host "  Give the agent the whole 'tool' folder, not just migrator.exe."
Write-Host "  The executable needs the published DLL files next to it."
