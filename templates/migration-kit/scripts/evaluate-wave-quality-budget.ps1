<#
.SYNOPSIS
Evaluate whether the latest wave stayed within the migration quality budget.

.DESCRIPTION
evaluate-wave-quality-budget summarizes wave-local quality signals such as source files,
test count, migrated actions, TODO count, syntax-fallback ratio, unmapped targets, and
verify-project status. If the wave is too noisy, it writes BLOCKED_BY_WAVE_QUALITY_BUDGET
with a concrete next action and mirrors evidence to state/wave-quality-budget.json and runs/$RunId/wave-quality-budget.json to switch into mapping/research/config improvement before
another wave is allowed.
#>
param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [string]$WaveId = "",
    [int]$MaxSourceFiles = 5,
    [int]$MaxTests = 25,
    [int]$MaxActions = 250,
    [int]$MaxTodos = 80,
    [int]$MaxUnmappedTargets = 50,
    [double]$MaxSyntaxFallbackRatio = 0.90,
    [int]$MinSemanticActions = 1,
    [switch]$AllowScaffoldingOnly
)

$ErrorActionPreference = "Stop"

function Read-TextIfExists([string]$Path) {
    if (Test-Path $Path) { return Get-Content -Raw -Path $Path }
    return ""
}

function Read-LatestRunId([string]$WorkspacePath) {
    $agentState = Join-Path $WorkspacePath "agent-state.md"
    $text = Read-TextIfExists $agentState
    $m = [regex]::Match($text, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
    if ($m.Success) { return $m.Groups[1].Value }

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

    return Get-ChildItem -Path $runsPath -Directory -Filter "wave-*" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Get-IntFromRegex([string]$Text, [string[]]$Patterns) {
    foreach ($pattern in $Patterns) {
        $m = [regex]::Match($Text, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if ($m.Success) {
            $number = 0
            if ([int]::TryParse($m.Groups[1].Value, [ref]$number)) { return $number }
        }
    }
    return $null
}

function Count-JsonMetric($Node, [string[]]$Names, [ref]$Found) {
    if ($null -eq $Node) { return 0 }
    $sum = 0

    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string] -and $Node -isnot [System.Management.Automation.PSCustomObject]) {
        foreach ($item in $Node) { $sum += Count-JsonMetric $item $Names $Found }
        return $sum
    }

    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        foreach ($property in $Node.PSObject.Properties) {
            foreach ($name in $Names) {
                if ($property.Name.Equals($name, [StringComparison]::OrdinalIgnoreCase)) {
                    $number = 0
                    if ([int]::TryParse($property.Value.ToString(), [ref]$number)) {
                        $Found.Value = $true
                        $sum += $number
                    }
                }
            }
            if ($property.Value -is [System.Management.Automation.PSCustomObject] -or
                ($property.Value -is [System.Collections.IEnumerable] -and $property.Value -isnot [string])) {
                $sum += Count-JsonMetric $property.Value $Names $Found
            }
        }
    }

    return $sum
}

function Get-JsonMetric([string]$Root, [string[]]$Names) {
    if (-not (Test-Path $Root)) { return $null }
    $found = $false
    $sum = 0
    foreach ($file in Get-ChildItem -Path $Root -Recurse -File -Include "*.json" -ErrorAction SilentlyContinue) {
        try {
            $json = Get-Content -Raw -Path $file.FullName | ConvertFrom-Json -ErrorAction Stop
            $sum += Count-JsonMetric $json $Names ([ref]$found)
        }
        catch { }
    }
    if ($found) { return $sum }
    return $null
}

