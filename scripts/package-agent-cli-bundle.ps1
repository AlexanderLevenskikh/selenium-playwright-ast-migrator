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
$project = Join-Path (Join-Path $root "Migrator.Cli") "Migrator.Cli.csproj"
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
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed with exit code $LASTEXITCODE"
    }
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
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

Write-Host "Copying full publish output..."
Copy-Item -Path (Join-Path $publishDir "*") -Destination $bundleDir -Recurse -Force

$publishedExe = Join-Path $bundleDir "Migrator.Cli.exe"
$publishedAppHost = Join-Path $bundleDir "Migrator.Cli"
$targetExe = Join-Path $bundleDir "$ToolName.exe"
$targetAppHost = Join-Path $bundleDir $ToolName
$displayExecutable = $targetExe

if (Test-Path $publishedExe) {
    Copy-Item $publishedExe $targetExe -Force
    $displayExecutable = $targetExe
}
elseif (Test-Path $publishedAppHost) {
    Copy-Item $publishedAppHost $targetAppHost -Force
    $displayExecutable = $targetAppHost
}
elseif (-not $NoSelfContained) {
    throw "Published executable was not found: $publishedExe or $publishedAppHost"
}
else {
    $displayExecutable = Join-Path $bundleDir "Migrator.Cli.dll"
}

Write-Host "Copying docs/schema/templates..."

$schema = Join-Path (Join-Path $root "schemas") "adapter-config.schema.json"
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


$templatesSource = Join-Path $root "templates"
if (Test-Path $templatesSource) {
    Copy-Item -Path (Join-Path $templatesSource "*") -Destination $templatesDir -Recurse -Force
}

$installMigrationKit = Join-Path (Join-Path $root "scripts") "install-migration-kit.ps1"
if (Test-Path $installMigrationKit) {
    Copy-Item $installMigrationKit (Join-Path $scriptsDir "install-migration-kit.ps1") -Force
}

$dogfoodSmoke = Join-Path (Join-Path $root "scripts") "run-harness-dogfood-smoke.ps1"
if (Test-Path $dogfoodSmoke) {
    Copy-Item $dogfoodSmoke (Join-Path $scriptsDir "run-harness-dogfood-smoke.ps1") -Force
}

$dogfoodSmokeSh = Join-Path (Join-Path $root "scripts") "run-harness-dogfood-smoke.sh"
if (Test-Path $dogfoodSmokeSh) {
    Copy-Item $dogfoodSmokeSh (Join-Path $scriptsDir "run-harness-dogfood-smoke.sh") -Force
}

$dashboardSmoke = Join-Path (Join-Path $root "scripts") "run-harness-dashboard-smoke.ps1"
if (Test-Path $dashboardSmoke) {
    Copy-Item $dashboardSmoke (Join-Path $scriptsDir "run-harness-dashboard-smoke.ps1") -Force
}

$dashboardSmokeSh = Join-Path (Join-Path $root "scripts") "run-harness-dashboard-smoke.sh"
if (Test-Path $dashboardSmokeSh) {
    Copy-Item $dashboardSmokeSh (Join-Path $scriptsDir "run-harness-dashboard-smoke.sh") -Force
}

