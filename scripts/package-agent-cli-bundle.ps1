param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Output = "artifacts/agent-cli-bundle",
    [string]$ToolName = "migrator",
    [switch]$RunTests,
    [switch]$NoSelfContained
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "Migrator.Cli\Migrator.Cli.csproj"
$outputRoot = if ([System.IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $root $Output }
$publishDir = Join-Path $outputRoot "publish"
$bundleDir = Join-Path $outputRoot "tool"
$docsDir = Join-Path $bundleDir "docs"
$schemasDir = Join-Path $bundleDir "schemas"
$templatesDir = Join-Path $bundleDir "templates"
$scriptsDir = Join-Path $bundleDir "scripts"

Write-Host "Packaging AST Migrator CLI bundle"
Write-Host "Repo root: $root"
Write-Host "Project:   $project"
Write-Host "Runtime:   $Runtime"
Write-Host "Output:    $outputRoot"
Write-Host ""

if (-not (Test-Path $project)) {
    throw "Migrator.Cli project was not found: $project"
}

if ($RunTests) {
    Write-Host "Running tests before packaging..."
    dotnet test (Join-Path $root "Migrator.sln") -c $Configuration --no-restore
}

Remove-Item -Recurse -Force $outputRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null
New-Item -ItemType Directory -Force -Path $bundleDir | Out-Null
New-Item -ItemType Directory -Force -Path $docsDir | Out-Null
New-Item -ItemType Directory -Force -Path $schemasDir | Out-Null
New-Item -ItemType Directory -Force -Path $templatesDir | Out-Null
New-Item -ItemType Directory -Force -Path $scriptsDir | Out-Null

$selfContainedValue = (-not $NoSelfContained).ToString().ToLowerInvariant()

Write-Host "Publishing CLI as regular folder, not single-file..."
Write-Host "Runtime: $Runtime"
Write-Host "SelfContained: $selfContainedValue"

# Important: do NOT publish as a single-file executable.
# Roslyn-based tools need real assembly files next to the executable.
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContainedValue `
    /p:PublishSingleFile=false `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    -o $publishDir

Write-Host "Copying full publish output..."
Copy-Item -Path (Join-Path $publishDir "*") -Destination $bundleDir -Recurse -Force

$publishedExe = Join-Path $bundleDir "Migrator.Cli.exe"
$targetExe = Join-Path $bundleDir "$ToolName.exe"

if (Test-Path $publishedExe) {
    Copy-Item $publishedExe $targetExe -Force
}
elseif (-not $NoSelfContained) {
    throw "Published executable was not found: $publishedExe"
}

Write-Host "Copying docs/schema/templates..."

$schema = Join-Path $root "schemas\adapter-config.schema.json"
if (Test-Path $schema) {
    Copy-Item $schema (Join-Path $schemasDir "adapter-config.schema.json") -Force
}

$docsSource = Join-Path $root "docs"
if (Test-Path $docsSource) {
    Copy-Item -Path (Join-Path $docsSource "*") -Destination $docsDir -Recurse -Force
}

$rootFiles = @(
    "README.md",
    "README.ru.md",
    "AGENTS.md"
)

foreach ($file in $rootFiles) {
    $source = Join-Path $root $file
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $bundleDir (Split-Path $file -Leaf)) -Force
    }
}

$agentLoopsSource = Join-Path $root ".agent-loops"
if (Test-Path $agentLoopsSource) {
    Copy-Item -Path $agentLoopsSource -Destination (Join-Path $bundleDir ".agent-loops") -Recurse -Force
}

$templatesSource = Join-Path $root "templates"
if (Test-Path $templatesSource) {
    Copy-Item -Path (Join-Path $templatesSource "*") -Destination $templatesDir -Recurse -Force
}

$installMigrationKit = Join-Path $root "scripts\install-migration-kit.ps1"
if (Test-Path $installMigrationKit) {
    Copy-Item $installMigrationKit (Join-Path $scriptsDir "install-migration-kit.ps1") -Force
}