function Get-SourceFileCount([string]$WaveRoot) {
    $sourceScope = Join-Path $WaveRoot "source-scope"
    if (Test-Path $sourceScope) {
        $count = @(Get-ChildItem -Path $sourceScope -Recurse -File -Include "*.cs" -ErrorAction SilentlyContinue).Count
        if ($count -gt 0) { return $count }
    }

    $inputScope = Join-Path $WaveRoot "input-scope"
    if (Test-Path $inputScope) {
        $count = @(Get-ChildItem -Path $inputScope -Recurse -File -Include "*.cs" -ErrorAction SilentlyContinue).Count
        if ($count -gt 0) { return $count }
    }

    $generated = Join-Path $WaveRoot "generated"
    if (Test-Path $generated) {
        return @(Get-ChildItem -Path $generated -Recurse -File -Include "*.cs" -ErrorAction SilentlyContinue).Count
    }

    return 0
}

function Get-RecursiveText([string]$Root) {
    if (-not (Test-Path $Root)) { return "" }
    $builder = New-Object System.Text.StringBuilder
    foreach ($file in Get-ChildItem -Path $Root -Recurse -File -Include "*.md", "*.json", "*.jsonl", "*.txt", "*.cs" -ErrorAction SilentlyContinue) {
        try { [void]$builder.AppendLine((Get-Content -Raw -Path $file.FullName -ErrorAction Stop)) } catch { }
    }
    return $builder.ToString()
}

function Get-MetricOrRegex([string]$Root, [string]$Text, [string[]]$JsonNames, [string[]]$Regexes, [int]$Default = 0) {
    $jsonValue = Get-JsonMetric $Root $JsonNames
    if ($jsonValue -ne $null) { return [int]$jsonValue }
    $regexValue = Get-IntFromRegex $Text $Regexes
    if ($regexValue -ne $null) { return [int]$regexValue }
    return $Default
}

$workspacePath = [System.IO.Path]::GetFullPath($Workspace)
if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = Read-LatestRunId $workspacePath }
$waveDir = Find-LatestWaveDir $workspacePath $WaveId

$stateDir = Join-Path $workspacePath "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null

