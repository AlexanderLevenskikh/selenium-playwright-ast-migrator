param(
    [string]$RepoRoot = ".",
    [string[]]$AllowedRoots = @("migration"),
    [string]$ScopeBaselinePath = "",
    [switch]$AllowNoGit
)

$ErrorActionPreference = "Stop"

function Normalize-GitPath([string]$Path) {
    return ($Path -replace "\\", "/").TrimStart("./")
}

function Convert-ToGitRelativePath([string]$GitRoot, [string]$Path) {
    $candidate = $Path
    if ([System.IO.Path]::IsPathRooted($candidate)) {
        try {
            $fullGitRoot = [System.IO.Path]::GetFullPath($GitRoot).TrimEnd('\', '/')
            $fullCandidate = [System.IO.Path]::GetFullPath((Resolve-Path $candidate).Path).TrimEnd('\', '/')
            if ($fullCandidate.Equals($fullGitRoot, [StringComparison]::OrdinalIgnoreCase)) {
                return ""
            }

            if ($fullCandidate.StartsWith($fullGitRoot + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
                $fullCandidate.StartsWith($fullGitRoot + [System.IO.Path]::AltDirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
                return $fullCandidate.Substring($fullGitRoot.Length).TrimStart('\', '/')
            }
        }
        catch {
            return $Path
        }
    }

    return $candidate
}

function Test-AllowedPath([string]$Path, [string[]]$AllowedRootPatterns) {
    $normalizedPath = Normalize-GitPath $Path
    foreach ($rootPattern in $AllowedRootPatterns) {
        $root = Normalize-GitPath $rootPattern
        if ([string]::IsNullOrWhiteSpace($root)) {
            return $true
        }

        $rootWithSlash = if ($root.EndsWith("/")) { $root } else { "$root/" }
        if ($normalizedPath.Equals($root.TrimEnd("/"), [StringComparison]::OrdinalIgnoreCase) -or
            $normalizedPath.StartsWith($rootWithSlash, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Read-GitStatusPorcelainZ {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "git"
    $psi.Arguments = "status --porcelain=v1 -z --untracked-files=all"
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = $psi
    [void]$process.Start()
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    $process.WaitForExit()

    if ($process.ExitCode -ne 0) {
        throw "git status failed: $stderr"
    }

    return $stdout
}

function Get-ChangedPathsFromPorcelainZ([string]$Status) {
    $tokens = $Status -split [char]0
    $paths = New-Object System.Collections.Generic.List[string]
    for ($i = 0; $i -lt $tokens.Length; $i++) {
        $entry = $tokens[$i]
        if ([string]::IsNullOrEmpty($entry)) {
            continue
        }

        if ($entry.Length -lt 4) {
            continue
        }

        $xy = $entry.Substring(0, 2)
        $path = $entry.Substring(3)
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            $paths.Add($path)
        }

        if ($xy.Contains("R") -or $xy.Contains("C")) {
            if ($i + 1 -lt $tokens.Length -and -not [string]::IsNullOrEmpty($tokens[$i + 1])) {
                $paths.Add($tokens[$i + 1])
                $i++
            }
        }
    }

    return $paths
}

function Resolve-ScopeBaselinePath([string]$GitRoot, [string[]]$AllowedRootPatterns, [string]$ExplicitPath) {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if ([System.IO.Path]::IsPathRooted($ExplicitPath)) { return $ExplicitPath }
        return Join-Path $GitRoot $ExplicitPath
    }

    foreach ($rootPattern in $AllowedRootPatterns) {
        $normalized = Normalize-GitPath (Convert-ToGitRelativePath -GitRoot $GitRoot -Path $rootPattern)
        if ([string]::IsNullOrWhiteSpace($normalized) -or $normalized.Contains("*") -or $normalized.Contains("?")) { continue }
        return Join-Path $GitRoot (Join-Path $normalized "state/scope-baseline.json")
    }

    return Join-Path $GitRoot "migration/state/scope-baseline.json"
}

function Read-ScopeBaseline([string]$Path) {
    $map = @{}
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path $Path)) {
        return $map
    }

    try {
        $json = Get-Content -Raw -Path $Path | ConvertFrom-Json -ErrorAction Stop
        foreach ($entry in @($json.entries)) {
            $pathValue = [string]$entry.path
            if ([string]::IsNullOrWhiteSpace($pathValue)) { continue }
            $map[(Normalize-GitPath $pathValue)] = [ordered]@{
                status = [string]$entry.status
                fingerprint = [string]$entry.fingerprint
            }
        }
    }
    catch {
        Write-Warning "Could not read scope baseline ${Path}: $($_.Exception.Message)"
    }

    return $map
}

function Get-FileFingerprint([string]$GitRoot, [string]$RelativePath) {
    $full = Join-Path $GitRoot $RelativePath
    if (-not (Test-Path -LiteralPath $full)) { return "missing" }
    try {
        $item = Get-Item -LiteralPath $full -ErrorAction Stop
        if ($item.PSIsContainer) { return "directory" }
        return (Get-FileHash -Algorithm SHA256 -LiteralPath $full).Hash.ToUpperInvariant()
    }
    catch {
        return "unavailable"
    }
}

function Get-StatusEntriesFromPorcelainZ([string]$Status) {
    $tokens = $Status -split [char]0
    $entries = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $tokens.Length; $i++) {
        $entry = $tokens[$i]
        if ([string]::IsNullOrEmpty($entry) -or $entry.Length -lt 4) { continue }

        $xy = $entry.Substring(0, 2)
        $path = $entry.Substring(3)
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            $entries.Add([pscustomobject]@{ status = $xy; path = $path })
        }

        if ($xy.Contains("R") -or $xy.Contains("C")) {
            if ($i + 1 -lt $tokens.Length -and -not [string]::IsNullOrEmpty($tokens[$i + 1])) {
                $entries.Add([pscustomobject]@{ status = $xy; path = $tokens[$i + 1] })
                $i++
            }
        }
    }

    return $entries
}

Push-Location $RepoRoot
try {
    $gitRoot = (& git rev-parse --show-toplevel 2>$null)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($gitRoot)) {
        if ($AllowNoGit) {
            Write-Host "SCOPE_GUARD: no git repository found; skipped because -AllowNoGit was set"
            exit 0
        }

        Write-Error "SCOPE_GUARD_FAILED: no git repository found at $RepoRoot"
        exit 2
    }

    $gitRoot = $gitRoot.Trim()
    $normalizedRoots = @($AllowedRoots | ForEach-Object {
        Convert-ToGitRelativePath -GitRoot $gitRoot -Path $_
    })

    # Project-local OpenCode files are migration harness/config files installed at
    # repository root so OpenCode Desktop/CLI can see them. They are not product
    # source edits and must not make routine scope checks fail.
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
    $normalizedRoots = @($normalizedRoots) + $projectLocalOpenCodeAllowedRoots

    $status = Read-GitStatusPorcelainZ
    $statusEntries = @(Get-StatusEntriesFromPorcelainZ $status)
    $baselinePath = Resolve-ScopeBaselinePath $gitRoot $AllowedRoots $ScopeBaselinePath
    $baseline = Read-ScopeBaseline $baselinePath

    $violations = New-Object System.Collections.Generic.List[string]
    $baselineIgnored = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $statusEntries) {
        $normalizedPath = Normalize-GitPath $entry.path
        if (Test-AllowedPath -Path $normalizedPath -AllowedRootPatterns $normalizedRoots) {
            continue
        }

        if ($baseline.ContainsKey($normalizedPath)) {
            $currentFingerprint = Get-FileFingerprint $gitRoot $normalizedPath
            $baselineEntry = $baseline[$normalizedPath]
            $sameStatus = ([string]$baselineEntry.status).Equals([string]$entry.status, [StringComparison]::Ordinal)
            $sameFingerprint = [string]::IsNullOrWhiteSpace([string]$baselineEntry.fingerprint) -or ([string]$baselineEntry.fingerprint).Equals($currentFingerprint, [StringComparison]::OrdinalIgnoreCase)
            if ($sameStatus -and $sameFingerprint) {
                $baselineIgnored.Add($normalizedPath)
                continue
            }
        }

        $violations.Add($normalizedPath)
    }

    if ($violations.Count -gt 0) {
        Write-Host "SCOPE_GUARD_FAILED: changed files outside allowed roots:"
        foreach ($violation in ($violations | Sort-Object -Unique)) {
            Write-Host "  $violation"
        }
        if ($baselineIgnored.Count -gt 0) {
            Write-Host ""
            Write-Host "Pre-existing unchanged out-of-scope paths ignored by baseline ${baselinePath}:"
            foreach ($ignored in ($baselineIgnored | Sort-Object -Unique)) {
                Write-Host "  $ignored"
            }
        }
        Write-Host ""
        Write-Host "Allowed roots:"
        foreach ($root in $AllowedRoots) {
            Write-Host "  $root"
        }
        exit 1
    }

    if ($baselineIgnored.Count -gt 0) {
        Write-Host "SCOPE_GUARD_WARNING: ignored pre-existing unchanged out-of-scope paths from baseline ${baselinePath}:"
        foreach ($ignored in ($baselineIgnored | Sort-Object -Unique)) {
            Write-Host "  $ignored"
        }
    }

    Write-Host "SCOPE_GUARD_PASSED: all new/changed files are inside allowed roots: $($AllowedRoots -join ', ')"
    exit 0
}
catch {
    Write-Error "SCOPE_GUARD_FAILED: $($_.Exception.Message)"
    exit 2
}
finally {
    Pop-Location
}
