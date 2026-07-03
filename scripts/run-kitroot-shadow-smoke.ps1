param(
    [string]$WorkRoot = ".kitroot-smoke",
    [switch]$Clean
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$workRootPath = if ([System.IO.Path]::IsPathRooted($WorkRoot)) { $WorkRoot } else { Join-Path $repoRoot $WorkRoot }
$productRoot = Join-Path $workRootPath "product-repo"
$workspace = "migration"
$logDir = Join-Path $workRootPath "logs"
$logPath = Join-Path $logDir "kitroot-shadow-smoke.log"

if ($Clean -and (Test-Path $workRootPath)) {
    Write-Host "clean: $workRootPath"
    Remove-Item -Recurse -Force $workRootPath
}

New-Item -ItemType Directory -Force -Path $productRoot | Out-Null
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# This intentionally simulates a product repository that has its own templates/migration-kit folder.
# bootstrap-opencode must NOT use this folder as the migration kit root.
$productTemplateRoot = Join-Path $productRoot "templates/migration-kit"
New-Item -ItemType Directory -Force -Path $productTemplateRoot | Out-Null
Set-Content -Path (Join-Path $productTemplateRoot "README.md") -Encoding UTF8 -Value @"
# PRODUCT_SHADOW_TEMPLATE_DO_NOT_USE

This fixture must never be copied into the installed migration workspace.
"@

$sourceRoot = Join-Path $productRoot "LegacyTests"
New-Item -ItemType Directory -Force -Path $sourceRoot | Out-Null
Set-Content -Path (Join-Path $sourceRoot "Smoke.cs") -Encoding UTF8 -Value "// smoke source placeholder"

$projectPath = Join-Path $repoRoot "Migrator.Cli/Migrator.Cli.csproj"
$args = @(
    "run", "--project", $projectPath, "--",
    "kit", "bootstrap-opencode",
    "--workspace", $workspace,
    "--source", "./LegacyTests",
    "--opencode-install", "none"
)

Write-Host "Running kitroot shadow smoke from product repo: $productRoot"
Write-Host "RUN: dotnet $($args -join ' ')"

$process = Start-Process -FilePath "dotnet" -ArgumentList $args -WorkingDirectory $productRoot -NoNewWindow -PassThru -Wait -RedirectStandardOutput "$logPath.out" -RedirectStandardError "$logPath.err"
$output = (Get-Content "$logPath.out" -Raw -ErrorAction SilentlyContinue) + (Get-Content "$logPath.err" -Raw -ErrorAction SilentlyContinue)
Set-Content -Path $logPath -Encoding UTF8 -Value $output
Write-Host $output

if ($process.ExitCode -ne 0) {
    throw "bootstrap-opencode failed with exit code $($process.ExitCode). Report: $logPath"
}

$normalizedProductRoot = [System.IO.Path]::GetFullPath($productRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
if ($output -match [regex]::Escape("Kit root:     $normalizedProductRoot")) {
    throw "bootstrap-opencode used product repo templates as Kit root. This would shadow bundled templates. Report: $logPath"
}

$workspacePath = Join-Path $productRoot $workspace
$required = @(
    "AGENT_CONTRACT.md",
    "harness/README.md",
    "state/harness-policy.json",
    "scripts/check-harness-policy.ps1",
    "scripts/new-harness-run.ps1",
    "scripts/write-harness-event.ps1",
    "scripts/build-harness-dashboard.ps1",
    "dashboard/i18n/en.json",
    "dashboard/i18n/ru.json",
    "opencode-team/global/.config/opencode/opencode.jsonc"
)

$missing = @()
foreach ($relative in $required) {
    if (-not (Test-Path (Join-Path $workspacePath $relative))) {
        $missing += $relative
    }
}

if ($missing.Count -gt 0) {
    throw "bootstrap-opencode did not install required bundled kit files: $($missing -join ', '). Report: $logPath"
}

$installedReadme = Join-Path $workspacePath "README.md"
if ((Test-Path $installedReadme) -and ((Get-Content $installedReadme -Raw) -match "PRODUCT_SHADOW_TEMPLATE_DO_NOT_USE")) {
    throw "Product shadow template was copied into migration workspace. Report: $logPath"
}

Write-Host "KITROOT_SHADOW_SMOKE_PASS"
Write-Host "Workspace: $workspacePath"
Write-Host "Log:       $logPath"
