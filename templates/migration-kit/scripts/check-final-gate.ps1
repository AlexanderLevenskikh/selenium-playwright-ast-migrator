param(
    [string]$Workspace = "migration",
    [string]$RepoRoot = ".",
    [string[]]$AllowedRoots = @($Workspace)
)

$ErrorActionPreference = "Stop"

function Add-Result($Results, [string]$Name, [bool]$Passed, [string]$Detail) {
    $Results.Add([ordered]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    })
}

function Read-TextIfExists([string]$Path) {
    if (Test-Path $Path) {
        return Get-Content -Raw -Path $Path
    }

    return ""
}

function Find-LatestFile([string]$Root, [string[]]$Names) {
    if (-not (Test-Path $Root)) {
        return $null
    }

    Get-ChildItem -Path $Root -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $Names -contains $_.Name } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
}

function Find-RunIds([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    $matches = [regex]::Matches($Text, '(?i)\brun[-_][0-9A-Za-z][0-9A-Za-z._-]*\b')
    return @($matches | ForEach-Object { $_.Value.ToLowerInvariant() } | Sort-Object -Unique)
}

function Test-JsonStatusPassed([string]$Path) {
    try {
        $json = Get-Content -Raw -Path $Path | ConvertFrom-Json
        $text = ($json | ConvertTo-Json -Depth 100 -Compress)
        if ($text -match '(?i)"(status|result|outcome)"\s*:\s*"(passed|pass|success|succeeded|ok)"') {
            return $true
        }

        if ($text -match '(?i)"(errorCount|errors|failed|failureCount)"\s*:\s*0\b') {
            return $true
        }
    }
    catch {
        return $false
    }

    return $false
}

function Test-JsonDiagnosticsRecorded([string]$Path) {
    try {
        $json = Get-Content -Raw -Path $Path | ConvertFrom-Json
        $text = ($json | ConvertTo-Json -Depth 100 -Compress)
        return $text -match '(?i)diagnostic|error|warning|validation'
    }
    catch {
        return $false
    }
}

function Test-TextLooksPassed([string]$Text) {
    return $Text -match '(?i)\b(pass|passed|success|succeeded|ok)\b' -and $Text -notmatch '(?i)\b(fail|failed|error)\b'
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path $RepoRoot $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)
$results = New-Object System.Collections.Generic.List[object]

$scopeScript = Join-Path $workspacePath "scripts/check-scope.ps1"
if (Test-Path $scopeScript) {
    & $scopeScript -RepoRoot $RepoRoot -AllowedRoots $AllowedRoots | Out-Host
    Add-Result $results "scope-guard" ($LASTEXITCODE -eq 0) "check-scope.ps1 exit code $LASTEXITCODE"
}
else {
    Add-Result $results "scope-guard" $false "missing $scopeScript"
}

$stateFiles = @(
    Join-Path $workspacePath "agent-state.md",
    Join-Path $workspacePath "current-ticket.md",
    Join-Path $workspacePath "state/run-ledger.md"
)
$boardFile = Find-LatestFile -Root $workspacePath -Names @("migration-board.md", "migration-board.json")
if ($boardFile -ne $null) {
    $stateFiles += $boardFile.FullName
}

$missingStateFiles = @($stateFiles | Where-Object { -not (Test-Path $_) })
if ($missingStateFiles.Count -gt 0) {
    Add-Result $results "run-id-consistency" $false ("missing files: " + ($missingStateFiles -join ", "))
}
else {
    $fileRunIds = @{}
    foreach ($file in $stateFiles) {
        $ids = Find-RunIds (Read-TextIfExists $file)
        $fileRunIds[$file] = $ids
    }

    $allIds = @($fileRunIds.Values | ForEach-Object { $_ } | Sort-Object -Unique)
    $filesWithoutIds = @($fileRunIds.Keys | Where-Object { $fileRunIds[$_].Count -eq 0 })
    if ($allIds.Count -eq 1 -and $filesWithoutIds.Count -eq 0) {
        Add-Result $results "run-id-consistency" $true "consistent run id: $($allIds[0])"
    }
    else {
        Add-Result $results "run-id-consistency" $false ("run ids: " + ($allIds -join ", ") + "; files without ids: " + ($filesWithoutIds -join ", "))
    }
}

$dashboard = Find-LatestFile -Root $workspacePath -Names @("migration-quality-dashboard.json", "migration-board.json")
if ($dashboard -eq $null) {
    Add-Result $results "quality-dashboard" $false "missing migration-quality-dashboard.json or migration-board.json"
}
else {
    $dashboardText = Read-TextIfExists $dashboard.FullName
    $dangerousPatterns = @(
        "DANGEROUS_ASSERTION_SUPPRESSION",
        "ASSERTION_SUPPRESSION_BLOCKED",
        "DEPENDS_ON_SUPPRESSED_SIDE_EFFECT"
    )
    $dangerousHits = @($dangerousPatterns | Where-Object { $dashboardText -match [regex]::Escape($_) })
    Add-Result $results "quality-dangerous-categories" ($dangerousHits.Count -eq 0) ("dashboard: $($dashboard.FullName); hits: " + ($dangerousHits -join ", "))

    $emptyTestHit = $dashboardText -match "EMPTY_TEST_AFTER_SUPPRESSION"
    $emptyLooksZero = $dashboardText -match 'EMPTY_TEST_AFTER_SUPPRESSION[^0-9]{0,120}0\b'
    $classificationFile = Join-Path $workspacePath "reports/empty-test-classification.md"
    Add-Result $results "empty-test-after-suppression" ((-not $emptyTestHit) -or $emptyLooksZero -or (Test-Path $classificationFile)) ("empty category present: $emptyTestHit; classification: $classificationFile")
}

$configReport = Find-LatestFile -Root $workspacePath -Names @("config-validate-report.json", "config-validate-report.md")
if ($configReport -eq $null) {
    Add-Result $results "config-validate" $false "missing config-validate report"
}
else {
    $configText = Read-TextIfExists $configReport.FullName
    $configPassed = ($configReport.Extension -eq ".json" -and (Test-JsonStatusPassed $configReport.FullName)) -or (Test-TextLooksPassed $configText)
    $diagnosticsRecorded = ($configReport.Extension -eq ".json" -and (Test-JsonDiagnosticsRecorded $configReport.FullName)) -or ($configText -match '(?i)diagnostic|error|warning|validation')
    Add-Result $results "config-validate" ($configPassed -or $diagnosticsRecorded) ("report: $($configReport.FullName); passed: $configPassed; diagnostics recorded: $diagnosticsRecorded")
}

$projectVerify = Find-LatestFile -Root $workspacePath -Names @("project-verify-report.json", "project-verify-report.md")
$finalTexts = @(
    Read-TextIfExists (Join-Path $workspacePath "agent-state.md"),
    Read-TextIfExists (Join-Path $workspacePath "state/handoff.md"),
    Read-TextIfExists (Join-Path $workspacePath "state/stop-policy-checklist.md"),
    Read-TextIfExists (Join-Path $workspacePath "state/final-gate.md")
) -join "`n"
$notRuntimeReady = $finalTexts -match "NOT RUNTIME READY"
if ($projectVerify -eq $null) {
    Add-Result $results "project-verify-or-runtime-status" $notRuntimeReady "project verify missing; NOT RUNTIME READY: $notRuntimeReady"
}
else {
    $verifyText = Read-TextIfExists $projectVerify.FullName
    $verifyPassed = ($projectVerify.Extension -eq ".json" -and (Test-JsonStatusPassed $projectVerify.FullName)) -or (Test-TextLooksPassed $verifyText)
    Add-Result $results "project-verify-or-runtime-status" ($verifyPassed -or $notRuntimeReady) ("report: $($projectVerify.FullName); passed: $verifyPassed; NOT RUNTIME READY: $notRuntimeReady")
}

$passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
$report = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    status = if ($passed) { "PASS" } else { "FAIL" }
    workspace = $workspacePath
    checks = $results
}

$stateDir = Join-Path $workspacePath "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$jsonPath = Join-Path $stateDir "final-gate-result.json"
$mdPath = Join-Path $stateDir "final-gate-result.md"
$report | ConvertTo-Json -Depth 20 | Set-Content -Path $jsonPath -Encoding UTF8

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Final Gate Result")
[void]$md.AppendLine()
[void]$md.AppendLine("Status: **$($report.status)**")
[void]$md.AppendLine()
foreach ($check in $results) {
    $status = if ($check.passed) { "PASS" } else { "FAIL" }
    [void]$md.AppendLine("- ${status}: $($check.name) - $($check.detail)")
}
Set-Content -Path $mdPath -Value $md.ToString() -Encoding UTF8

Write-Host "FINAL_GATE_$($report.status)"
Write-Host "Report: $mdPath"

if ($passed) {
    exit 0
}

exit 1
