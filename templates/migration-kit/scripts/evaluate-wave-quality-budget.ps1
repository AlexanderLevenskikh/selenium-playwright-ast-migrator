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
    [int]$MaxSourceFiles = 2,
    [int]$MaxTests = 4,
    [int]$MaxActions = 90,
    [int]$MaxTodos = 80,
    [int]$MaxUnmappedTargets = 50,
    [double]$MaxSyntaxFallbackRatio = 0.90,
    [int]$MinSemanticActions = 1,
    [int]$MaxPostFinalTickets = 4,
    [int]$MaxConsecutiveNoProgressTickets = 2,
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
    $values = New-Object System.Collections.Generic.List[int]
    foreach ($file in Get-ChildItem -Path $Root -Recurse -File -Include "*.json" -ErrorAction SilentlyContinue) {
        try {
            $json = Get-Content -Raw -Path $file.FullName | ConvertFrom-Json -ErrorAction Stop
            $foundInFile = $false
            $value = Count-JsonMetric $json $Names ([ref]$foundInFile)
            if ($foundInFile) { $values.Add([int]$value) | Out-Null }
        }
        catch { }
    }
    if ($values.Count -gt 0) {
        # Do not sum repeated report snapshots. report.json, migration-board.json,
        # quality artifacts, and copied run evidence can contain the same counters.
        # The maximum is a conservative fallback when no canonical report exists.
        return [int](($values | Measure-Object -Maximum).Maximum)
    }
    return $null
}

function Read-CanonicalMigrationReport([string]$WaveRoot) {
    $candidates = @(
        (Join-Path $WaveRoot "generated/report.json"),
        (Join-Path $WaveRoot "report.json")
    )
    foreach ($candidate in $candidates) {
        if (-not (Test-Path $candidate)) { continue }
        try {
            $value = Get-Content -Raw -Path $candidate | ConvertFrom-Json -ErrorAction Stop
            return [pscustomobject]@{ path = $candidate; value = $value }
        }
        catch { }
    }
    return $null
}

function Get-JsonIntProperty($Object, [string[]]$Names, [int]$Default = 0) {
    if ($null -eq $Object) { return $Default }
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties | Where-Object { $_.Name -ieq $name } | Select-Object -First 1
        if ($null -eq $property) { continue }
        $number = 0
        if ([int]::TryParse([string]$property.Value, [ref]$number)) { return $number }
    }
    return $Default
}

function Read-WavePreflight([string]$WaveRoot) {
    $path = Join-Path $WaveRoot "preflight-budget.json"
    if (-not (Test-Path $path)) { return $null }
    try {
        $value = Get-Content -Raw -Path $path | ConvertFrom-Json -ErrorAction Stop
        if ([string]$value.schemaVersion -ne "migration-wave-preflight-budget/v1") { return $null }
        return $value
    }
    catch { return $null }
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

function Read-JsonLines([string]$Path) {
    $items = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path $Path)) { return @() }
    foreach ($line in (Get-Content -Path $Path -ErrorAction SilentlyContinue)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { [void]$items.Add(($line | ConvertFrom-Json -ErrorAction Stop)) } catch { }
    }
    return $items.ToArray()
}

