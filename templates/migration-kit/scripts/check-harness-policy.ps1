param(
    [string]$Workspace = "migration",
    [string]$RepoRoot = ".",
    [string[]]$AllowedRoots = @($Workspace),
    [switch]$AllowNoGit,
    [switch]$AllowGuardChanges
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
    try {
        $changed = @(Get-GitChangedPaths $repoRootPath -AllowNoGit:$AllowNoGit)
        Add-Result $results "git-status-readable" $true "changed paths: $($changed.Count)"
    } catch {
        Add-Result $results "git-status-readable" $false $_.Exception.Message
    }

    if ($changed.Count -gt 0) {
        $outsideAllowed = @($changed | Where-Object { -not (Test-AnyPattern $_ @($policy.allowedWrites)) })
        Add-Result $results "changed-paths-allowed" ($outsideAllowed.Count -eq 0) $(if ($outsideAllowed.Count -eq 0) { "all changed paths match allowedWrites" } else { "outside allowedWrites: " + ($outsideAllowed -join ", ") })

        $guardChanges = @($changed | Where-Object { Test-AnyPattern $_ @($policy.guardSensitiveWrites) })
        $guardOk = ($guardChanges.Count -eq 0) -or $AllowGuardChanges
        Add-Result $results "guard-sensitive-clean" $guardOk $(if ($guardChanges.Count -eq 0) { "no guard-sensitive changed paths" } else { "guard-sensitive changes: " + ($guardChanges -join ", ") })
    } else {
        Add-Result $results "changed-paths-allowed" $true "no git changes detected or git skipped"
        Add-Result $results "guard-sensitive-clean" $true "no guard-sensitive changed paths detected"
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
        Add-Result $results "opencode-edit-policy" ($hasDenyAllEdit -and $hasMigrationAllow) "config=$openCodeConfig; denyAllEdit=$hasDenyAllEdit; migrationAllow=$hasMigrationAllow"
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
