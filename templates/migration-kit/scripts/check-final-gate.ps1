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


function Normalize-ScopeContractPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }
    $p = $Path.Replace("\", "/")
    while ($p.StartsWith("./", [StringComparison]::Ordinal)) { $p = $p.Substring(2) }
    return $p.TrimEnd("/")
}

function Test-ScopeContractPathUnderRoot([string]$Path, [string]$Root) {
    $p = Normalize-ScopeContractPath $Path
    $r = (Normalize-ScopeContractPath $Root).TrimEnd("/")
    if ([string]::IsNullOrWhiteSpace($r)) { return $false }
    return $p.Equals($r, [StringComparison]::OrdinalIgnoreCase) -or $p.StartsWith($r + "/", [StringComparison]::OrdinalIgnoreCase)
}

function Read-ScopeContractOrNull([string]$WorkspacePath) {
    $contractPath = Join-Path $WorkspacePath "state/scope-contract.json"
    if (-not (Test-Path $contractPath)) { return $null }
    try {
        return Get-Content -Raw -Path $contractPath | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return [pscustomobject]@{ __invalid = $true; __error = $_.Exception.Message }
    }
}

function Get-ScopeContractAllowedRoots([object]$Contract) {
    $roots = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Contract -or $Contract.__invalid) { return @() }
    if (-not [string]::IsNullOrWhiteSpace([string]$Contract.workspaceRoot)) { $roots.Add((Normalize-ScopeContractPath ([string]$Contract.workspaceRoot))) | Out-Null }
    foreach ($root in @($Contract.allowedSourceRoots)) {
        $normalized = Normalize-ScopeContractPath ([string]$root)
        if (-not [string]::IsNullOrWhiteSpace($normalized)) { $roots.Add($normalized) | Out-Null }
    }
    return @($roots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
}

function Get-GitChangedPathsForScopeContract([string]$Root) {
    Push-Location $Root
    try {
        $gitRoot = (& git rev-parse --show-toplevel 2>$null)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitRoot)) { return @() }
        $status = (& git status --porcelain=v1 -z --untracked-files=all)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrEmpty($status)) { return @() }
        $tokens = $status -split [char]0
        $paths = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt $tokens.Length; $i++) {
            $entry = $tokens[$i]
            if ([string]::IsNullOrEmpty($entry) -or $entry.Length -lt 4) { continue }
            $xy = $entry.Substring(0, 2)
            $path = Normalize-ScopeContractPath $entry.Substring(3)
            if (-not [string]::IsNullOrWhiteSpace($path)) { $paths.Add($path) | Out-Null }
            if ($xy.Contains("R") -or $xy.Contains("C")) {
                if ($i + 1 -lt $tokens.Length -and -not [string]::IsNullOrEmpty($tokens[$i + 1])) {
                    $paths.Add((Normalize-ScopeContractPath $tokens[$i + 1])) | Out-Null
                    $i++
                }
            }
        }
        return @($paths | Sort-Object -Unique)
    }
    finally {
        Pop-Location
    }
}