function Get-RemediationBudgetMetrics([string]$WorkspacePath, [string]$RunId, [string]$WaveId) {
    $progressPath = Join-Path $WorkspacePath "state/wave-progress-ledger.jsonl"
    $progressEntries = @(Read-JsonLines $progressPath | Where-Object {
        [string]$_.ticketStatus -eq "DONE" -and
        [string]$_.ticketId -like "post-final-*" -and
        ([string]::IsNullOrWhiteSpace($WaveId) -or [string]$_.waveId -eq $WaveId) -and
        ([string]::IsNullOrWhiteSpace($RunId) -or [string]::IsNullOrWhiteSpace([string]$_.runId) -or [string]$_.runId -eq $RunId)
    })

    if ($progressEntries.Count -eq 0) {
        $ticketLedger = Join-Path $WorkspacePath "state/current-ticket-ledger.jsonl"
        $fallback = @(Read-JsonLines $ticketLedger | Where-Object {
            [string]$_.status -eq "DONE" -and
            [string]$_.ticketId -like "post-final-*" -and
            ([string]::IsNullOrWhiteSpace($RunId) -or [string]::IsNullOrWhiteSpace([string]$_.runId) -or [string]$_.runId -eq $RunId)
        })
        $distinctFallback = @($fallback | Group-Object { [string]$_.ticketId } | ForEach-Object { $_.Group | Select-Object -Last 1 })
        return [ordered]@{
            completedTickets = $distinctFallback.Count
            consecutiveNoProgressTickets = 0
            progressEvidenceAvailable = $false
            completedTicketIds = @($distinctFallback | ForEach-Object { [string]$_.ticketId })
        }
    }

    $latestByTicket = @($progressEntries | Group-Object { [string]$_.ticketId } | ForEach-Object { $_.Group | Select-Object -Last 1 })
    $ordered = @($latestByTicket | Sort-Object { [string]$_.updatedAtUtc })
    $consecutiveNoProgress = 0
    for ($index = $ordered.Count - 1; $index -ge 0; $index--) {
        if ($ordered[$index].meaningfulProgress -eq $false) {
            $consecutiveNoProgress++
        }
        else {
            break
        }
    }
    return [ordered]@{
        completedTickets = $ordered.Count
        consecutiveNoProgressTickets = $consecutiveNoProgress
        progressEvidenceAvailable = $true
        completedTicketIds = @($ordered | ForEach-Object { [string]$_.ticketId })
    }
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
            maxPostFinalTickets = $MaxPostFinalTickets
            maxConsecutiveNoProgressTickets = $MaxConsecutiveNoProgressTickets
            allowScaffoldingOnly = [bool]$AllowScaffoldingOnly
        }
        violations = @()
        nextAction = $null
    }
}
else {
    $waveRoot = $waveDir.FullName

    # Recalculate outcome-oriented quality from generated code when the installed CLI supports it.
    # A non-zero exit here means the hard gate is not ready; the JSON packet is still written and
    # consumed below. Old editable report counters remain observability only.
    $migratorCommand = $null
    try {
        $migratorCommand = Get-Command selenium-pw-migrator -ErrorAction SilentlyContinue
        if ($null -ne $migratorCommand) {
            & selenium-pw-migrator migration measure-wave --out $waveRoot 2>$null | Out-Null
        }
    }
    catch { }

    $waveText = Get-RecursiveText $waveRoot
    $preflight = Read-WavePreflight $waveRoot
    $canonicalReport = Read-CanonicalMigrationReport $waveRoot
    $outcomeMetricsPath = Join-Path $waveRoot "wave-quality-metrics.json"
    $outcomeMetrics = $null
    if (Test-Path $outcomeMetricsPath) {
        try { $outcomeMetrics = Get-Content -Raw -Path $outcomeMetricsPath | ConvertFrom-Json -ErrorAction Stop } catch { }
    }
    $waveAcceptancePath = Join-Path $waveRoot "wave-acceptance.json"
    $waveAcceptance = $null
    if (Test-Path $waveAcceptancePath) {
        try { $waveAcceptance = Get-Content -Raw -Path $waveAcceptancePath | ConvertFrom-Json -ErrorAction Stop } catch { }
    }
    $acceptanceCliValidated = $false
    if ($null -ne $migratorCommand -and $null -ne $waveAcceptance) {
        try {
            & selenium-pw-migrator migration check-wave-acceptance --out $waveRoot 2>$null | Out-Null
            $acceptanceCliValidated = $LASTEXITCODE -eq 0
        }
        catch { $acceptanceCliValidated = $false }
    }

    $plannedSourceFiles = Get-SourceFileCount $waveRoot
    $plannedTests = 0
    $estimatedActions = 0
    if ($null -ne $preflight) {
        if ($null -ne $preflight.metrics.sourceFileCount) { $plannedSourceFiles = [int]$preflight.metrics.sourceFileCount }
        if ($null -ne $preflight.metrics.testCount) { $plannedTests = [int]$preflight.metrics.testCount }
        if ($null -ne $preflight.metrics.estimatedActions) { $estimatedActions = [int]$preflight.metrics.estimatedActions }
    }

    $sourceFiles = $plannedSourceFiles
    $testCount = $plannedTests
    $migratedActions = $estimatedActions
    $syntaxFallbackActions = 0
    $semanticActions = 0
    $unmappedTargets = 0
    $todoCount = 0
    $canonicalReportPath = $null

    if ($null -ne $canonicalReport) {
        $canonicalReportPath = $canonicalReport.path
        $generatedRoot = Join-Path $waveRoot "generated"
        $generatedSourceFiles = 0
        if (Test-Path $generatedRoot) {
            $generatedSourceFiles = @(Get-ChildItem -Path $generatedRoot -Recurse -File -Include "*.cs", "*.ts" -ErrorAction SilentlyContinue).Count
        }
        $sourceFilesDefault = $sourceFiles
        if ($generatedSourceFiles -gt 0) { $sourceFilesDefault = $generatedSourceFiles }
        $sourceFiles = Get-JsonIntProperty $canonicalReport.value @("FilesProcessed", "filesProcessed") $sourceFilesDefault
        $testCount = Get-JsonIntProperty $canonicalReport.value @("TestsFound", "TotalTests", "testCount", "tests") $testCount
        $syntaxFallbackActions = Get-JsonIntProperty $canonicalReport.value @("SyntaxFallbackActions", "syntaxFallbackActions") 0
        $semanticActions = Get-JsonIntProperty $canonicalReport.value @("SemanticActions", "semanticActions") 0
        $actionDefault = $semanticActions + $syntaxFallbackActions
        if ($actionDefault -eq 0) { $actionDefault = $migratedActions }
        $migratedActions = Get-JsonIntProperty $canonicalReport.value @("ActionsFound", "migratedActions", "actionsMigrated") $actionDefault
        $unmappedTargets = Get-JsonIntProperty $canonicalReport.value @("UnmappedTargets", "unmappedTargets") 0
        $todoCount = Get-JsonIntProperty $canonicalReport.value @("TodoComments", "todoCount", "todos") 0
    }
    else {
        $sourceFiles = Get-MetricOrRegex $waveRoot $waveText @("filesProcessed", "sourceFiles") @('files?\s+processed\s*[:=]\s*(\d+)', 'source\s+files?\s*[:=]\s*(\d+)') $sourceFiles
        $testCount = Get-MetricOrRegex $waveRoot $waveText @("testCount", "tests", "totalTests", "TestsFound") @('(\d+)\s+tests?\s+in\s+scope', 'tests?\s*[:=]\s*(\d+)') $testCount
        $migratedActions = Get-MetricOrRegex $waveRoot $waveText @("migratedActions", "migratedActionCount", "seleniumActions", "actionsMigrated", "ActionsFound") @('(\d+)\s+Selenium\s+actions\s+migrated', 'migrated\s+actions?\s*[:=]\s*(\d+)') $migratedActions
        $syntaxFallbackActions = Get-MetricOrRegex $waveRoot $waveText @("syntaxFallbackActions", "syntaxFallback", "fallbackActions") @('(\d+)\s+syntax[- ]fallback', 'syntax[- ]fallback\s+actions?\s*[:=]\s*(\d+)') 0
        $semanticActions = Get-MetricOrRegex $waveRoot $waveText @("semanticActions", "semanticMappings", "semanticActionCount") @('(\d+)\s+semantic', 'semantic\s+actions?\s*[:=]\s*(\d+)') 0
        $unmappedTargets = Get-MetricOrRegex $waveRoot $waveText @("unmappedTargets", "unmappedTargetCount", "unmapped") @('(\d+)\s+unmapped\s+targets?', 'unmapped\s+targets?\s*[:=]\s*(\d+)') 0
        $todoCount = Get-MetricOrRegex $waveRoot $waveText @("todoCount", "todos", "todoComments") @('(\d+)\s+TODO\s+comments?', 'TODO\s+comments?\s*[:=]\s*(\d+)') 0
        if ($todoCount -eq 0) { $todoCount = [regex]::Matches($waveText, '(?i)\bTODO\b').Count }
    }

    if ($semanticActions -eq 0 -and $migratedActions -gt 0 -and $syntaxFallbackActions -lt $migratedActions) {
        $semanticActions = $migratedActions - $syntaxFallbackActions
    }

    # Outcome metrics are authoritative for readiness. Legacy report fields are retained below
    # so users do not lose semantic/fallback/TODO history, but they cannot grant wave acceptance.
    if ($null -ne $outcomeMetrics) {
        $testCount = [int]$outcomeMetrics.selectedTests
        $todoCount = [int]$outcomeMetrics.blockingTodoCount + [int]$outcomeMetrics.softTodoCount
    }
    $syntaxFallbackRatio = 0.0
    if ($migratedActions -gt 0) {
        $syntaxFallbackRatio = [Math]::Round(($syntaxFallbackActions / [double]$migratedActions), 4)
    }
    $verifyFailed = $waveText -match '(?i)verify-project.{0,120}(FAILED|failed)|NU1008|compilation not verified'

    $scopeMismatchReasons = New-Object System.Collections.Generic.List[string]
    if ($plannedSourceFiles -gt 0 -and $sourceFiles -gt $plannedSourceFiles) {
        $scopeMismatchReasons.Add("actual source files $sourceFiles exceed planned wave files $plannedSourceFiles") | Out-Null
    }
    if ($plannedTests -gt 0 -and $testCount -gt $plannedTests) {
        $scopeMismatchReasons.Add("actual tests $testCount exceed planned wave tests $plannedTests") | Out-Null
    }
    $waveScopeMismatch = $scopeMismatchReasons.Count -gt 0

    $violations = New-Object System.Collections.Generic.List[object]
    $advisories = New-Object System.Collections.Generic.List[object]
    $preflightStatus = "MISSING"
    if ($null -ne $preflight) {
        $preflightStatus = [string]$preflight.status
    }
    if ($preflightStatus -eq "BLOCKED") { $violations.Add([ordered]@{ metric = "preflightBudgetStatus"; actual = $preflightStatus; budget = "not BLOCKED"; severity = "high" }) | Out-Null }
    elseif ($preflightStatus -ne "PASS") { $advisories.Add([ordered]@{ metric = "preflightBudgetStatus"; actual = $preflightStatus; budget = "PASS"; severity = "advisory"; note = "soft/heavy preflight states remain executable but should influence manager sizing" }) | Out-Null }
    if ($waveScopeMismatch) { $violations.Add([ordered]@{ metric = "waveScopeMismatch"; actual = ($scopeMismatchReasons -join "; "); budget = "actual migration scope must not exceed input-scope.json"; severity = "high" }) | Out-Null }
    if ($sourceFiles -gt $MaxSourceFiles) { $advisories.Add([ordered]@{ metric = "sourceFiles"; actual = $sourceFiles; budget = $MaxSourceFiles; severity = "advisory"; note = "planned wave size is managed adaptively; this legacy threshold is observational" }) | Out-Null }
    if ($testCount -gt $MaxTests) { $advisories.Add([ordered]@{ metric = "testCount"; actual = $testCount; budget = $MaxTests; severity = "advisory"; note = "test count does not replace outcome readiness" }) | Out-Null }
    if ($migratedActions -gt $MaxActions) { $advisories.Add([ordered]@{ metric = "migratedActions"; actual = $migratedActions; budget = $MaxActions; severity = "advisory"; note = "action volume informs cost but cannot block an otherwise accepted wave" }) | Out-Null }
    if ($todoCount -gt $MaxTodos) { $advisories.Add([ordered]@{ metric = "todoCount"; actual = $todoCount; budget = $MaxTodos; severity = "advisory"; note = "flat TODO count is observational; root blocking patterns decide readiness" }) | Out-Null }
    if ($unmappedTargets -gt $MaxUnmappedTargets) { $advisories.Add([ordered]@{ metric = "unmappedTargets"; actual = $unmappedTargets; budget = $MaxUnmappedTargets; severity = "advisory" }) | Out-Null }
    if ($migratedActions -gt 0 -and $syntaxFallbackRatio -gt $MaxSyntaxFallbackRatio) { $advisories.Add([ordered]@{ metric = "syntaxFallbackRatio"; actual = $syntaxFallbackRatio; budget = $MaxSyntaxFallbackRatio; severity = "advisory"; note = "fallback is retained as a diagnostic metric; generated behavior and hard invariants decide acceptance" }) | Out-Null }
    if ((-not $AllowScaffoldingOnly) -and $migratedActions -gt 0 -and $semanticActions -lt $MinSemanticActions) { $advisories.Add([ordered]@{ metric = "semanticActions"; actual = $semanticActions; budget = ">= $MinSemanticActions"; severity = "advisory"; note = "semantic count cannot be edited into acceptance" }) | Out-Null }
    if ($verifyFailed) { $violations.Add([ordered]@{ metric = "verifyProject"; actual = "FAILED"; budget = "PASS or explicit NOT RUNTIME READY blocker"; severity = "high" }) | Out-Null }

    if ($null -eq $outcomeMetrics) {
        $violations.Add([ordered]@{ metric = "outcomeMetrics"; actual = "MISSING"; budget = "wave-quality-metrics/v1"; severity = "high" }) | Out-Null
    }
    elseif (-not [bool]$outcomeMetrics.hardGatePassed) {
        $failureText = @($outcomeMetrics.hardGateFailures) -join "; "
        $violations.Add([ordered]@{ metric = "outcomeHardGate"; actual = $failureText; budget = "PASS"; severity = "high" }) | Out-Null
    }

    $acceptanceValid = $false
    if ($null -ne $waveAcceptance -and $null -ne $outcomeMetrics) {
        $acceptedStatus = [string]$waveAcceptance.status
        $acceptanceValid = $acceptanceCliValidated -and
            $acceptedStatus -in @("ACCEPTED", "ACCEPTED_WITH_SCAFFOLDING", "ACCEPTED_WITH_DEFERRED_SOFT_DEBT", "ACCEPTED_WITH_SCAFFOLDING_AND_DEFERRED_SOFT_DEBT") -and
            [string]$waveAcceptance.metricsFingerprint -eq [string]$outcomeMetrics.metricsFingerprint -and
            [string]$waveAcceptance.generatedTreeHash -eq [string]$outcomeMetrics.generatedTreeHash
    }
    if (-not $acceptanceValid) {
        $violations.Add([ordered]@{ metric = "waveAcceptance"; actual = "MISSING_OR_STALE"; budget = "valid immutable wave-acceptance.json"; severity = "high" }) | Out-Null
    }

    # Revalidate the complete wave chain at every quality/final-gate evaluation. A predecessor
    # may have drifted after a later wave was materialized; checking only the latest receipt would
    # allow stale early-wave learning to survive until FINAL_GATE_PASS.
    $acceptanceChainFailures = New-Object System.Collections.Generic.List[string]
    $runsRoot = Join-Path $workspacePath "runs"
    if ($null -eq $migratorCommand) {
        $acceptanceChainFailures.Add("selenium-pw-migrator CLI unavailable") | Out-Null
    }
    elseif (Test-Path $runsRoot) {
        $waveCandidates = @(Get-ChildItem -Path $runsRoot -Directory -Filter "wave-*" -ErrorAction SilentlyContinue | Sort-Object Name)
        foreach ($candidateWave in $waveCandidates) {
            try {
                & selenium-pw-migrator migration check-wave-acceptance --out $candidateWave.FullName 2>$null | Out-Null
                if ($LASTEXITCODE -ne 0) { $acceptanceChainFailures.Add($candidateWave.Name) | Out-Null }
            }
            catch { $acceptanceChainFailures.Add($candidateWave.Name) | Out-Null }
        }
    }
    if ($acceptanceChainFailures.Count -gt 0) {
        $violations.Add([ordered]@{
            metric = "waveAcceptanceChain"
            actual = ($acceptanceChainFailures.ToArray() -join ", ")
            budget = "every materialized wave must retain a current validated acceptance receipt"
            severity = "high"
        }) | Out-Null
    }

    $remediation = Get-RemediationBudgetMetrics $workspacePath $RunId $waveDir.Name
    $ticketLimitReached = [int]$remediation.completedTickets -ge $MaxPostFinalTickets
    $noProgressLimitReached = [int]$remediation.consecutiveNoProgressTickets -ge $MaxConsecutiveNoProgressTickets
    $remediationBudgetExhausted = $ticketLimitReached -or $noProgressLimitReached
    if ($ticketLimitReached) { $violations.Add([ordered]@{ metric = "postFinalTickets"; actual = [int]$remediation.completedTickets; budget = $MaxPostFinalTickets; severity = "high" }) | Out-Null }
    if ($noProgressLimitReached) { $violations.Add([ordered]@{ metric = "consecutiveNoProgressTickets"; actual = [int]$remediation.consecutiveNoProgressTickets; budget = $MaxConsecutiveNoProgressTickets; severity = "high" }) | Out-Null }

    $qualityBlocked = @($violations | Where-Object { $_.metric -notin @("postFinalTickets", "consecutiveNoProgressTickets") }).Count -gt 0
    $budgetStatus = "PASS"
    if ($remediationBudgetExhausted) {
        $budgetStatus = "REMEDIATION_BUDGET_EXHAUSTED"
    }
    elseif ($qualityBlocked) {
        $budgetStatus = "BLOCKED_BY_WAVE_QUALITY_BUDGET"
    }

    $gateStatus = "PASS"
    if ($budgetStatus -eq "BLOCKED_BY_WAVE_QUALITY_BUDGET") {
        $gateStatus = "FAIL"
    }

    $routing = "ROUTE_TO_NEXT_WAVE"
    if ($remediationBudgetExhausted) {
        $routing = "STOP_FOR_REVIEW_WITH_LIMITATIONS"
    }
    elseif ($waveScopeMismatch) {
        $routing = "ROUTE_TO_WAVE_SCOPE_REPAIR"
    }
    elseif ($qualityBlocked) {
        $routing = "ROUTE_TO_WAVE_MANAGER_OR_REMEDIATION"
    }

    $nextAction = $null
    if ((-not $remediationBudgetExhausted) -and $waveScopeMismatch) {
        $nextAction = "Preserve the full-project output under migration/runs/$RunId/full-project-rerun/, restore the bounded wave generated directory, and rerun runs/$($waveDir.Name)/run-migrate.ps1 or run-migrate.sh so input-scope.json and selected-tests.txt remain authoritative."
    }
    elseif ((-not $remediationBudgetExhausted) -and $qualityBlocked) {
        $nextAction = "Run selenium-pw-migrator migration measure-wave --out $waveRoot, invoke migration-wave-manager on wave-manager-packet.json, and obey its bounded decision. For REMEDIATE_CURRENT_WAVE, slice one bounded normal implementation attempt. For SCAFFOLD_CURRENT_ROOT, add only one exact measured-no-progress helper/POM scaffold and keep runtimeReady=false. If no deterministic root candidate exists, run migration/scripts/collect-mapping-research-memory.ps1 (or .sh) once to produce bounded evidence; do not start open-ended repeated research."
    }

    # Materialize the generic list before assigning it into an ordered hashtable.
    # Windows PowerShell 5.1 and some PowerShell 7 runtimes can throw
    # "Argument types do not match" for @($genericList) in a hashtable value.
    $scopeIntegrityStatus = "PASS"
    if ($waveScopeMismatch) { $scopeIntegrityStatus = "CONTAMINATED_BY_FULL_SCOPE_RERUN" }

    $violationItems = $violations.ToArray()
    $advisoryItems = $advisories.ToArray()
    $outcomeHardGatePassed = $false
    $outcomeReadyTests = 0
    $outcomeDraftTests = 0
    $outcomeEmptyTests = 0
    $outcomeRootBlockingPatterns = 0
    $outcomeCascadeTodoCount = 0
    $outcomeAssertionPreservationRate = 0.0
    $outcomeBehaviorPresenceRate = 0.0
    $outcomeBehaviorlessTests = 0
    if ($null -ne $outcomeMetrics) {
        $outcomeHardGatePassed = [bool]$outcomeMetrics.hardGatePassed
        $outcomeReadyTests = [int]$outcomeMetrics.readyTests
        $outcomeDraftTests = [int]$outcomeMetrics.draftTests
        $outcomeEmptyTests = [int]$outcomeMetrics.emptyTests
        $outcomeRootBlockingPatterns = [int]$outcomeMetrics.rootBlockingPatterns
        $outcomeCascadeTodoCount = [int]$outcomeMetrics.cascadeTodoCount
        $outcomeAssertionPreservationRate = [double]$outcomeMetrics.assertionPreservationRate
        $outcomeBehaviorPresenceRate = [double]$outcomeMetrics.behaviorPresenceRate
        $behaviorlessItems = $outcomeMetrics.behaviorlessTests
        if ($null -eq $behaviorlessItems) { $outcomeBehaviorlessTests = 0 }
        elseif ($behaviorlessItems -is [System.Array]) { $outcomeBehaviorlessTests = [int]$behaviorlessItems.Length }
        else { $outcomeBehaviorlessTests = 1 }
    }

    $report = [ordered]@{
        schemaVersion = "wave-quality-budget/v1"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
        runId = $RunId
        waveId = $waveDir.Name
        waveRoot = $waveRoot
        status = $gateStatus
        budgetStatus = $budgetStatus
        scopeIntegrity = [ordered]@{
            status = $scopeIntegrityStatus
            mismatchDetected = [bool]$waveScopeMismatch
            reasons = $scopeMismatchReasons.ToArray()
            canonicalReport = $canonicalReportPath
        }
        metrics = [ordered]@{
            plannedSourceFiles = $plannedSourceFiles
            plannedTests = $plannedTests
            sourceFiles = $sourceFiles
            testCount = $testCount
            estimatedActions = $estimatedActions
            migratedActions = $migratedActions
            preflightBudgetStatus = $preflightStatus
            semanticActions = $semanticActions
            syntaxFallbackActions = $syntaxFallbackActions
            syntaxFallbackRatio = $syntaxFallbackRatio
            unmappedTargets = $unmappedTargets
            todoCount = $todoCount
            verifyProjectFailed = [bool]$verifyFailed
            completedPostFinalTickets = [int]$remediation.completedTickets
            consecutiveNoProgressTickets = [int]$remediation.consecutiveNoProgressTickets
            progressEvidenceAvailable = [bool]$remediation.progressEvidenceAvailable
            remediationBudgetExhausted = [bool]$remediationBudgetExhausted
            outcomeHardGatePassed = [bool]$outcomeHardGatePassed
            readyTests = [int]$outcomeReadyTests
            draftTests = [int]$outcomeDraftTests
            emptyTests = [int]$outcomeEmptyTests
            rootBlockingPatterns = [int]$outcomeRootBlockingPatterns
            cascadeTodoCount = [int]$outcomeCascadeTodoCount
            assertionPreservationRate = [double]$outcomeAssertionPreservationRate
            behaviorPresenceRate = [double]$outcomeBehaviorPresenceRate
            behaviorlessTests = [int]$outcomeBehaviorlessTests
            waveAcceptanceValid = [bool]$acceptanceValid
        }
        budgets = [ordered]@{
            maxSourceFiles = $MaxSourceFiles
            maxTests = $MaxTests
            maxActions = $MaxActions
            maxTodos = $MaxTodos
            maxUnmappedTargets = $MaxUnmappedTargets
            maxSyntaxFallbackRatio = $MaxSyntaxFallbackRatio
            minSemanticActions = $MinSemanticActions
            maxPostFinalTickets = $MaxPostFinalTickets
            maxConsecutiveNoProgressTickets = $MaxConsecutiveNoProgressTickets
            allowScaffoldingOnly = [bool]$AllowScaffoldingOnly
        }
        violations = $violationItems
        advisories = $advisoryItems
        qualityManager = [ordered]@{
            metricsArtifact = $outcomeMetricsPath
            acceptanceArtifact = $waveAcceptancePath
            acceptanceValid = [bool]$acceptanceValid
            hardGatesOverrideable = $false
            cliValidationPassed = [bool]$acceptanceCliValidated
            fastChangesCeremonyNotQuality = $true
        }
        nextAction = $nextAction
        routing = $routing
        legacyMappingRouting = "ROUTE_TO_MAPPING_RESEARCH_OR_CONFIG_IMPROVEMENT"
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
if ($report.budgetStatus -eq "REMEDIATION_BUDGET_EXHAUSTED") {
    [void]$md.AppendLine("## Remediation stop")
    [void]$md.AppendLine("Automatic remediation budget exhausted. Stop for review with remaining limitations; do not create another post-final ticket automatically.")
    if (@($report.violations).Count -gt 0) {
        [void]$md.AppendLine()
        [void]$md.AppendLine("## Remaining violations")
        foreach ($violation in @($report.violations)) {
            [void]$md.AppendLine("- $($violation.severity): ``$($violation.metric)`` actual ``$($violation.actual)`` budget ``$($violation.budget)``")
        }
    }
}
elseif (@($report.violations).Count -gt 0) {
    [void]$md.AppendLine("## Violations")
    foreach ($violation in @($report.violations)) {
        [void]$md.AppendLine("- $($violation.severity): ``$($violation.metric)`` actual ``$($violation.actual)`` budget ``$($violation.budget)``")
    }
    [void]$md.AppendLine()
    [void]$md.AppendLine("Next action: $($report.nextAction)")
}
else {
    [void]$md.AppendLine("No blocking budget violations. The next wave is allowed only with a valid wave-acceptance.json receipt.")
}
if ($advisoryItems.Length -gt 0) {
    [void]$md.AppendLine()
    [void]$md.AppendLine("## Advisory metrics")
    foreach ($advisory in $advisoryItems) {
        [void]$md.AppendLine("- $($advisory.metric): actual ``$($advisory.actual)`` reference ``$($advisory.budget)`` - $($advisory.note)")
    }
}
Set-Content -Path $stateMd -Value $md.ToString() -Encoding UTF8
if (-not [string]::IsNullOrWhiteSpace($RunId)) {
    Set-Content -Path (Join-Path $workspacePath "runs/$RunId/wave-quality-budget.md") -Value $md.ToString() -Encoding UTF8
}

Write-Host "WAVE_QUALITY_BUDGET_$($report.budgetStatus)"
if ($report.PSObject.Properties.Name -contains "routing") { Write-Host "Routing: $($report.routing)" }
Write-Host "Report: $stateMd"
if ($report.nextAction) { Write-Host "Next action: $($report.nextAction)" }

if ($report.budgetStatus -in @("PASS", "REMEDIATION_BUDGET_EXHAUSTED")) { exit 0 }
exit 1
