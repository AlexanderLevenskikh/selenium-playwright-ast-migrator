param(
    [string]$RepoRoot = ".",
    [string[]]$AllowedRoots = @("migration"),
    [switch]$AllowNoGit
)

$ErrorActionPreference = "Stop"

function Normalize-GitPath([string]$Path) {
    return ($Path -replace "\\", "/").TrimStart("./")
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

    $status = & git status --short --untracked-files=all
    if ($LASTEXITCODE -ne 0) {
        Write-Error "SCOPE_GUARD_FAILED: git status failed"
        exit 2
    }

    $normalizedRoots = $AllowedRoots | ForEach-Object {
        $candidateRoot = $_
        if ([System.IO.Path]::IsPathRooted($candidateRoot)) {
            try {
                $fullGitRoot = [System.IO.Path]::GetFullPath($gitRoot.Trim()).TrimEnd('\', '/')
                $fullCandidate = [System.IO.Path]::GetFullPath((Resolve-Path $candidateRoot).Path).TrimEnd('\', '/')
                if ($fullCandidate.StartsWith($fullGitRoot, [StringComparison]::OrdinalIgnoreCase)) {
                    $candidateRoot = $fullCandidate.Substring($fullGitRoot.Length).TrimStart('\', '/')
                }
            }
            catch {
                $candidateRoot = $_
            }
        }

        $root = Normalize-GitPath $candidateRoot
        if ($root.EndsWith("/")) { $root } else { "$root/" }
    }

    $violations = New-Object System.Collections.Generic.List[string]
    foreach ($line in $status) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $pathPart = $line.Substring([Math]::Min(3, $line.Length)).Trim()
        if ($pathPart.Contains(" -> ")) {
            $pathPart = $pathPart.Split(" -> ")[-1].Trim()
        }

        $path = Normalize-GitPath $pathPart
        $isAllowed = $false
        foreach ($root in $normalizedRoots) {
            if ($path.Equals($root.TrimEnd("/"), [StringComparison]::OrdinalIgnoreCase) -or
                $path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
                $isAllowed = $true
                break
            }
        }

        if (-not $isAllowed) {
            $violations.Add($line)
        }
    }

    if ($violations.Count -gt 0) {
        Write-Host "SCOPE_GUARD_FAILED: changed files outside allowed roots:"
        foreach ($violation in $violations) {
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
finally {
    Pop-Location
}
