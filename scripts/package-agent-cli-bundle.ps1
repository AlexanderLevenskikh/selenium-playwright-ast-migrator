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
$outputRoot = Join-Path $root $Output
$publishDir = Join-Path $outputRoot "publish"
$bundleDir = Join-Path $outputRoot "tool"
$docsDir = Join-Path $bundleDir "docs"
$schemasDir = Join-Path $bundleDir "schemas"

if ($RunTests) {
    Write-Host "Running tests before packaging..."
    dotnet test (Join-Path $root "Migrator.sln") -c $Configuration --no-restore
}

Remove-Item -Recurse -Force $outputRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $publishDir, $bundleDir, $docsDir, $schemasDir | Out-Null

$selfContainedValue = (-not $NoSelfContained).ToString().ToLowerInvariant()

Write-Host "Publishing CLI as single-file executable..."
Write-Host "Runtime: $Runtime"
Write-Host "SelfContained: $selfContainedValue"

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained $selfContainedValue `
    /p:PublishSingleFile=false `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    -o $publishDir

$publishedExe = Get-ChildItem $publishDir -Filter "*.exe" | Select-Object -First 1
if (-not $publishedExe) {
    throw "Published executable was not found in $publishDir"
}

$targetExe = Join-Path $bundleDir "$ToolName.exe"
Copy-Item $publishedExe.FullName $targetExe -Force

$rootDocs = @(
    "README.md",
    "README.ru.md",
    "FIRST_AGENT_PROMPT_TEMPLATE.md"
)

$docFiles = @(
    "docs/agent-tool-boundary.md",
    "docs/migration-safety-playbook.md",
    "docs/agent-command-set.md",
    "docs/agent-safety.md",
    "docs/agent-first-checklist.md",
    "docs/config-driven-recognizers.md",
    "docs/config-layering.md",
    "docs/project-verification.md",
    "docs/explain-todo.md",
    "docs/wait-policy.md",
    "docs/user-guide/quick-start.ru.md",
    "docs/user-guide/common-recipes.ru.md",
    "docs/user-guide/reports-and-quality-gates.ru.md"
)

foreach ($file in $rootDocs) {
    $source = Join-Path $root $file
    if (Test-Path $source) {
        Copy-Item $source (Join-Path $bundleDir (Split-Path $file -Leaf)) -Force
    }
}

foreach ($file in $docFiles) {
    $source = Join-Path $root $file
    if (Test-Path $source) {
        $relativeDir = Split-Path $file -Parent
        $targetDir = Join-Path $bundleDir $relativeDir
        New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        Copy-Item $source (Join-Path $bundleDir $file) -Force
    }
}

$schema = Join-Path $root "schemas\adapter-config.schema.json"
if (Test-Path $schema) {
    Copy-Item $schema (Join-Path $schemasDir "adapter-config.schema.json") -Force
}

$readmeAgent = @"
# AST Migrator CLI bundle for agents

This folder contains the compiled AST Migrator CLI and documentation for config-driven migration agents.

## Important boundary

The migrator is provided as a compiled CLI tool. The agent must not search for or edit migrator C# source code.

Allowed agent work:

- edit migration/profiles/*.adapter.json;
- edit migration/migration-progress.md;
- edit migration/migrator-tickets.md;
- create migration/run-* outputs;
- run this CLI.

Forbidden:

- edit source Selenium project;
- edit generated .cs files as final solution;
- edit migrator C# code;
- suppress business logic blindly.

Read first:

1. docs/agent-tool-boundary.md
2. docs/migration-safety-playbook.md
3. FIRST_AGENT_PROMPT_TEMPLATE.md
4. schemas/adapter-config.schema.json

## Example

```powershell
.\migrator.exe --mode config-validate --config "<target>\migration\profiles\project.adapter.json" --out "<target>\migration\config-validate"

.\migrator.exe --mode migrate --input "<source-selenium-tests>" --config "<target>\migration\profiles\project.adapter.json" --out "<target>\migration\run-001"
```

If a core limitation is found, create a ticket in migration/migrator-tickets.md instead of editing migrator source code.
"@

Set-Content -Path (Join-Path $bundleDir "README_AGENT_TOOL.md") -Value $readmeAgent -Encoding UTF8

$runTemplate = @'
param(
    [Parameter(Mandatory=$true)]
    [ValidateSet("doctor", "config-validate", "migrate", "verify", "verify-project", "explain-todo", "guard", "config-diff", "migration-board", "smoke-plan", "runtime-classify", "profile-match")]
    [string]$Mode,

    [Parameter(Mandatory=$true)]
    [string]$Input,

    [Parameter(Mandatory=$true)]
    [string]$Config,

    [Parameter(Mandatory=$true)]
    [string]$Out,

    [string]$Before,
    [string]$After,
    [string]$Format = "both"
)

$ErrorActionPreference = "Stop"
$tool = Join-Path $PSScriptRoot "migrator.exe"

$args = @("--mode", $Mode, "--format", $Format)

if ($Mode -eq "guard") {
    if (-not $Before -or -not $After) {
        throw "guard mode requires -Before and -After"
    }
    $args += @("--before", $Before, "--after", $After, "--out", $Out)
}
elseif ($Mode -eq "config-diff") {
    if (-not $Before -or -not $After) {
        throw "config-diff mode requires -Before and -After"
    }
    $args += @("--before", $Before, "--after", $After, "--out", $Out)
}
else {
    $args += @("--input", $Input, "--config", $Config, "--out", $Out)
}

& $tool @args
exit $LASTEXITCODE
'@

Set-Content -Path (Join-Path $bundleDir "run-migrator-template.ps1") -Value $runTemplate -Encoding UTF8

Write-Host "Agent CLI bundle created: $bundleDir"
Write-Host "Executable: $targetExe"
Write-Host "Give this folder to migration agents instead of migrator source code."