$runTemplatePath = Join-Path $templatesDir "run-migrator-template.ps1"
$runTemplateLines = @(
    'param(',
    '    [Parameter(Mandatory=$true)]',
    '    [ValidateSet("doctor", "config-validate", "migrate", "verify", "verify-project", "explain-todo", "guard", "config-diff", "migration-board", "smoke-plan", "runtime-classify", "profile-match")]',
    '    [string]$Mode,',
    '',
    '    [Parameter(Mandatory=$true)]',
    '    [string]$Input,',
    '',
    '    [Parameter(Mandatory=$true)]',
    '    [string]$Config,',
    '',
    '    [Parameter(Mandatory=$true)]',
    '    [string]$Out,',
    '',
    '    [string]$Before,',
    '    [string]$After,',
    '    [string]$Format = "both"',
    ')',
    '',
    '$ErrorActionPreference = "Stop"',
    '$tool = Join-Path $PSScriptRoot "..\migrator.exe"',
    '',
    '$args = @("--mode", $Mode, "--format", $Format)',
    '',
    'if ($Mode -eq "guard") {',
    '    if (-not $Before -or -not $After) { throw "guard mode requires -Before and -After" }',
    '    $args += @("--before", $Before, "--after", $After, "--out", $Out)',
    '}',
    'elseif ($Mode -eq "config-diff") {',
    '    if (-not $Before -or -not $After) { throw "config-diff mode requires -Before and -After" }',
    '    $args += @("--before", $Before, "--after", $After, "--out", $Out)',
    '}',
    'else {',
    '    $args += @("--input", $Input, "--config", $Config, "--out", $Out)',
    '}',
    '',
    '& $tool @args',
    'exit $LASTEXITCODE'
)
Set-Content -Path $runTemplatePath -Value $runTemplateLines -Encoding UTF8

$readmeAgent = Join-Path $bundleDir "README_AGENT_TOOL.md"
$readmeLines = @(
    '# AST Migrator CLI bundle for agents',
    '',
    'This folder contains the compiled AST Migrator CLI and documentation for config-driven migration agents.',
    '',
    'Important boundary:',
    '  The migrator is provided as a compiled CLI tool.',
    '  The agent must not search for or edit migrator C# source code.',
    '',
    'Run:',
    '  .\migrator.exe --help',
    '  .\Migrator.Cli.exe --help',
    '',
    'Do not move migrator.exe out of this folder.',
    'The executable depends on the DLL files published next to it.',
    '',
    'Allowed agent work:',
    '  edit migration/profiles/*.adapter.json',
    '  edit migration/migration-progress.md',
    '  edit migration/pom-recovery.md',
    '  edit migration/migrator-tickets.md',
    '  create migration/run-* outputs',
    '  run this CLI',
    '',
    'Forbidden:',
    '  edit source Selenium project',
    '  edit generated .cs files as final solution',
    '  edit migrator C# code',
    '  suppress business logic blindly',
    '  add broad POM suppressions without POM recovery',
    '',
    'Quick install into a migration project:',
    '  scripts/install-migration-kit.ps1 -Workspace migration -Source <selenium-tests> -Config migration/profiles/adapter-config.json',
    '',
    'Safe update of an existing migration workspace:',
    '  scripts/install-migration-kit.ps1 -Workspace migration -Update -Backup',
    '',
    'Read first:',
    '  docs/agent-tool-boundary.md',
    '  docs/autopilot-loop.md',
    '  docs/pom-recovery-policy.md',
    '  .agent-loops/kickoff-prompt.txt',
    '  templates/migration-kit/prompts/loop-batch-prompt.txt',
    '  schemas/adapter-config.schema.json',
    '',
    'Windows PowerShell execution policy:',
    '  Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass',
    '',
    'This affects only the current PowerShell session.'
)
Set-Content -Path $readmeAgent -Value $readmeLines -Encoding UTF8

Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Agent CLI bundle created: $bundleDir"
Write-Host "Executable: $targetExe"
Write-Host ""
Write-Host "IMPORTANT:"
Write-Host "  Give the agent the whole 'tool' folder, not just $ToolName.exe."
Write-Host "  The executable needs the published DLL files next to it."
Write-Host ""
Write-Host "Tool folder preview:"
Get-ChildItem $bundleDir | Select-Object -First 30 Name, Length | Format-Table -AutoSize
