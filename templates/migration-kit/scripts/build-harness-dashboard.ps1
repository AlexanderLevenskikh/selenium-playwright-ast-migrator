# build-harness-dashboard.ps1
param(
    [string]$Workspace = "migration",
    [string]$Out = "dashboard/harness",
    [ValidateSet("en", "ru")]
    [string]$Language = "en"
)

$ErrorActionPreference = "Stop"

function Resolve-FromRoot {
    param([string]$PathValue, [string]$BasePath)
    if ([System.IO.Path]::IsPathRooted($PathValue)) { return [System.IO.Path]::GetFullPath($PathValue) }
    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $PathValue))
}

function Read-JsonFileOrNull {
    param([string]$PathValue)
    if (-not (Test-Path $PathValue)) { return $null }
    $raw = Get-Content -Raw -Path $PathValue
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    $raw = $raw.TrimStart([char]0xFEFF)
    return $raw | ConvertFrom-Json
}

function Read-JsonLines {
    param([string]$PathValue)
    $items = @()
    if (-not (Test-Path $PathValue)) { return $items }
    foreach ($line in Get-Content -Path $PathValue) {
        $line = $line.TrimStart([char]0xFEFF).Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try {
            $items += ($line | ConvertFrom-Json)
        }
        catch {
            $items += [pscustomobject]@{
                utc = $null
                runId = $null
                phase = "parse"
                action = "invalid-json-line"
                status = "warn"
                detail = $line
            }
        }
    }
    return $items
}

function HtmlEncode {
    param([object]$Value)
    return [System.Net.WebUtility]::HtmlEncode([string]$Value)
}

function JsonForHtmlScript {
    param([object]$Value)
    $json = $Value | ConvertTo-Json -Depth 20
    return $json.Replace("</", "<\/")
}

$repoRoot = (Get-Location).Path
$workspacePath = Resolve-FromRoot -PathValue $Workspace -BasePath $repoRoot
if (-not (Test-Path $workspacePath)) {
    throw "Migration workspace was not found: $workspacePath"
}

$outPath = Resolve-FromRoot -PathValue $Out -BasePath $workspacePath
New-Item -ItemType Directory -Force -Path $outPath | Out-Null

$stateDir = Join-Path $workspacePath "state"
$runStatePath = Join-Path $stateDir "harness-run.json"
$runState = Read-JsonFileOrNull $runStatePath
if ($null -eq $runState) {
    throw "Active harness run was not found or is invalid: $runStatePath"
}

$runId = [string]$runState.runId
if ([string]::IsNullOrWhiteSpace($runId)) {
    throw "Active harness run does not contain runId: $runStatePath"
}

$eventsPath = Join-Path $stateDir "harness-events.jsonl"
$tracePath = Join-Path (Join-Path $workspacePath "runs") (Join-Path $runId "trace.jsonl")
$policyResultPath = Join-Path $stateDir "harness-policy-result.json"
$policyResultMd = Join-Path $stateDir "harness-policy-result.md"

$events = @(Read-JsonLines $eventsPath)
$traceEvents = @(Read-JsonLines $tracePath)
$policyResult = Read-JsonFileOrNull $policyResultPath
$policyChecks = @()
if ($null -ne $policyResult -and $null -ne $policyResult.checks) { $policyChecks = @($policyResult.checks) }

$permissionAsks = @($events | Where-Object {
    ([string]$_.action -match "permission|approval|ask") -or ([string]$_.status -match "ask|needs-approval")
}).Count
$scopeViolations = @($events | Where-Object {
    ([string]$_.action -match "scope" -and [string]$_.status -match "fail|violation") -or ([string]$_.detail -match "scope violation")
}).Count
$failedPolicyChecks = @($policyChecks | Where-Object { -not $_.passed }).Count
$repeatedCommands = @($events | Where-Object { [string]$_.action -match "repeated-command" }).Count

# Dashboard UI dictionaries live under dashboard/i18n; data remains language-neutral.
$i18nDir = Join-Path (Join-Path $workspacePath "dashboard") "i18n"
$enPath = Join-Path $i18nDir "en.json"
$ruPath = Join-Path $i18nDir "ru.json"
$en = Read-JsonFileOrNull $enPath
$ru = Read-JsonFileOrNull $ruPath
if ($null -eq $en -or $null -eq $ru) {
    throw "Dashboard i18n dictionaries are missing. Expected: $enPath and $ruPath"
}