function Test-ScopeContractChangedPaths([string]$WorkspacePath, [string]$RepoRootPath) {
    $contractPath = Join-Path $WorkspacePath "state/scope-contract.json"
    if (-not (Test-Path $contractPath)) {
        return [pscustomobject][ordered]@{
            status = "SKIPPED"
            contractPath = $contractPath
            changedFilesChecked = 0
            outOfScopeFiles = @()
            forbiddenRootHits = @()
            reason = "scope contract is not installed in this workspace"
        }
    }

    $contract = Read-ScopeContractOrNull $WorkspacePath
    if ($null -eq $contract -or $contract.__invalid) {
        $reason = if ($contract.__error) { [string]$contract.__error } else { "invalid scope contract" }
        return [pscustomobject][ordered]@{
            status = "FAIL"
            contractPath = $contractPath
            changedFilesChecked = 0
            outOfScopeFiles = @()
            forbiddenRootHits = @()
            reason = $reason
        }
    }

    $changed = @(Get-GitChangedPathsForScopeContract $RepoRootPath)
    $workspaceRoot = Normalize-ScopeContractPath ([string]$contract.workspaceRoot)
    $allowedSourceRoots = @($contract.allowedSourceRoots | ForEach-Object { Normalize-ScopeContractPath ([string]$_) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $allowedFiles = @($contract.allowedFiles | ForEach-Object { Normalize-ScopeContractPath ([string]$_) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $forbiddenRoots = @($contract.forbiddenRoots | ForEach-Object { Normalize-ScopeContractPath ([string]$_) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $maxChangedFiles = 0
    try { $maxChangedFiles = [int]$contract.maxChangedFiles } catch { $maxChangedFiles = 0 }

    $outOfScope = New-Object System.Collections.Generic.List[string]
    $forbiddenHits = New-Object System.Collections.Generic.List[string]
    foreach ($path in $changed) {
        $forbidden = $false
        foreach ($root in $forbiddenRoots) {
            if (Test-ScopeContractPathUnderRoot $path $root) {
                $forbiddenHits.Add($path) | Out-Null
                $forbidden = $true
                break
            }
        }
        if ($forbidden) { continue }

        $allowed = $false
        if (-not [string]::IsNullOrWhiteSpace($workspaceRoot) -and (Test-ScopeContractPathUnderRoot $path $workspaceRoot)) { $allowed = $true }
        elseif ($allowedFiles.Count -gt 0) { $allowed = $allowedFiles -contains $path }
        else {
            foreach ($root in $allowedSourceRoots) {
                if (Test-ScopeContractPathUnderRoot $path $root) { $allowed = $true; break }
            }
        }

        if (-not $allowed) { $outOfScope.Add($path) | Out-Null }
    }

    $tooMany = $maxChangedFiles -gt 0 -and $changed.Count -gt $maxChangedFiles
    $ok = $outOfScope.Count -eq 0 -and $forbiddenHits.Count -eq 0 -and -not $tooMany
    $reason = if ($forbiddenHits.Count -gt 0) {
        "Changed file is under a forbiddenRoot from scope-contract.json."
    } elseif ($outOfScope.Count -gt 0) {
        "Changed file is outside allowedSourceRoots/allowedFiles for this migration wave."
    } elseif ($tooMany) {
        "Changed file count $($changed.Count) exceeds maxChangedFiles $maxChangedFiles."
    } else {
        "All changed files satisfy scope-contract.json."
    }

    return [pscustomobject][ordered]@{
        status = if ($ok) { "PASS" } else { "FAIL" }
        contractPath = $contractPath
        changedFilesChecked = $changed.Count
        outOfScopeFiles = @($outOfScope | Sort-Object -Unique)
        forbiddenRootHits = @($forbiddenHits | Sort-Object -Unique)
        reason = $reason
    }
}

function Test-ClaimStatusForScopeContract([string]$WorkspacePath, [object]$Contract) {
    if ($null -eq $Contract -or $Contract.__invalid) {
        return [pscustomobject][ordered]@{ status = "SKIPPED"; reason = "scope contract unavailable" }
    }
    $requiresClaim = $false
    try { $requiresClaim = [bool]$Contract.requiresClaim } catch { $requiresClaim = $false }
    $ticketId = [string]$Contract.ticketId
    $runId = [string]$Contract.runId
    $claimRoots = @((Join-Path $WorkspacePath "state/claims/active"), (Join-Path $WorkspacePath "state/claims/completed"))
    $matching = New-Object System.Collections.Generic.List[string]
    foreach ($root in $claimRoots) {
        if (-not (Test-Path $root)) { continue }
        foreach ($file in @(Get-ChildItem -Path $root -Filter "*.json" -File -ErrorAction SilentlyContinue)) {
            try { $claim = Get-Content -Raw -Path $file.FullName | ConvertFrom-Json -ErrorAction Stop } catch { continue }
            if (([string]$claim.ticketId -eq $ticketId) -and ([string]$claim.runId -eq $runId)) { $matching.Add($file.FullName) | Out-Null }
        }
    }
    $ok = (-not $requiresClaim) -or $matching.Count -gt 0
    return [pscustomobject][ordered]@{
        status = if ($ok) { "PASS" } else { "FAIL" }
        required = $requiresClaim
        matchingClaims = @($matching | Sort-Object)
        reason = if ($ok) { "claim requirement satisfied or not required" } else { "missing active/completed claim for scope contract ticket/run" }
    }
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

function Test-NestedMigrationWorkspace([string]$RepoRootPath, [string]$WorkspacePath, [ref]$Detail) {
    if (-not (Test-Path $RepoRootPath)) {
        $Detail.Value = "repo root missing: $RepoRootPath"
        return $false
    }

    $repoFull = [System.IO.Path]::GetFullPath($RepoRootPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $workspaceFull = [System.IO.Path]::GetFullPath($WorkspacePath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $nested = New-Object System.Collections.Generic.List[string]
    $ignoredNames = @(".git", "bin", "obj", "node_modules", ".vs", "playwright-report", "TestResults")

    $candidateDirs = @(Get-ChildItem -Path $repoFull -Directory -Recurse -Filter "migration" -ErrorAction SilentlyContinue)
    foreach ($dir in $candidateDirs) {
        $full = $dir.FullName.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
        if ($full.Equals($workspaceFull, [System.StringComparison]::OrdinalIgnoreCase)) { continue }

        $relative = Get-RelativePathCompat $repoFull $full
        $skip = $false
        foreach ($ignored in $ignoredNames) {
            if ($relative -match "(^|[\\/])$([regex]::Escape($ignored))([\\/]|$)") {
                $skip = $true
                break
            }
        }
        if ($skip) { continue }

        if ((Test-Path (Join-Path $full "state")) -or
            (Test-Path (Join-Path $full "runs")) -or
            (Test-Path (Join-Path $full "plan")) -or
            (Test-Path (Join-Path $full "AGENT_CONTRACT.md"))) {
            $nested.Add($relative)
        }
    }

    if ($nested.Count -gt 0) {
        $Detail.Value = "nested migration workspace artifacts outside repo-root workspace: " + ($nested -join "; ")
        return $false
    }

    $Detail.Value = "no nested migration workspace artifacts outside repo-root workspace"
    return $true
}


function Test-SentinelInspectionPresent([string]$WorkspacePath, [string]$LatestRunId, [ref]$Detail) {
    if ([string]::IsNullOrWhiteSpace($LatestRunId)) {
        $Detail.Value = "latest run id unavailable; cannot verify sentinel inspection"
        return $false
    }

    $runSentinelDir = Join-Path $WorkspacePath "runs/$LatestRunId/sentinel"
    $inspectionPath = Join-Path $runSentinelDir "sentinel-inspection.json"
    $reportPath = Join-Path $runSentinelDir "sentinel-report.md"

    if (-not (Test-Path $inspectionPath)) {
        $Detail.Value = "missing $inspectionPath; run harness-sentinel or complete-sentinel-inspection before final handoff"
        return $false
    }
    if (-not (Test-Path $reportPath)) {
        $Detail.Value = "missing $reportPath; sentinel inspection must include a human-readable report"
        return $false
    }

    try {
        $inspection = Get-Content -Raw -Path $inspectionPath | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        $Detail.Value = "invalid sentinel inspection JSON: $($_.Exception.Message)"
        return $false
    }

    if ([string]$inspection.runId -ne $LatestRunId) {
        $Detail.Value = "sentinel inspection runId mismatch: expected $LatestRunId, found $($inspection.runId)"
        return $false
    }
    if ([string]::IsNullOrWhiteSpace([string]$inspection.inspectedAtUtc)) {
        $Detail.Value = "sentinel inspection missing inspectedAtUtc"
        return $false
    }

    $status = [string]$inspection.status
    if ($status -match '^(?i:blocked|failed|error)$') {
        $Detail.Value = "sentinel inspection status is blocking: $status"
        return $false
    }

    $Detail.Value = "sentinel inspection present for ${LatestRunId}: $status"
    return $true
}


function Normalize-SentinelLifecycleStatus([string]$Status) {
    if ([string]::IsNullOrWhiteSpace($Status)) { return "OPEN" }
    $normalized = $Status.Trim().ToUpperInvariant().Replace("-", "_")
    switch ($normalized) {
        "OPEN" { return "OPEN" }
        "ASSIGNED" { return "ASSIGNED" }
        "FIX_ATTEMPTED" { return "FIX_ATTEMPTED" }
        "VERIFIED" { return "VERIFIED" }
        "CLOSED" { return "CLOSED" }
        "BLOCKED" { return "BLOCKED" }
        "NON_AGENT_EXECUTABLE" { return "NON_AGENT_EXECUTABLE" }
        "NON_AGENT" { return "NON_AGENT_EXECUTABLE" }
        "ACCEPTED_RISK" { return "ACCEPTED_RISK" }
        "ACCEPTED" { return "ACCEPTED_RISK" }
        "RESOLVED" { return "CLOSED" }
        "TRIAGED" { return "ASSIGNED" }
        "NON_BLOCKING" { return "ACCEPTED_RISK" }
        default { return $normalized }
    }
}

function Test-SentinelLifecycleTerminal([string]$Status) {
    $normalized = Normalize-SentinelLifecycleStatus $Status
    return $normalized -match '^(VERIFIED|CLOSED|NON_AGENT_EXECUTABLE|ACCEPTED_RISK)$'
}

function Read-SentinelFindingLifecycleStatuses([string]$WorkspacePath) {
    $map = @{}
    $paths = @()
    $stateLedger = Join-Path $WorkspacePath "state/sentinel-finding-ledger.jsonl"
    if (Test-Path $stateLedger) { $paths += $stateLedger }
    if (Test-Path (Join-Path $WorkspacePath "runs")) {
        $paths += @(Get-ChildItem -Path (Join-Path $WorkspacePath "runs") -Recurse -File -Filter "sentinel-finding-lifecycle.jsonl" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    }

    foreach ($path in ($paths | Sort-Object -Unique)) {
        foreach ($line in (Get-Content -Path $path -ErrorAction SilentlyContinue)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try { $entry = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
            $findingId = [string]$entry.findingId
            if ([string]::IsNullOrWhiteSpace($findingId)) { continue }
            $status = Normalize-SentinelLifecycleStatus ([string]$entry.status)
            $updatedAtUtc = [string]$entry.updatedAtUtc
            if (-not $map.ContainsKey($findingId)) {
                $map[$findingId] = [pscustomobject]@{ status = $status; updatedAtUtc = $updatedAtUtc; ticketId = [string]$entry.ticketId; evidence = [string]$entry.evidence }
                continue
            }
            $previousTime = [string]$map[$findingId].updatedAtUtc
            if ([string]::CompareOrdinal($updatedAtUtc, $previousTime) -ge 0) {
                $map[$findingId] = [pscustomobject]@{ status = $status; updatedAtUtc = $updatedAtUtc; ticketId = [string]$entry.ticketId; evidence = [string]$entry.evidence }
            }
        }
    }
    return $map
}

function Test-OpenSentinelBlockingFindings([string]$WorkspacePath, [ref]$Detail) {
    $lifecycleStatuses = Read-SentinelFindingLifecycleStatuses $WorkspacePath
    $paths = @()
    $stateLedger = Join-Path $WorkspacePath "state/sentinel-ledger.jsonl"
    if (Test-Path $stateLedger) { $paths += $stateLedger }

    if (Test-Path (Join-Path $WorkspacePath "runs")) {
        $paths += @(Get-ChildItem -Path (Join-Path $WorkspacePath "runs") -Recurse -File -Filter "sentinel-findings.jsonl" -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    }

    if ($paths.Count -eq 0) {
        $Detail.Value = "no sentinel findings found"
        return $true
    }

    $blocking = New-Object System.Collections.Generic.List[string]
    foreach ($path in ($paths | Sort-Object -Unique)) {
        $lineNumber = 0
        foreach ($line in (Get-Content -Path $path -ErrorAction SilentlyContinue)) {
            $lineNumber += 1
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $entry = $line | ConvertFrom-Json -ErrorAction Stop
            } catch {
                $blocking.Add("invalid sentinel JSONL: ${path}:$lineNumber")
                continue
            }

            $severity = [string]$entry.severity
            $id = if ($entry.findingId) { [string]$entry.findingId } else { "line-$lineNumber" }
            $status = Normalize-SentinelLifecycleStatus ([string]$entry.status)
            $lifecycleStatus = $null
            if ($lifecycleStatuses.ContainsKey($id)) {
                $lifecycleStatus = $lifecycleStatuses[$id]
                $status = Normalize-SentinelLifecycleStatus ([string]$lifecycleStatus.status)
            }
            $agentExecutable = $true
            if ($null -ne $entry.agentExecutable) {
                $agentExecutable = [bool]$entry.agentExecutable
            }

            $isHigh = $severity -match '^(?i:high|critical)$'
            $isOpen = -not (Test-SentinelLifecycleTerminal $status)
            if ($isHigh -and $isOpen -and $agentExecutable) {
                $category = if ($entry.category) { [string]$entry.category } else { "UNKNOWN" }
                $ticketHint = if ($null -ne $lifecycleStatus -and -not [string]::IsNullOrWhiteSpace([string]$lifecycleStatus.ticketId)) { " ticket=$($lifecycleStatus.ticketId)" } else { "" }
                $blocking.Add("$id $category $severity status=$status$ticketHint in $path")
            }
        }
    }

    if ($blocking.Count -gt 0) {
        $Detail.Value = "open high/critical agent-executable sentinel findings: " + ($blocking -join "; ")
        return $false
    }

    $Detail.Value = "sentinel findings present, no open high/critical agent-executable findings"
    return $true
}


function Test-AgentSkillUsageEvidence([string]$WorkspacePath, [string]$LatestRunId, [ref]$Detail) {
    $skillMap = Join-Path $WorkspacePath "agent-skills/skill-map.md"
    $manifest = Join-Path $WorkspacePath "agent-skills/manifest.json"
    if (-not (Test-Path $skillMap) -and -not (Test-Path $manifest)) {
        $Detail.Value = "agent skill layer not installed"
        return $true
    }

    if ([string]::IsNullOrWhiteSpace($LatestRunId)) {
        $Detail.Value = "latest run id unavailable; cannot verify agent skill usage evidence"
        return $false
    }

    $runSkillDir = Join-Path $WorkspacePath "runs/$LatestRunId/skills"
    $summaryPath = Join-Path $runSkillDir "applied-skills.md"
    $runLedger = Join-Path $runSkillDir "agent-skill-usage.jsonl"
    $stateLedger = Join-Path $WorkspacePath "state/agent-skill-usage.jsonl"

    if (-not (Test-Path $summaryPath)) {
        $Detail.Value = "missing $summaryPath; run scripts/record-agent-skill-profile.ps1 or scripts/write-agent-skill-usage.ps1 before final handoff"
        return $false
    }

    $paths = @()
    if (Test-Path $runLedger) { $paths += $runLedger }
    if (Test-Path $stateLedger) { $paths += $stateLedger }
    if ($paths.Count -eq 0) {
        $Detail.Value = "missing agent skill usage ledger for $LatestRunId; run scripts/record-agent-skill-profile.ps1 or scripts/write-agent-skill-usage.ps1"
        return $false
    }

    $skills = New-Object System.Collections.Generic.List[string]
    $invalid = New-Object System.Collections.Generic.List[string]
    foreach ($path in ($paths | Sort-Object -Unique)) {
        $lineNumber = 0
        foreach ($line in (Get-Content -Path $path -ErrorAction SilentlyContinue)) {
            $lineNumber += 1
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $entry = $line | ConvertFrom-Json -ErrorAction Stop
            }
            catch {
                $invalid.Add("${path}:$lineNumber")
                continue
            }

            if ([string]$entry.runId -ne $LatestRunId) { continue }
            $skillName = [string]$entry.skillName
            $schemaVersion = [string]$entry.schemaVersion
            if ([string]::IsNullOrWhiteSpace($skillName)) {
                $invalid.Add("${path}:$lineNumber missing skillName")
                continue
            }
            if ($schemaVersion -ne "agent-skill-usage/v1") {
                $invalid.Add("${path}:$lineNumber unexpected schemaVersion '$schemaVersion'")
                continue
            }
            if ($skillName -notmatch '^[a-z0-9][a-z0-9._-]*$') {
                $invalid.Add("${path}:$lineNumber invalid skillName '$skillName'")
                continue
            }

            $skillFile = Join-Path $WorkspacePath "agent-skills/$skillName/SKILL.md"
            if (-not (Test-Path $skillFile)) {
                $invalid.Add("${path}:$lineNumber unknown skill '$skillName'")
                continue
            }

            $skills.Add($skillName)
        }
    }

    if ($invalid.Count -gt 0) {
        $Detail.Value = "invalid agent skill usage evidence: " + ($invalid -join "; ")
        return $false
    }

    $uniqueSkills = @($skills | Sort-Object -Unique)
    if ($uniqueSkills.Count -eq 0) {
        $Detail.Value = "no latest-run agent skill usage evidence for $LatestRunId; run scripts/record-agent-skill-profile.ps1 or scripts/write-agent-skill-usage.ps1"
        return $false
    }

    $summaryText = Read-TextIfExists $summaryPath
    foreach ($skill in $uniqueSkills) {
        if ($summaryText -notmatch [regex]::Escape($skill)) {
            $Detail.Value = "applied-skills.md does not mention recorded skill '$skill'"
            return $false
        }
    }

    $Detail.Value = "applied agent skill evidence for ${LatestRunId}: " + ($uniqueSkills -join ", ")
    return $true
}


function Test-EventHashChain([string]$EventsPath, [ref]$Detail) {
    if (-not (Test-Path $EventsPath)) {
        $Detail.Value = "events.jsonl missing: $EventsPath"
        return $false
    }

    $previousHash = $null
    $lineNumber = 0
    $hashedEvents = 0
    foreach ($line in (Get-Content -Path $EventsPath -ErrorAction SilentlyContinue)) {
        $lineNumber += 1
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        try { $event = $line | ConvertFrom-Json -ErrorAction Stop }
        catch {
            $Detail.Value = "invalid JSONL event at ${EventsPath}:$lineNumber"
            return $false
        }

        if (-not $event.PSObject.Properties["eventHash"] -or [string]::IsNullOrWhiteSpace([string]$event.eventHash)) {
            $Detail.Value = "event at ${EventsPath}:$lineNumber has no eventHash"
            return $false
        }

        $prevValue = if ($event.PSObject.Properties["prevEventHash"]) { [string]$event.prevEventHash } else { "" }
        if ($null -eq $previousHash) {
            if (-not [string]::IsNullOrWhiteSpace($prevValue)) {
                $Detail.Value = "first event at ${EventsPath}:$lineNumber must not have prevEventHash"
                return $false
            }
        }
        elseif ($prevValue -ne $previousHash) {
            $Detail.Value = "event hash chain break at ${EventsPath}:$lineNumber; expected prevEventHash=$previousHash, actual=$prevValue"
            return $false
        }

        $previousHash = [string]$event.eventHash
        $hashedEvents += 1
    }

    $Detail.Value = "events hash-chain valid; events=$hashedEvents"
    return $true
}

function Test-RunEvidenceBundle([string]$WorkspacePath, [string]$LatestRunId, [ref]$Detail) {
    if ([string]::IsNullOrWhiteSpace($LatestRunId)) {
        $Detail.Value = "latest run id unavailable; run evidence bundle check skipped"
        return $true
    }

    $runDir = Join-Path $WorkspacePath "runs/$LatestRunId"
    $indexPath = Join-Path $runDir "evidence/index.json"
    if (-not (Test-Path $indexPath)) {
        $Detail.Value = "legacy workspace: missing runs/$LatestRunId/evidence/index.json; run scripts/record-run-evidence.ps1 for strict bundle validation"
        return $true
    }

    try { $index = Get-Content -Raw -Path $indexPath | ConvertFrom-Json -ErrorAction Stop }
    catch {
        $Detail.Value = "invalid evidence index JSON: $indexPath"
        return $false
    }

    $problems = New-Object System.Collections.Generic.List[string]
    $artifactCount = 0
    foreach ($artifact in @($index.artifacts)) {
        $artifactCount += 1
        $relative = [string]$artifact.path
        if ([string]::IsNullOrWhiteSpace($relative)) {
            $problems.Add("artifact[$artifactCount] missing path") | Out-Null
            continue
        }

        $artifactPath = Join-Path $WorkspacePath $relative
        if (-not (Test-Path $artifactPath)) {
            $problems.Add("missing artifact: $relative") | Out-Null
            continue
        }

        $expectedHash = [string]$artifact.sha256
        if (-not [string]::IsNullOrWhiteSpace($expectedHash)) {
            $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $artifactPath).Hash.ToLowerInvariant()
            if ($actualHash -ne $expectedHash.ToLowerInvariant()) {
                $problems.Add("sha256 mismatch for $relative") | Out-Null
            }
        }
    }

    $eventDetail = ""
    $eventsPath = Join-Path $runDir "events.jsonl"
    $eventsOk = Test-EventHashChain $eventsPath ([ref]$eventDetail)
    if (-not $eventsOk) { $problems.Add($eventDetail) | Out-Null }

    if ($problems.Count -gt 0) {
        $Detail.Value = "evidence bundle invalid: " + ($problems -join "; ")
        return $false
    }

    $Detail.Value = "evidence bundle valid; artifacts=$artifactCount; $eventDetail"
    return $true
}

function Test-SessionExportHonest([string]$WorkspacePath, [string]$LatestRunId, [ref]$Detail) {
    if ([string]::IsNullOrWhiteSpace($LatestRunId)) {
        $Detail.Value = "latest run id unavailable; cannot verify session export honesty"
        return $false
    }

    $runDir = Join-Path $WorkspacePath "runs/$LatestRunId"
    $sessionPath = Join-Path $runDir "opencode-session-export.md"
    $manifestPath = Join-Path $runDir "opencode-session-export.json"
    if (-not (Test-Path $sessionPath)) {
        $Detail.Value = "missing $sessionPath"
        return $false
    }
    if (-not (Test-Path $manifestPath)) {
        $Detail.Value = "missing $manifestPath; session export status must be explicit"
        return $false
    }

    try {
        $manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json -ErrorAction Stop
        $status = [string]$manifest.exportStatus
        $reason = [string]$manifest.unavailableReason
        if ($status -eq "REAL_EXPORT") {
            $text = Read-TextIfExists $sessionPath
            if ($text -match '(?i)No native OpenCode transcript was provided|Transcript unavailable') {
                $Detail.Value = "session export claims REAL_EXPORT but contains unavailable/template text"
                return $false
            }
            $Detail.Value = "real session export present for $LatestRunId"
            return $true
        }
        if ($status -eq "UNAVAILABLE_WITH_REASON" -and -not [string]::IsNullOrWhiteSpace($reason)) {
            $Detail.Value = "session transcript unavailable with explicit reason: $reason"
            return $true
        }
        $Detail.Value = "session export has invalid exportStatus '$status'; expected REAL_EXPORT or UNAVAILABLE_WITH_REASON"
        return $false
    }
    catch {
        $Detail.Value = "invalid session export manifest: $($_.Exception.Message)"
        return $false
    }
}

function Test-RunPlanSanitized([string]$WorkspacePath, [string]$LatestRunId, [ref]$Detail) {
    if ([string]::IsNullOrWhiteSpace($LatestRunId)) {
        $Detail.Value = "latest run id unavailable; cannot verify Plan.md"
        return $false
    }
    $planPath = Join-Path $WorkspacePath "runs/$LatestRunId/Plan.md"
    if (-not (Test-Path $planPath)) {
        $Detail.Value = "missing $planPath"
        return $false
    }
    $text = Read-TextIfExists $planPath
    $badPatterns = @(
        '(?im)^\s*function\s+Set-Utf8NoBom\b',
        '(?im)^\s*Set-Content\b',
        '(?im)^\s*Add-Content\b',
        '(?im)^\s*Out-File\b',
        '(?im)^\s*New-Item\b',
        '(?im)^\s*cat\s*<<',
        '(?im)^\s*@"\s*$',
        '(?im)^\s*"@\s*$'
    )
    $hits = New-Object System.Collections.Generic.List[string]
    foreach ($pattern in $badPatterns) {
        if ($text -match $pattern) { $hits.Add($pattern) }
    }
    if ($hits.Count -gt 0) {
        $Detail.Value = "Plan.md appears to contain raw shell/write payloads or helper code: " + ($hits -join ", ")
        return $false
    }
    $Detail.Value = "Plan.md is free of raw shell write payloads"
    return $true
}

function Test-MemoryResearchThresholds([string]$WorkspacePath, [string]$LatestRunId, [ref]$Detail) {
    if ([string]::IsNullOrWhiteSpace($LatestRunId)) {
        $Detail.Value = "latest run id unavailable; cannot evaluate research/memory thresholds"
        return $true
    }

    $runRoot = Join-Path $WorkspacePath "runs/$LatestRunId"
    if (-not (Test-Path $runRoot)) {
        $Detail.Value = "latest run directory missing; threshold check skipped"
        return $true
    }

    $text = ""
    foreach ($file in Get-ChildItem -Path $runRoot -Recurse -File -Include "*.md", "*.json", "*.jsonl", "*.txt" -ErrorAction SilentlyContinue) {
        try { $text += "`n" + (Get-Content -Raw -Path $file.FullName -ErrorAction Stop) } catch { }
    }
    $todoMatches = [regex]::Matches($text, '(?i)\bTODO\b')
    $syntaxFallbackMatches = [regex]::Matches($text, '(?i)syntax[- ]fallback')
    $unresolvedMatches = [regex]::Matches($text, '(?i)UNRESOLVED_SYMBOL|unresolved symbols?')
    $verifyFailed = $text -match '(?i)verify-project.{0,80}(FAILED|failed)|NU1008|compilation not verified'

    $needsResearch = $todoMatches.Count -ge 25 -or $syntaxFallbackMatches.Count -ge 25 -or $unresolvedMatches.Count -gt 0 -or $verifyFailed
    if (-not $needsResearch) {
        $Detail.Value = "research threshold not reached"
        return $true
    }

    $memoryDir = Join-Path $WorkspacePath "state/memory"
    $memoryLines = 0
    foreach ($jsonlName in @("decisions.jsonl", "warnings.jsonl", "antipatterns.jsonl", "final-gate-lessons.jsonl", "user-notes.jsonl")) {
        $path = Join-Path $memoryDir $jsonlName
        if (Test-Path $path) {
            $memoryLines += @((Get-Content -Path $path -ErrorAction SilentlyContinue) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
        }
    }
    $researchFiles = @()
    foreach ($candidate in @((Join-Path $runRoot "research"), (Join-Path $runRoot "research/mapping-research-memory.json"), (Join-Path $WorkspacePath "state/mapping-research-memory.json"), (Join-Path $WorkspacePath "state/mapping-research-candidates.jsonl"), (Join-Path $runRoot "explain-todo.md"), (Join-Path $runRoot "generated/explain-todo.md"))) {
        if (Test-Path $candidate) { $researchFiles += $candidate }
    }

    if ($memoryLines -eq 0 -and $researchFiles.Count -eq 0) {
        $Detail.Value = "research/memory threshold reached (TODO=$($todoMatches.Count), syntaxFallback=$($syntaxFallbackMatches.Count), unresolved=$($unresolvedMatches.Count), verifyFailed=$verifyFailed) but no memory/research artifact was recorded"
        return $false
    }

    $Detail.Value = "research/memory threshold reached and evidence exists (memoryLines=$memoryLines, researchFiles=$($researchFiles.Count))"
    return $true
}


function Test-WaveQualityBudget([string]$WorkspacePath, [string]$LatestRunId, [ref]$Detail) {
    $waveBudgetFiles = @()
    if (-not [string]::IsNullOrWhiteSpace($LatestRunId)) {
        $runBudget = Join-Path $WorkspacePath "runs/$LatestRunId/wave-quality-budget.json"
        if (Test-Path $runBudget) { $waveBudgetFiles += $runBudget }
    }
    $stateBudget = Join-Path $WorkspacePath "state/wave-quality-budget.json"
    if (Test-Path $stateBudget) { $waveBudgetFiles += $stateBudget }

    if ($waveBudgetFiles.Count -eq 0) {
        $runsPath = Join-Path $WorkspacePath "runs"
        $waveDirs = @()
        if (Test-Path $runsPath) {
            $waveDirs = @(Get-ChildItem -Path $runsPath -Directory -Filter "wave-*" -ErrorAction SilentlyContinue)
        }
        if ($waveDirs.Count -eq 0) {
            $Detail.Value = "no wave-run artifacts found"
            return $true
        }

        $Detail.Value = "wave-run artifacts found but missing wave-quality-budget/v1 evidence; run migration/scripts/evaluate-wave-quality-budget.ps1 -Workspace migration before another wave"
        return $false
    }

    $budgetPath = @($waveBudgetFiles | Sort-Object -Unique)[0]
    try {
        $budget = Get-Content -Raw -Path $budgetPath | ConvertFrom-Json -ErrorAction Stop
        $schemaVersion = [string]$budget.schemaVersion
        if ($schemaVersion -ne "wave-quality-budget/v1") {
            $Detail.Value = "unexpected wave quality budget schema '$schemaVersion' in $budgetPath"
            return $false
        }

        $budgetStatus = [string]$budget.budgetStatus
        if ([string]::IsNullOrWhiteSpace($budgetStatus)) { $budgetStatus = [string]$budget.status }
        $violationCount = @($budget.violations).Count
        $nextAction = [string]$budget.nextAction
        if ($budgetStatus -eq "PASS") {
            $Detail.Value = "wave quality budget passed: $budgetPath"
            return $true
        }

        if ($budgetStatus -eq "REMEDIATION_BUDGET_EXHAUSTED") {
            $Detail.Value = "automatic remediation budget exhausted; final checkpoint may stop with limitations: $budgetPath"
            return $true
        }

        if ($budgetStatus -eq "BLOCKED_BY_WAVE_QUALITY_BUDGET" -and -not [string]::IsNullOrWhiteSpace($nextAction)) {
            $Detail.Value = "wave quality budget blocked next wave (violations=$violationCount): $nextAction"
            return $false
        }

        $Detail.Value = "wave quality budget did not pass and has no actionable nextAction: status=$budgetStatus path=$budgetPath"
        return $false
    }
    catch {
        $Detail.Value = "invalid wave quality budget JSON: $($_.Exception.Message)"
        return $false
    }
}



function Test-MappingResearchMemoryAfterBlockedBudget([string]$WorkspacePath, [string]$LatestRunId, [ref]$Detail) {
    $budgetCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace($LatestRunId)) {
        $runBudget = Join-Path $WorkspacePath "runs/$LatestRunId/wave-quality-budget.json"
        if (Test-Path $runBudget) { $budgetCandidates += $runBudget }
    }
    $stateBudget = Join-Path $WorkspacePath "state/wave-quality-budget.json"
    if (Test-Path $stateBudget) { $budgetCandidates += $stateBudget }

    if ($budgetCandidates.Count -eq 0) {
        $Detail.Value = "no wave-quality-budget/v1 evidence requiring mapping research"
        return $true
    }

    $blockedBudget = $false
    foreach ($budgetPath in @($budgetCandidates | Sort-Object -Unique)) {
        try {
            $budget = Get-Content -Raw -Path $budgetPath | ConvertFrom-Json -ErrorAction Stop
            $schemaVersion = [string]$budget.schemaVersion
            $budgetStatus = [string]$budget.budgetStatus
            if ($schemaVersion -eq "wave-quality-budget/v1" -and $budgetStatus -eq "BLOCKED_BY_WAVE_QUALITY_BUDGET") {
                $blockedBudget = $true
                break
            }
        }
        catch { }
    }

    if (-not $blockedBudget) {
        $Detail.Value = "wave quality budget is not blocked"
        return $true
    }

    $memoryCandidates = @((Join-Path $WorkspacePath "state/mapping-research-memory.json"))
    if (-not [string]::IsNullOrWhiteSpace($LatestRunId)) {
        $memoryCandidates += (Join-Path $WorkspacePath "runs/$LatestRunId/research/mapping-research-memory.json")
    }

    foreach ($memoryPath in @($memoryCandidates | Sort-Object -Unique)) {
        if (-not (Test-Path $memoryPath)) { continue }
        try {
            $memory = Get-Content -Raw -Path $memoryPath | ConvertFrom-Json -ErrorAction Stop
            if ([string]$memory.schemaVersion -eq "mapping-research-memory/v1") {
                $candidateCount = @($memory.recommendedNextTickets).Count
                $Detail.Value = "mapping-research-memory/v1 evidence exists: $memoryPath (recommendedNextTickets=$candidateCount)"
                return $true
            }
        }
        catch {
            $Detail.Value = "invalid mapping-research-memory JSON: $($_.Exception.Message)"
            return $false
        }
    }

    $Detail.Value = "wave quality budget is BLOCKED_BY_WAVE_QUALITY_BUDGET but missing mapping-research-memory/v1 evidence; run migration/scripts/collect-mapping-research-memory.ps1 -Workspace migration before another wave"
    return $false
}

function Update-HarnessRunStateFromFinalGate([string]$WorkspacePath, $Report, $Continuation, $Results) {
    $stateDir = Join-Path $WorkspacePath "state"
    $harnessRunPath = Join-Path $stateDir "harness-run.json"
    if (-not (Test-Path $harnessRunPath)) { return }

    try {
        $existingHarnessRun = Get-Content -Raw -Path $harnessRunPath | ConvertFrom-Json
        $harnessRunState = [ordered]@{}
        foreach ($property in $existingHarnessRun.PSObject.Properties) {
            $harnessRunState[$property.Name] = $property.Value
        }

        $previousHarnessStatus = $null
        if ($harnessRunState.Contains("status")) { $previousHarnessStatus = [string]$harnessRunState["status"] }

        $latestChecks = [ordered]@{}
        foreach ($check in $Results) {
            $latestChecks[$check.name] = if ($check.passed) { "PASS" } else { "FAIL" }
        }

        $harnessRunState["previousStatus"] = $previousHarnessStatus
        $harnessRunState["finalGateStatus"] = [string]$Report.status
        $harnessRunState["continuationStatus"] = [string]$Continuation.status
        $harnessRunState["latestChecks"] = $latestChecks
        $harnessRunState["lastFinalGateAtUtc"] = [DateTimeOffset]::UtcNow.ToString("o")
        $harnessRunState["finalGateReport"] = "state/final-gate-result.json"
        $harnessRunState["continuationDecision"] = "state/continuation-decision.json"

        if ([string]$Report.status -eq "PASS" -and [string]$Continuation.status -eq "FINAL_WITH_LIMITATIONS") {
            $harnessRunState["status"] = "WAVE_REMEDIATION_BUDGET_EXHAUSTED"
            $harnessRunState["finalizedAtUtc"] = [DateTimeOffset]::UtcNow.ToString("o")
            $harnessRunState["postSuccessPolicy"] = "STOP_FOR_REVIEW"
            $harnessRunState["continueCommand"] = $Continuation.continueCommand
            $harnessRunState["postFinalContinueAction"] = "NONE_REMEDIATION_BUDGET_EXHAUSTED"
            $harnessRunState["allowedNextAction"] = $null
            $harnessRunState["remainingLimitationsRequired"] = $true
        }
        elseif ([string]$Report.status -eq "PASS" -and [string]$Continuation.status -eq "FINAL" -and [string]$Continuation.postSuccessPolicy -eq "STOP_FOR_REVIEW") {
            $harnessRunState["status"] = "FINAL_STOPPED_FOR_REVIEW"
            $harnessRunState["finalizedAtUtc"] = [DateTimeOffset]::UtcNow.ToString("o")
            $harnessRunState["postSuccessPolicy"] = $Continuation.postSuccessPolicy
            $harnessRunState["continueCommand"] = $Continuation.continueCommand
            $harnessRunState["postFinalContinueAction"] = $Continuation.postFinalContinueAction
            $harnessRunState["postFinalResearchAgent"] = $Continuation.postFinalResearchAgent
            $harnessRunState["postFinalReviewAgent"] = $Continuation.postFinalReviewAgent
            $harnessRunState["postFinalResearchLeadAgent"] = $Continuation.postFinalResearchLeadAgent
            $harnessRunState["postFinalTaskSlicerAgent"] = $Continuation.postFinalTaskSlicerAgent
            $harnessRunState["postFinalWorkflow"] = $Continuation.postFinalWorkflow
        }
        elseif ([string]$Report.status -eq "FAIL") {
            $harnessRunState["status"] = [string]$Continuation.status
            $harnessRunState["allowedNextAction"] = if ($Continuation.nextAction) { [string]$Continuation.nextAction } else { "FIX_GATE_FAILURES" }
            $harnessRunState["blockedAtUtc"] = [DateTimeOffset]::UtcNow.ToString("o")
        }
        else {
            $harnessRunState["status"] = [string]$Continuation.status
        }

        ($harnessRunState | ConvertTo-Json -Depth 30) | Set-Content -Path $harnessRunPath -Encoding UTF8
    }
    catch {
        Write-Warning "Could not reconcile harness-run.json with final gate result: $($_.Exception.Message)"
    }
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
        $checked = New-Object System.Collections.Generic.List[string]
        $skippedOptional = New-Object System.Collections.Generic.List[string]
        foreach ($guardFile in $GuardFiles) {
            $relative = $guardFile.Replace("\", "/")
            $fullPath = Join-Path $WorkspacePath $guardFile
            $hasExpectedChecksum = $expected.ContainsKey($relative)
            $exists = Test-Path $fullPath

            if (-not $hasExpectedChecksum -and -not $exists) {
                # Version-aware optional guard: newer kit releases may know about scripts
                # that are not present in older/minimal test workspaces. Do not fail those
                # workspaces merely because check-final-gate.ps1 is newer than their installed
                # guard-checksums.json. If a script is installed or listed in the checksum
                # manifest, it is still validated strictly below.
                $skippedOptional.Add($relative)
                continue
            }

            if (-not $exists) {
                $mismatches.Add("$relative missing")
                continue
            }

            if (-not $hasExpectedChecksum) {
                $mismatches.Add("$relative missing expected checksum")
                continue
            }

            $actual = (Get-FileHash -Algorithm SHA256 -Path $fullPath).Hash.ToUpperInvariant()
            if ($actual -ne $expected[$relative]) {
                $mismatches.Add("$relative checksum mismatch")
                continue
            }

            $checked.Add($relative)
        }

        if ($mismatches.Count -eq 0) {
            $detailParts = New-Object System.Collections.Generic.List[string]
            $detailParts.Add("guard checksums match")
            $detailParts.Add("checked: $($checked.Count)")
            if ($skippedOptional.Count -gt 0) {
                $detailParts.Add("optional not installed: " + ($skippedOptional -join ", "))
            }
            $Detail.Value = $detailParts -join "; "
        }
        else {
            $Detail.Value = $mismatches -join "; "
        }
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
            # Preserve empty arrays from ConvertFrom-Json. A plain `return $property.Value`
            # enumerates arrays through the pipeline; an empty `selectors: []` then
            # becomes `$null` at the caller and is misreported as a missing property.
            Write-Output -NoEnumerate $property.Value
            return
        }
    }

    return $null
}

function Test-UnsafeAssertionSuppressionMemoryText([string]$Text) {
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $false
    }

    $lower = $Text.ToLowerInvariant()
    $mentionsAssertion = $lower.Contains("assert") -or $lower.Contains("fluentassertions") -or $lower.Contains("nunit")
    $mentionsSuppress = $lower.Contains("suppress") -or $lower.Contains("hide") -or $lower.Contains("skip") -or $lower.Contains("remove")
    if (-not ($mentionsAssertion -and $mentionsSuppress)) {
        return $false
    }

    $forbids = $lower.Contains("do not") -or $lower.Contains("don't") -or $lower.Contains("never") -or $lower.Contains("cannot") -or $lower.Contains("must not") -or $lower.Contains("forbid") -or $lower.Contains("block") -or $lower.Contains("refuse") -or $lower.Contains("no assertion") -or $lower.Contains("zero assertions suppressed")
    return -not $forbids
}

function Test-MigrationMemory([string]$WorkspacePath, [ref]$Detail) {
    $memoryDir = Join-Path $WorkspacePath "state/memory"
    if (-not (Test-Path $memoryDir)) {
        $Detail.Value = "project-scoped memory is optional and missing"
        return $true
    }

    $problems = New-Object System.Collections.Generic.List[string]

    foreach ($jsonl in @("decisions.jsonl", "warnings.jsonl", "antipatterns.jsonl", "final-gate-lessons.jsonl", "user-notes.jsonl")) {
        $path = Join-Path $memoryDir $jsonl
        if (-not (Test-Path $path)) {
            continue
        }

        $lineNumber = 0
        foreach ($line in Get-Content -Path $path) {
            $lineNumber++
            if ([string]::IsNullOrWhiteSpace($line)) {
                continue
            }

            try {
                $entry = $line | ConvertFrom-Json
                foreach ($required in @("kind", "text", "source", "status")) {
                    if ($null -eq (Get-ObjectProperty $entry @($required))) {
                        $problems.Add("${jsonl}:$lineNumber missing $required")
                    }
                }

                $status = Get-ObjectProperty $entry @("status")
                $text = [string](Get-ObjectProperty $entry @("text"))
                if ($status -ne $null -and $status.ToString().Equals("active", [StringComparison]::OrdinalIgnoreCase) -and (Test-UnsafeAssertionSuppressionMemoryText $text)) {
                    $problems.Add("${jsonl}:$lineNumber active memory appears to allow assertion suppression")
                }
            }
            catch {
                $problems.Add("${jsonl}:$lineNumber invalid JSONL: $($_.Exception.Message)")
            }
        }
    }

    $selectorMap = Join-Path $memoryDir "selector-map.json"
    if (Test-Path $selectorMap) {
        try {
            $selectorJson = Get-Content -Raw -Path $selectorMap | ConvertFrom-Json
            $selectors = Get-ObjectProperty $selectorJson @("selectors")
            if ($null -eq $selectors) {
                $problems.Add("selector-map.json missing selectors array")
            }
            else {
                $index = 0
                foreach ($selector in @($selectors)) {
                    $index++
                    $sourceExpression = Get-ObjectProperty $selector @("sourceExpression")
                    $targetLocator = Get-ObjectProperty $selector @("targetLocator")
                    $evidence = Get-ObjectProperty $selector @("evidence")
                    if ([string]::IsNullOrWhiteSpace([string]$sourceExpression) -or [string]::IsNullOrWhiteSpace([string]$targetLocator) -or $null -eq $evidence -or @($evidence).Count -eq 0) {
                        $problems.Add("selector-map.json selector[$index] requires sourceExpression, targetLocator, and evidence[]")
                    }
                }
            }
        }
        catch {
            $problems.Add("selector-map.json invalid JSON: $($_.Exception.Message)")
        }
    }

    $Detail.Value = if ($problems.Count -eq 0) { "project-scoped memory doctor checks passed" } else { $problems -join "; " }
    return $problems.Count -eq 0
}


function Normalize-MemoryRecallPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }
    $normalized = $Path.Replace("\\", "/").Trim()
    while ($normalized.StartsWith("./", [System.StringComparison]::Ordinal)) {
        $normalized = $normalized.Substring(2)
    }
    return $normalized
}

function Test-MemoryRecallEvidence([string]$WorkspacePath, [ref]$Detail) {
    $memoryDir = Join-Path $WorkspacePath "state/memory"
    if (-not (Test-Path $memoryDir)) { $Detail.Value = "project-scoped memory missing; recall evidence not required"; return $true }

    $activeEntries = 0
    foreach ($name in @("decisions.jsonl", "warnings.jsonl", "antipatterns.jsonl", "final-gate-lessons.jsonl", "user-notes.jsonl")) {
        $path = Join-Path $memoryDir $name
        if (-not (Test-Path $path)) { continue }
        foreach ($line in @(Get-Content -Path $path -ErrorAction SilentlyContinue)) {
            if ([string]::IsNullOrWhiteSpace($line)) { continue }
            try {
                $entry = $line | ConvertFrom-Json -ErrorAction Stop
                if ([string]$entry.status -eq "active") { $activeEntries++ }
            }
            catch { }
        }
    }
    if ($activeEntries -eq 0) { $Detail.Value = "no active project memory; recall evidence not required"; return $true }

    $waveScopes = @(Get-ChildItem -Path (Join-Path $WorkspacePath "runs") -Recurse -File -Filter "input-scope.json" -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '[\\/]wave-[^\\/]+[\\/]input-scope\.json$' } |
        Sort-Object LastWriteTimeUtc -Descending)
    if ($waveScopes.Count -eq 0) { $Detail.Value = "active memory exists but no wave input scope is present; recall evidence deferred"; return $true }

    $scopePath = $waveScopes[0].FullName
    try { $scope = Get-Content -Raw -Path $scopePath | ConvertFrom-Json -ErrorAction Stop }
    catch { $Detail.Value = "invalid wave input scope for recall evidence: $($_.Exception.Message)"; return $false }
    $scopeGeneratedAt = [DateTimeOffset]::MinValue
    [void][DateTimeOffset]::TryParse([string]$scope.generatedAtUtc, [ref]$scopeGeneratedAt)
    $scopeFiles = New-Object System.Collections.Generic.List[string]
    foreach ($value in @($scope.copiedFiles) + @($scope.files)) {
        $normalized = Normalize-MemoryRecallPath ([string]$value)
        if (-not [string]::IsNullOrWhiteSpace($normalized)) { $scopeFiles.Add($normalized) | Out-Null }
    }
    $scopeFiles = @($scopeFiles | Sort-Object -Unique)
    if ($scopeFiles.Count -eq 0) { $Detail.Value = "wave input scope contains no files; recall evidence not required"; return $true }

    $indexPath = Join-Path $memoryDir "recall-index.json"
    if (-not (Test-Path $indexPath)) { $Detail.Value = "missing recall-index.json; run memory recall for each wave file"; return $false }
    try { $index = Get-Content -Raw -Path $indexPath | ConvertFrom-Json -ErrorAction Stop }
    catch { $Detail.Value = "invalid recall-index.json: $($_.Exception.Message)"; return $false }
    $receipts = @($index.entries)
    if ($receipts.Count -eq 0) { $Detail.Value = "recall-index.json has no receipts; run memory recall for each wave file"; return $false }

    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($scopeFile in $scopeFiles) {
        $matched = $false
        foreach ($receipt in $receipts) {
            $receiptFile = Normalize-MemoryRecallPath ([string](Get-ObjectProperty $receipt @("normalizedFile", "file")))
            if ([string]::IsNullOrWhiteSpace($receiptFile)) { continue }
            $pathMatches = $receiptFile.Equals($scopeFile, [StringComparison]::OrdinalIgnoreCase) -or
                $receiptFile.EndsWith("/" + $scopeFile, [StringComparison]::OrdinalIgnoreCase) -or
                $scopeFile.EndsWith("/" + $receiptFile, [StringComparison]::OrdinalIgnoreCase)
            if (-not $pathMatches) { continue }
            $recordedAt = [DateTimeOffset]::MinValue
            [void][DateTimeOffset]::TryParse([string]$receipt.recordedAtUtc, [ref]$recordedAt)
            if ($scopeGeneratedAt -ne [DateTimeOffset]::MinValue -and $recordedAt -ne [DateTimeOffset]::MinValue -and $recordedAt -lt $scopeGeneratedAt) { continue }
            $matched = $true
            break
        }
        if (-not $matched) { $missing.Add($scopeFile) | Out-Null }
    }

    if ($missing.Count -gt 0) {
        $Detail.Value = "missing current-wave memory recall receipt(s): " + ($missing -join ", ") + "; run selenium-pw-migrator memory recall --file <file> --workspace migration"
        return $false
    }
    $Detail.Value = "memory recall receipts cover all $($scopeFiles.Count) file(s) in latest wave scope"
    return $true
}

function Test-ConfigDeltaMerge($WorkspaceRoot, [ref]$Detail) {
    $problems = New-Object System.Collections.Generic.List[string]
    $mergeDir = Join-Path $WorkspaceRoot "config-merge"
    if (-not (Test-Path $mergeDir)) {
        $Detail.Value = "no config-merge candidate present; merge-deltas not required"
        return $true
    }

    $candidate = Join-Path $mergeDir "adapter-config.merged.json"
    $validateReport = Join-Path $mergeDir "validate-merge-report.json"
    $conflicts = Join-Path $mergeDir "conflicts.jsonl"

    if ((Test-Path $candidate) -and -not (Test-Path $validateReport)) {
        $problems.Add("adapter-config.merged.json exists without validate-merge-report.json; run config validate-merge")
    }

    if (Test-Path $validateReport) {
        try {
            $report = Get-Content -Raw -Path $validateReport | ConvertFrom-Json
            $status = Get-ObjectProperty $report @("status")
            if ($status -ne $null -and $status.ToString().Equals("invalid", [StringComparison]::OrdinalIgnoreCase)) {
                $problems.Add("config validate-merge status is invalid")
            }
            $reportConflicts = Get-ObjectProperty $report @("conflicts")
            if ($reportConflicts -ne $null -and @($reportConflicts).Count -gt 0) {
                $problems.Add("config validate-merge has conflicts: $(@($reportConflicts).Count)")
            }
        }
        catch {
            $problems.Add("validate-merge-report.json invalid JSON: $($_.Exception.Message)")
        }
    }

    if (Test-Path $conflicts) {
        $activeConflicts = @(Get-Content -Path $conflicts | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($activeConflicts.Count -gt 0) {
            $problems.Add("config merge conflicts are present in conflicts.jsonl: $($activeConflicts.Count)")
        }
    }

    $Detail.Value = if ($problems.Count -eq 0) { "config delta merge checks passed" } else { $problems -join "; " }
    return $problems.Count -eq 0
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

function Find-DangerousQualityHitsFromText([string]$Text, [string[]]$DangerousPatterns) {
    $hits = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($Text)) {
        return @()
    }

    foreach ($pattern in $DangerousPatterns) {
        $escaped = [regex]::Escape($pattern)
        $countSeenForPattern = $false

        # Count-aware fallback for JSON/YAML/Markdown snippets. A plain text
        # match on the category name is too conservative for structured
        # dashboards: `{ "category": "ASSERTION_SUPPRESSION_BLOCKED", "count": 0 }`
        # must be a clean PASS, not a dangerous hit. Only positive numeric
        # counts are structural hits.
        $propertyMatches = [regex]::Matches(
            $Text,
            '"?' + $escaped + '"?\s*[:=]\s*(?<count>-?\d+)',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        foreach ($match in $propertyMatches) {
            $count = Convert-ToIntOrNull $match.Groups["count"].Value
            if ($count -ne $null) {
                $countSeenForPattern = $true
                if ($count -gt 0) {
                    $hits.Add("${pattern}:$count")
                }
            }
        }

        $categoryObjectMatches = [regex]::Matches(
            $Text,
            '"?(?:category|name|id|key)"?\s*[:=]\s*"?' + $escaped + '"?[\s\S]{0,240}?"?(?:count|total|value)"?\s*[:=]\s*(?<count>-?\d+)',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
        foreach ($match in $categoryObjectMatches) {
            $count = Convert-ToIntOrNull $match.Groups["count"].Value
            if ($count -ne $null) {
                $countSeenForPattern = $true
                if ($count -gt 0) {
                    $hits.Add("${pattern}:$count")
                }
            }
        }

        if ((-not $countSeenForPattern) -and ($Text -match $escaped)) {
            $hits.Add("${pattern}:present")
        }
    }

    return @($hits | Sort-Object -Unique)
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
            # Fall back to count-aware text matching below.
        }
    }

    return @(Find-DangerousQualityHitsFromText $Text $DangerousPatterns)
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



function Read-JsonObjectIfExists([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return $null
    }

    try {
        return Get-Content -Raw -Path $Path -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        return $null
    }
}

function Get-JsonStringValue($Object, [string[]]$Names) {
    if ($null -eq $Object) {
        return $null
    }

    foreach ($name in $Names) {
        $prop = $Object.PSObject.Properties[$name]
        if ($null -eq $prop -or $null -eq $prop.Value) {
            continue
        }

        if (-not ($prop.Value -is [string])) {
            continue
        }

        $value = [string]$prop.Value
        if (-not [string]::IsNullOrWhiteSpace($value)) {
            return $value.Trim()
        }
    }

    return $null
}

function Get-JsonArrayCount($Object, [string[]]$Names) {
    if ($null -eq $Object) {
        return 0
    }

    foreach ($name in $Names) {
        $prop = $Object.PSObject.Properties[$name]
        if ($null -eq $prop -or $null -eq $prop.Value) {
            continue
        }

        if ($prop.Value -is [System.Collections.IEnumerable] -and -not ($prop.Value -is [string])) {
            return @($prop.Value).Count
        }
    }

    return 0
}

function New-StructuredContinuationCandidateFromJson([string]$SourceName, [string]$Path, $Json) {
    if ($null -eq $Json) {
        return $null
    }

    $fileName = [System.IO.Path]::GetFileName($Path)
    $status = Get-JsonStringValue $Json @("status", "Status", "continuationStatus", "ContinuationStatus", "result", "outcome")
    $statusText = if ($status -eq $null) { "" } else { $status }
    $nextAction = Get-JsonStringValue $Json @("nextAction", "NextAction", "next", "recommendedNextAction", "RecommendedNextAction", "suggestedAction", "SuggestedAction")

    if ($fileName -ieq "current-ticket-status.json") {
        if ($statusText -match '(?i)ready|in[-_ ]?progress|blocked|review|continue|pending') {
            $action = if ([string]::IsNullOrWhiteSpace($nextAction)) { "Review migration/current-ticket.md and execute the current bounded ticket under migration/**." } else { $nextAction }
            if (Test-AllowedContinuationActionText $action) {
                return New-ContinuationCandidate $SourceName $action ("structured status: $statusText")
            }
        }
    }

    if ($fileName -imatch 'wave-quality-budget\.json$') {
        $blocked = $statusText -match '(?i)blocked|failed|needs|exceeded|continue|required'
        $findingCount = Get-JsonArrayCount $Json @("violations", "Violations", "blockingFindings", "BlockingFindings", "failures", "Failures")
        if ($blocked -or $findingCount -gt 0) {
            $action = if ([string]::IsNullOrWhiteSpace($nextAction)) { "Run migration-board/explain-todo for the latest wave artifacts under migration/**, then create a bounded config-author or research ticket." } else { $nextAction }
            if (Test-AllowedContinuationActionText $action) {
                return New-ContinuationCandidate $SourceName $action ("structured wave quality budget: status=$statusText; findings=$findingCount")
            }
        }
    }

    if ($fileName -imatch 'mapping-research-memory\.json$') {
        $recommended = Get-JsonArrayCount $Json @("recommendedNextTickets", "RecommendedNextTickets", "candidates", "Candidates", "items", "Items")
        if ($recommended -gt 0 -or $statusText -match '(?i)ready|continue|research|blocked') {
            $action = if ([string]::IsNullOrWhiteSpace($nextAction)) { "Review migration mapping-research-memory and turn one recommendedNextTickets item into current-ticket.md before continuing." } else { $nextAction }
            if (Test-AllowedContinuationActionText $action) {
                return New-ContinuationCandidate $SourceName $action ("structured mapping research memory: status=$statusText; candidates=$recommended")
            }
        }
    }

    if ($fileName -imatch '(project-verify-report|verify-report)\.json$') {
        if ($statusText -match '(?i)fail|error|blocked') {
            $diagnostics = Get-JsonArrayCount $Json @("diagnostics", "Diagnostics", "classifiedDiagnostics", "ClassifiedDiagnostics")
            $action = if ([string]::IsNullOrWhiteSpace($nextAction)) { "Review project-verify-report.json diagnostics under migration/**, then run explain-todo/config-author or update verification config before finalizing." } else { $nextAction }
            if (Test-AllowedContinuationActionText $action) {
                return New-ContinuationCandidate $SourceName $action ("structured verify status: $statusText; diagnostics=$diagnostics")
            }
        }
    }

    if ($fileName -imatch 'config-validate-report\.json$') {
        if ($statusText -match '(?i)fail|error|blocked|warning') {
            $issues = Get-JsonArrayCount $Json @("issues", "Issues", "errors", "Errors", "warnings", "Warnings")
            $action = if ([string]::IsNullOrWhiteSpace($nextAction)) { "Review config-validate-report.json and fix adapter-config/config delta under migration/** before finalizing." } else { $nextAction }
            if (Test-AllowedContinuationActionText $action) {
                return New-ContinuationCandidate $SourceName $action ("structured config validation status: $statusText; issues=$issues")
            }
        }
    }

    if ($fileName -imatch 'migration-board\.json$') {
        $recommended = Get-JsonArrayCount $Json @("recommendedNextActions", "RecommendedNextActions")
        if ($recommended -gt 0 -and $statusText -notmatch '(?i)^pass|passed|success|ok$') {
            $action = "Review migration-board.json recommendedNextActions under migration/** and execute one bounded evidence/config action before finalizing."
            return New-ContinuationCandidate $SourceName $action ("structured migration board recommended actions: $recommended")
        }
    }

    return $null
}

function Find-StructuredContinuationCandidate($Sources) {
    $sourceItems = New-Object System.Collections.ArrayList
    if ($null -ne $Sources) {
        if ($Sources -is [System.Collections.IEnumerable] -and -not ($Sources -is [string])) {
            foreach ($item in $Sources) { [void]$sourceItems.Add($item) }
        }
        else {
            [void]$sourceItems.Add($Sources)
        }
    }

    foreach ($sourceInfo in $sourceItems) {
        if ($null -eq $sourceInfo) { continue }

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

        if ([string]::IsNullOrWhiteSpace($sourcePath) -or -not (Test-Path $sourcePath)) { continue }
        if ([string]::IsNullOrWhiteSpace($sourceName)) { $sourceName = $sourcePath }
        if ([System.IO.Path]::GetExtension($sourcePath) -ne ".json") { continue }

        $json = Read-JsonObjectIfExists $sourcePath
        $candidate = New-StructuredContinuationCandidateFromJson $sourceName $sourcePath $json
        if ($candidate -ne $null) {
            return $candidate
        }
    }

    return $null
}



function New-GateFollowupSlicerCandidate([string]$WorkspacePath, $Results) {
    $failed = @($Results | Where-Object { -not $_.passed })
    if ($failed.Count -eq 0) { return $null }

    $slicerPs = Join-Path $WorkspacePath "scripts/slice-gate-followups.ps1"
    $slicerSh = Join-Path $WorkspacePath "scripts/slice-gate-followups.sh"
    if (-not (Test-Path $slicerPs) -and -not (Test-Path $slicerSh)) { return $null }

    $failedNames = ($failed | ForEach-Object { $_.name }) -join ", "
    return New-ContinuationCandidate "gate-followup-slicer" "Run migration/scripts/slice-gate-followups.ps1 -Workspace migration to convert failed final-gate/sentinel diagnostics into migration/current-ticket.md before the next wave." "failed checks: $failedNames"
}

function Test-RemediationBudgetExhausted([string]$WorkspacePath) {
    $path = Join-Path $WorkspacePath "state/wave-quality-budget.json"
    if (-not (Test-Path $path)) { return $false }
    try {
        $budget = Get-Content -Raw -Path $path | ConvertFrom-Json -ErrorAction Stop
        return [string]$budget.budgetStatus -eq "REMEDIATION_BUDGET_EXHAUSTED"
    }
    catch { return $false }
}

function New-ContinuationDecision([bool]$Passed, $Results, $Candidate, [bool]$HasNonFinalStatus, [bool]$RemediationBudgetExhausted) {
    if ($Passed -and $RemediationBudgetExhausted) {
        return [pscustomobject][ordered]@{
            status = "FINAL_WITH_LIMITATIONS"
            protocol = "The wave reached its automatic remediation budget. Stop with the remaining limitations recorded. Do not create another post-final ticket automatically and do not start the next wave without an explicit fresh-wave decision."
            nextAction = $null
            source = "state/wave-quality-budget.json"
            evidence = "REMEDIATION_BUDGET_EXHAUSTED"
            mustContinueBeforeUserMessage = $false
            postSuccessPolicy = "STOP_FOR_REVIEW"
            continueRequires = "explicit user approval to extend the remediation budget, or archive the pilot and start a fresh bounded wavefront"
            continueCommand = "/supervised-task waves fresh"
            postFinalContinueAction = "NONE_REMEDIATION_BUDGET_EXHAUSTED"
            remainingLimitationsRequired = $true
        }
    }

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

    $terminalGateNames = @("guard-checksums", "harness-policy", "scope-guard", "nested-migration-workspace")
    $terminalFailures = @($Results | Where-Object { (-not $_.passed) -and ($terminalGateNames -contains $_.name) })

    if ($terminalFailures.Count -gt 0) {
        if ($Candidate -ne $null -and [string]$Candidate.source -eq "gate-followup-slicer") {
            return [pscustomobject][ordered]@{
                status = "CONTINUE_REQUIRED"
                protocol = "Guard/scope/harness-policy failures are terminal for migration execution, but they must first be sliced into bounded gate follow-up tasks before a user-facing handoff. Do not start another wave."
                nextAction = $Candidate.action
                source = $Candidate.source
                evidence = $Candidate.evidence
                mustContinueBeforeUserMessage = $true
                gateFollowupSlicer = "migration/scripts/slice-gate-followups.ps1"
            }
        }

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
            protocol = "Final gate passed; stop once for review. Report evidence and do not start another migration run in the same fresh checkpoint. On the next supervised-task invocation, persisted FINAL_STOPPED_FOR_REVIEW resumes the closed post-final research, research-lead review, task-slicing, change-review, and bounded executor loop automatically; explicit continue remains supported but is not required."
            nextAction = $null
            source = $null
            mustContinueBeforeUserMessage = $false
            postSuccessPolicy = "STOP_FOR_REVIEW"
            continueRequires = "persisted FINAL_STOPPED_FOR_REVIEW starts or resumes the closed post-final loop on zero-argument /supervised-task or explicit /supervised-task continue; existing research must be reviewed/sliced, and one bounded migration-artifact executor task may run under migration/** unless reviewer or policy blocks it"
            continueCommand = "/supervised-task continue"
            postFinalContinueAction = "RESUME_OR_START_POST_FINAL_LOOP"
            postFinalDispatch = "existing current-ticket/backlog -> change-reviewer -> executor; else approved research -> task-slicer; else existing research -> research-lead; else researcher"
            postFinalStopRule = "Do not report no bounded action after persisted FINAL_STOPPED_FOR_REVIEW dispatch until task-slicer writes BLOCKED_NO_AGENT_EXECUTABLE_TASKS or change-reviewer writes a concrete blocker."
            postFinalWorkflow = @(
                "POST_FINAL_RESEARCH",
                "REVIEW_POST_FINAL_RESEARCH_WITH_RESEARCH_LEAD",
                "SLICE_RESEARCH_INTO_BOUNDED_TASKS",
                "RUN_NEXT_BOUNDED_TASK"
            )
            postFinalResearchAgent = "migration-researcher"
            postFinalResearchLeadAgent = "migration-research-lead"
            postFinalReviewAgent = "migration-change-reviewer"
            postFinalTaskSlicerAgent = "migration-task-slicer"
            postFinalExecutorAgent = "executor"
            postFinalResearchRoot = "migration/runs/<active-run-id>/research/**"
            postFinalBacklogRoot = "migration/state/backlog/**"
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
$scopeContractForRoots = Read-ScopeContractOrNull $workspacePath
$scopeContractAllowedRoots = @(Get-ScopeContractAllowedRoots $scopeContractForRoots)
$effectiveAllowedRoots = @($AllowedRoots) + $scopeContractAllowedRoots + $projectLocalOpenCodeAllowedRoots

$checksumDetail = ""
$guardFiles = @(
    "scripts/check-scope.ps1",
    "scripts/check-final-gate.ps1",
    "scripts/check-harness-policy.ps1",
    "scripts/new-claim.ps1",
    "scripts/new-claim.sh",
    "scripts/update-claim-heartbeat.ps1",
    "scripts/update-claim-heartbeat.sh",
    "scripts/complete-claim.ps1",
    "scripts/complete-claim.sh",
    "scripts/claim-doctor.ps1",
    "scripts/claim-doctor.sh",
    "scripts/write-agent-skill-usage.ps1",
    "scripts/write-agent-skill-usage.sh",
    "scripts/record-agent-skill-profile.ps1",
    "scripts/record-agent-skill-profile.sh",
    "scripts/slice-gate-followups.ps1",
    "scripts/slice-gate-followups.sh",
    "scripts/evaluate-wave-quality-budget.ps1",
    "scripts/evaluate-wave-quality-budget.sh",
    "scripts/start-fresh-wavefront-run.ps1",
    "scripts/start-fresh-wavefront-run.sh",
    "scripts/collect-mapping-research-memory.ps1",
    "scripts/collect-mapping-research-memory.sh",
    "scripts/create-feedback-bundle.ps1",
    "scripts/create-feedback-bundle.sh",
    "scripts/update-current-ticket-status.ps1",
    "scripts/update-current-ticket-status.sh",
    "scripts/export-opencode-session.ps1",
    "scripts/export-opencode-session.sh",
    "scripts/write-sentinel-finding.ps1",
    "scripts/write-sentinel-finding.sh",
    "scripts/complete-sentinel-inspection.ps1",
    "scripts/complete-sentinel-inspection.sh",
    "scripts/update-sentinel-finding-status.ps1",
    "scripts/update-sentinel-finding-status.sh"
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

$scopeContractResult = Test-ScopeContractChangedPaths $workspacePath $RepoRoot
$scopeContractOk = $scopeContractResult.status -ne "FAIL"
Add-Result $results "scope-contract" $scopeContractOk $scopeContractResult.reason
$scopeContractClaim = Test-ClaimStatusForScopeContract $workspacePath $scopeContractForRoots
$claimOk = $scopeContractClaim.status -ne "FAIL"
Add-Result $results "claim-status" $claimOk $scopeContractClaim.reason

$nestedWorkspaceDetail = ""
$nestedWorkspaceOk = Test-NestedMigrationWorkspace $RepoRoot $workspacePath ([ref]$nestedWorkspaceDetail)
Add-Result $results "nested-migration-workspace" $nestedWorkspaceOk $nestedWorkspaceDetail

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

$memoryDetail = ""
$memoryOk = Test-MigrationMemory $workspacePath ([ref]$memoryDetail)
Add-Result $results "memory-doctor" $memoryOk $memoryDetail

$memoryRecallDetail = ""
$memoryRecallOk = Test-MemoryRecallEvidence $workspacePath ([ref]$memoryRecallDetail)
Add-Result $results "memory-recall-evidence" $memoryRecallOk $memoryRecallDetail

$configMergeDetail = ""
$configMergeOk = Test-ConfigDeltaMerge $workspacePath ([ref]$configMergeDetail)
Add-Result $results "config-delta-merge" $configMergeOk $configMergeDetail

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

$runSessionExportPath = if (-not [string]::IsNullOrWhiteSpace($latestRunId)) { Join-Path $workspacePath "runs/$latestRunId/opencode-session-export.md" } else { "" }
$runSessionManifestPath = if (-not [string]::IsNullOrWhiteSpace($latestRunId)) { Join-Path $workspacePath "runs/$latestRunId/opencode-session-export.json" } else { "" }
$hasRunSessionExport = (-not [string]::IsNullOrWhiteSpace($runSessionExportPath)) -and (Test-Path $runSessionExportPath)
$hasRunSessionManifest = (-not [string]::IsNullOrWhiteSpace($runSessionManifestPath)) -and (Test-Path $runSessionManifestPath)
if ($hasRunSessionExport -or $hasRunSessionManifest) {
    $sessionExportDetail = ""
    $sessionExportOk = Test-SessionExportHonest $workspacePath $latestRunId ([ref]$sessionExportDetail)
    Add-Result $results "opencode-session-export-honesty" $sessionExportOk $sessionExportDetail
}

$planSanitizerDetail = ""
$planSanitizerOk = Test-RunPlanSanitized $workspacePath $latestRunId ([ref]$planSanitizerDetail)
Add-Result $results "plan-artifact-sanitized" $planSanitizerOk $planSanitizerDetail

$artifactHygieneScript = Join-Path $workspacePath "scripts/validate-run-artifacts.ps1"
if (Test-Path $artifactHygieneScript) {
    $artifactHygieneArgs = @("-Workspace", $Workspace, "-RepoRoot", $RepoRoot)
    if (-not [string]::IsNullOrWhiteSpace($latestRunId)) {
        $artifactHygieneArgs += @("-RunId", $latestRunId)
    }
    $artifactHygieneExitCode = Invoke-PowerShellScript $artifactHygieneScript $artifactHygieneArgs
    Add-Result $results "artifact-hygiene" ($artifactHygieneExitCode -eq 0) "validate-run-artifacts.ps1 exit code $artifactHygieneExitCode; schema artifact-hygiene/v1"
}
else {
    Add-Result $results "artifact-hygiene" $true "validate-run-artifacts.ps1 not installed in this workspace; artifact-hygiene/v1 skipped for backward-compatible/minimal harness workspace"
}

$researchThresholdDetail = ""
$researchThresholdOk = Test-MemoryResearchThresholds $workspacePath $latestRunId ([ref]$researchThresholdDetail)
Add-Result $results "research-memory-thresholds" $researchThresholdOk $researchThresholdDetail

$waveBudgetScript = Join-Path $workspacePath "scripts/evaluate-wave-quality-budget.ps1"
if (Test-Path $waveBudgetScript) {
    $waveBudgetArgs = @("-Workspace", $workspacePath)
    if (-not [string]::IsNullOrWhiteSpace($latestRunId)) { $waveBudgetArgs += @("-RunId", $latestRunId) }
    [void](Invoke-PowerShellScript $waveBudgetScript $waveBudgetArgs)
}

$waveBudgetDetail = ""
$waveBudgetOk = Test-WaveQualityBudget $workspacePath $latestRunId ([ref]$waveBudgetDetail)
Add-Result $results "wave-quality-budget" $waveBudgetOk $waveBudgetDetail

$mappingResearchDetail = ""
$mappingResearchOk = Test-MappingResearchMemoryAfterBlockedBudget $workspacePath $latestRunId ([ref]$mappingResearchDetail)
Add-Result $results "mapping-research-memory" $mappingResearchOk $mappingResearchDetail

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

$sentinelInspectionDetail = ""
$sentinelInspectionOk = Test-SentinelInspectionPresent $workspacePath $latestRunId ([ref]$sentinelInspectionDetail)
Add-Result $results "sentinel-inspection-present" $sentinelInspectionOk $sentinelInspectionDetail

$sentinelDetail = ""
$sentinelOk = Test-OpenSentinelBlockingFindings $workspacePath ([ref]$sentinelDetail)
Add-Result $results "sentinel-open-critical-findings" $sentinelOk $sentinelDetail

$skillUsageDetail = ""
$skillUsageOk = Test-AgentSkillUsageEvidence $workspacePath $latestRunId ([ref]$skillUsageDetail)
Add-Result $results "agent-skill-usage-evidence" $skillUsageOk $skillUsageDetail

$runEvidenceDetail = ""
$runEvidenceOk = Test-RunEvidenceBundle $workspacePath $latestRunId ([ref]$runEvidenceDetail)
Add-Result $results "run-evidence-bundle" $runEvidenceOk $runEvidenceDetail

$passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
$continuationSources = @()
foreach ($candidatePath in @(
    (Join-Path $workspacePath "current-ticket.md"),
    (Join-Path $workspacePath "state/current-ticket-status.json"),
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
    (Find-LatestFile -Root $workspacePath -Names @("wave-quality-budget.md", "wave-quality-budget.json")),
    (Find-LatestFile -Root $workspacePath -Names @("mapping-research-memory.md", "mapping-research-memory.json", "mapping-research-candidates.jsonl")),
    (Find-LatestFile -Root $workspacePath -Names @("migration-board.md", "migration-board.json"))
)) {
    if ($latestEvidence -ne $null) {
        $continuationSources += [pscustomobject]@{ name = (Get-RelativePathCompat $workspacePath $latestEvidence.FullName); path = $latestEvidence.FullName }
    }
}
$continuationCandidate = Find-StructuredContinuationCandidate $continuationSources
if ($null -eq $continuationCandidate) {
    $continuationCandidate = Find-ContinuationCandidate $continuationSources
}
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

if ($null -eq $continuationCandidate -and -not $passed) {
    $continuationCandidate = New-GateFollowupSlicerCandidate $workspacePath $results
}

$remediationBudgetExhausted = Test-RemediationBudgetExhausted $workspacePath
$continuation = New-ContinuationDecision $passed $results $continuationCandidate $hasNonFinalStatus $remediationBudgetExhausted

$report = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    status = if ($passed -and $continuation.status -ne "CONTINUE_REQUIRED") { "PASS" } else { "FAIL" }
    continuationStatus = $continuation.status
    workspace = $workspacePath
    checks = $results
    scopeContract = $scopeContractResult
    claimStatus = $scopeContractClaim
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
if ($continuation.postSuccessPolicy) {
    [void]$md.AppendLine("Post-success policy: **$($continuation.postSuccessPolicy)**")
    if ($continuation.status -eq "FINAL_WITH_LIMITATIONS") {
        [void]$md.AppendLine("Stopped because the automatic remediation budget is exhausted. Remaining limitations must be reported; no further post-final ticket may be created automatically.")
    }
    else {
        [void]$md.AppendLine("Stopped because SUCCESS checkpoint requires review before post-final research or another bounded ticket.")
    }
    [void]$md.AppendLine(("To continue, run: {0}" -f $continuation.continueCommand))
    if ($continuation.postFinalContinueAction) {
        [void]$md.AppendLine(("Post-final continue action: {0}" -f $continuation.postFinalContinueAction))
    }
}
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
if ($continuation.postSuccessPolicy) {
    [void]$continuationMd.AppendLine()
    [void]$continuationMd.AppendLine(("Post-success policy: {0}" -f $continuation.postSuccessPolicy))
    [void]$continuationMd.AppendLine(("Continue requires: {0}" -f $continuation.continueRequires))
    [void]$continuationMd.AppendLine(("Continue command: {0}" -f $continuation.continueCommand))
    if ($continuation.postFinalContinueAction) {
        [void]$continuationMd.AppendLine(("Post-final continue action: {0}" -f $continuation.postFinalContinueAction))
        [void]$continuationMd.AppendLine(("Research agent: {0}" -f $continuation.postFinalResearchAgent))
        [void]$continuationMd.AppendLine(("Research lead agent: {0}" -f $continuation.postFinalResearchLeadAgent))
        [void]$continuationMd.AppendLine(("Compatibility review agent: {0}" -f $continuation.postFinalReviewAgent))
        [void]$continuationMd.AppendLine(("Task slicer agent: {0}" -f $continuation.postFinalTaskSlicerAgent))
    }
}
if ($continuation.nextAction) {
    [void]$continuationMd.AppendLine()
    [void]$continuationMd.AppendLine(("Next action: {0}" -f $continuation.nextAction))
    [void]$continuationMd.AppendLine(("Source: {0}" -f $continuation.source))
}
Set-Content -Path $continuationMdPath -Value $continuationMd.ToString() -Encoding UTF8

Update-HarnessRunStateFromFinalGate $workspacePath $report $continuation $results

Write-Host "FINAL_GATE_$($report.status)"
Write-Host "HARNESS_CONTINUATION_$($continuation.status)"
if ($continuation.postSuccessPolicy) {
    Write-Host "HARNESS_SUCCESS_$($continuation.postSuccessPolicy)"
    if ($continuation.status -eq "FINAL_WITH_LIMITATIONS") {
        Write-Host "Harness run status: WAVE_REMEDIATION_BUDGET_EXHAUSTED"
        Write-Host "Stopped because the automatic remediation budget is exhausted. Remaining limitations must be reported."
    }
    else {
        Write-Host "Harness run status: FINAL_STOPPED_FOR_REVIEW"
        Write-Host "Stopped because SUCCESS checkpoint requires review before post-final research or another bounded ticket."
    }
    Write-Host "To continue, run: $($continuation.continueCommand)"
    Write-Host "Continue command: $($continuation.continueCommand)"
    if ($continuation.postFinalContinueAction) {
        Write-Host "Post-final continue action: $($continuation.postFinalContinueAction)"
    }
}
if ($continuation.nextAction) {
    Write-Host "Next action: $($continuation.nextAction)"
}

foreach ($check in $results) {
    if (-not $check.passed) {
        Write-Host "Check failed: $($check.name) - $($check.detail)"
    }
}

Write-Host "Report: $mdPath"
Write-Host "Continuation: $continuationMdPath"

if ($passed -and $continuation.status -ne "CONTINUE_REQUIRED") {
    exit 0
}

exit 1
