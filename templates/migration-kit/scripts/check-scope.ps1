param(
    [string]$RepoRoot = ".",
    [string[]]$AllowedRoots = @("migration"),
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
    $normalizedRoots = $AllowedRoots | ForEach-Object {
        Convert-ToGitRelativePath -GitRoot $gitRoot -Path $_
    }

    $status = Read-GitStatusPorcelainZ
    $changedPaths = Get-ChangedPathsFromPorcelainZ $status

    $violations = New-Object System.Collections.Generic.List[string]
    foreach ($path in $changedPaths) {
        if (-not (Test-AllowedPath -Path $path -AllowedRootPatterns $normalizedRoots)) {
            $violations.Add((Normalize-GitPath $path))
        }
    }

    if ($violations.Count -gt 0) {
        Write-Host "SCOPE_GUARD_FAILED: changed files outside allowed roots:"
        foreach ($violation in ($violations | Sort-Object -Unique)) {
            Write-Host "  $violation"
        }
        Write-Host ""
        Write-Host "Allowed roots:"
        foreach ($root in $AllowedRoots) {
            Write-Host "  $root"
        }
        exit 1
    }

    Write-Host "SCOPE_GUARD_PASSED: all changed files are inside allowed roots: $($AllowedRoots -join ', ')"
    exit 0
}
catch {
    Write-Error "SCOPE_GUARD_FAILED: $($_.Exception.Message)"
    exit 2
}
finally {
    Pop-Location
}