$dashboard = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
    workspace = $workspacePath
    languageDefault = $Language
    i18nLanguages = @("en", "ru")
    run = [ordered]@{
        runId = $runId
        status = [string]$runState.status
        mode = [string]$runState.mode
        taskTitle = [string]$runState.taskTitle
        goal = [string]$runState.goal
        createdAtUtc = [string]$runState.createdAtUtc
    }
    metrics = [ordered]@{
        events = $events.Count
        traceEvents = $traceEvents.Count
        policyChecks = $policyChecks.Count
        failedPolicyChecks = $failedPolicyChecks
        permissionAsks = $permissionAsks
        scopeViolations = $scopeViolations
        repeatedCommands = $repeatedCommands
    }
    policy = [ordered]@{
        status = if ($null -ne $policyResult) { [string]$policyResult.status } else { "UNKNOWN" }
        checks = $policyChecks
    }
    events = $events
    artifacts = [ordered]@{
        dashboardJson = "harness-dashboard.json"
        dashboardMarkdown = "harness-dashboard.md"
        policyResult = if (Test-Path $policyResultMd) { "../state/harness-policy-result.md" } else { $null }
        trace = "../runs/$runId/trace.jsonl"
    }
}

$jsonOut = Join-Path $outPath "harness-dashboard.json"
$mdOut = Join-Path $outPath "harness-dashboard.md"
$htmlOut = Join-Path $outPath "index.html"

$dashboard | ConvertTo-Json -Depth 30 | Set-Content -Path $jsonOut -Encoding UTF8

$md = @(
    "# Migrator Agent Harness Dashboard",
    "",
    "Run: $runId",
    "Status: $($dashboard.run.status)",
    "Generated at UTC: $($dashboard.generatedAtUtc)",
    "",
    "## Metrics",
    "",
    "| Metric | Value |",
    "|---|---:|",
    "| Events | $($dashboard.metrics.events) |",
    "| Trace events | $($dashboard.metrics.traceEvents) |",
    "| Policy checks | $($dashboard.metrics.policyChecks) |",
    "| Failed policy checks | $($dashboard.metrics.failedPolicyChecks) |",
    "| Permission asks | $($dashboard.metrics.permissionAsks) |",
    "| Scope violations | $($dashboard.metrics.scopeViolations) |",
    "| Repeated commands | $($dashboard.metrics.repeatedCommands) |",
    "",
    "## Language policy",
    "",
    "English is the default dashboard language. Russian is available through the language switch. Machine-readable data stays language-neutral."
)
$md | Set-Content -Path $mdOut -Encoding UTF8

$dataJson = JsonForHtmlScript $dashboard
$i18nJson = JsonForHtmlScript ([ordered]@{ en = $en; ru = $ru })
$defaultLanguage = HtmlEncode $Language

