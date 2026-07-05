param(
    [string]$Workspace = "migration",
    [string]$RepoRoot = ".",
    [string[]]$AllowedRoots = @($Workspace),
    [switch]$RequireOpenCodeExport,
    [switch]$RequireExplainTodo,
    [switch]$RequireVerificationArtifacts
)

$ErrorActionPreference = "Stop"


function Get-PowerShellExecutable() {
    $candidates = if ($IsWindows) { @("powershell", "pwsh") } else { @("pwsh", "powershell") }
    foreach ($candidate in $candidates) {
        $command = Get-Command $candidate -ErrorAction SilentlyContinue
        if ($command -ne $null) {
            return $command.Source
        }
    }

    throw "PowerShell executable was not found. Install PowerShell 7 (`pwsh`) on non-Windows runners."
}

function Invoke-PowerShellScript([string]$ScriptPath, [string[]]$Arguments) {
    try {
        $powerShell = Get-PowerShellExecutable
        $scriptArgs = @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath) + $Arguments
        & $powerShell @scriptArgs | Out-Host
        if ($null -eq $LASTEXITCODE) {
            return 0
        }

        return $LASTEXITCODE
    }
    catch {
        Write-Warning "Failed to run ${ScriptPath}: $($_.Exception.Message)"
        return 127
    }
}

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

