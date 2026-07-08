<#
.SYNOPSIS
Collect mapping/research memory from a noisy migration wave.

.DESCRIPTION
collect-mapping-research-memory reads the latest wave artifacts, wave quality budget,
generated files, TODO explanations, migration boards, and verify-project reports. It
produces mapping-research-memory/v1 artifacts that turn noisy wave output into reusable
migrator improvement evidence: top unresolved symbols, TODO clusters, unmapped targets,
syntax-fallback clusters, verify blockers, and candidate config/POM/recognizer tickets.

Run-local outputs include runs/$RunId/research/mapping-research-memory.json and
runs/$RunId/research/mapping-research-memory.md when an active run id is available.
#>
param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [string]$WaveId = "",
    [int]$MaxItems = 20,
    [switch]$CreateCurrentTicket
)

$ErrorActionPreference = "Stop"

function Read-TextIfExists([string]$Path) {
    if (Test-Path $Path) { return Get-Content -Raw -Path $Path -ErrorAction SilentlyContinue }
    return ""
}

function Read-LatestRunId([string]$WorkspacePath) {
    $agentState = Join-Path $WorkspacePath "agent-state.md"
    $text = Read-TextIfExists $agentState
    $m = [regex]::Match($text, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
    if ($m.Success) { return $m.Groups[1].Value }

    $stateRun = Join-Path $WorkspacePath "state/harness-run.json"
    if (Test-Path $stateRun) {
        try {
            $json = Get-Content -Raw -Path $stateRun | ConvertFrom-Json -ErrorAction Stop
            if (-not [string]::IsNullOrWhiteSpace([string]$json.runId)) { return [string]$json.runId }
        } catch { }
    }

    $runsPath = Join-Path $WorkspacePath "runs"
    if (Test-Path $runsPath) {
        $latest = Get-ChildItem -Path $runsPath -Directory -Filter "run-*" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($latest -ne $null) { return $latest.Name }
    }

    return ""
}

function Find-LatestWaveDir([string]$WorkspacePath, [string]$WaveId) {
    $runsPath = Join-Path $WorkspacePath "runs"
    if (-not (Test-Path $runsPath)) { return $null }

    if (-not [string]::IsNullOrWhiteSpace($WaveId)) {
        $direct = Join-Path $runsPath $WaveId
        if (Test-Path $direct) { return (Get-Item $direct) }
    }

    $budgetPath = Join-Path $WorkspacePath "state/wave-quality-budget.json"
    if (Test-Path $budgetPath) {
        try {
            $budget = Get-Content -Raw -Path $budgetPath | ConvertFrom-Json -ErrorAction Stop
            $budgetWave = [string]$budget.waveId
            if (-not [string]::IsNullOrWhiteSpace($budgetWave)) {
                $candidate = Join-Path $runsPath $budgetWave
                if (Test-Path $candidate) { return (Get-Item $candidate) }
            }
        } catch { }
    }

    return Get-ChildItem -Path $runsPath -Directory -Filter "wave-*" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Add-Count([hashtable]$Table, [string]$Key, [int]$Delta = 1) {
    if ([string]::IsNullOrWhiteSpace($Key)) { return }
    $normalized = $Key.Trim().Trim('`', '"', "'", ':', ';', ',', '.', ')', ']', '}')
    if ([string]::IsNullOrWhiteSpace($normalized)) { return }
    if (-not $Table.ContainsKey($normalized)) { $Table[$normalized] = 0 }
    $Table[$normalized] = [int]$Table[$normalized] + $Delta
}

function Convert-Counts([hashtable]$Table, [int]$Limit) {
    $items = New-Object System.Collections.Generic.List[object]
    foreach ($key in $Table.Keys) {
        $items.Add([ordered]@{ value = [string]$key; count = [int]$Table[$key] }) | Out-Null
    }
    return @($items | Sort-Object @{ Expression = { $_.count }; Descending = $true }, @{ Expression = { $_.value }; Descending = $false } | Select-Object -First $Limit)
}

function Add-LineSample([System.Collections.Generic.List[object]]$Samples, [string]$Kind, [string]$FilePath, [int]$LineNumber, [string]$Line, [int]$Limit) {
    if ($Samples.Count -ge $Limit) { return }
    $Samples.Add([ordered]@{
        kind = $Kind
        path = $FilePath
        line = $LineNumber
        text = $Line.Trim()
    }) | Out-Null
}

function Classify-Todo([string]$Line) {
    if ($Line -match '(?i)assert|Should\(|Assert\.|FluentAssertions|Expect') { return "assertion" }
    if ($Line -match '(?i)locator|selector|By\.|FindElement|data-test|xpath|css') { return "locator" }
    if ($Line -match '(?i)wait|timeout|loading|spinner|ExpectedConditions') { return "wait" }
    if ($Line -match '(?i)Page|Card|List|Dialog|Modal|Panel|Form|Grid|Table|POM|page object') { return "pom" }
    if ($Line -match '(?i)verify-project|compilation|NU1008|CS\d{4}') { return "verify" }
    if ($Line -match '(?i)config|adapter|mapping|recognizer|renderer') { return "config" }
    return "unknown"
}

$workspacePath = [System.IO.Path]::GetFullPath($Workspace)
if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = Read-LatestRunId $workspacePath }
$waveDir = Find-LatestWaveDir $workspacePath $WaveId

$stateDir = Join-Path $workspacePath "state"
$memoryDir = Join-Path $stateDir "memory"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
New-Item -ItemType Directory -Force -Path $memoryDir | Out-Null

$waveRoot = if ($waveDir -ne $null) { $waveDir.FullName } else { "" }
$waveName = if ($waveDir -ne $null) { $waveDir.Name } else { $WaveId }

$sourceArtifacts = New-Object System.Collections.Generic.List[object]
$todoSamples = New-Object System.Collections.Generic.List[object]
$verifyBlockers = New-Object System.Collections.Generic.List[object]
$unresolved = @{}
$pageObjects = @{}
$unmapped = @{}
$syntaxFallback = @{}
$todoClusters = @{}
$allText = New-Object System.Text.StringBuilder

$rootsToScan = New-Object System.Collections.Generic.List[string]
if (-not [string]::IsNullOrWhiteSpace($waveRoot) -and (Test-Path $waveRoot)) { $rootsToScan.Add($waveRoot) | Out-Null }
if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    $runRoot = Join-Path $workspacePath "runs/$RunId"
    if (Test-Path $runRoot) { $rootsToScan.Add($runRoot) | Out-Null }
}