$html = @"
<!doctype html>
<html lang="$defaultLanguage">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Migrator Agent Harness Dashboard</title>
  <style>
    :root { color-scheme: light dark; font-family: Segoe UI, system-ui, sans-serif; }
    body { margin: 0; padding: 24px; background: Canvas; color: CanvasText; }
    header { display: flex; justify-content: space-between; gap: 16px; align-items: flex-start; margin-bottom: 20px; }
    h1 { margin: 0 0 6px; font-size: 28px; }
    h2 { margin-top: 24px; }
    .muted { color: color-mix(in srgb, CanvasText 65%, Canvas 35%); }
    .grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; }
    .card { border: 1px solid color-mix(in srgb, CanvasText 20%, Canvas 80%); border-radius: 12px; padding: 14px; background: color-mix(in srgb, Canvas 94%, CanvasText 6%); }
    .metric { font-size: 26px; font-weight: 700; }
    table { width: 100%; border-collapse: collapse; margin-top: 12px; }
    th, td { border-bottom: 1px solid color-mix(in srgb, CanvasText 18%, Canvas 82%); padding: 8px; text-align: left; vertical-align: top; }
    th { font-size: 12px; text-transform: uppercase; letter-spacing: .04em; }
    .status-pass { color: #2e7d32; font-weight: 700; }
    .status-fail { color: #c62828; font-weight: 700; }
    select { padding: 6px 8px; border-radius: 8px; }
    code { font-family: Consolas, ui-monospace, monospace; }
  </style>
</head>
<body>
  <script id="dashboard-data" type="application/json">$dataJson</script>
  <script id="dashboard-i18n" type="application/json">$i18nJson</script>
  <header>
    <div>
      <h1 data-i18n="dashboard.title">Migrator Agent Harness Dashboard</h1>
      <div class="muted" data-i18n="dashboard.subtitle">Run lifecycle, policy checks, and trace events.</div>
    </div>
    <label><span data-i18n="language.label">Language</span>: <select id="languageSelect"><option value="en">English</option><option value="ru">Русский</option></select></label>
  </header>

  <section class="card">
    <h2 data-i18n="summary.title">Summary</h2>
    <div class="grid" id="summaryGrid"></div>
  </section>

  <section>
    <h2 data-i18n="policy.title">Harness policy checks</h2>
    <table id="policyTable"></table>
  </section>

  <section>
    <h2 data-i18n="events.title">Events</h2>
    <table id="eventsTable"></table>
  </section>

  <section>
    <h2 data-i18n="artifacts.title">Artifacts</h2>
    <ul>
      <li><a href="harness-dashboard.json" data-i18n="artifact.dashboardJson">Dashboard JSON</a></li>
      <li><a href="harness-dashboard.md" data-i18n="artifact.dashboardMarkdown">Dashboard Markdown</a></li>
      <li><a href="../state/harness-policy-result.md" data-i18n="artifact.policyResult">Policy result</a></li>
      <li><a href="../runs/$runId/trace.jsonl" data-i18n="artifact.trace">Run trace</a></li>
    </ul>
  </section>

<script>
const data = JSON.parse(document.getElementById('dashboard-data').textContent);
const i18n = JSON.parse(document.getElementById('dashboard-i18n').textContent);
const select = document.getElementById('languageSelect');
function t(key) { return (i18n[select.value] && i18n[select.value][key]) || (i18n.en && i18n.en[key]) || key; }
function esc(value) { return String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
function renderText() { document.querySelectorAll('[data-i18n]').forEach(el => el.textContent = t(el.dataset.i18n)); }
function metricCard(labelKey, value) { return `<div class="card"><div class="muted">${esc(t(labelKey))}</div><div class="metric">${esc(value)}</div></div>`; }
function renderSummary() {
  document.getElementById('summaryGrid').innerHTML = [
    metricCard('run.id', data.run.runId),
    metricCard('run.status', data.run.status),
    metricCard('metrics.events', data.metrics.events),
    metricCard('metrics.policyChecks', data.metrics.policyChecks),
    metricCard('metrics.permissionAsks', data.metrics.permissionAsks),
    metricCard('metrics.scopeViolations', data.metrics.scopeViolations)
  ].join('');
}
function renderPolicy() {
  const rows = (data.policy.checks || []).map(c => `<tr><td><code>${esc(c.name)}</code></td><td class="${c.passed ? 'status-pass' : 'status-fail'}">${esc(c.passed ? t('status.pass') : t('status.fail'))}</td><td>${esc(c.detail)}</td></tr>`).join('');
  document.getElementById('policyTable').innerHTML = `<thead><tr><th>Name</th><th>${esc(t('events.status'))}</th><th>${esc(t('events.detail'))}</th></tr></thead><tbody>${rows}</tbody>`;
}
function renderEvents() {
  const rows = (data.events || []).map(e => `<tr><td>${esc(e.utc)}</td><td><code>${esc(e.phase)}</code></td><td><code>${esc(e.action)}</code></td><td>${esc(e.status)}</td><td>${esc(e.detail)}</td></tr>`).join('');
  document.getElementById('eventsTable').innerHTML = `<thead><tr><th>UTC</th><th>${esc(t('events.phase'))}</th><th>${esc(t('events.action'))}</th><th>${esc(t('events.status'))}</th><th>${esc(t('events.detail'))}</th></tr></thead><tbody>${rows}</tbody>`;
}
function renderAll() { document.documentElement.lang = select.value; renderText(); renderSummary(); renderPolicy(); renderEvents(); localStorage.setItem('harnessDashboardLanguage', select.value); }
select.value = localStorage.getItem('harnessDashboardLanguage') || data.languageDefault || 'en';
select.addEventListener('change', renderAll);
renderAll();
</script>
</body>
</html>
"@

$html | Set-Content -Path $htmlOut -Encoding UTF8

Write-Host "HARNESS_DASHBOARD_WRITTEN: $htmlOut"
Write-Host "Dashboard JSON: $jsonOut"
Write-Host "Dashboard Markdown: $mdOut"
