param(
    [string]$Workspace = "migration",
    [string]$RepoRoot = ".",
    [string[]]$AllowedRoots = @($Workspace),
    [switch]$AllowNoGit,
    [switch]$AllowGuardChanges,
    [switch]$SkipGitStatus
)

$ErrorActionPreference = "Stop"

function Add-Result($Results, [string]$Name, [bool]$Passed, [string]$Detail) {
    $Results.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail }) | Out-Null
}

function Read-TextIfExists([string]$Path) {
    if (Test-Path $Path) { return Get-Content -Raw -Path $Path }
    return ""
}

function Get-FullPath([string]$Base, [string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path $Base $Path))
}

function Find-LatestRunId([string]$WorkspacePath) {
    $agentState = Read-TextIfExists (Join-Path $WorkspacePath "agent-state.md")
    $m = [regex]::Match($agentState, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
    if ($m.Success) { return $m.Groups[1].Value }
    return ""
}

function Test-GlobPath([string]$Path, [string]$Pattern) {
    $p = $Path.Replace("\", "/").TrimStart("./")
    $rx = "^" + [regex]::Escape($Pattern.Replace("\", "/").TrimStart("./")).Replace("\*\*", ".*").Replace("\*", "[^/]*").Replace("\?", ".") + "$"
    return $p -match $rx
}

function Test-AnyPattern([string]$Path, [object[]]$Patterns) {
    foreach ($pattern in @($Patterns)) {
        if (Test-GlobPath $Path $pattern.ToString()) { return $true }
    }
    return $false
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
    try { return Get-Content -Raw -Path $contractPath | ConvertFrom-Json -ErrorAction Stop }
    catch { return [pscustomobject]@{ __invalid = $true; __error = $_.Exception.Message } }
}

function Get-ScopeContractAllowedRoots([object]$Contract) {
    $roots = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Contract -or $Contract.__invalid) { return @() }
    if (-not [string]::IsNullOrWhiteSpace([string]$Contract.workspaceRoot)) { $roots.Add((Normalize-ScopeContractPath ([string]$Contract.workspaceRoot))) | Out-Null }
    foreach ($root in @($Contract.allowedSourceRoots)) {
        $normalized = Normalize-ScopeContractPath ([string]$root)
        if (-not [string]::IsNullOrWhiteSpace($normalized)) { $roots.Add($normalized) | Out-Null }
    }
    return @($roots | Sort-Object -Unique)
}

function Test-ChangedPathsAgainstScopeContract([object]$Contract, [string[]]$Changed, [ref]$Detail) {
    if ($null -eq $Contract) { $Detail.Value = "scope contract missing; skipped"; return $true }
    if ($Contract.__invalid) { $Detail.Value = "invalid scope contract: $($Contract.__error)"; return $false }

    $workspaceRoot = Normalize-ScopeContractPath ([string]$Contract.workspaceRoot)
    $allowedSourceRoots = @($Contract.allowedSourceRoots | ForEach-Object { Normalize-ScopeContractPath ([string]$_) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $allowedFiles = @($Contract.allowedFiles | ForEach-Object { Normalize-ScopeContractPath ([string]$_) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $forbiddenRoots = @($Contract.forbiddenRoots | ForEach-Object { Normalize-ScopeContractPath ([string]$_) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $outOfScope = New-Object System.Collections.Generic.List[string]
    $forbidden = New-Object System.Collections.Generic.List[string]

    foreach ($raw in @($Changed)) {
        $path = Normalize-ScopeContractPath $raw
        $hitForbidden = $false
        foreach ($root in $forbiddenRoots) {
            if (Test-ScopeContractPathUnderRoot $path $root) { $forbidden.Add($path) | Out-Null; $hitForbidden = $true; break }
        }
        if ($hitForbidden) { continue }

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

    $maxChangedFiles = 0
    try { $maxChangedFiles = [int]$Contract.maxChangedFiles } catch { $maxChangedFiles = 0 }
    $tooMany = $maxChangedFiles -gt 0 -and @($Changed).Count -gt $maxChangedFiles
    if ($forbidden.Count -gt 0) { $Detail.Value = "forbiddenRoot hits: " + (($forbidden | Sort-Object -Unique) -join ", "); return $false }
    if ($outOfScope.Count -gt 0) { $Detail.Value = "out-of-scope paths: " + (($outOfScope | Sort-Object -Unique) -join ", "); return $false }
    if ($tooMany) { $Detail.Value = "changed file count $(@($Changed).Count) exceeds maxChangedFiles $maxChangedFiles"; return $false }
    $Detail.Value = "scope-contract changed path check passed; checked $(@($Changed).Count) paths"
    return $true
}

function Convert-ToWorkspaceRelativePath([string]$Path, [string]$WorkspaceRootName) {
    $normalized = $Path.Replace("\", "/").TrimStart("./")
    $workspacePrefix = $WorkspaceRootName.Replace("\", "/").TrimStart("./").TrimEnd("/")
    if (-not [string]::IsNullOrWhiteSpace($workspacePrefix) -and
        $normalized.StartsWith($workspacePrefix + "/", [StringComparison]::OrdinalIgnoreCase)) {
        return $normalized.Substring($workspacePrefix.Length + 1)
    }

    return $normalized
}

function Read-GuardChecksumIndex([string]$WorkspacePath) {
    $checksumPath = Join-Path $WorkspacePath ".migration-kit/guard-checksums.json"
    if (-not (Test-Path $checksumPath)) {
        throw "missing $checksumPath"
    }

    $json = Get-Content -Raw -Path $checksumPath | ConvertFrom-Json
    $expected = @{}
    foreach ($entry in @($json.files)) {
        if ($entry.path -and $entry.sha256) {
            $expected[$entry.path.ToString().Replace("\", "/")] = $entry.sha256.ToString().ToUpperInvariant()
        }
    }

    return $expected
}

function Get-RequiredGuardChecksumFiles {
    return @(
        "scripts/check-scope.ps1",
        "scripts/check-scope.sh",
        "scripts/check-final-gate.ps1",
        "scripts/check-final-gate.sh",
        "scripts/check-harness-policy.ps1",
        "scripts/check-harness-policy.sh",
        "scripts/new-claim.ps1",
        "scripts/new-claim.sh",
        "scripts/update-claim-heartbeat.ps1",
        "scripts/update-claim-heartbeat.sh",
        "scripts/complete-claim.ps1",
        "scripts/complete-claim.sh",
        "scripts/claim-doctor.ps1",
        "scripts/claim-doctor.sh",
        "scripts/build-harness-dashboard.ps1",
        "scripts/build-harness-dashboard.sh",
        "scripts/export-opencode-session.ps1",
        "scripts/export-opencode-session.sh",
        "scripts/slice-gate-followups.ps1",
        "scripts/slice-gate-followups.sh",
        "scripts/evaluate-wave-quality-budget.ps1",
        "scripts/evaluate-wave-quality-budget.sh",
        "scripts/collect-mapping-research-memory.ps1",
        "scripts/collect-mapping-research-memory.sh",
        "scripts/create-feedback-bundle.ps1",
        "scripts/create-feedback-bundle.sh",
        "scripts/write-sentinel-finding.ps1",
        "scripts/write-sentinel-finding.sh",
        "scripts/complete-sentinel-inspection.ps1",
        "scripts/complete-sentinel-inspection.sh",
        "scripts/update-current-ticket-status.ps1",
        "scripts/update-current-ticket-status.sh",
        "scripts/update-sentinel-finding-status.ps1",
        "scripts/update-sentinel-finding-status.sh"
    )
}

function Test-GuardChecksumIndexMatchesCurrentFiles([string]$WorkspacePath, [hashtable]$Expected, [ref]$Detail) {
    $mismatches = New-Object System.Collections.Generic.List[string]
    foreach ($required in @(Get-RequiredGuardChecksumFiles)) {
        if (-not $Expected.ContainsKey($required)) {
            $mismatches.Add("$required missing checksum baseline") | Out-Null
        }
    }

    foreach ($relative in @($Expected.Keys | Sort-Object)) {
        $fullPath = Join-Path $WorkspacePath $relative
        if (-not (Test-Path $fullPath)) {
            $mismatches.Add("$relative missing on disk") | Out-Null
            continue
        }

        $actual = (Get-FileHash -Algorithm SHA256 -Path $fullPath).Hash.ToUpperInvariant()
        if ($actual -ne $Expected[$relative]) {
            $mismatches.Add("$relative checksum mismatch") | Out-Null
        }
    }

    if ($mismatches.Count -gt 0) {
        $Detail.Value = $mismatches -join "; "
        return $false
    }

    $Detail.Value = "all guard file hashes match guard-checksums baseline"
    return $true
}

function Test-GuardSensitiveChangesMatchChecksumBaseline([string]$WorkspacePath, [string]$WorkspaceRootName, [string[]]$GuardChanges, [ref]$Detail) {
    if ($null -eq $GuardChanges -or $GuardChanges.Count -eq 0) {
        $Detail.Value = "no guard-sensitive changes"
        return $true
    }

    $checksumPathPattern = ($WorkspaceRootName.Replace("\", "/").TrimStart("./").TrimEnd("/") + "/.migration-kit/guard-checksums.json")
    $checksumChanged = $false
    $guardFileChanges = New-Object System.Collections.Generic.List[string]
    foreach ($path in @($GuardChanges)) {
        $normalized = $path.Replace("\", "/").TrimStart("./")
        if ($normalized.Equals($checksumPathPattern, [StringComparison]::OrdinalIgnoreCase)) {
            $checksumChanged = $true
            continue
        }

        $guardFileChanges.Add($normalized) | Out-Null
    }

    try {
        $expected = Read-GuardChecksumIndex $WorkspacePath
    }
    catch {
        $Detail.Value = $_.Exception.Message
        return $false
    }

    if ($checksumChanged -and $guardFileChanges.Count -eq 0) {
        $currentBaselineDetail = ""
        $currentBaselineOk = Test-GuardChecksumIndexMatchesCurrentFiles `
            -WorkspacePath $WorkspacePath `
            -Expected $expected `
            -Detail ([ref]$currentBaselineDetail)

        if ($currentBaselineOk) {
            $Detail.Value = "guard-checksums.json metadata-only change accepted; $currentBaselineDetail"
            return $true
        }

        $Detail.Value = "guard-checksums.json changed without changed guard scripts; $currentBaselineDetail"
        return $false
    }

    $mismatches = New-Object System.Collections.Generic.List[string]
    foreach ($changedPath in $guardFileChanges) {
        $relative = Convert-ToWorkspaceRelativePath $changedPath $WorkspaceRootName
        if (-not $expected.ContainsKey($relative)) {
            $mismatches.Add("$changedPath missing checksum baseline") | Out-Null
            continue
        }

        $fullPath = Join-Path $WorkspacePath $relative
        if (-not (Test-Path $fullPath)) {
            $mismatches.Add("$changedPath missing on disk") | Out-Null
            continue
        }

        $actual = (Get-FileHash -Algorithm SHA256 -Path $fullPath).Hash.ToUpperInvariant()
        if ($actual -ne $expected[$relative]) {
            $mismatches.Add("$changedPath checksum mismatch") | Out-Null
        }
    }

    if ($mismatches.Count -gt 0) {
        $Detail.Value = $mismatches -join "; "
        return $false
    }

    $changedSummary = @($GuardChanges | Sort-Object -Unique) -join ", "
    $Detail.Value = "guard-sensitive changes match guard-checksums baseline; changed: $changedSummary"
    return $true
}

function Expand-AllowedRootPatterns([string[]]$Roots) {
    $patterns = New-Object System.Collections.Generic.List[string]
    foreach ($rootValue in @($Roots)) {
        if ([string]::IsNullOrWhiteSpace($rootValue)) { continue }
        $normalized = $rootValue.Replace("\", "/").TrimStart("./").TrimEnd("/")
        if ([string]::IsNullOrWhiteSpace($normalized)) { continue }
        $patterns.Add($normalized) | Out-Null
        if (-not $normalized.EndsWith("/**")) {
            $patterns.Add("$normalized/**") | Out-Null
        }
    }
    return @($patterns | Sort-Object -Unique)
}

function Get-ProjectLocalOpenCodePatterns {
    return @(
        "AGENTS.md",
        "opencode.jsonc",
        ".opencode",
        ".opencode/**",
        ".opencode-migrator",
        ".opencode-migrator/**",
        "opencode",
        "opencode/**"
    )
}

function Read-ScopeBaselinePathSet([string]$WorkspacePath) {
    $paths = @{}
    $baselinePath = Join-Path $WorkspacePath "state/scope-baseline.json"
    if (-not (Test-Path $baselinePath)) { return $paths }
    try {
        $json = Get-Content -Raw -Path $baselinePath | ConvertFrom-Json -ErrorAction Stop
        foreach ($entry in @($json.entries)) {
            $pathValue = [string]$entry.path
            if ([string]::IsNullOrWhiteSpace($pathValue)) { continue }
            $paths[$pathValue.Replace("\", "/").TrimStart("./")] = $true
        }
    }
    catch {
        Write-Warning "Could not read scope baseline ${baselinePath}: $($_.Exception.Message)"
    }
    return $paths
}

function Get-GitChangedPaths([string]$Root, [switch]$AllowNoGit) {
    Push-Location $Root
    try {
        $gitRoot = (& git rev-parse --show-toplevel 2>$null)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitRoot)) {
            if ($AllowNoGit) { return @() }
            throw "No git repository found at $Root."
        }
        $bytes = & git status --porcelain=v1 -z --untracked-files=all
        if ([string]::IsNullOrEmpty($bytes)) { return @() }
        $tokens = $bytes -split [char]0
        $paths = New-Object System.Collections.Generic.List[string]
        for ($i = 0; $i -lt $tokens.Length; $i++) {
            $entry = $tokens[$i]
            if ([string]::IsNullOrEmpty($entry) -or $entry.Length -lt 4) { continue }
            $xy = $entry.Substring(0, 2)
            $path = $entry.Substring(3)
            if (-not [string]::IsNullOrWhiteSpace($path)) { $paths.Add($path.Replace("\", "/")) | Out-Null }
            if ($xy.Contains("R") -or $xy.Contains("C")) {
                if ($i + 1 -lt $tokens.Length -and -not [string]::IsNullOrEmpty($tokens[$i + 1])) {
                    $paths.Add($tokens[$i + 1].Replace("\", "/")) | Out-Null
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

$repoRootPath = Get-FullPath (Get-Location) $RepoRoot
$workspacePath = Get-FullPath $repoRootPath $Workspace
$results = New-Object System.Collections.Generic.List[object]

$policyPath = Join-Path $workspacePath "state/harness-policy.json"
if (-not (Test-Path $policyPath)) {
    Add-Result $results "policy-file" $false "missing $policyPath"
    $policy = $null
} else {
    try {
        $policy = Get-Content -Raw -Path $policyPath | ConvertFrom-Json
        Add-Result $results "policy-file" $true "loaded $policyPath"
    } catch {
        Add-Result $results "policy-file" $false "invalid JSON: $($_.Exception.Message)"
        $policy = $null
    }
}

if ($policy -ne $null) {
    Add-Result $results "policy-schema" ($policy.schemaVersion -ge 1 -and $policy.mode) "schemaVersion=$($policy.schemaVersion); mode=$($policy.mode)"

    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($relative in @($policy.requiredFiles)) {
        if (-not (Test-Path (Join-Path $workspacePath $relative.ToString()))) { $missing.Add($relative.ToString()) | Out-Null }
    }
    Add-Result $results "required-files" ($missing.Count -eq 0) $(if ($missing.Count -eq 0) { "all required files exist" } else { "missing: " + ($missing -join ", ") })

    $scopeContract = Read-ScopeContractOrNull $workspacePath
    $scopeContractAllowedRoots = @(Get-ScopeContractAllowedRoots $scopeContract)

    $latestRunId = Find-LatestRunId $workspacePath
    $runPath = if ([string]::IsNullOrWhiteSpace($latestRunId)) { "" } else { Join-Path $workspacePath "runs/$latestRunId" }
    $runFiles = @("Prompt.md", "Plan.md", "Implement.md", "Documentation.md", "trace.jsonl")
    $missingRunFiles = New-Object System.Collections.Generic.List[string]
    if ([string]::IsNullOrWhiteSpace($latestRunId)) {
        $missingRunFiles.Add("agent-state.md Latest run line") | Out-Null
    } else {
        foreach ($file in $runFiles) {
            if (-not (Test-Path (Join-Path $runPath $file))) { $missingRunFiles.Add("runs/$latestRunId/$file") | Out-Null }
        }
    }
    Add-Result $results "active-run-files" ($missingRunFiles.Count -eq 0) $(if ($missingRunFiles.Count -eq 0) { "latest run $latestRunId is resumable" } else { "missing: " + ($missingRunFiles -join ", ") })

    $changed = @()
    if ($SkipGitStatus) {
        Add-Result $results "git-status-readable" $true "skipped by -SkipGitStatus"
        Add-Result $results "changed-paths-allowed" $true "skipped by -SkipGitStatus"
        Add-Result $results "scope-contract" $true "skipped by -SkipGitStatus"
        Add-Result $results "guard-sensitive-clean" $true "skipped by -SkipGitStatus"
    } else {
        try {
            $changed = @(Get-GitChangedPaths $repoRootPath -AllowNoGit:$AllowNoGit)
            Add-Result $results "git-status-readable" $true "changed paths: $($changed.Count)"
        } catch {
            Add-Result $results "git-status-readable" $false $_.Exception.Message
        }

        if ($changed.Count -gt 0) {
            $projectLocalOpenCodePatterns = @(Get-ProjectLocalOpenCodePatterns)
            $effectiveAllowedWrites = @($policy.allowedWrites) + @(Expand-AllowedRootPatterns (@($AllowedRoots) + $scopeContractAllowedRoots)) + $projectLocalOpenCodePatterns
            $scopeBaselinePaths = Read-ScopeBaselinePathSet $workspacePath
            $outsideAllowedRaw = @($changed | Where-Object { -not (Test-AnyPattern $_ @($effectiveAllowedWrites)) })
            $outsideAllowed = @($outsideAllowedRaw | Where-Object { -not $scopeBaselinePaths.ContainsKey($_.Replace("\", "/").TrimStart("./")) })
            $outsideBaselineIgnored = @($outsideAllowedRaw | Where-Object { $scopeBaselinePaths.ContainsKey($_.Replace("\", "/").TrimStart("./")) })
            Add-Result $results "changed-paths-allowed" ($outsideAllowed.Count -eq 0) $(if ($outsideAllowed.Count -eq 0) { "all new/changed paths match allowedWrites/AllowedRoots; ignored pre-existing baseline paths: " + ($outsideBaselineIgnored.Count) } else { "outside allowedWrites/AllowedRoots: " + ($outsideAllowed -join ", ") })
            $scopeContractDetail = ""
            $scopeContractOk = Test-ChangedPathsAgainstScopeContract -Contract $scopeContract -Changed @($changed) -Detail ([ref]$scopeContractDetail)
            Add-Result $results "scope-contract" $scopeContractOk $scopeContractDetail

            $projectLocalOpenCodeChanges = @($changed | Where-Object { Test-AnyPattern $_ $projectLocalOpenCodePatterns })
            $guardChanges = @($changed | Where-Object {
                (Test-AnyPattern $_ @($policy.guardSensitiveWrites)) -and
                (-not (Test-AnyPattern $_ $projectLocalOpenCodePatterns))
            })
            $baselineDetail = ""
            $baselineOk = $false
            if ($guardChanges.Count -gt 0 -and -not $AllowGuardChanges) {
                $baselineOk = Test-GuardSensitiveChangesMatchChecksumBaseline `
                    -WorkspacePath $workspacePath `
                    -WorkspaceRootName $Workspace `
                    -GuardChanges @($guardChanges) `
                    -Detail ([ref]$baselineDetail)
            }

            $guardOk = ($guardChanges.Count -eq 0) -or $AllowGuardChanges -or $baselineOk
            $guardDetail = if ($guardChanges.Count -eq 0) {
                if ($projectLocalOpenCodeChanges.Count -gt 0) {
                    "no guard-sensitive changed paths; ignored project-local OpenCode config: " + (($projectLocalOpenCodeChanges | Sort-Object -Unique) -join ", ")
                }
                else {
                    "no guard-sensitive changed paths"
                }
            }
            elseif ($AllowGuardChanges) {
                "guard-sensitive changes allowed by -AllowGuardChanges: " + ($guardChanges -join ", ")
            }
            elseif ($baselineOk) {
                $baselineDetail
            }
            else {
                "guard-sensitive changes: " + ($guardChanges -join ", ") + "; baseline check: " + $baselineDetail
            }
            Add-Result $results "guard-sensitive-clean" $guardOk $guardDetail
        } else {
            Add-Result $results "changed-paths-allowed" $true "no git changes detected or git skipped"
            Add-Result $results "scope-contract" $true "no git changes detected or git skipped"
            Add-Result $results "guard-sensitive-clean" $true "no guard-sensitive changed paths detected"
        }
    }

    $openCodeCandidates = @(
        (Join-Path $repoRootPath "opencode.jsonc")
        (Join-Path $repoRootPath ".opencode-migrator/opencode.jsonc")
        (Join-Path $repoRootPath ".opencode/opencode.jsonc")
    )
    $openCodeConfig = $openCodeCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ($openCodeConfig) {
        $text = Read-TextIfExists $openCodeConfig
        $hasDenyAllEdit = $text -match '"edit"\s*:\s*\{[\s\S]*?"\*"\s*:\s*"deny"'
        $hasMigrationAllow = $text -match '"migration/\*\*"\s*:\s*"allow"'
        $hasTrustedProjectProfile = ($text -match '"edit"\s*:\s*"allow"') -and
            ($text -match '"bash"\s*:\s*"allow"') -and
            ($text -match '"external_directory"\s*:\s*"deny"')
        $policyOk = ($hasDenyAllEdit -and $hasMigrationAllow) -or $hasTrustedProjectProfile
        Add-Result $results "opencode-edit-policy" $policyOk "config=$openCodeConfig; denyAllEdit=$hasDenyAllEdit; migrationAllow=$hasMigrationAllow; trustedProject=$hasTrustedProjectProfile"
    } else {
        Add-Result $results "opencode-edit-policy" $true "no project OpenCode config found; skip template-level check"
    }
}

$passed = @($results | Where-Object { -not $_.passed }).Count -eq 0
$stateDir = Join-Path $workspacePath "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null

$report = [ordered]@{
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    status = if ($passed) { "PASS" } else { "FAIL" }
    workspace = $workspacePath
    checks = $results
}

$jsonPath = Join-Path $stateDir "harness-policy-result.json"
$mdPath = Join-Path $stateDir "harness-policy-result.md"
$report | ConvertTo-Json -Depth 20 | Set-Content -Path $jsonPath -Encoding UTF8

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Harness Policy Result")
[void]$md.AppendLine()
[void]$md.AppendLine("Status: **$($report.status)**")
[void]$md.AppendLine()
foreach ($check in $results) {
    $status = if ($check.passed) { "PASS" } else { "FAIL" }
    [void]$md.AppendLine("- ${status}: $($check.name) - $($check.detail)")
}
Set-Content -Path $mdPath -Value $md.ToString() -Encoding UTF8

Write-Host "HARNESS_POLICY_$($report.status)"
Write-Host "Report: $mdPath"
if ($passed) { exit 0 }
exit 1