function Find-ExplicitLatestRunId([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    $match = [regex]::Match($Text, '(?im)^\s*(Latest run|Latest run id|Current run|Run id)\s*:\s*(run[-_][0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
    if ($match.Success) {
        return $match.Groups[2].Value.ToLowerInvariant()
    }

    return $null
}

function Test-ExplicitStatus([string]$Text, [string[]]$Statuses) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    foreach ($line in ($Text -split "\r?\n")) {
        $match = [regex]::Match($line, '^\s*(Runtime-ready|Runtime ready|Project verify|Final status|Status|Config validate|Config status)\s*:\s*(.+?)\s*$')
        if (-not $match.Success) {
            continue
        }

        $value = $match.Groups[2].Value.Trim()
        foreach ($status in $Statuses) {
            if ($value.Equals($status, [StringComparison]::OrdinalIgnoreCase)) {
                return $true
            }
        }
    }

    return $false
}

function Test-GuardChecksums([string]$WorkspacePath, [string[]]$GuardFiles, [ref]$Detail) {
    $checksumPath = Join-Path $WorkspacePath ".migration-kit/guard-checksums.json"
    if (-not (Test-Path $checksumPath)) {
        $Detail.Value = "missing $checksumPath"
        return $false
    }

    try {
        $json = Get-Content -Raw -Path $checksumPath | ConvertFrom-Json
        $expected = @{}
        foreach ($entry in @($json.files)) {
            if ($entry.path -and $entry.sha256) {
                $expected[$entry.path.ToString().Replace("\", "/")] = $entry.sha256.ToString().ToUpperInvariant()
            }
        }

        $mismatches = New-Object System.Collections.Generic.List[string]
        foreach ($guardFile in $GuardFiles) {
            $relative = $guardFile.Replace("\", "/")
            $fullPath = Join-Path $WorkspacePath $guardFile
            if (-not (Test-Path $fullPath)) {
                $mismatches.Add("$relative missing")
                continue
            }

            if (-not $expected.ContainsKey($relative)) {
                $mismatches.Add("$relative missing expected checksum")
                continue
            }

            $actual = (Get-FileHash -Algorithm SHA256 -Path $fullPath).Hash.ToUpperInvariant()
            if ($actual -ne $expected[$relative]) {
                $mismatches.Add("$relative checksum mismatch")
            }
        }

        $Detail.Value = if ($mismatches.Count -eq 0) { "guard checksums match" } else { $mismatches -join "; " }
        return $mismatches.Count -eq 0
    }
    catch {
        $Detail.Value = "checksum validation failed: $($_.Exception.Message)"
        return $false
    }
}

function Test-JsonStatusPassed([string]$Path) {
    try {
        $json = Get-Content -Raw -Path $Path | ConvertFrom-Json
        $text = ($json | ConvertTo-Json -Depth 100 -Compress)
        if ($text -match '(?i)"(status|result|outcome)"\s*:\s*"(passed|pass|success|succeeded|ok)"') {
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

function Convert-ToIntOrNull($Value) {
    if ($null -eq $Value) {
        return $null
    }

    $number = 0
    if ([int]::TryParse($Value.ToString(), [ref]$number)) {
        return $number
    }

    return $null
}

function Get-ObjectProperty($Object, [string[]]$Names) {
    if ($null -eq $Object) {
        return $null
    }

    foreach ($name in $Names) {
        $property = $Object.PSObject.Properties |
            Where-Object { $_.Name.Equals($name, [StringComparison]::OrdinalIgnoreCase) } |
            Select-Object -First 1
        if ($property -ne $null) {
            return $property.Value
        }
    }

    return $null
}

function Find-DangerousQualityHitsFromJson($Node, [string[]]$DangerousPatterns, $Hits, [ref]$StructuredSeen) {
    if ($null -eq $Node) {
        return
    }

    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string] -and $Node -isnot [System.Management.Automation.PSCustomObject]) {
        foreach ($item in $Node) {
            Find-DangerousQualityHitsFromJson $item $DangerousPatterns $Hits $StructuredSeen
        }
        return
    }

    $categoryName = Get-ObjectProperty $Node @("category", "name", "id", "key")
    if ($categoryName -ne $null) {
        foreach ($pattern in $DangerousPatterns) {
            if ($categoryName.ToString().Equals($pattern, [StringComparison]::OrdinalIgnoreCase)) {
                $count = Convert-ToIntOrNull (Get-ObjectProperty $Node @("count", "total", "value"))
                if ($count -ne $null) {
                    $StructuredSeen.Value = $true
                    if ($count -gt 0) {
                        $Hits.Add("${pattern}:$count")
                    }
                }
            }
        }
    }

    foreach ($property in $Node.PSObject.Properties) {
        foreach ($pattern in $DangerousPatterns) {
            if ($property.Name.Equals($pattern, [StringComparison]::OrdinalIgnoreCase)) {
                $count = Convert-ToIntOrNull $property.Value
                if ($count -eq $null) {
                    $count = Convert-ToIntOrNull (Get-ObjectProperty $property.Value @("count", "total", "value"))
                }

                if ($count -ne $null) {
                    $StructuredSeen.Value = $true
                    if ($count -gt 0) {
                        $Hits.Add("${pattern}:$count")
                    }
                }
            }
        }

        if ($property.Value -is [System.Management.Automation.PSCustomObject] -or
            ($property.Value -is [System.Collections.IEnumerable] -and $property.Value -isnot [string])) {
            Find-DangerousQualityHitsFromJson $property.Value $DangerousPatterns $Hits $StructuredSeen
        }
    }
}

function Find-DangerousQualityHits([string]$Path, [string]$Text, [string[]]$DangerousPatterns) {
    $hits = New-Object System.Collections.Generic.List[string]
    if ([System.IO.Path]::GetExtension($Path).Equals(".json", [StringComparison]::OrdinalIgnoreCase)) {
        try {
            $json = Get-Content -Raw -Path $Path | ConvertFrom-Json
            $structuredSeen = $false
            Find-DangerousQualityHitsFromJson $json $DangerousPatterns $hits ([ref]$structuredSeen)
            if ($structuredSeen) {
                return @($hits | Sort-Object -Unique)
            }
        }
        catch {
            # Fall back to text matching below.
        }
    }

    return @($DangerousPatterns | Where-Object { $Text -match [regex]::Escape($_) })
}

function Test-PathMatchesAnyPattern([string]$RelativePath, [string[]]$Patterns) {
    $normalized = $RelativePath.Replace("\", "/")
    foreach ($pattern in $Patterns) {
        $regex = "^" + [regex]::Escape($pattern).Replace("\*", ".*").Replace("\?", ".") + "$"
        if ($normalized -match $regex) {
            return $true
        }

        $leaf = @($normalized -split "/")[-1]
        $leafRegex = "^" + [regex]::Escape($pattern).Replace("\*", ".*").Replace("\?", ".") + "$"
        if ($leaf -match $leafRegex) {
            return $true
        }
    }

    return $false
}

function Get-RelativePathCompat([string]$BasePath, [string]$FullPath) {
    $baseFull = [System.IO.Path]::GetFullPath($BasePath)
    $targetFull = [System.IO.Path]::GetFullPath($FullPath)

    if (-not $baseFull.EndsWith([System.IO.Path]::DirectorySeparatorChar.ToString()) -and
        -not $baseFull.EndsWith([System.IO.Path]::AltDirectorySeparatorChar.ToString())) {
        $baseFull = $baseFull + [System.IO.Path]::DirectorySeparatorChar
    }

    $baseUri = New-Object System.Uri($baseFull)
    $targetUri = New-Object System.Uri($targetFull)
    $relativeUri = $baseUri.MakeRelativeUri($targetUri)
    return [System.Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', [System.IO.Path]::DirectorySeparatorChar)
}

function Test-EvidenceExists([string]$Root, [string[]]$Patterns, [string]$LatestRunId = "", [switch]$RequireLatestRun) {
    if (-not (Test-Path $Root)) {
        return $false
    }

    $rootFull = [System.IO.Path]::GetFullPath($Root)
    $items = Get-ChildItem -Path $Root -Recurse -Force -ErrorAction SilentlyContinue

    foreach ($item in $items) {
        $relative = (Get-RelativePathCompat $rootFull $item.FullName).Replace("\", "/")
        if (-not (Test-PathMatchesAnyPattern $relative $Patterns)) {
            continue
        }

        if (-not $RequireLatestRun) {
            return $true
        }

        if ([string]::IsNullOrWhiteSpace($LatestRunId)) {
            continue
        }

        if ($relative -match [regex]::Escape($LatestRunId)) {
            return $true
        }

        if (-not $item.PSIsContainer) {
            try {
                $text = Get-Content -Raw -Path $item.FullName -ErrorAction Stop
                if ($text -match [regex]::Escape($LatestRunId)) {
                    return $true
                }
            }
            catch {
                # Ignore unreadable candidate files.
            }
        }
    }

    return $false
}

function Test-TextLooksPassed([string]$Text) {
    return $Text -match '(?i)\b(pass|passed|success|succeeded|ok)\b' -and $Text -notmatch '(?i)\b(fail|failed|error)\b'
}


function Test-AllowedContinuationActionText([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    return $Text -match '(?i)(migration[\\/]|adapter-config|target-shadow|generated-pom|proposals?[\/]|discover-target|explain-todo|config-validate|verify|migration-board|quality-dashboard|current-ticket\.md|handoff\.md)'
}

function New-ContinuationCandidate([string]$Source, [string]$Action, [string]$Evidence) {
    [pscustomobject][ordered]@{
        source = $Source
        action = $Action.Trim()
        evidence = $Evidence.Trim()
    }
}

function Find-ExplicitContinuationCandidateInText([string]$Source, [string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    # Keep the newline regex escaped as text. A previous patch accidentally wrote a
    # physical newline inside the PowerShell string, which made parser errors report
    # absolute temp paths instead of reaching the continuation marker.
    foreach ($line in @($Text -split "\r?\n")) {
        $match = [regex]::Match($line, '(?i)^\s*(?:[-*]\s*)?(?:next action|one concrete next action|next bounded action|next_action_command|continue command|continuation command)\s*[:：]\s*(.+?)\s*$')
        if ($match.Success) {
            $action = $match.Groups[1].Value.Trim()
            if (Test-AllowedContinuationActionText $action) {
                return New-ContinuationCandidate $Source $action $line
            }
        }
    }

    if (Test-AllowedContinuationActionText $Text) {
        return New-ContinuationCandidate $Source "Review status files and execute the named allowed next config/scaffold/evidence action under migration/**." $Text
    }

    return $null
}

function Find-ContinuationCandidate($Sources) {
    # Normalize sources manually instead of relying on @($Sources): Windows PowerShell
    # can throw "argument types do not match" when wrapping some generic lists.
    $sourceItems = New-Object System.Collections.ArrayList
    if ($null -ne $Sources) {
        if ($Sources -is [System.Collections.IEnumerable] -and -not ($Sources -is [string])) {
            foreach ($item in $Sources) {
                [void]$sourceItems.Add($item)
            }
        }
        else {
            [void]$sourceItems.Add($Sources)
        }
    }

    foreach ($sourceInfo in $sourceItems) {
        if ($null -eq $sourceInfo) {
            continue
        }

        $sourcePath = $null
        $sourceName = $null
        if ($sourceInfo -is [string]) {
            $sourcePath = $sourceInfo
            $sourceName = $sourceInfo
        }
        elseif ($sourceInfo -is [System.IO.FileInfo]) {
            $sourcePath = $sourceInfo.FullName
            $sourceName = $sourceInfo.Name
        }
        else {
            $sourcePath = [string]$sourceInfo.path
            $sourceName = [string]$sourceInfo.name
        }

        if ([string]::IsNullOrWhiteSpace($sourcePath) -or -not (Test-Path $sourcePath)) {
            continue
        }
        if ([string]::IsNullOrWhiteSpace($sourceName)) {
            $sourceName = $sourcePath
        }

        $text = Read-TextIfExists $sourcePath
        if ([string]::IsNullOrWhiteSpace($text)) {
            continue
        }

        $lines = @($text -split '\r?\n')
        $captureHeader = $null
        for ($index = 0; $index -lt $lines.Count; $index++) {
            $line = $lines[$index]
            $trimmed = $line.Trim()
            if ([string]::IsNullOrWhiteSpace($trimmed)) {
                continue
            }

            $inline = [regex]::Match($line, '(?i)^\s*(?:[-*]\s*)?(?:next action|one concrete next action|next bounded action|next_action_command|continue command|continuation command)\s*[:：]\s*(.+?)\s*$')
            if ($inline.Success) {
                $action = $inline.Groups[1].Value.Trim()
                if (Test-AllowedContinuationActionText $action) {
                    return New-ContinuationCandidate $sourceName $action $line
                }
            }

            if ($captureHeader -ne $null -and $trimmed -notmatch '^#+\s' -and $trimmed -notmatch '^```') {
                $candidate = $trimmed.TrimStart('-', '*', ' ')
                if (Test-AllowedContinuationActionText $candidate) {
                    return New-ContinuationCandidate $sourceName $candidate $line
                }
            }

            if ($trimmed -match '(?i)^#+\s*(one concrete next action|next action|next bounded action|continuation command)\s*$') {
                $captureHeader = $trimmed
            }
        }

        $statusMatch = [regex]::Match($text, '(?is)(NOT FINAL - INVESTIGATION RESULT ONLY|NOT RUNTIME READY).{0,160}(migration[\\/][^\r\n`]+|adapter-config|target-shadow|generated-pom|discover-target|explain-todo|config-validate|verify)')
        if ($statusMatch.Success) {
            $action = "Review $sourceName and execute the named allowed next config/scaffold/evidence action under migration/**."
            return New-ContinuationCandidate $sourceName $action $statusMatch.Value
        }
    }

    return $null
}

function New-ContinuationDecision([bool]$Passed, $Results, $Candidate, [bool]$HasNonFinalStatus) {
    if ($HasNonFinalStatus -and $Candidate -ne $null) {
        return [pscustomobject][ordered]@{
            status = "CONTINUE_REQUIRED"
            protocol = "NOT FINAL is not a reportable terminal state when an allowed next action exists. Execute the next bounded action before sending a user-facing handoff."
            nextAction = $Candidate.action
            source = $Candidate.source
            evidence = $Candidate.evidence
            mustContinueBeforeUserMessage = $true
        }
    }

    $terminalGateNames = @("guard-checksums", "harness-policy", "scope-guard")
    $terminalFailures = @($Results | Where-Object { (-not $_.passed) -and ($terminalGateNames -contains $_.name) })

    if ($terminalFailures.Count -gt 0) {
        return [pscustomobject][ordered]@{
            status = "BLOCKED_BY_GATE"
            protocol = "Do not continue until guard/scope/harness-policy failures are fixed or reverted."
            nextAction = $null
            source = ($terminalFailures | ForEach-Object { $_.name }) -join ", "
            mustContinueBeforeUserMessage = $false
        }
    }

    if ($Passed) {
        return [pscustomobject][ordered]@{
            status = "FINAL"
            protocol = "Final gate passed; FINAL may be reported with evidence."
            nextAction = $null
            source = $null
            mustContinueBeforeUserMessage = $false
        }
    }

    if ($Candidate -ne $null) {
        return [pscustomobject][ordered]@{
            status = "CONTINUE_REQUIRED"
            protocol = "NOT FINAL is not a reportable terminal state when an allowed next action exists. Execute the next bounded action before sending a user-facing handoff."
            nextAction = $Candidate.action
            source = $Candidate.source
            evidence = $Candidate.evidence
            mustContinueBeforeUserMessage = $true
        }
    }

    return [pscustomobject][ordered]@{
        status = "BLOCKED_NO_ALLOWED_NEXT_ACTION"
        protocol = "No allowed next config/scaffold/evidence action was found. Stop only after writing a classified blocker and one concrete next action request."
        nextAction = $null
        source = $null
        mustContinueBeforeUserMessage = $false
    }
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path $RepoRoot $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)
$results = New-Object System.Collections.Generic.List[object]

# Project-local OpenCode bootstrap files are intentionally created at repository root.
# They are migration harness/config files, not product source edits, so routine scope/harness
# checks must not fail just because AGENTS.md/opencode.jsonc/.opencode/** are untracked.
$projectLocalOpenCodeAllowedRoots = @(
    "AGENTS.md",
    "opencode.jsonc",
    ".opencode",
    ".opencode/**",
    ".opencode-migrator",
    ".opencode-migrator/**",
    "opencode",
    "opencode/**"
)
$effectiveAllowedRoots = @($AllowedRoots) + $projectLocalOpenCodeAllowedRoots

$checksumDetail = ""
$guardFiles = @(
    "scripts/check-scope.ps1",
    "scripts/check-final-gate.ps1",
    "scripts/check-harness-policy.ps1"
)
$checksumOk = Test-GuardChecksums $workspacePath $guardFiles ([ref]$checksumDetail)
Add-Result $results "guard-checksums" $checksumOk $checksumDetail

$harnessPolicyScript = Join-Path $workspacePath "scripts/check-harness-policy.ps1"
if (Test-Path $harnessPolicyScript) {
    $harnessArgs = @("-Workspace", $Workspace, "-RepoRoot", $RepoRoot, "-AllowedRoots") + @($effectiveAllowedRoots)

    $harnessExitCode = Invoke-PowerShellScript $harnessPolicyScript $harnessArgs
    Add-Result $results "harness-policy" ($harnessExitCode -eq 0) "check-harness-policy.ps1 exit code $harnessExitCode"
}
else {
    Add-Result $results "harness-policy" $false "missing $harnessPolicyScript"
}

$scopeScript = Join-Path $workspacePath "scripts/check-scope.ps1"
if (Test-Path $scopeScript) {
    $scopeArgs = @("-RepoRoot", $RepoRoot, "-AllowedRoots") + @($effectiveAllowedRoots)

    $scopeExitCode = Invoke-PowerShellScript $scopeScript $scopeArgs
    Add-Result $results "scope-guard" ($scopeExitCode -eq 0) "check-scope.ps1 exit code $scopeExitCode"
}
else {
    Add-Result $results "scope-guard" $false "missing $scopeScript"
}

$agentStatePath = Join-Path $workspacePath "agent-state.md"
$currentTicketPath = Join-Path $workspacePath "current-ticket.md"
$runLedgerPath = Join-Path $workspacePath "state/run-ledger.md"
$stateFiles = @($agentStatePath, $currentTicketPath, $runLedgerPath)
$latestRunId = $null
$boardFile = Find-LatestFile -Root $workspacePath -Names @("migration-board.md", "migration-board.json")
if ($boardFile -ne $null) {
    $stateFiles += $boardFile.FullName
}

$missingStateFiles = @($stateFiles | Where-Object { -not (Test-Path $_) })
if ($boardFile -eq $null) {
    Add-Result $results "run-id-consistency" $false "missing migration-board.md or migration-board.json"
}
elseif ($missingStateFiles.Count -gt 0) {
    Add-Result $results "run-id-consistency" $false ("missing files: " + ($missingStateFiles -join ", "))
}
else {
    $agentStateText = Read-TextIfExists $agentStatePath
    $latestRunId = Find-ExplicitLatestRunId $agentStateText
    if ([string]::IsNullOrWhiteSpace($latestRunId)) {
        Add-Result $results "run-id-consistency" $false "agent-state.md missing explicit Latest run: run-* line"
    }
    else {
        $currentTicketIds = Find-RunIds (Read-TextIfExists $currentTicketPath)
        $boardIds = Find-RunIds (Read-TextIfExists $boardFile.FullName)
        $ledgerIds = Find-RunIds (Read-TextIfExists $runLedgerPath)
        $activeRunIds = @($currentTicketIds) + @($boardIds)
        $activeOtherIds = @($activeRunIds | Where-Object { $_ -ne $latestRunId } | Sort-Object -Unique)
        $problems = New-Object System.Collections.Generic.List[string]
        if (-not ($currentTicketIds -contains $latestRunId)) {
            $problems.Add("current-ticket.md missing $latestRunId")
        }

        if (-not ($boardIds -contains $latestRunId)) {
            $problems.Add("$($boardFile.Name) missing $latestRunId")
        }

        if (-not ($ledgerIds -contains $latestRunId)) {
            $problems.Add("run-ledger.md missing $latestRunId")
        }

        if ($activeOtherIds.Count -gt 0) {
            $problems.Add("active files contain other run ids: " + ($activeOtherIds -join ", "))
        }

        if ($problems.Count -eq 0) {
            Add-Result $results "run-id-consistency" $true "latest active run id: $latestRunId; ledger ids may be historical"
        }
        else {
            Add-Result $results "run-id-consistency" $false ("latest active run id: $latestRunId; " + ($problems -join "; "))
        }
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
    $dangerousHits = @(Find-DangerousQualityHits -Path $dashboard.FullName -Text $dashboardText -DangerousPatterns $dangerousPatterns)
    Add-Result $results "quality-dangerous-categories" ($dangerousHits.Count -eq 0) ("dashboard: $($dashboard.FullName); hits: " + ($dangerousHits -join ", "))

    $emptyTestHit = $dashboardText -match "EMPTY_TEST_AFTER_SUPPRESSION"
    $emptyLooksZero = $dashboardText -match 'EMPTY_TEST_AFTER_SUPPRESSION[^0-9]{0,120}0\b'
    $classificationFile = Join-Path $workspacePath "reports/empty-test-classification.md"
    Add-Result $results "empty-test-after-suppression" ((-not $emptyTestHit) -or $emptyLooksZero -or (Test-Path $classificationFile)) ("empty category present: $emptyTestHit; classification: $classificationFile")
}

if ($RequireOpenCodeExport) {
    $hasOpenCodeExport = Test-EvidenceExists $workspacePath @("opencode-session-export.*", "opencode-chat-bundle.*", "opencode-chat-bundle-*", "opencode-review-bundle.*", "opencode-review-bundle-*")
    Add-Result $results "opencode-evidence-export" $hasOpenCodeExport "required: $RequireOpenCodeExport; patterns: opencode-session-export.*, opencode-chat-bundle.*, opencode-chat-bundle-*, opencode-review-bundle.*, opencode-review-bundle-*"
}

if ($RequireExplainTodo) {
    $hasExplainTodo = Test-EvidenceExists $workspacePath @("explain-todo.json", "explain-todo.md") $latestRunId -RequireLatestRun
    Add-Result $results "explain-todo-artifacts" $hasExplainTodo "required: $RequireExplainTodo; latest run id: $latestRunId"
}

if ($RequireVerificationArtifacts) {
    $hasVerificationArtifacts = Test-EvidenceExists $workspacePath @("verify-report.json", "verify-report.md", "project-verify-report.json", "project-verify-report.md") $latestRunId -RequireLatestRun
    Add-Result $results "verification-artifacts" $hasVerificationArtifacts "required: $RequireVerificationArtifacts; latest run id: $latestRunId"
}

$actualStatusTexts = @(
    Read-TextIfExists (Join-Path $workspacePath "agent-state.md")
    Read-TextIfExists (Join-Path $workspacePath "current-ticket.md")
    Read-TextIfExists (Join-Path $workspacePath "state/handoff.md")
    Read-TextIfExists (Join-Path $workspacePath "state/stop-policy-checklist.md")
) -join "`n"
$notRuntimeReady = Test-ExplicitStatus -Text $actualStatusTexts -Statuses @("NOT RUNTIME READY")
$notFinalStatus = Test-ExplicitStatus -Text $actualStatusTexts -Statuses @("NOT FINAL", "NOT FINAL - INVESTIGATION RESULT ONLY")
$hasNonFinalStatus = $notRuntimeReady -or $notFinalStatus
$configBlocked = Test-ExplicitStatus -Text $actualStatusTexts -Statuses @("NOT RUNTIME READY", "BLOCKED_BY_CONFIG", "BLOCKED_BY_DIAGNOSTICS")

$configReport = Find-LatestFile -Root $workspacePath -Names @("config-validate-report.json", "config-validate-report.md")
if ($configReport -eq $null) {
    Add-Result $results "config-validate" $false "missing config-validate report"
}
else {
    $configText = Read-TextIfExists $configReport.FullName
    $configPassed = ($configReport.Extension -eq ".json" -and (Test-JsonStatusPassed $configReport.FullName)) -or (Test-TextLooksPassed $configText)
    $diagnosticsRecorded = ($configReport.Extension -eq ".json" -and (Test-JsonDiagnosticsRecorded $configReport.FullName)) -or ($configText -match '(?i)diagnostic|error|warning|validation')
    Add-Result $results "config-validate" ($configPassed -or $configBlocked) ("report: $($configReport.FullName); passed: $configPassed; diagnostics recorded: $diagnosticsRecorded; explicit blocker status: $configBlocked")
}

$projectVerify = Find-LatestFile -Root $workspacePath -Names @("project-verify-report.json", "project-verify-report.md")
if ($projectVerify -eq $null) {
    Add-Result $results "project-verify-or-runtime-status" $notRuntimeReady "project verify missing; explicit NOT RUNTIME READY status: $notRuntimeReady"
}
else {
    $verifyText = Read-TextIfExists $projectVerify.FullName
    $verifyPassed = ($projectVerify.Extension -eq ".json" -and (Test-JsonStatusPassed $projectVerify.FullName)) -or (Test-TextLooksPassed $verifyText)
    Add-Result $results "project-verify-or-runtime-status" ($verifyPassed -or $notRuntimeReady) ("report: $($projectVerify.FullName); passed: $verifyPassed; explicit NOT RUNTIME READY status: $notRuntimeReady")
}

$passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
$continuationSources = @()
foreach ($candidatePath in @(
    (Join-Path $workspacePath "current-ticket.md"),
    (Join-Path $workspacePath "state/handoff.md"),
    (Join-Path $workspacePath "state/stop-policy-checklist.md"),
    (Join-Path $workspacePath "agent-state.md")
)) {
    if (Test-Path $candidatePath) {
        $continuationSources += [pscustomobject]@{ name = (Get-RelativePathCompat $workspacePath $candidatePath); path = $candidatePath }
    }
}
foreach ($latestEvidence in @(
    (Find-LatestFile -Root $workspacePath -Names @("explain-todo.md", "explain-todo.json")),
    (Find-LatestFile -Root $workspacePath -Names @("verify-report.md", "verify-report.json", "project-verify-report.md", "project-verify-report.json")),
    (Find-LatestFile -Root $workspacePath -Names @("config-validate-report.md", "config-validate-report.json")),
    (Find-LatestFile -Root $workspacePath -Names @("migration-board.md", "migration-board.json"))
)) {
    if ($latestEvidence -ne $null) {
        $continuationSources += [pscustomobject]@{ name = (Get-RelativePathCompat $workspacePath $latestEvidence.FullName); path = $latestEvidence.FullName }
    }
}
$continuationCandidate = Find-ContinuationCandidate $continuationSources
if ($null -eq $continuationCandidate -and $hasNonFinalStatus) {
    $continuationCandidate = Find-ExplicitContinuationCandidateInText "status-files" $actualStatusTexts
}

# Last-resort strict continuation fallback. Keep this deliberately simple and
# duplicated from the richer parser above: several tests and real agent handoffs
# use plain "Status: NOT RUNTIME READY" + "Next action: ..." text, and that
# must never degrade into FINAL just because a formatting variant escaped the
# richer parser.
if ($null -eq $continuationCandidate) {
    $plainNextActionMatch = [regex]::Match($actualStatusTexts, '(?im)^\s*(?:[-*]\s*)?Next action\s*[:：]\s*(.+?)\s*$')
    $plainNonFinalStatus = $actualStatusTexts -match '(?im)^\s*(?:Status|Final status|Runtime-ready|Runtime ready|Project verify)\s*[:：]\s*(NOT FINAL|NOT FINAL - INVESTIGATION RESULT ONLY|NOT RUNTIME READY)\s*$'
    if ($plainNonFinalStatus -and $plainNextActionMatch.Success) {
        $plainAction = $plainNextActionMatch.Groups[1].Value.Trim()
        if (Test-AllowedContinuationActionText $plainAction) {
            $continuationCandidate = New-ContinuationCandidate "status-files" $plainAction $plainNextActionMatch.Value
            $hasNonFinalStatus = $true
        }
    }
}

$continuation = New-ContinuationDecision $passed $results $continuationCandidate $hasNonFinalStatus

$report = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    status = if ($passed -and $continuation.status -ne "CONTINUE_REQUIRED") { "PASS" } else { "FAIL" }
    continuationStatus = $continuation.status
    workspace = $workspacePath
    checks = $results
    continuation = $continuation
}

$stateDir = Join-Path $workspacePath "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$jsonPath = Join-Path $stateDir "final-gate-result.json"
$mdPath = Join-Path $stateDir "final-gate-result.md"
$continuationJsonPath = Join-Path $stateDir "continuation-decision.json"
$continuationMdPath = Join-Path $stateDir "continuation-decision.md"
$report | ConvertTo-Json -Depth 20 | Set-Content -Path $jsonPath -Encoding UTF8
$continuation | ConvertTo-Json -Depth 20 | Set-Content -Path $continuationJsonPath -Encoding UTF8

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Final Gate Result")
[void]$md.AppendLine()
[void]$md.AppendLine("Status: **$($report.status)**")
[void]$md.AppendLine("Continuation: **$($continuation.status)**")
if ($continuation.nextAction) {
    [void]$md.AppendLine("Next action: $($continuation.nextAction)")
}
[void]$md.AppendLine()
foreach ($check in $results) {
    $status = if ($check.passed) { "PASS" } else { "FAIL" }
    [void]$md.AppendLine("- ${status}: $($check.name) - $($check.detail)")
}
Set-Content -Path $mdPath -Value $md.ToString() -Encoding UTF8

$continuationMd = New-Object System.Text.StringBuilder
[void]$continuationMd.AppendLine("# Harness Continuation Decision")
[void]$continuationMd.AppendLine()
[void]$continuationMd.AppendLine("Status: **$($continuation.status)**")
[void]$continuationMd.AppendLine()
[void]$continuationMd.AppendLine($continuation.protocol)
if ($continuation.nextAction) {
    [void]$continuationMd.AppendLine()
    [void]$continuationMd.AppendLine(("Next action: {0}" -f $continuation.nextAction))
    [void]$continuationMd.AppendLine(("Source: {0}" -f $continuation.source))
}
Set-Content -Path $continuationMdPath -Value $continuationMd.ToString() -Encoding UTF8

Write-Host "FINAL_GATE_$($report.status)"
Write-Host "HARNESS_CONTINUATION_$($continuation.status)"
if ($continuation.nextAction) {
    Write-Host "Next action: $($continuation.nextAction)"
}
Write-Host "Report: $mdPath"
Write-Host "Continuation: $continuationMdPath"

if ($passed -and $continuation.status -ne "CONTINUE_REQUIRED") {
    exit 0
}

exit 1
