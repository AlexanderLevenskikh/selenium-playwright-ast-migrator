# build-harness-dashboard.ps1
param(
    [string]$Workspace = "migration",
    [string]$Out = "dashboard/harness",
    [ValidateSet("en", "ru")]
    [string]$Language = "en",
    [switch]$Watch,
    [ValidateRange(2, 300)]
    [int]$RefreshSeconds = 5
)

$ErrorActionPreference = "Stop"

function Resolve-FromRoot {
    param([string]$PathValue, [string]$BasePath)
    if ([System.IO.Path]::IsPathRooted($PathValue)) { return [System.IO.Path]::GetFullPath($PathValue) }
    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $PathValue))
}

function Get-RelativePathCompat {
    param([string]$BasePath, [string]$FullPath)
    $baseFull = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $targetFull = [System.IO.Path]::GetFullPath($FullPath)
    if ($targetFull.StartsWith($baseFull, [StringComparison]::OrdinalIgnoreCase)) {
        return $targetFull.Substring($baseFull.Length).Replace('\', '/')
    }
    return $targetFull.Replace('\', '/')
}

function Read-JsonFileOrNull {
    param([string]$PathValue)
    if (-not (Test-Path $PathValue)) { return $null }
    try {
        $raw = Get-Content -Raw -Path $PathValue -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
        $raw = $raw.TrimStart([char]0xFEFF)
        return $raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch { return $null }
}

function Read-JsonLines {
    param([string]$PathValue)
    $items = New-Object System.Collections.Generic.List[object]
    if (-not (Test-Path $PathValue)) { return @() }
    foreach ($line in (Get-Content -Path $PathValue -ErrorAction SilentlyContinue)) {
        $line = $line.TrimStart([char]0xFEFF).Trim()
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $items.Add(($line | ConvertFrom-Json -ErrorAction Stop)) | Out-Null }
        catch {
            $items.Add([pscustomobject]@{
                utc = $null
                runId = $null
                phase = "parse"
                action = "invalid-json-line"
                status = "warn"
                detail = $line
            }) | Out-Null
        }
    }
    return $items.ToArray()
}

function Get-PropertyValue {
    param($Object, [string[]]$Names, $Default = $null)
    if ($null -eq $Object) { return $Default }
    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties | Where-Object { $_.Name -ieq $name } | Select-Object -First 1
        if ($null -ne $property -and $null -ne $property.Value) { return $property.Value }
    }
    return $Default
}

function Get-IntValue {
    param($Object, [string[]]$Names, [int]$Default = 0)
    $value = Get-PropertyValue $Object $Names $null
    $number = 0
    if ($null -ne $value -and [int]::TryParse([string]$value, [ref]$number)) { return $number }
    return $Default
}

function Get-MarkdownField {
    param([string]$Text, [string[]]$Names, [string]$Default = "")
    foreach ($name in $Names) {
        $match = [regex]::Match($Text, "(?im)^\s*" + [regex]::Escape($name) + "\s*:\s*`?(.+?)`?\s*$")
        if ($match.Success) { return $match.Groups[1].Value.Trim().Trim('`') }
    }
    return $Default
}

function Read-TextIfExists {
    param([string]$PathValue)
    if (-not (Test-Path $PathValue)) { return "" }
    try { return Get-Content -Raw -Path $PathValue -ErrorAction Stop }
    catch { return "" }
}

function HtmlEncode {
    param([object]$Value)
    return [System.Net.WebUtility]::HtmlEncode([string]$Value)
}

function JsonForHtmlScript {
    param([object]$Value)
    $json = $Value | ConvertTo-Json -Depth 40
    return $json.Replace("</", "<\/")
}

function Limit-Percent {
    param([double]$Value)
    if ($Value -lt 0) { return 0 }
    if ($Value -gt 100) { return 100 }
    return [int][Math]::Round($Value)
}

function Get-WaveStatusKind {
    param([string]$Status, [bool]$IsCurrent, [bool]$QualityBlocked)
    $normalized = $Status.ToLowerInvariant()
    if ($QualityBlocked -and $IsCurrent) { return "attention" }
    if ($normalized -match 'fail|error|blocked|incomplete') { return "danger" }
    if ($normalized -match 'done|complete|accepted|verified|pass') { return "success" }
    if ($normalized -match 'migrated|review|generated') { return "progress" }
    if ($IsCurrent) { return "current" }
    if ($normalized -match 'prepared|ready') { return "ready" }
    return "pending"
}

function Find-LatestWaveDirectory {
    param([string]$WorkspacePath, [string]$PreferredWaveId)
    $runsPath = Join-Path $WorkspacePath "runs"
    if (-not [string]::IsNullOrWhiteSpace($PreferredWaveId)) {
        $preferred = Join-Path $runsPath $PreferredWaveId
        if (Test-Path $preferred) { return Get-Item $preferred }
    }
    if (-not (Test-Path $runsPath)) { return $null }
    return Get-ChildItem -Path $runsPath -Directory -Filter "wave-*" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function New-DashboardSnapshot {
    param(
        [string]$WorkspacePath,
        [string]$OutPath,
        [string]$DefaultLanguage,
        [bool]$WatchMode,
        [int]$IntervalSeconds
    )

    $stateDir = Join-Path $WorkspacePath "state"
    $runStatePath = Join-Path $stateDir "harness-run.json"
    $runState = Read-JsonFileOrNull $runStatePath
    if ($null -eq $runState) { throw "Active harness run was not found or is invalid: $runStatePath" }

    $runId = [string](Get-PropertyValue $runState @("runId") "")
    if ([string]::IsNullOrWhiteSpace($runId)) { throw "Active harness run does not contain runId: $runStatePath" }

    $events = @(Read-JsonLines (Join-Path $stateDir "harness-events.jsonl"))
    $traceEvents = @(Read-JsonLines (Join-Path $WorkspacePath "runs/$runId/trace.jsonl"))
    $policyResult = Read-JsonFileOrNull (Join-Path $stateDir "harness-policy-result.json")
    $finalGate = Read-JsonFileOrNull (Join-Path $stateDir "final-gate-result.json")
    $continuation = Read-JsonFileOrNull (Join-Path $stateDir "continuation-decision.json")
    $ticketStatus = Read-JsonFileOrNull (Join-Path $stateDir "current-ticket-status.json")
    $qualityBudget = Read-JsonFileOrNull (Join-Path $stateDir "wave-quality-budget.json")
    $plan = Read-JsonFileOrNull (Join-Path $WorkspacePath "plan/waves.json")
    $sourceScope = Read-JsonFileOrNull (Join-Path $stateDir "source-scope.json")

    $currentTicketText = Read-TextIfExists (Join-Path $WorkspacePath "current-ticket.md")
    $ticketTitle = Get-MarkdownField $currentTicketText @("Task", "Title", "Ticket") ""
    if ([string]::IsNullOrWhiteSpace($ticketTitle)) {
        $heading = [regex]::Match($currentTicketText, '(?m)^#\s+(?:Current Ticket\s*:\s*)?(.+?)\s*$')
        if ($heading.Success) { $ticketTitle = $heading.Groups[1].Value.Trim() }
    }
    $ticketGoal = Get-MarkdownField $currentTicketText @("Goal", "Objective") ""
    $ticketId = [string](Get-PropertyValue $ticketStatus @("ticketId") "")
    if ([string]::IsNullOrWhiteSpace($ticketId)) {
        $ticketMatch = [regex]::Match($currentTicketText, '(?i)\bpost-final-[0-9A-Za-z_.-]+\b')
        if ($ticketMatch.Success) { $ticketId = $ticketMatch.Value }
    }
    $ticketState = [string](Get-PropertyValue $ticketStatus @("status") (Get-MarkdownField $currentTicketText @("Status") "UNKNOWN"))

    $qualityStatus = [string](Get-PropertyValue $qualityBudget @("budgetStatus", "status") "UNKNOWN")
    $currentWaveId = [string](Get-PropertyValue $qualityBudget @("waveId") "")
    $waveDir = Find-LatestWaveDirectory $WorkspacePath $currentWaveId
    if ([string]::IsNullOrWhiteSpace($currentWaveId) -and $null -ne $waveDir) { $currentWaveId = $waveDir.Name }

    $outcomeMetrics = $null
    $waveManagerDecision = $null
    $currentWaveAcceptance = $null
    if ($null -ne $waveDir) {
        $outcomeMetrics = Read-JsonFileOrNull (Join-Path $waveDir.FullName "wave-quality-metrics.json")
        $waveManagerDecision = Read-JsonFileOrNull (Join-Path $waveDir.FullName "wave-manager-decision.json")
        $currentWaveAcceptance = Read-JsonFileOrNull (Join-Path $waveDir.FullName "wave-acceptance.json")
    }

    $planWaves = @()
    if ($null -ne $plan) {
        $candidateWaves = Get-PropertyValue $plan @("waves", "Waves") @()
        if ($null -ne $candidateWaves) { $planWaves = @($candidateWaves) }
    }

    $waveItems = New-Object System.Collections.Generic.List[object]
    $totalTests = 0
    $acceptedTests = 0
    $migratedTests = 0
    $acceptedWaves = 0
    $migratedWaves = 0
    $currentWaveIndex = 0

    for ($i = 0; $i -lt $planWaves.Count; $i++) {
        $wave = $planWaves[$i]
        $waveId = [string](Get-PropertyValue $wave @("id", "Id", "waveId", "WaveId") ("wave-{0:D3}" -f ($i + 1)))
        $tests = @(Get-PropertyValue $wave @("tests", "Tests") @())
        $files = @(Get-PropertyValue $wave @("files", "Files") @())
        $testCount = $tests.Count
        $fileCount = $files.Count
        $totalTests += $testCount

        $waveRoot = Join-Path $WorkspacePath "runs/$waveId"
        $waveStatus = Read-JsonFileOrNull (Join-Path $waveRoot "wave-status.json")
        $waveAcceptance = Read-JsonFileOrNull (Join-Path $waveRoot "wave-acceptance.json")
        $waveOutcomeMetrics = Read-JsonFileOrNull (Join-Path $waveRoot "wave-quality-metrics.json")
        $rawStatus = [string](Get-PropertyValue $waveStatus @("status") "pending")
        $acceptanceStatus = [string](Get-PropertyValue $waveAcceptance @("status") "")
        $generatedCount = Get-IntValue $waveStatus @("generatedSourceFileCount", "generatedFileCount") 0
        $isCurrent = $waveId -eq $currentWaveId
        if ($isCurrent) { $currentWaveIndex = $i + 1 }
        $qualityBlocked = $isCurrent -and $qualityStatus -eq "BLOCKED_BY_WAVE_QUALITY_BUDGET"
        $kind = Get-WaveStatusKind $rawStatus $isCurrent $qualityBlocked

        $isMigrated = $generatedCount -gt 0 -or $rawStatus -match '(?i)migrated|complete|done|accepted|verified|pass'
        $acceptanceFingerprintMatches = $null -ne $waveAcceptance -and $null -ne $waveOutcomeMetrics -and
            [string](Get-PropertyValue $waveAcceptance @("metricsFingerprint") "") -eq [string](Get-PropertyValue $waveOutcomeMetrics @("metricsFingerprint") "") -and
            [string](Get-PropertyValue $waveAcceptance @("generatedTreeHash") "") -eq [string](Get-PropertyValue $waveOutcomeMetrics @("generatedTreeHash") "")
        $isAccepted = $acceptanceStatus -in @("ACCEPTED", "ACCEPTED_WITH_DEFERRED_SOFT_DEBT") -and $acceptanceFingerprintMatches
        if ($isAccepted) {
            $rawStatus = $acceptanceStatus
            $kind = "success"
        }
        elseif (-not [string]::IsNullOrWhiteSpace($acceptanceStatus)) {
            $rawStatus = "STALE_ACCEPTANCE"
            $kind = "attention"
        }
        if ($isMigrated) {
            $migratedWaves++
            $migratedTests += $testCount
        }
        if ($isAccepted) {
            $acceptedWaves++
            $acceptedTests += $testCount
        }

        $waveItems.Add([ordered]@{
            id = $waveId
            index = $i + 1
            phase = [string](Get-PropertyValue $wave @("phase", "Phase") "")
            cluster = [string](Get-PropertyValue $wave @("cluster", "Cluster") "")
            testCount = $testCount
            fileCount = $fileCount
            status = $rawStatus
            kind = $kind
            current = $isCurrent
            generatedFiles = $generatedCount
            acceptanceStatus = $acceptanceStatus
            acceptanceValid = [bool]$isAccepted
        }) | Out-Null
    }

    if ($planWaves.Count -eq 0 -and $null -ne $waveDir) {
        $scope = Read-JsonFileOrNull (Join-Path $waveDir.FullName "input-scope.json")
        $waveStatus = Read-JsonFileOrNull (Join-Path $waveDir.FullName "wave-status.json")
        $tests = @(Get-PropertyValue $scope @("tests", "Tests") @())
        $files = @(Get-PropertyValue $scope @("files", "Files") @())
        $totalTests = $tests.Count
        $currentWaveIndex = 1
        $singleWaveKind = "current"
        if ($qualityStatus -eq "BLOCKED_BY_WAVE_QUALITY_BUDGET") { $singleWaveKind = "attention" }
        $waveItems.Add([ordered]@{
            id = $currentWaveId
            index = 1
            phase = [string](Get-PropertyValue $scope @("phase") "")
            cluster = [string](Get-PropertyValue $scope @("cluster") "")
            testCount = $tests.Count
            fileCount = $files.Count
            status = [string](Get-PropertyValue $waveStatus @("status") "active")
            kind = $singleWaveKind
            current = $true
            generatedFiles = Get-IntValue $waveStatus @("generatedSourceFileCount", "generatedFileCount") 0
        }) | Out-Null
    }

    $qualityMetrics = Get-PropertyValue $qualityBudget @("metrics") $null
    $actualDraftTests = Get-IntValue $outcomeMetrics @("generatedTests", "selectedTests") (Get-IntValue $qualityMetrics @("testCount") $migratedTests)
    if ($actualDraftTests -lt $migratedTests) { $actualDraftTests = $migratedTests }
    $draftCoveragePercent = 0
    $acceptedPercent = 0
    if ($totalTests -gt 0) {
        $draftCoveragePercent = Limit-Percent (($actualDraftTests / [double]$totalTests) * 100)
        $acceptedPercent = Limit-Percent (($acceptedTests / [double]$totalTests) * 100)
    }
    $estimatedProcessPercent = Limit-Percent (($draftCoveragePercent * 0.35) + ($acceptedPercent * 0.65))

    $scopeIntegrity = Get-PropertyValue $qualityBudget @("scopeIntegrity") $null
    $scopeMismatch = [bool](Get-PropertyValue $scopeIntegrity @("mismatchDetected") $false)
    $continuationStatus = [string](Get-PropertyValue $continuation @("status") "UNKNOWN")
    $nextAction = [string](Get-PropertyValue $continuation @("nextAction") "")
    if ([string]::IsNullOrWhiteSpace($nextAction)) { $nextAction = [string](Get-PropertyValue $qualityBudget @("nextAction") "") }

    $runStatus = [string](Get-PropertyValue $runState @("status") "UNKNOWN")
    $terminalStatuses = @("DONE", "FINAL_WITH_LIMITATIONS", "WAVE_REMEDIATION_BUDGET_EXHAUSTED", "HUMAN_DECISION_REQUIRED")
    $isTerminal = $terminalStatuses -contains $runStatus -or $terminalStatuses -contains $continuationStatus
    $mustContinue = [bool](Get-PropertyValue $continuation @("mustContinueBeforeUserMessage") $false)
    $continuousMode = [string](Get-PropertyValue $runState @("continuationMode") "default")
    $continuousRequested = [bool](Get-PropertyValue $runState @("continuousRequested") $false)

    $tone = "calm"
    $headlineKey = "human.working.title"
    $summaryKey = "human.working.summary"
    $reasonKey = "human.working.reason"
    $interventionKey = "intervention.none"
    $interventionTone = "success"
    if ($scopeMismatch) {
        $tone = "attention"
        $headlineKey = "human.scopeRepair.title"
        $summaryKey = "human.scopeRepair.summary"
        $reasonKey = "human.scopeRepair.reason"
    }
    elseif ($qualityStatus -eq "BLOCKED_BY_WAVE_QUALITY_BUDGET") {
        $tone = "attention"
        $headlineKey = "human.quality.title"
        $summaryKey = "human.quality.summary"
        $reasonKey = "human.quality.reason"
    }
    elseif ($ticketState -match '(?i)READY|IN_PROGRESS|REVIEW_READY') {
        $tone = "calm"
        $headlineKey = "human.ticket.title"
        $summaryKey = "human.ticket.summary"
        $reasonKey = "human.ticket.reason"
    }
    elseif ($continuationStatus -eq "CONTINUE_REQUIRED" -or $mustContinue) {
        $tone = "calm"
        $headlineKey = "human.continuing.title"
        $summaryKey = "human.continuing.summary"
        $reasonKey = "human.continuing.reason"
    }
    elseif ($isTerminal -and $runStatus -eq "DONE") {
        $tone = "success"
        $headlineKey = "human.done.title"
        $summaryKey = "human.done.summary"
        $reasonKey = "human.done.reason"
    }
    elseif ($isTerminal -and ($runStatus -eq "FINAL_WITH_LIMITATIONS" -or $runStatus -eq "WAVE_REMEDIATION_BUDGET_EXHAUSTED" -or $continuationStatus -eq "FINAL_WITH_LIMITATIONS")) {
        $tone = "attention"
        $headlineKey = "human.limit.title"
        $summaryKey = "human.limit.summary"
        $reasonKey = "human.limit.reason"
        $interventionKey = "intervention.required"
        $interventionTone = "attention"
    }
    elseif ($isTerminal) {
        $tone = "danger"
        $headlineKey = "human.stopped.title"
        $summaryKey = "human.stopped.summary"
        $reasonKey = "human.stopped.reason"
        $interventionKey = "intervention.required"
        $interventionTone = "danger"
    }

    $policyChecks = @()
    if ($null -ne $policyResult) { $policyChecks = @(Get-PropertyValue $policyResult @("checks") @()) }
    $finalChecks = @()
    if ($null -ne $finalGate) { $finalChecks = @(Get-PropertyValue $finalGate @("checks") @()) }
    $failedPolicyChecks = @($policyChecks | Where-Object { -not [bool](Get-PropertyValue $_ @("passed") $false) }).Count
    $failedFinalChecks = @($finalChecks | Where-Object { -not [bool](Get-PropertyValue $_ @("passed") $false) }).Count
    $permissionAsks = @($events | Where-Object { ([string]$_.action -match "permission|approval|ask") -or ([string]$_.status -match "ask|needs-approval") }).Count
    $scopeViolations = @($events | Where-Object { (([string]$_.action -match "scope") -and ([string]$_.status -match "fail|violation")) -or ([string]$_.detail -match "scope violation") }).Count

    $recentEvents = @($events | Select-Object -Last 12)
    [array]::Reverse($recentEvents)

    $violations = @()
    if ($null -ne $qualityBudget) { $violations = @(Get-PropertyValue $qualityBudget @("violations") @()) }

    $sourcePath = [string](Get-PropertyValue $sourceScope @("source", "sourcePath", "configuredSource") "")
    if ([string]::IsNullOrWhiteSpace($sourcePath)) { $sourcePath = [string](Get-PropertyValue $sourceScope @("path") "") }

    $effectiveRefreshSeconds = 0
    if ($WatchMode) { $effectiveRefreshSeconds = $IntervalSeconds }

    $testPreviews = New-Object System.Collections.Generic.List[object]
    if ($null -ne $waveDir) {
        $generatedRoot = Join-Path $waveDir.FullName "generated"
        if (Test-Path $generatedRoot) {
            foreach ($file in @(Get-ChildItem -Path $generatedRoot -Recurse -File -Filter "*.cs" -ErrorAction SilentlyContinue | Sort-Object FullName | Select-Object -First 3)) {
                $lines = @(Get-Content -Path $file.FullName -TotalCount 80 -ErrorAction SilentlyContinue)
                $testPreviews.Add([ordered]@{
                    path = Get-RelativePathCompat $WorkspacePath $file.FullName
                    snippet = ($lines -join "`n")
                    previewLines = $lines.Count
                }) | Out-Null
            }
        }
    }

    $processStage = "draft"
    if ($planWaves.Count -eq 0 -and $null -eq $waveDir) { $processStage = "plan" }
    elseif ($scopeMismatch -or $qualityStatus -eq "BLOCKED_BY_WAVE_QUALITY_BUDGET" -or $ticketState -match '(?i)READY|IN_PROGRESS|REVIEW_READY') { $processStage = "improve" }
    elseif ($failedPolicyChecks -gt 0 -or $failedFinalChecks -gt 0) { $processStage = "verify" }
    elseif ($acceptedWaves -gt 0 -and $acceptedWaves -ge $waveItems.Count -and $waveItems.Count -gt 0) { $processStage = "accept" }
    elseif ($null -ne $outcomeMetrics -and [bool](Get-PropertyValue $outcomeMetrics @("hardGatePassed") $false)) { $processStage = "verify" }

    $processOrder = @("plan", "draft", "improve", "verify", "accept")
    $currentProcessIndex = [array]::IndexOf($processOrder, $processStage)
    $processSteps = New-Object System.Collections.Generic.List[object]
    for ($processIndex = 0; $processIndex -lt $processOrder.Count; $processIndex++) {
        $stepId = $processOrder[$processIndex]
        $stepKind = "pending"
        if ($processIndex -lt $currentProcessIndex) { $stepKind = "done" }
        elseif ($processIndex -eq $currentProcessIndex) { $stepKind = "current" }
        $processSteps.Add([ordered]@{ id = $stepId; kind = $stepKind }) | Out-Null
    }

    return [ordered]@{
        schemaVersion = 2
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        workspace = $WorkspacePath
        languageDefault = $DefaultLanguage
        i18nLanguages = @("en", "ru")
        refresh = [ordered]@{
            watchMode = $WatchMode
            intervalSeconds = $effectiveRefreshSeconds
        }
        run = [ordered]@{
            runId = $runId
            status = $runStatus
            mode = [string](Get-PropertyValue $runState @("mode") "")
            continuationMode = $continuousMode
            continuousRequested = $continuousRequested
            taskTitle = [string](Get-PropertyValue $runState @("taskTitle") "")
            goal = [string](Get-PropertyValue $runState @("goal") "")
            createdAtUtc = [string](Get-PropertyValue $runState @("createdAtUtc") "")
            sourcePath = $sourcePath
        }
        humanStatus = [ordered]@{
            tone = $tone
            headlineKey = $headlineKey
            summaryKey = $summaryKey
            reasonKey = $reasonKey
            interventionKey = $interventionKey
            interventionTone = $interventionTone
            terminal = $isTerminal
            mustContinue = $mustContinue
            nextAction = $nextAction
        }
        progress = [ordered]@{
            estimatedProcessPercent = $estimatedProcessPercent
            draftCoveragePercent = $draftCoveragePercent
            acceptedPercent = $acceptedPercent
            totalWaves = $waveItems.Count
            currentWaveIndex = $currentWaveIndex
            currentWaveId = $currentWaveId
            migratedWaves = $migratedWaves
            acceptedWaves = $acceptedWaves
            totalTests = $totalTests
            draftTests = $actualDraftTests
            acceptedTests = $acceptedTests
        }
        process = [ordered]@{
            currentStage = $processStage
            steps = $processSteps.ToArray()
        }
        testPreviews = $testPreviews.ToArray()
        currentWork = [ordered]@{
            ticketId = $ticketId
            title = $ticketTitle
            goal = $ticketGoal
            status = $ticketState
            continuationStatus = $continuationStatus
        }
        quality = [ordered]@{
            status = $qualityStatus
            routing = [string](Get-PropertyValue $qualityBudget @("routing") "")
            scopeMismatch = $scopeMismatch
            scopeIntegrity = $scopeIntegrity
            metrics = $qualityMetrics
            outcomeMetrics = $outcomeMetrics
            violations = $violations
            manager = [ordered]@{
                decision = [string](Get-PropertyValue $waveManagerDecision @("decision") "PENDING")
                selectedPattern = [string](Get-PropertyValue $waveManagerDecision @("selectedPattern") "")
                reason = [string](Get-PropertyValue $waveManagerDecision @("reason") "")
                hardGatePassed = [bool](Get-PropertyValue $outcomeMetrics @("hardGatePassed") $false)
                readyTests = Get-IntValue $outcomeMetrics @("readyTests") 0
                draftTests = Get-IntValue $outcomeMetrics @("draftTests") 0
                emptyTests = Get-IntValue $outcomeMetrics @("emptyTests") 0
                rootBlockingPatterns = Get-IntValue $outcomeMetrics @("rootBlockingPatterns") 0
                blockingTodoCount = Get-IntValue $outcomeMetrics @("blockingTodoCount") 0
                cascadeTodoCount = Get-IntValue $outcomeMetrics @("cascadeTodoCount") 0
                assertionPreservationRate = Get-PropertyValue $outcomeMetrics @("assertionPreservationRate") 0
                behaviorPresenceRate = Get-PropertyValue $outcomeMetrics @("behaviorPresenceRate") 0
                behaviorlessTests = Get-PropertyValue $outcomeMetrics @("behaviorlessTests") @()
                remainingRemediationCycles = Get-IntValue $outcomeMetrics @("remainingRemediationCycles") 0
                acceptanceStatus = [string](Get-PropertyValue $currentWaveAcceptance @("status") "NOT_ACCEPTED")
            }
        }
        gates = [ordered]@{
            policyStatus = [string](Get-PropertyValue $policyResult @("status") "UNKNOWN")
            finalGateStatus = [string](Get-PropertyValue $finalGate @("status") "UNKNOWN")
            failedPolicyChecks = $failedPolicyChecks
            failedFinalChecks = $failedFinalChecks
            policyChecks = $policyChecks
            finalChecks = $finalChecks
        }
        metrics = [ordered]@{
            events = $events.Count
            traceEvents = $traceEvents.Count
            permissionAsks = $permissionAsks
            scopeViolations = $scopeViolations
        }
        waves = $waveItems.ToArray()
        recentActivity = $recentEvents
        artifacts = [ordered]@{
            dashboardJson = "harness-dashboard.json"
            dashboardMarkdown = "harness-dashboard.md"
            policyResult = "../../state/harness-policy-result.md"
            finalGate = "../../state/final-gate.md"
            qualityBudget = "../../state/wave-quality-budget.md"
            continuation = "../../state/continuation-decision.md"
            trace = "../../runs/$runId/trace.jsonl"
        }
    }
}

function Write-DashboardFiles {
    param(
        [string]$WorkspacePath,
        [string]$OutPath,
        [string]$DefaultLanguage,
        [bool]$WatchMode,
        [int]$IntervalSeconds,
        $I18n
    )

    $dashboard = New-DashboardSnapshot $WorkspacePath $OutPath $DefaultLanguage $WatchMode $IntervalSeconds
    $jsonOut = Join-Path $OutPath "harness-dashboard.json"
    $mdOut = Join-Path $OutPath "harness-dashboard.md"
    $htmlOut = Join-Path $OutPath "index.html"
    $dashboard | ConvertTo-Json -Depth 40 | Set-Content -Path $jsonOut -Encoding UTF8

    $md = @(
        "# Migration Progress Dashboard",
        "",
        "Run: $($dashboard.run.runId)",
        "Status: $($dashboard.run.status)",
        "Current wave: $($dashboard.progress.currentWaveId) ($($dashboard.progress.currentWaveIndex)/$($dashboard.progress.totalWaves))",
        "Draft coverage: $($dashboard.progress.draftCoveragePercent)%",
        "Accepted progress: $($dashboard.progress.acceptedPercent)%",
        "Quality status: $($dashboard.quality.status)",
        "Current ticket: $($dashboard.currentWork.ticketId) $($dashboard.currentWork.title)",
        "",
        "## What happens next",
        "",
        $(if ([string]::IsNullOrWhiteSpace([string]$dashboard.humanStatus.nextAction)) { "No explicit next action is recorded." } else { [string]$dashboard.humanStatus.nextAction }),
        "",
        "Generated at UTC: $($dashboard.generatedAtUtc)",
        "",
        "Machine-readable statuses remain language-neutral. The HTML page explains them in plain English or Russian."
    )
    $md | Set-Content -Path $mdOut -Encoding UTF8

    $dataJson = JsonForHtmlScript $dashboard
    $i18nJson = JsonForHtmlScript $I18n
    $defaultLanguage = HtmlEncode $DefaultLanguage

    $htmlTemplate = @'
<!doctype html>
<html lang="__DEFAULT_LANGUAGE__">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <title>Migration Progress</title>
  <style>
    :root { color-scheme: light; font-family: Inter, "Segoe UI", system-ui, -apple-system, sans-serif; --bg:#f4f7fb; --surface:#ffffff; --ink:#152033; --muted:#64748b; --line:#dce4ef; --accent:#3268e8; --accent-soft:#eaf0ff; --good:#17845b; --good-soft:#e8f7f1; --warn:#b66b08; --warn-soft:#fff4df; --bad:#c23d4b; --bad-soft:#ffebee; --shadow:0 16px 44px rgba(28,52,86,.10); }
    * { box-sizing:border-box; }
    body { margin:0; background:var(--bg); color:var(--ink); }
    button, select { font:inherit; }
    .shell { max-width:1180px; margin:0 auto; padding:24px 24px 56px; }
    .topbar { display:flex; justify-content:space-between; align-items:center; gap:16px; margin-bottom:20px; }
    .brand { display:flex; align-items:center; gap:12px; font-weight:760; letter-spacing:-.02em; }
    .brand-mark { width:36px; height:36px; display:grid; place-items:center; border-radius:11px; background:var(--ink); color:#fff; font-weight:800; }
    .controls { display:flex; align-items:center; gap:10px; color:var(--muted); font-size:14px; }
    select { border:1px solid var(--line); background:var(--surface); color:var(--ink); padding:8px 10px; border-radius:10px; }
    .live { display:inline-flex; align-items:center; gap:7px; padding:7px 10px; border:1px solid var(--line); border-radius:999px; background:var(--surface); }
    .live-dot { width:8px; height:8px; border-radius:50%; background:#94a3b8; }
    .live.is-live .live-dot { background:var(--good); box-shadow:0 0 0 5px rgba(23,132,91,.12); }
    .hero { position:relative; overflow:hidden; border-radius:24px; background:var(--surface); box-shadow:var(--shadow); border:1px solid rgba(220,228,239,.9); padding:28px; }
    .hero::after { content:""; position:absolute; width:280px; height:280px; right:-100px; top:-130px; border-radius:50%; background:var(--accent-soft); opacity:.9; }
    .hero.attention::after { background:var(--warn-soft); }
    .hero.danger::after { background:var(--bad-soft); }
    .hero.success::after { background:var(--good-soft); }
    .hero-grid { position:relative; z-index:1; display:grid; grid-template-columns:1.5fr .75fr; gap:32px; align-items:center; }
    .eyeline { color:var(--muted); font-size:14px; margin-bottom:8px; }
    h1 { margin:0; max-width:760px; font-size:clamp(30px,4.2vw,50px); line-height:1.08; letter-spacing:-.045em; }
    .lead { margin:14px 0 0; max-width:760px; color:#42526a; font-size:18px; line-height:1.55; }
    .reason { margin-top:18px; display:flex; align-items:flex-start; gap:10px; color:var(--muted); line-height:1.5; }
    .intervention { display:inline-flex; align-items:center; gap:7px; margin-top:12px; padding:7px 10px; border-radius:999px; font-size:12px; font-weight:750; background:var(--good-soft); color:var(--good); }
    .intervention::before { content:""; width:7px; height:7px; border-radius:50%; background:currentColor; }
    .intervention.attention { background:var(--warn-soft); color:var(--warn); }
    .intervention.danger { background:var(--bad-soft); color:var(--bad); }
    .progress-orb { justify-self:end; width:180px; height:180px; border-radius:50%; display:grid; place-items:center; background:conic-gradient(var(--accent) calc(var(--p)*1%), #e7edf5 0); position:relative; }
    .attention .progress-orb { background:conic-gradient(var(--warn) calc(var(--p)*1%), #f2e8d8 0); }
    .success .progress-orb { background:conic-gradient(var(--good) calc(var(--p)*1%), #dcefe7 0); }
    .progress-orb::after { content:""; position:absolute; inset:14px; border-radius:50%; background:var(--surface); }
    .orb-copy { position:relative; z-index:1; text-align:center; }
    .orb-value { display:block; font-size:42px; font-weight:800; letter-spacing:-.05em; }
    .orb-label { display:block; max-width:110px; color:var(--muted); font-size:12px; line-height:1.25; }
    .section { margin-top:22px; }
    .section-title { display:flex; align-items:center; gap:8px; margin:0 0 12px; font-size:20px; letter-spacing:-.02em; }
    .panel { background:var(--surface); border:1px solid var(--line); border-radius:18px; padding:20px; }
    .grid { display:grid; grid-template-columns:repeat(3,minmax(0,1fr)); gap:14px; }
    .metric { padding:18px; background:var(--surface); border:1px solid var(--line); border-radius:16px; min-width:0; }
    .metric-label { display:flex; align-items:center; gap:7px; color:var(--muted); font-size:13px; }
    .metric-value { margin-top:8px; font-size:29px; font-weight:780; letter-spacing:-.035em; }
    .metric-note { margin-top:5px; color:var(--muted); font-size:12px; line-height:1.4; }
    .bar { margin-top:12px; height:8px; border-radius:999px; background:#e8edf4; overflow:hidden; }
    .bar > span { display:block; height:100%; border-radius:inherit; background:var(--accent); }
    .bar.good > span { background:var(--good); }
    .now { display:grid; grid-template-columns:1fr 1fr; gap:14px; }
    .now-card { padding:20px; border:1px solid var(--line); border-radius:16px; background:var(--surface); }
    .now-card h3 { display:flex; align-items:center; gap:7px; margin:0 0 10px; font-size:16px; }
    .now-main { font-size:19px; font-weight:700; line-height:1.35; }
    .now-sub { margin-top:7px; color:var(--muted); line-height:1.5; overflow-wrap:anywhere; }
    .wave-list { display:grid; gap:8px; }
    .wave-row { display:grid; grid-template-columns:34px 1fr auto auto; gap:12px; align-items:center; padding:12px 10px; border-radius:12px; }
    .wave-row.current { background:var(--accent-soft); }
    .wave-row.attention { background:var(--warn-soft); }
    .wave-index { width:30px; height:30px; display:grid; place-items:center; border-radius:50%; background:#edf1f6; color:var(--muted); font-size:12px; font-weight:700; }
    .wave-row.current .wave-index { background:var(--accent); color:#fff; }
    .wave-row.attention .wave-index { background:var(--warn); color:#fff; }
    .process { display:grid; grid-template-columns:repeat(5,minmax(0,1fr)); gap:8px; }
    .process-step { position:relative; min-height:128px; padding:16px; border:1px solid var(--line); border-radius:14px; background:var(--surface); }
    .process-step.current { background:var(--accent-soft); border-color:#b8caff; }
    .process-step.done { background:var(--good-soft); border-color:#bce5d5; }
    .process-num { width:28px; height:28px; display:grid; place-items:center; border-radius:50%; background:#e9eef5; color:var(--muted); font-size:12px; font-weight:800; }
    .process-step.current .process-num { background:var(--accent); color:#fff; }
    .process-step.done .process-num { background:var(--good); color:#fff; }
    .process-title { display:flex; align-items:center; gap:6px; margin-top:12px; font-weight:750; }
    .process-copy { margin-top:7px; color:var(--muted); font-size:12px; line-height:1.45; }
    .preview-list { display:grid; gap:12px; }
    .preview-file { border:1px solid var(--line); border-radius:12px; overflow:hidden; }
    .preview-file summary { padding:12px 14px; background:#f8fafc; font-family:Consolas,ui-monospace,monospace; font-size:12px; overflow-wrap:anywhere; }
    pre { margin:0; padding:14px; overflow:auto; max-height:360px; background:#101827; color:#dbe7f5; font:12px/1.55 Consolas,ui-monospace,monospace; tab-size:4; }
    .wave-name { font-weight:700; }
    .wave-meta { color:var(--muted); font-size:12px; margin-top:3px; }
    .status { display:inline-flex; align-items:center; gap:6px; border-radius:999px; padding:6px 9px; font-size:12px; font-weight:700; background:#edf1f6; color:#58677a; }
    .status::before { content:""; width:7px; height:7px; border-radius:50%; background:currentColor; }
    .status.success { background:var(--good-soft); color:var(--good); }
    .status.attention { background:var(--warn-soft); color:var(--warn); }
    .status.danger { background:var(--bad-soft); color:var(--bad); }
    .status.progress,.status.current { background:var(--accent-soft); color:var(--accent); }
    .hint { width:20px; height:20px; padding:0; display:inline-grid; place-items:center; border-radius:50%; border:1px solid #cbd5e1; background:#fff; color:#64748b; font-size:12px; font-weight:800; cursor:help; position:relative; flex:0 0 auto; }
    .hint::after { content:attr(data-hint); position:absolute; z-index:20; width:min(300px,70vw); left:50%; bottom:calc(100% + 9px); transform:translateX(-50%) translateY(4px); padding:10px 12px; border-radius:10px; background:#172033; color:#fff; font-size:12px; font-weight:500; line-height:1.45; opacity:0; pointer-events:none; transition:.15s ease; box-shadow:0 12px 30px rgba(0,0,0,.2); }
    .hint:hover::after,.hint:focus::after,.hint.open::after { opacity:1; transform:translateX(-50%) translateY(0); }
    details { border-top:1px solid var(--line); padding:13px 0; }
    details:first-child { border-top:0; }
    summary { cursor:pointer; font-weight:700; display:flex; align-items:center; gap:7px; }
    table { width:100%; border-collapse:collapse; margin-top:12px; font-size:13px; }
    th,td { text-align:left; padding:9px 8px; border-bottom:1px solid var(--line); vertical-align:top; }
    th { color:var(--muted); font-size:11px; text-transform:uppercase; letter-spacing:.05em; }
    code { font-family:Consolas,ui-monospace,monospace; font-size:.92em; }
    .empty { color:var(--muted); padding:10px 0; }
    .activity { display:grid; gap:10px; }
    .activity-item { display:grid; grid-template-columns:8px 1fr auto; gap:10px; align-items:start; }
    .activity-dot { width:8px; height:8px; border-radius:50%; margin-top:6px; background:#94a3b8; }
    .activity-title { font-size:13px; font-weight:700; }
    .activity-detail { color:var(--muted); font-size:12px; line-height:1.4; margin-top:2px; }
    .activity-time { color:var(--muted); font-size:11px; white-space:nowrap; }
    .footer { margin-top:22px; color:var(--muted); font-size:12px; display:flex; justify-content:space-between; gap:16px; flex-wrap:wrap; }
    .links { display:flex; gap:14px; flex-wrap:wrap; }
    a { color:var(--accent); }
    @media (max-width:820px) { .process{grid-template-columns:1fr}.hero-grid{grid-template-columns:1fr}.progress-orb{justify-self:start;width:138px;height:138px}.orb-value{font-size:34px}.grid{grid-template-columns:1fr}.now{grid-template-columns:1fr}.wave-row{grid-template-columns:34px 1fr}.wave-row .status,.wave-row .wave-count{grid-column:2}.topbar{align-items:flex-start;flex-direction:column}.controls{width:100%;justify-content:space-between}.shell{padding:16px 14px 40px}.hero{padding:22px} }
    @media (prefers-reduced-motion:reduce) { * { transition:none!important; scroll-behavior:auto!important; } }
  </style>
</head>
<body>
  <script id="dashboard-data" type="application/json">__DASHBOARD_DATA_JSON__</script>
  <script id="dashboard-i18n" type="application/json">__DASHBOARD_I18N_JSON__</script>
  <main class="shell">
    <div class="topbar">
      <div class="brand"><div class="brand-mark">M</div><span data-i18n="dashboard.title">Migration Progress</span></div>
      <div class="controls">
        <div id="liveBadge" class="live"><span class="live-dot"></span><span id="liveText"></span></div>
        <label><span data-i18n="language.label">Language</span> <select id="languageSelect"><option value="en">English</option><option value="ru">Русский</option></select></label>
      </div>
    </div>
    <section id="hero" class="hero">
      <div class="hero-grid">
        <div>
          <div class="eyeline" id="locationLine"></div>
          <h1 id="headline"></h1>
          <p class="lead" id="summary"></p>
          <div class="reason"><span id="reason"></span><span id="reasonHint"></span></div>
          <div id="interventionBadge" class="intervention"></div>
        </div>
        <div class="progress-orb" id="progressOrb"><div class="orb-copy"><span class="orb-value" id="processPercent"></span><span class="orb-label" id="processLabel"></span></div></div>
      </div>
    </section>

    <section class="section">
      <h2 class="section-title"><span data-i18n="progress.title">Progress</span><span id="progressHint"></span></h2>
      <div class="grid" id="metricsGrid"></div>
    </section>

    <section class="section">
      <h2 class="section-title"><span data-i18n="now.title">What is happening now</span><span id="nowHint"></span></h2>
      <div class="now" id="nowGrid"></div>
    </section>

    <section class="section">
      <h2 class="section-title"><span data-i18n="process.title">How the migration works</span><span id="processHint"></span></h2>
      <div class="process" id="processGuide"></div>
    </section>

    <section class="section">
      <h2 class="section-title"><span data-i18n="waves.title">Migration waves</span><span id="wavesHint"></span></h2>
      <div class="panel"><div class="wave-list" id="waveList"></div></div>
    </section>

    <section class="section">
      <h2 class="section-title"><span data-i18n="details.title">Details</span><span id="detailsHint"></span></h2>
      <div class="panel">
        <details open><summary><span data-i18n="quality.title">Quality gate</span><span id="qualityHint"></span></summary><div id="qualityDetails"></div></details>
        <details><summary><span data-i18n="gates.title">Safety checks</span><span id="gatesHint"></span></summary><div id="gatesDetails"></div></details>
        <details><summary><span data-i18n="previews.title">What the migrated tests look like</span><span id="previewsHint"></span></summary><div id="previewDetails"></div></details>
        <details><summary><span data-i18n="activity.title">Recent activity</span><span id="activityHint"></span></summary><div id="activityDetails"></div></details>
      </div>
    </section>

    <footer class="footer">
      <span id="generatedAt"></span>
      <span class="links"><a href="harness-dashboard.json" data-i18n="artifact.dashboardJson">Dashboard JSON</a><a href="harness-dashboard.md" data-i18n="artifact.dashboardMarkdown">Dashboard Markdown</a><a href="../../state/final-gate.md" data-i18n="artifact.finalGate">Final gate</a><a href="../../state/wave-quality-budget.md" data-i18n="artifact.qualityBudget">Quality budget</a></span>
    </footer>
  </main>
<script>
const data = JSON.parse(document.getElementById('dashboard-data').textContent);
const i18n = JSON.parse(document.getElementById('dashboard-i18n').textContent);
const select = document.getElementById('languageSelect');
function t(key) { return (i18n[select.value] && i18n[select.value][key]) || (i18n.en && i18n.en[key]) || key; }
function esc(value) { return String(value ?? '').replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c])); }
function hint(key) { const text=t(key); return `<button class="hint" type="button" aria-label="${esc(text)}" data-hint="${esc(text)}">?</button>`; }
function pct(value) { return `${Number(value || 0)}%`; }
function metric(labelKey, value, noteKey, hintKey, progress, good=false) { return `<div class="metric"><div class="metric-label"><span>${esc(t(labelKey))}</span>${hint(hintKey)}</div><div class="metric-value">${esc(value)}</div><div class="metric-note">${esc(t(noteKey))}</div>${progress==null?'':`<div class="bar ${good?'good':''}"><span style="width:${Number(progress)}%"></span></div>`}</div>`; }
function statusLabel(kind, raw) { const key=`wave.kind.${kind}`; const translated=t(key); return translated===key ? raw : translated; }
function renderText() { document.querySelectorAll('[data-i18n]').forEach(el => el.textContent=t(el.dataset.i18n)); }
function renderHeader() {
  const hs=data.humanStatus; const hero=document.getElementById('hero'); hero.className=`hero ${hs.tone||'calm'}`;
  document.getElementById('headline').textContent=t(hs.headlineKey);
  document.getElementById('summary').textContent=t(hs.summaryKey);
  document.getElementById('reason').textContent=t(hs.reasonKey);
  document.getElementById('reasonHint').innerHTML=hint('hint.humanStatus');
  const intervention=document.getElementById('interventionBadge');
  intervention.className=`intervention ${hs.interventionTone||'success'}`;
  intervention.innerHTML=`${esc(t(hs.interventionKey||'intervention.none'))}${hint('hint.intervention')}`;
  const wave=data.progress.currentWaveIndex||0,total=data.progress.totalWaves||0;
  document.getElementById('locationLine').textContent=`${t('run.label')} ${data.run.runId} · ${t('wave.label')} ${wave}/${total||'—'} · ${data.run.continuationMode==='continuous'||data.run.continuousRequested?t('mode.continuous'):t('mode.default')}`;
  document.getElementById('progressOrb').style.setProperty('--p',data.progress.estimatedProcessPercent||0);
  document.getElementById('processPercent').textContent=pct(data.progress.estimatedProcessPercent);
  document.getElementById('processLabel').textContent=t('progress.estimated');
  const live=document.getElementById('liveBadge'); live.classList.toggle('is-live',Boolean(data.refresh.watchMode));
  document.getElementById('liveText').textContent=data.refresh.watchMode?`${t('refresh.live')} ${data.refresh.intervalSeconds}${t('refresh.seconds')}`:t('refresh.snapshot');
}
function renderMetrics() {
  document.getElementById('progressHint').innerHTML=hint('hint.progress');
  document.getElementById('metricsGrid').innerHTML=[
    metric('metric.draftCoverage',pct(data.progress.draftCoveragePercent),'metric.draftCoverage.note','hint.draftCoverage',data.progress.draftCoveragePercent),
    metric('metric.acceptedProgress',pct(data.progress.acceptedPercent),'metric.acceptedProgress.note','hint.acceptedProgress',data.progress.acceptedPercent,true),
    metric('metric.tests',`${data.progress.draftTests}/${data.progress.totalTests||'—'}`,'metric.tests.note','hint.tests',null)
  ].join('');
}
function renderNow() {
  document.getElementById('nowHint').innerHTML=hint('hint.now');
  const ticketTitle=data.currentWork.title||t('now.noTicket');
  const ticketId=data.currentWork.ticketId?`<code>${esc(data.currentWork.ticketId)}</code> · `:'';
  const next=data.humanStatus.nextAction||t('now.noNextAction');
  document.getElementById('nowGrid').innerHTML=`<div class="now-card"><h3>${esc(t('now.currentTask'))}${hint('hint.currentTask')}</h3><div class="now-main">${ticketId}${esc(ticketTitle)}</div><div class="now-sub">${esc(data.currentWork.goal||`${t('status.label')}: ${data.currentWork.status||data.currentWork.continuationStatus}`)}</div></div><div class="now-card"><h3>${esc(t('now.nextStep'))}${hint('hint.nextStep')}</h3><div class="now-main">${esc(t('now.automatic'))}</div><div class="now-sub">${esc(next)}</div></div>`;
}
function renderProcess() {
  document.getElementById('processHint').innerHTML=hint('hint.process');
  const steps=(data.process&&data.process.steps)||[];
  document.getElementById('processGuide').innerHTML=steps.map((step,index)=>`<div class="process-step ${esc(step.kind)}"><div class="process-num">${step.kind==='done'?'✓':index+1}</div><div class="process-title">${esc(t(`process.${step.id}.title`))}${hint(`hint.process.${step.id}`)}</div><div class="process-copy">${esc(t(`process.${step.id}.copy`))}</div></div>`).join('');
}
function renderWaves() {
  document.getElementById('wavesHint').innerHTML=hint('hint.waves');
  const waves=data.waves||[];
  document.getElementById('waveList').innerHTML=waves.length?waves.map(w=>`<div class="wave-row ${esc(w.kind)}"><div class="wave-index">${w.index}</div><div><div class="wave-name">${esc(w.id)}${w.current?` · ${esc(t('wave.current'))}`:''}</div><div class="wave-meta">${esc(w.cluster||w.phase||t('wave.noCluster'))}</div></div><div class="wave-count">${w.testCount} ${esc(t('wave.tests'))} · ${w.fileCount} ${esc(t('wave.files'))}</div><span class="status ${esc(w.kind)}">${esc(statusLabel(w.kind,w.status))}</span></div>`).join(''):`<div class="empty">${esc(t('waves.empty'))}</div>`;
}
function table(rows, headers) { return `<table><thead><tr>${headers.map(h=>`<th>${esc(h)}</th>`).join('')}</tr></thead><tbody>${rows.join('')}</tbody></table>`; }
function renderDetails() {
  document.getElementById('detailsHint').innerHTML=hint('hint.details');
  document.getElementById('qualityHint').innerHTML=hint('hint.quality');
  document.getElementById('gatesHint').innerHTML=hint('hint.gates');
  document.getElementById('previewsHint').innerHTML=hint('hint.previews');
  document.getElementById('activityHint').innerHTML=hint('hint.activity');
  const violations=data.quality.violations||[];
  const manager=data.quality.manager||{};
  const outcome=data.quality.outcomeMetrics||{};
  const managerSummary=`<div class="now-card"><h3>${esc(t('quality.manager.title'))}${hint('hint.quality.manager')}</h3><div class="now-main">${esc(t('quality.manager.decision'))}: <code>${esc(manager.decision||'PENDING')}</code></div><div class="now-sub">${esc(t('quality.manager.readiness'))}: ${esc(manager.readyTests||0)}/${esc((manager.readyTests||0)+(manager.draftTests||0))} · ${esc(t('quality.manager.roots'))}: ${esc(manager.rootBlockingPatterns||0)} · ${esc(t('quality.manager.acceptance'))}: <code>${esc(manager.acceptanceStatus||'NOT_ACCEPTED')}</code></div><div class="now-sub">${esc(manager.reason||t('quality.manager.pending'))}</div></div>`;
  const metricRows=[
    [t('quality.metrics.readyDraft'),`${outcome.readyTests||0} / ${outcome.draftTests||0} / ${outcome.emptyTests||0}`],
    [t('quality.metrics.todos'),`${outcome.blockingTodoCount||0} / ${outcome.rootBlockingPatterns||0} / ${outcome.cascadeTodoCount||0} / ${outcome.softTodoCount||0}`],
    [t('quality.metrics.actions'),`${outcome.reportedSemanticActions??'—'} / ${outcome.reportedSyntaxFallbackActions??'—'} / ${outcome.reportedActions??'—'}`],
    [t('quality.metrics.unmapped'),`${outcome.reportedUnmappedTargets??'—'}`],
    [t('quality.metrics.assertions'),`${Math.round((outcome.assertionPreservationRate||0)*100)}%`],
    [t('quality.metrics.behavior'),`${Math.round((outcome.behaviorPresenceRate||0)*100)}% · ${(outcome.behaviorlessTests||[]).length}`],
    [t('quality.metrics.awaits'),`${outcome.activeAwaitActions||0}`]
  ].map(row=>`<tr><td>${esc(row[0])}</td><td><code>${esc(row[1])}</code></td></tr>`);
  const metricSummary=`<h3>${esc(t('quality.metrics.title'))}${hint('hint.quality.metrics')}</h3>${table(metricRows,[t('quality.metrics.name'),t('quality.metrics.value')])}`;
  const scopeNote=data.quality.scopeMismatch?`<p class="now-sub"><strong>${esc(t('quality.scopeMismatch'))}</strong> ${esc(t('quality.scopeMismatch.explain'))}</p>`:'';
  const violationRows=violations.map(v=>`<tr><td><code>${esc(v.metric)}</code></td><td>${esc(v.actual)}</td><td>${esc(v.budget)}</td><td><span class="status ${v.severity==='high'?'attention':'pending'}">${esc(v.severity)}</span></td></tr>`);
  document.getElementById('qualityDetails').innerHTML=`${managerSummary}${metricSummary}<p><span class="status ${data.quality.status==='PASS'?'success':'attention'}">${esc(data.quality.status||t('status.unknown'))}</span></p>${scopeNote}${violationRows.length?table(violationRows,[t('quality.metric'),t('quality.actual'),t('quality.budget'),t('quality.severity')]):`<div class="empty">${esc(t('quality.noViolations'))}</div>`}`;
  const checks=[...(data.gates.policyChecks||[]),...(data.gates.finalChecks||[])];
  const checkRows=checks.map(c=>`<tr><td><code>${esc(c.name)}</code></td><td><span class="status ${c.passed?'success':'attention'}">${esc(c.passed?t('status.pass'):t('status.fail'))}</span></td><td>${esc(c.detail||'')}</td></tr>`);
  document.getElementById('gatesDetails').innerHTML=checkRows.length?table(checkRows,[t('gates.check'),t('status.label'),t('events.detail')]):`<div class="empty">${esc(t('gates.empty'))}</div>`;
  const previews=data.testPreviews||[];
  document.getElementById('previewDetails').innerHTML=previews.length?`<div class="preview-list">${previews.map((p,i)=>`<details class="preview-file" ${i===0?'open':''}><summary>${esc(p.path)}</summary><pre><code>${esc(p.snippet||'')}</code></pre></details>`).join('')}</div>`:`<div class="empty">${esc(t('previews.empty'))}</div>`;
  const activity=data.recentActivity||[];
  document.getElementById('activityDetails').innerHTML=activity.length?`<div class="activity">${activity.map(e=>`<div class="activity-item"><span class="activity-dot"></span><div><div class="activity-title">${esc(e.action||e.phase||t('activity.event'))}</div><div class="activity-detail">${esc(e.detail||e.status||'')}</div></div><div class="activity-time">${esc(e.utc||'')}</div></div>`).join('')}</div>`:`<div class="empty">${esc(t('activity.empty'))}</div>`;
}
function bindHints() { document.querySelectorAll('.hint').forEach(btn=>btn.addEventListener('click',e=>{e.stopPropagation();document.querySelectorAll('.hint.open').forEach(x=>{if(x!==btn)x.classList.remove('open')});btn.classList.toggle('open')})); document.addEventListener('click',()=>document.querySelectorAll('.hint.open').forEach(x=>x.classList.remove('open')),{once:true}); }
function renderAll() { document.documentElement.lang=select.value; renderText(); renderHeader(); renderMetrics(); renderNow(); renderProcess(); renderWaves(); renderDetails(); document.getElementById('generatedAt').textContent=`${t('generatedAt')}: ${data.generatedAtUtc}`; localStorage.setItem('harnessDashboardLanguage',select.value); bindHints(); }
select.value=localStorage.getItem('harnessDashboardLanguage')||data.languageDefault||'en'; select.addEventListener('change',renderAll); renderAll();
if(data.refresh.watchMode&&data.refresh.intervalSeconds>0){setTimeout(()=>location.reload(),data.refresh.intervalSeconds*1000);}
</script>
</body>
</html>
'@

    $html = $htmlTemplate.Replace("__DEFAULT_LANGUAGE__", $defaultLanguage).Replace("__DASHBOARD_DATA_JSON__", $dataJson).Replace("__DASHBOARD_I18N_JSON__", $i18nJson)
    $html | Set-Content -Path $htmlOut -Encoding UTF8
    return $htmlOut
}

$repoRoot = (Get-Location).Path
$workspacePath = Resolve-FromRoot -PathValue $Workspace -BasePath $repoRoot
if (-not (Test-Path $workspacePath)) { throw "Migration workspace was not found: $workspacePath" }
$outPath = Resolve-FromRoot -PathValue $Out -BasePath $workspacePath
New-Item -ItemType Directory -Force -Path $outPath | Out-Null

# Installed dictionaries: migration/dashboard/i18n/en.json and migration/dashboard/i18n/ru.json
$i18nDir = Join-Path (Join-Path $workspacePath "dashboard") "i18n"
$enPath = Join-Path $i18nDir "en.json"
$ruPath = Join-Path $i18nDir "ru.json"
$en = Read-JsonFileOrNull $enPath
$ru = Read-JsonFileOrNull $ruPath
if ($null -eq $en -or $null -eq $ru) { throw "Dashboard i18n dictionaries are missing. Expected: $enPath and $ruPath" }
$i18n = [ordered]@{ en = $en; ru = $ru }

if ($Watch) {
    Write-Host "HARNESS_DASHBOARD_WATCH_STARTED: refresh every $RefreshSeconds second(s). Press Ctrl+C to stop."
    while ($true) {
        $htmlOut = Write-DashboardFiles $workspacePath $outPath $Language $true $RefreshSeconds $i18n
        Write-Host "HARNESS_DASHBOARD_REFRESHED: $((Get-Date).ToString('HH:mm:ss')) $htmlOut"
        Start-Sleep -Seconds $RefreshSeconds
    }
}
else {
    $htmlOut = Write-DashboardFiles $workspacePath $outPath $Language $false $RefreshSeconds $i18n
    Write-Host "HARNESS_DASHBOARD_WRITTEN: $htmlOut"
    Write-Host "Dashboard JSON: $(Join-Path $outPath 'harness-dashboard.json')"
    Write-Host "Dashboard Markdown: $(Join-Path $outPath 'harness-dashboard.md')"
    Write-Host "For live refresh: .\migration\scripts\build-harness-dashboard.ps1 -Workspace migration -Out dashboard/harness -Language $Language -Watch -RefreshSeconds $RefreshSeconds"
}