if ($waveDir -eq $null) {
    $report = [ordered]@{
        schemaVersion = "wave-quality-budget/v1"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        runId = $RunId
        waveId = $WaveId
        status = "PASS"
        budgetStatus = "PASS"
        detail = "no wave-run workspace found"
        routing = "ROUTE_TO_NEXT_WAVE"
        metrics = [ordered]@{}
        budgets = [ordered]@{
            maxSourceFiles = $MaxSourceFiles
            maxTests = $MaxTests
            maxActions = $MaxActions
            maxTodos = $MaxTodos
            maxUnmappedTargets = $MaxUnmappedTargets
            maxSyntaxFallbackRatio = $MaxSyntaxFallbackRatio
            minSemanticActions = $MinSemanticActions
            allowScaffoldingOnly = [bool]$AllowScaffoldingOnly
        }
        violations = @()
        nextAction = $null
    }
}
else {
    $waveRoot = $waveDir.FullName
    $waveText = Get-RecursiveText $waveRoot
    $sourceFiles = Get-SourceFileCount $waveRoot
    $testCount = Get-MetricOrRegex $waveRoot $waveText @("testCount", "tests", "totalTests") @('(\d+)\s+tests?\s+in\s+scope', 'tests?\s*[:=]\s*(\d+)') 0
    $migratedActions = Get-MetricOrRegex $waveRoot $waveText @("migratedActions", "migratedActionCount", "seleniumActions", "actionsMigrated") @('(\d+)\s+Selenium\s+actions\s+migrated', 'migrated\s+actions?\s*[:=]\s*(\d+)') 0
    $syntaxFallbackActions = Get-MetricOrRegex $waveRoot $waveText @("syntaxFallbackActions", "syntaxFallback", "fallbackActions") @('(\d+)\s+syntax[- ]fallback', 'syntax[- ]fallback\s+actions?\s*[:=]\s*(\d+)') 0
    $semanticActions = Get-MetricOrRegex $waveRoot $waveText @("semanticActions", "semanticMappings", "semanticActionCount") @('(\d+)\s+semantic', 'semantic\s+actions?\s*[:=]\s*(\d+)') 0
    $unmappedTargets = Get-MetricOrRegex $waveRoot $waveText @("unmappedTargets", "unmappedTargetCount", "unmapped") @('(\d+)\s+unmapped\s+targets?', 'unmapped\s+targets?\s*[:=]\s*(\d+)') 0
    $todoCount = Get-MetricOrRegex $waveRoot $waveText @("todoCount", "todos", "todoComments") @('(\d+)\s+TODO\s+comments?', 'TODO\s+comments?\s*[:=]\s*(\d+)') 0
    if ($todoCount -eq 0) { $todoCount = [regex]::Matches($waveText, '(?i)\bTODO\b').Count }
    if ($semanticActions -eq 0 -and $migratedActions -gt 0 -and $syntaxFallbackActions -lt $migratedActions) {
        $semanticActions = $migratedActions - $syntaxFallbackActions
    }
    $syntaxFallbackRatio = if ($migratedActions -gt 0) { [Math]::Round(($syntaxFallbackActions / [double]$migratedActions), 4) } else { 0.0 }
    $verifyFailed = $waveText -match '(?i)verify-project.{0,120}(FAILED|failed)|NU1008|compilation not verified'

    $violations = New-Object System.Collections.Generic.List[object]
    if ($sourceFiles -gt $MaxSourceFiles) { $violations.Add([ordered]@{ metric = "sourceFiles"; actual = $sourceFiles; budget = $MaxSourceFiles; severity = "medium" }) | Out-Null }
    if ($testCount -gt $MaxTests) { $violations.Add([ordered]@{ metric = "testCount"; actual = $testCount; budget = $MaxTests; severity = "medium" }) | Out-Null }
    if ($migratedActions -gt $MaxActions) { $violations.Add([ordered]@{ metric = "migratedActions"; actual = $migratedActions; budget = $MaxActions; severity = "medium" }) | Out-Null }
    if ($todoCount -gt $MaxTodos) { $violations.Add([ordered]@{ metric = "todoCount"; actual = $todoCount; budget = $MaxTodos; severity = "high" }) | Out-Null }
    if ($unmappedTargets -gt $MaxUnmappedTargets) { $violations.Add([ordered]@{ metric = "unmappedTargets"; actual = $unmappedTargets; budget = $MaxUnmappedTargets; severity = "high" }) | Out-Null }
    if ($migratedActions -gt 0 -and $syntaxFallbackRatio -gt $MaxSyntaxFallbackRatio) { $violations.Add([ordered]@{ metric = "syntaxFallbackRatio"; actual = $syntaxFallbackRatio; budget = $MaxSyntaxFallbackRatio; severity = "high" }) | Out-Null }
    if ((-not $AllowScaffoldingOnly) -and $migratedActions -gt 0 -and $semanticActions -lt $MinSemanticActions) { $violations.Add([ordered]@{ metric = "semanticActions"; actual = $semanticActions; budget = ">= $MinSemanticActions"; severity = "high" }) | Out-Null }
    if ($verifyFailed) { $violations.Add([ordered]@{ metric = "verifyProject"; actual = "FAILED"; budget = "PASS or explicit NOT RUNTIME READY blocker"; severity = "high" }) | Out-Null }

    $budgetStatus = if ($violations.Count -eq 0) { "PASS" } else { "BLOCKED_BY_WAVE_QUALITY_BUDGET" }
    $gateStatus = if ($violations.Count -eq 0) { "PASS" } else { "FAIL" }
    $routing = if ($violations.Count -eq 0) { "ROUTE_TO_NEXT_WAVE" } else { "ROUTE_TO_MAPPING_RESEARCH_OR_CONFIG_IMPROVEMENT" }
    $nextAction = if ($violations.Count -eq 0) { $null } else { "Run migration/scripts/collect-mapping-research-memory.ps1 before the next wave: summarize top TODO causes, syntax-fallback clusters, unmapped targets, unresolved symbols, and verify-project blockers; then slice a bounded config/POM/recognizer improvement ticket." }

    $report = [ordered]@{
        schemaVersion = "wave-quality-budget/v1"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        runId = $RunId
        waveId = $waveDir.Name
        waveRoot = $waveRoot
        status = $gateStatus
        budgetStatus = $budgetStatus
        metrics = [ordered]@{
            sourceFiles = $sourceFiles
            testCount = $testCount
            migratedActions = $migratedActions
            semanticActions = $semanticActions
            syntaxFallbackActions = $syntaxFallbackActions
            syntaxFallbackRatio = $syntaxFallbackRatio
            unmappedTargets = $unmappedTargets
            todoCount = $todoCount
            verifyProjectFailed = [bool]$verifyFailed
        }
        budgets = [ordered]@{
            maxSourceFiles = $MaxSourceFiles
            maxTests = $MaxTests
            maxActions = $MaxActions
            maxTodos = $MaxTodos
            maxUnmappedTargets = $MaxUnmappedTargets
            maxSyntaxFallbackRatio = $MaxSyntaxFallbackRatio
            minSemanticActions = $MinSemanticActions
            allowScaffoldingOnly = [bool]$AllowScaffoldingOnly
        }
        violations = @($violations)
        nextAction = $nextAction
        routing = $routing
    }
}