foreach ($rootPath in @($rootsToScan | Select-Object -Unique)) {
    foreach ($file in Get-ChildItem -Path $rootPath -Recurse -File -Include "*.md", "*.json", "*.jsonl", "*.txt", "*.cs" -ErrorAction SilentlyContinue) {
        $relative = $file.FullName.Substring($workspacePath.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Replace('\', '/')
        $sourceArtifacts.Add([ordered]@{ path = $relative; bytes = $file.Length }) | Out-Null
        $lines = @(Get-Content -Path $file.FullName -ErrorAction SilentlyContinue)
        $lineNo = 0
        foreach ($line in $lines) {
            $lineNo += 1
            [void]$allText.AppendLine($line)
            if ($line -match '(?i)\bTODO\b') {
                $cluster = Classify-Todo $line
                Add-Count $todoClusters $cluster
                Add-LineSample $todoSamples "todo" $relative $lineNo $line $MaxItems
            }
            foreach ($m in [regex]::Matches($line, '(?i)UNRESOLVED_SYMBOL[\s:`"''=,-]+([A-Za-z_][A-Za-z0-9_\.<>]*)')) {
                Add-Count $unresolved $m.Groups[1].Value
            }
            foreach ($m in [regex]::Matches($line, '(?i)unresolved\s+symbols?\s*[:=]\s*`?([A-Za-z_][A-Za-z0-9_\.<>]*)')) {
                Add-Count $unresolved $m.Groups[1].Value
            }
            foreach ($m in [regex]::Matches($line, '\b([a-z][A-Za-z0-9_]*(?:Page|Card|List|Dialog|Modal|Panel|Form|Grid|Table))\b')) {
                Add-Count $pageObjects $m.Groups[1].Value
            }
            foreach ($m in [regex]::Matches($line, '(?i)unmapped\s+targets?\s*[:=]?\s*`?([A-Za-z_][A-Za-z0-9_\.<>/-]*)')) {
                Add-Count $unmapped $m.Groups[1].Value
            }
            if ($line -match '(?i)syntax[- ]fallback') {
                Add-Count $syntaxFallback $relative
            }
            if ($line -match '(?i)verify-project|NU1008|compilation not verified|\bCS\d{4}\b') {
                Add-LineSample $verifyBlockers "verify" $relative $lineNo $line $MaxItems
            }
        }
    }
}

$budgetPath = Join-Path $stateDir "wave-quality-budget.json"
$budget = $null
if (Test-Path $budgetPath) {
    try { $budget = Get-Content -Raw -Path $budgetPath | ConvertFrom-Json -ErrorAction Stop } catch { }
}

$recommendedTickets = New-Object System.Collections.Generic.List[object]
$topPages = Convert-Counts $pageObjects $MaxItems
$topUnresolved = Convert-Counts $unresolved $MaxItems
$topTodo = Convert-Counts $todoClusters $MaxItems
$topUnmapped = Convert-Counts $unmapped $MaxItems
$topSyntax = Convert-Counts $syntaxFallback $MaxItems

if (@($topPages).Count -gt 0) {
    $recommendedTickets.Add([ordered]@{
        title = "Improve POM/config mappings for top page-object symbols"
        kind = "pom-mapping"
        source = "mapping-research-memory/v1"
        examples = @($topPages | Select-Object -First 5)
        validation = "Rerun migrate/verify on the same wave; semanticActions should increase and TODO/unmapped counts should decrease."
    }) | Out-Null
}
if (@($topUnresolved).Count -gt 0 -or @($topUnmapped).Count -gt 0) {
    $recommendedTickets.Add([ordered]@{
        title = "Add resolver/config support for top unresolved or unmapped targets"
        kind = "resolver-config"
        source = "mapping-research-memory/v1"
        examples = @(@($topUnresolved | Select-Object -First 5) + @($topUnmapped | Select-Object -First 5))
        validation = "Run evaluate-wave-quality-budget and check-final-gate; unresolved/unmapped evidence must shrink or be reclassified."
    }) | Out-Null
}
if (@($verifyBlockers).Count -gt 0) {
    $recommendedTickets.Add([ordered]@{
        title = "Fix verify-project blocker before scaling waves"
        kind = "verify-harness"
        source = "mapping-research-memory/v1"
        examples = @($verifyBlockers | Select-Object -First 5)
        validation = "verify-project no longer fails for the same source scope, or reports a classified NOT RUNTIME READY blocker."
    }) | Out-Null
}
if (@($topTodo).Count -gt 0) {
    $recommendedTickets.Add([ordered]@{
        title = "Reduce dominant TODO cluster with one recognizer/renderer improvement"
        kind = "recognizer-or-renderer"
        source = "mapping-research-memory/v1"
        examples = @($topTodo | Select-Object -First 5)
        validation = "The same wave produces fewer TODOs in the dominant cluster without assertion suppression."
    }) | Out-Null
}

$status = if (@($recommendedTickets).Count -gt 0) { "RESEARCH_READY" } else { "NO_ACTIONABLE_MAPPING_RESEARCH" }
$routing = if ($status -eq "RESEARCH_READY") { "ROUTE_TO_CONFIG_POM_RECOGNIZER_IMPROVEMENT" } else { "NO_ACTIONABLE_MAPPING_RESEARCH" }

$report = [ordered]@{
    schemaVersion = "mapping-research-memory/v1"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    runId = $RunId
    waveId = $waveName
    waveRoot = $waveRoot
    status = $status
    routing = $routing
    sourceArtifacts = @($sourceArtifacts | Select-Object -First 200)
    waveQualityBudget = if ($budget -ne $null) { [ordered]@{ status = [string]$budget.budgetStatus; routing = [string]$budget.routing; metrics = $budget.metrics; violations = $budget.violations } } else { $null }
    topUnresolvedSymbols = @($topUnresolved)
    topPageObjectSymbols = @($topPages)
    topTodoClusters = @($topTodo)
    topUnmappedTargets = @($topUnmapped)
    syntaxFallbackClusters = @($topSyntax)
    todoSamples = @($todoSamples)
    verifyBlockers = @($verifyBlockers)
    recommendedNextTickets = @($recommendedTickets)
    nextAction = if ($status -eq "RESEARCH_READY") { "Slice one bounded config/POM/recognizer or verify-harness improvement ticket from mapping-research-memory/v1 before another wave." } else { "No actionable mapping research was detected; require human review before another wave if the budget is still blocked." }
}

$stateJson = Join-Path $stateDir "mapping-research-memory.json"
$stateMd = Join-Path $stateDir "mapping-research-memory.md"
$stateJsonl = Join-Path $stateDir "mapping-research-candidates.jsonl"
$report | ConvertTo-Json -Depth 30 | Set-Content -Path $stateJson -Encoding UTF8

if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    $researchDir = Join-Path $workspacePath "runs/$RunId/research"
    New-Item -ItemType Directory -Force -Path $researchDir | Out-Null
    $report | ConvertTo-Json -Depth 30 | Set-Content -Path (Join-Path $researchDir "mapping-research-memory.json") -Encoding UTF8
}

# Rewrite candidate JSONL from the current snapshot so downstream slicers can read compact entries.
if (Test-Path $stateJsonl) { Remove-Item -Path $stateJsonl -Force }
foreach ($ticket in @($recommendedTickets)) {
    $line = ([ordered]@{
        schemaVersion = "mapping-research-candidate/v1"
        generatedAtUtc = $report.generatedAtUtc
        runId = $RunId
        waveId = $waveName
        title = $ticket.title
        kind = $ticket.kind
        source = "mapping-research-memory/v1"
        validation = $ticket.validation
        examples = $ticket.examples
    } | ConvertTo-Json -Compress -Depth 20)
    Add-Content -Path $stateJsonl -Encoding UTF8 -Value $line
}

$memoryEntry = [ordered]@{
    kind = "final-gate-lesson"
    text = "Mapping research memory collected for wave '$waveName': $(@($recommendedTickets).Count) candidate improvement tickets."
    source = "collect-mapping-research-memory"
    status = if ($status -eq "RESEARCH_READY") { "active" } else { "blocked" }
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    data = [ordered]@{
        schemaVersion = "mapping-research-memory/v1"
        runId = $RunId
        waveId = $waveName
        report = "state/mapping-research-memory.json"
        candidateCount = @($recommendedTickets).Count
    }
}
Add-Content -Path (Join-Path $memoryDir "final-gate-lessons.jsonl") -Encoding UTF8 -Value ($memoryEntry | ConvertTo-Json -Compress -Depth 20)

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Mapping Research Memory")
[void]$md.AppendLine()
[void]$md.AppendLine("Schema: `mapping-research-memory/v1`")
[void]$md.AppendLine("Run id: `$RunId`")
[void]$md.AppendLine("Wave id: `$waveName`")
[void]$md.AppendLine("Status: **$status**")
[void]$md.AppendLine("Routing: `$routing`")
[void]$md.AppendLine()
[void]$md.AppendLine("## Top page-object symbols")
foreach ($item in @($topPages | Select-Object -First 10)) { [void]$md.AppendLine("- `$($item.value)` — $($item.count)") }
[void]$md.AppendLine()
[void]$md.AppendLine("## Top unresolved symbols")
foreach ($item in @($topUnresolved | Select-Object -First 10)) { [void]$md.AppendLine("- `$($item.value)` — $($item.count)") }
[void]$md.AppendLine()
[void]$md.AppendLine("## TODO clusters")
foreach ($item in @($topTodo | Select-Object -First 10)) { [void]$md.AppendLine("- `$($item.value)` — $($item.count)") }
[void]$md.AppendLine()
[void]$md.AppendLine("## Verify blockers")
foreach ($item in @($verifyBlockers | Select-Object -First 10)) { [void]$md.AppendLine("- `$($item.path):$($item.line)` $($item.text)") }
[void]$md.AppendLine()
[void]$md.AppendLine("## Recommended next tickets")
foreach ($ticket in @($recommendedTickets)) {
    [void]$md.AppendLine("- **$($ticket.title)** (`$($ticket.kind)`) — $($ticket.validation)")
}
[void]$md.AppendLine()
[void]$md.AppendLine("Next action: $($report.nextAction)")
Set-Content -Path $stateMd -Value $md.ToString() -Encoding UTF8
if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    $researchDir = Join-Path $workspacePath "runs/$RunId/research"
    Set-Content -Path (Join-Path $researchDir "mapping-research-memory.md") -Value $md.ToString() -Encoding UTF8
}

if ($CreateCurrentTicket -and @($recommendedTickets).Count -gt 0) {
    $ticket = @($recommendedTickets)[0]
    $ticketText = @"
# Current Ticket: $($ticket.title)

Source: `mapping-research-memory/v1`
Run id: `$RunId`
Wave id: `$waveName`
Kind: `$($ticket.kind)`

## Objective
Use `migration/state/mapping-research-memory.json` and `migration/state/mapping-research-candidates.jsonl` to implement exactly one bounded config/POM/recognizer or verify-harness improvement before another wave.

## Evidence
- `migration/state/mapping-research-memory.md`
- `migration/state/mapping-research-memory.json`
- `migration/state/mapping-research-candidates.jsonl`

## Validation
$($ticket.validation)
"@
    Set-Content -Path (Join-Path $workspacePath "current-ticket.md") -Value $ticketText -Encoding UTF8
}

Write-Host "MAPPING_RESEARCH_MEMORY_$status"
Write-Host "Report: $stateMd"
Write-Host "Candidates: $stateJsonl"
if ($report.nextAction) { Write-Host "Next action: $($report.nextAction)" }

if ($status -eq "RESEARCH_READY") { exit 0 }
exit 1
