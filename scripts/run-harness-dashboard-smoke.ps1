param(
    [switch]$Clean,
    [ValidateSet("en", "ru")]
    [string]$Language = "en"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$workspace = Join-Path $repoRoot ".dogfood/migration"
$dogfoodScript = Join-Path $repoRoot "scripts/run-harness-dogfood-smoke.ps1"

if ($Clean -or -not (Test-Path (Join-Path $workspace "state/harness-run.json"))) {
    if (-not (Test-Path $dogfoodScript)) { throw "Dogfood smoke script was not found: $dogfoodScript" }
    & $dogfoodScript -Clean:$Clean
    if ($LASTEXITCODE -ne 0) { throw "run-harness-dogfood-smoke.ps1 failed with exit code $LASTEXITCODE" }
}

$dashboardScript = Join-Path $workspace "scripts/build-harness-dashboard.ps1"
if (-not (Test-Path $dashboardScript)) {
    throw "Installed dashboard script was not found: $dashboardScript"
}

& $dashboardScript -Workspace $workspace -Out "dashboard/harness" -Language $Language
if ($LASTEXITCODE -ne 0) { throw "build-harness-dashboard.ps1 failed with exit code $LASTEXITCODE" }

$outDir = Join-Path $workspace "dashboard/harness"
$html = Join-Path $outDir "index.html"
$json = Join-Path $outDir "harness-dashboard.json"
$md = Join-Path $outDir "harness-dashboard.md"
$en = Join-Path $workspace "dashboard/i18n/en.json"
$ru = Join-Path $workspace "dashboard/i18n/ru.json"

foreach ($required in @($html, $json, $md, $en, $ru)) {
    if (-not (Test-Path $required)) { throw "Expected dashboard artifact was not found: $required" }
}

$htmlText = Get-Content -Raw -Path $html
foreach ($marker in @("languageSelect", "English", "Русский", "harness-dashboard.json", "Migration Progress", "draftCoveragePercent", "data-hint", "What is happening now", "processGuide", "previewDetails")) {
    if (-not $htmlText.Contains($marker)) { throw "Dashboard HTML is missing marker: $marker" }
}

$evidenceDir = Join-Path $workspace "evidence"
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null
$evidence = Join-Path $evidenceDir "harness-dashboard-smoke.md"
@(
    "# Harness Dashboard Smoke Evidence",
    "",
    "Status: PASS",
    "Workspace: .dogfood/migration",
    "Dashboard: dashboard/harness/index.html",
    "Default language: $Language",
    "Generated at UTC: $((Get-Date).ToUniversalTime().ToString('o'))",
    "",
    "Checked artifacts:",
    "",
    "- dashboard/harness/index.html",
    "- dashboard/harness/harness-dashboard.json",
    "- dashboard/harness/harness-dashboard.md",
    "- dashboard/i18n/en.json",
    "- dashboard/i18n/ru.json"
) | Set-Content -Path $evidence -Encoding UTF8

Write-Host "HARNESS_DASHBOARD_SMOKE_PASS"
Write-Host "Dashboard: $html"
Write-Host "Evidence: $evidence"