$installMigrationKitSh = Join-Path (Join-Path $root "scripts") "install-migration-kit.sh"
if (Test-Path $installMigrationKitSh) {
    Copy-Item $installMigrationKitSh (Join-Path $scriptsDir "install-migration-kit.sh") -Force
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
    '  Windows self-contained: .\migrator.exe --help',
    '  Linux/macOS self-contained: ./migrator --help',
    '  Framework-dependent bundle: dotnet Migrator.Cli.dll --help',
    '',
    'Do not move the executable or Migrator.Cli.dll out of this folder.',
    'The CLI depends on the DLL files published next to it.',
    '',
    'Allowed agent work:',
    '  edit migration/profiles/*.adapter.json',
    '  edit migration/migration-progress.md',
    '  edit migration/pom-recovery.md',
    '  edit migration/migrator-tickets.md',
    '  create migration/run-* outputs',
    '  create migration/runs/<run-id>/ harness artifacts',
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
    '  .\migrator.exe kit init --workspace migration --source <selenium-tests> --config migration/profiles/adapter-config.json',
    '  ./migrator kit init --workspace migration --source <selenium-tests> --config migration/profiles/adapter-config.json',
    '  dotnet Migrator.Cli.dll kit init --workspace migration --source <selenium-tests> --config migration/profiles/adapter-config.json',
    '  scripts/install-migration-kit.ps1 -Workspace migration -Source <selenium-tests> -Config migration/profiles/adapter-config.json',
    '  scripts/install-migration-kit.sh --workspace migration --source <selenium-tests> --config migration/profiles/adapter-config.json',
    '',
    'Safe update of an existing migration workspace:',
    '  .\migrator.exe kit update --workspace migration --backup',
    '  scripts/install-migration-kit.ps1 -Workspace migration -Update -Backup',
    '  scripts/install-migration-kit.sh --workspace migration --update --backup',
    '',
    'Optional Codex bounded-ticket prompts:',
    '  templates/codex/CODEX.md (after install: migration/codex/CODEX.md)',
    '',
    'Optional OpenCode team template:',
    '  scripts/install-migration-kit.ps1 -Workspace migration -Update -Backup -WithTeam',
    '  Then read migration/opencode-team/README.md',
    '',
    'Refresh migration kit:',
    '  scripts/install-migration-kit.ps1 -Workspace migration -Update -Backup',
    '',
    'Harness autopilot kit:',
    '  templates/migration-kit/harness/README.md',
    '  templates/migration-kit/agent-skills/skill-map.md',
    '  templates/migration-kit/agent-skills/manifest.json',
    '  templates/migration-kit/state/harness-policy.json',
    '  templates/migration-kit/scripts/new-harness-run.ps1 / new-harness-run.sh',
    '  templates/migration-kit/scripts/check-harness-policy.ps1 / check-harness-policy.sh',
    '  templates/migration-kit/scripts/check-final-gate.ps1 / check-final-gate.sh',
    '  templates/migration-kit/scripts/build-harness-dashboard.ps1 / build-harness-dashboard.sh',
    '  templates/migration-kit/scripts/write-agent-skill-usage.ps1 / write-agent-skill-usage.sh',
    '  templates/migration-kit/scripts/record-agent-skill-profile.ps1 / record-agent-skill-profile.sh',
    '  templates/migration-kit/scripts/slice-gate-followups.ps1 / slice-gate-followups.sh',
    '  templates/migration-kit/dashboard/i18n/en.json',
    '  templates/migration-kit/dashboard/i18n/ru.json',
    '  docs/migrator-agent-harness-dogfood.md',
    '  docs/migrator-agent-harness-dashboard.md',
    '  scripts/run-harness-dogfood-smoke.ps1',
    '  scripts/run-harness-dogfood-smoke.sh',
    '  docs/migrator-agent-harness-dashboard.md',
    '  scripts/run-harness-dashboard-smoke.ps1',
    '  scripts/run-harness-dashboard-smoke.sh',
    '',
    'Read first:',
    '  docs/guarded-opencode-desktop-runbook.ru.md',
    '  docs/pom-recovery-policy.md',
    '  templates/migration-kit/prompts/kickoff-prompt.txt',
    '  templates/migration-kit/prompts/loop-batch-prompt.txt',
    '  templates/migration-kit/prompts/autopilot-loop-prompt.txt',
    '  templates/migration-kit/agent-skills/plow-ahead/SKILL.md',
    '  templates/migration-kit/scripts/write-agent-skill-usage.ps1',
    '  templates/migration-kit/scripts/record-agent-skill-profile.ps1',
    '  templates/migration-kit/scripts/slice-gate-followups.ps1',
    '  templates/migration-kit/harness/README.md',
    '  templates/migration-kit/agent-skills/skill-map.md',
    '  docs/migrator-agent-harness-dogfood.md',
    '  docs/migrator-agent-harness-dashboard.md',
    '  schemas/adapter-config.schema.json',
    '',
    'Windows PowerShell execution policy:',
    '  Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass',
    '',
    'This affects only the current PowerShell session.'
)
Set-Content -Path $readmeAgent -Value $readmeLines -Encoding UTF8

function Get-RelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$BasePath,

        [Parameter(Mandatory = $true)]
        [string]$TargetPath
    )

    $base = (Resolve-Path $BasePath).Path.TrimEnd('\','/')
    $target = (Resolve-Path $TargetPath).Path

    if ($target.StartsWith($base, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $target.Substring($base.Length).TrimStart('\','/').Replace('\','/')
    }

    $baseUri = New-Object System.Uri(($base + [System.IO.Path]::DirectorySeparatorChar))
    $targetUri = New-Object System.Uri($target)

    return [System.Uri]::UnescapeDataString(
        $baseUri.MakeRelativeUri($targetUri).ToString()
    ).Replace('\','/').Replace('/','/')
}

Write-Host "Generating bundle manifest and checksums..."
$manifestShaPath = Join-Path $bundleDir "MANIFEST.sha256"
$manifestJsonPath = Join-Path $bundleDir "manifest.json"

$bundleFiles = Get-ChildItem $bundleDir -File -Recurse |
    Where-Object { $_.FullName -ne $manifestShaPath -and $_.FullName -ne $manifestJsonPath } |
    Sort-Object FullName

$shaLines = New-Object System.Collections.Generic.List[string]
$jsonFiles = @()
foreach ($file in $bundleFiles) {
    $relative = Get-RelativePath `
        -BasePath $bundleDir `
        -TargetPath $file.FullName

    $hash = (Get-FileHash -Algorithm SHA256 -Path $file.FullName).Hash.ToLowerInvariant()
    $shaLines.Add("$hash  $relative")
    $jsonFiles += [pscustomobject]@{
        path = $relative
        sizeBytes = $file.Length
        sha256 = $hash
    }
}

Set-Content -Path $manifestShaPath -Value $shaLines -Encoding UTF8
$manifest = [pscustomobject]@{
    schemaVersion = 1
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    runtime = $Runtime
    configuration = $Configuration
    toolName = $ToolName
    selfContained = (-not $NoSelfContained)
    files = $jsonFiles
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestJsonPath -Encoding UTF8

Remove-Item -Recurse -Force $publishDir -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "Agent CLI bundle created: $bundleDir"
Write-Host "Executable: $displayExecutable"
Write-Host ""
Write-Host "IMPORTANT:"
Write-Host "  Give the agent the whole 'tool' folder, not just $ToolName.exe."
Write-Host "  The executable needs the published DLL files next to it."
Write-Host ""
Write-Host "Tool folder preview:"
Get-ChildItem $bundleDir | Select-Object -First 30 Name, Length | Format-Table -AutoSize