$stateJson = Join-Path $stateDir "wave-quality-budget.json"
$stateMd = Join-Path $stateDir "wave-quality-budget.md"
$report | ConvertTo-Json -Depth 20 | Set-Content -Path $stateJson -Encoding UTF8

if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    $runDir = Join-Path $workspacePath "runs/$RunId"
    New-Item -ItemType Directory -Force -Path $runDir | Out-Null
    $report | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $runDir "wave-quality-budget.json") -Encoding UTF8
}

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Wave Quality Budget")
[void]$md.AppendLine()
[void]$md.AppendLine("Schema: ``wave-quality-budget/v1``")
[void]$md.AppendLine("Run id: ``$RunId``")
[void]$md.AppendLine("Wave id: ``$($report.waveId)``")
[void]$md.AppendLine("Status: **$($report.budgetStatus)**")
if ($report.PSObject.Properties.Name -contains "routing") { [void]$md.AppendLine("Routing: ``$($report.routing)``") }
[void]$md.AppendLine()
[void]$md.AppendLine("## Metrics")
foreach ($property in $report.metrics.Keys) {
    [void]$md.AppendLine("- ${property}: ``$($report.metrics[$property])``")
}
[void]$md.AppendLine()
[void]$md.AppendLine("## Budgets")
foreach ($property in $report.budgets.Keys) {
    [void]$md.AppendLine("- ${property}: ``$($report.budgets[$property])``")
}
[void]$md.AppendLine()
if (@($report.violations).Count -gt 0) {
    [void]$md.AppendLine("## Violations")
    foreach ($violation in @($report.violations)) {
        [void]$md.AppendLine("- $($violation.severity): ``$($violation.metric)`` actual ``$($violation.actual)`` budget ``$($violation.budget)``")
    }
    [void]$md.AppendLine()
    [void]$md.AppendLine("Next action: $($report.nextAction)")
}
else {
    [void]$md.AppendLine("No budget violations. Next wave may be considered after the normal gates pass.")
}
Set-Content -Path $stateMd -Value $md.ToString() -Encoding UTF8
if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    Set-Content -Path (Join-Path $workspacePath "runs/$RunId/wave-quality-budget.md") -Value $md.ToString() -Encoding UTF8
}

Write-Host "WAVE_QUALITY_BUDGET_$($report.budgetStatus)"
if ($report.PSObject.Properties.Name -contains "routing") { Write-Host "Routing: $($report.routing)" }
Write-Host "Report: $stateMd"
if ($report.nextAction) { Write-Host "Next action: $($report.nextAction)" }

if ($report.budgetStatus -eq "PASS") { exit 0 }
exit 1
