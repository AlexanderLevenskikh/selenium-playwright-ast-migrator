<#
.SYNOPSIS
Removes a previous Migrator-managed OpenCode worktree and bootstraps a fresh isolated migration workspace.

.DESCRIPTION
The primary checkout is never deleted or reset. Its migration/profiles and
migration/state/memory remain available as long-lived knowledge; the managed
worktree bootstrap copies only those long-lived areas into a fresh worktree.
Old runs, autonomy state, handoff and verification evidence are intentionally
not carried into the new managed checkout.

.EXAMPLE
.\scripts\cleanup-and-bootstrap-opencode-kit.ps1 -ProjectRoot "C:\work\selenium_tests" -Source ".\SeleniumTests"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$Source,

    [string]$ProjectRoot = "",

    [ValidateNotNullOrEmpty()]
    [string]$Workspace = "migration",

    [ValidateNotNullOrEmpty()]
    [string]$WorktreeBranch = "migrator/selenium-playwright",

    [string]$WorktreePath = "",

    [ValidateNotNullOrEmpty()]
    [string]$WorktreeBase = "HEAD"
)

$ErrorActionPreference = "Stop"

function Require-Command([string]$Name) {
    $command = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $command) {
        throw "Required command was not found in PATH: $Name"
    }
    return $command
}

function Invoke-GitCapture([string]$RepositoryRoot, [string[]]$Arguments, [switch]$AllowFailure) {
    $output = @(& git -C $RepositoryRoot @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $text = ($output | Out-String).TrimEnd()

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $exitCode.`n$text"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Text = $text
    }
}

function Normalize-PathForCompare([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return [System.IO.Path]::GetFullPath($Value).TrimEnd([char[]]@('\', '/'))
}

function Test-PathsEqual([string]$Left, [string]$Right) {
    return [string]::Equals(
        (Normalize-PathForCompare $Left),
        (Normalize-PathForCompare $Right),
        [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-RegisteredWorktrees([string]$RepositoryRoot) {
    $result = Invoke-GitCapture $RepositoryRoot @("worktree", "list", "--porcelain")
    $entries = @()
    $path = $null
    $branch = $null

    foreach ($line in ($result.Text -split "`r?`n")) {
        if ($line.StartsWith("worktree ", [System.StringComparison]::Ordinal)) {
            if (-not [string]::IsNullOrWhiteSpace($path)) {
                $entries += [pscustomobject]@{ Path = $path; Branch = $branch }
            }
            $path = $line.Substring("worktree ".Length).Trim()
            $branch = $null
            continue
        }

        if ($line.StartsWith("branch ", [System.StringComparison]::Ordinal)) {
            $branch = $line.Substring("branch ".Length).Trim()
            continue
        }

        if ([string]::IsNullOrWhiteSpace($line) -and -not [string]::IsNullOrWhiteSpace($path)) {
            $entries += [pscustomobject]@{ Path = $path; Branch = $branch }
            $path = $null
            $branch = $null
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($path)) {
        $entries += [pscustomobject]@{ Path = $path; Branch = $branch }
    }

    return @($entries)
}

function Test-IsManagedWorktree([string]$CandidatePath, [string]$ExpectedPath, [string]$WorkspaceRelativePath) {
    if (Test-PathsEqual $CandidatePath $ExpectedPath) {
        return $true
    }

    $descriptor = Join-Path (Join-Path $CandidatePath $WorkspaceRelativePath) ".migration-kit/agent-launch.json"
    if (-not (Test-Path -LiteralPath $descriptor)) {
        return $false
    }

    try {
        $json = Get-Content -LiteralPath $descriptor -Raw | ConvertFrom-Json
        if ($json.isolation -ne "managed-worktree") {
            return $false
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$json.workingDirectory)) {
            return (Test-PathsEqual ([string]$json.workingDirectory) $CandidatePath)
        }
        return $true
    }
    catch {
        return $false
    }
}

function Clear-ReadOnlyAttributes([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory)) { return }

    Get-ChildItem -LiteralPath $Directory -Force -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            if (-not $_.PSIsContainer) {
                $_.IsReadOnly = $false
            }
        }
        catch { }
    }
}

function Remove-DirectoryWithRetry([string]$Directory) {
    if (-not (Test-Path -LiteralPath $Directory)) { return }

    $lastError = $null
    for ($attempt = 1; $attempt -le 5; $attempt++) {
        try {
            Clear-ReadOnlyAttributes $Directory
            Remove-Item -LiteralPath $Directory -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            $lastError = $_
            Start-Sleep -Milliseconds (200 * $attempt)
        }
    }

    throw "Could not remove managed worktree directory '$Directory'. Close editors/terminals using that directory and retry. Last error: $($lastError.Exception.Message)"
}

Require-Command "git" | Out-Null
Require-Command "selenium-pw-migrator" | Out-Null

$initialLocation = (Get-Location).Path
try {
    $rootProbe = if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $initialLocation
    }
    elseif ([System.IO.Path]::IsPathRooted($ProjectRoot)) {
        [System.IO.Path]::GetFullPath($ProjectRoot)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $initialLocation $ProjectRoot))
    }

    if (-not (Test-Path -LiteralPath $rootProbe)) {
        throw "ProjectRoot was not found: $rootProbe"
    }

    $rootResult = @(& git -C $rootProbe rev-parse --show-toplevel 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "ProjectRoot must point inside the Git repository that contains the Selenium project. Probe: $rootProbe"
    }

    $projectRoot = [System.IO.Path]::GetFullPath((($rootResult | Select-Object -First 1).ToString().Trim()))
    Set-Location $projectRoot

    if ([System.IO.Path]::IsPathRooted($Workspace)) {
        throw "Workspace must be repository-relative for managed worktree isolation. Got: $Workspace"
    }

    $resolvedSource = if ([System.IO.Path]::IsPathRooted($Source)) {
        [System.IO.Path]::GetFullPath($Source)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $projectRoot $Source))
    }
    if (-not (Test-Path -LiteralPath $resolvedSource)) {
        throw "Source path was not found: $resolvedSource"
    }

    $rootPrefix = $projectRoot.TrimEnd([char[]]@('\', '/')) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $resolvedSource.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase) -and -not (Test-PathsEqual $resolvedSource $projectRoot)) {
        throw "Source must be inside the Git repository so it can be mapped into the managed worktree. Source: $resolvedSource"
    }

    $repoName = Split-Path -Leaf $projectRoot
    foreach ($invalid in [System.IO.Path]::GetInvalidFileNameChars()) {
        $repoName = $repoName.Replace([string]$invalid, "_")
    }
    $repoName = ($repoName -replace '\s', '_')
    if ([string]::IsNullOrWhiteSpace($repoName)) { $repoName = "repository" }

    $resolvedWorktreePath = if ([string]::IsNullOrWhiteSpace($WorktreePath)) {
        Join-Path (Join-Path (Join-Path $HOME ".selenium-pw-migrator") "worktrees") (Join-Path $repoName "migration")
    }
    elseif ([System.IO.Path]::IsPathRooted($WorktreePath)) {
        [System.IO.Path]::GetFullPath($WorktreePath)
    }
    else {
        [System.IO.Path]::GetFullPath((Join-Path $projectRoot $WorktreePath))
    }

    if (Test-PathsEqual $resolvedWorktreePath $projectRoot) {
        throw "Refusing to use the primary checkout as the managed worktree."
    }

    Write-Host "== Current project =="
    Write-Host "Repository:      $projectRoot"
    Write-Host "Source:          $resolvedSource"
    Write-Host "Workspace:       $Workspace"
    Write-Host "Managed branch:  $WorktreeBranch"
    Write-Host "Managed worktree:$resolvedWorktreePath"

    Write-Host ""
    Write-Host "== Cleanup previous managed worktree =="

    Invoke-GitCapture $projectRoot @("worktree", "prune", "--expire", "now") | Out-Null
    $worktrees = @(Get-RegisteredWorktrees $projectRoot)
    $branchRef = "refs/heads/$WorktreeBranch"

    $candidates = @($worktrees | Where-Object {
        (Test-PathsEqual $_.Path $resolvedWorktreePath) -or
        [string]::Equals($_.Branch, $branchRef, [System.StringComparison]::Ordinal)
    })

    foreach ($candidate in $candidates) {
        if (Test-PathsEqual $candidate.Path $projectRoot) {
            throw "Refusing to remove the primary checkout. Branch '$WorktreeBranch' is currently checked out there."
        }

        if (-not (Test-IsManagedWorktree $candidate.Path $resolvedWorktreePath $Workspace)) {
            throw "Branch '$WorktreeBranch' is checked out in an unmanaged worktree: $($candidate.Path). Refusing to delete it automatically."
        }

        Write-Host "Removing registered managed worktree: $($candidate.Path)"
        $removeResult = Invoke-GitCapture $projectRoot @("worktree", "remove", "--force", $candidate.Path) -AllowFailure
        if ($removeResult.ExitCode -ne 0) {
            Write-Warning "git worktree remove reported a failure; filesystem cleanup will be attempted. $($removeResult.Text)"
        }

        if (Test-Path -LiteralPath $candidate.Path) {
            Remove-DirectoryWithRetry $candidate.Path
        }
    }

    Invoke-GitCapture $projectRoot @("worktree", "prune", "--expire", "now") | Out-Null

    if (Test-Path -LiteralPath $resolvedWorktreePath) {
        if (-not (Test-IsManagedWorktree $resolvedWorktreePath $resolvedWorktreePath $Workspace)) {
            throw "Expected managed worktree path already exists but is not safe to delete: $resolvedWorktreePath"
        }
        Write-Host "Removing stale managed worktree directory: $resolvedWorktreePath"
        Remove-DirectoryWithRetry $resolvedWorktreePath
    }

    $branchExists = Invoke-GitCapture $projectRoot @("show-ref", "--verify", "--quiet", $branchRef) -AllowFailure
    if ($branchExists.ExitCode -eq 0) {
        $oldTip = (Invoke-GitCapture $projectRoot @("rev-parse", $WorktreeBranch)).Text.Trim()
        $baseCommit = (Invoke-GitCapture $projectRoot @("rev-parse", $WorktreeBase)).Text.Trim()
        $uniqueResult = Invoke-GitCapture $projectRoot @("rev-list", "--count", "$baseCommit..$oldTip")
        $uniqueCount = 0
        [void][int]::TryParse($uniqueResult.Text.Trim(), [ref]$uniqueCount)

        if ($uniqueCount -gt 0) {
            $safeBranchName = ($WorktreeBranch -replace '[^A-Za-z0-9._-]', '-')
            $archiveBranch = "migrator/archive/$safeBranchName-" + [DateTime]::UtcNow.ToString("yyyyMMdd-HHmmss")
            Write-Host "Preserving $uniqueCount managed-branch commit(s) as: $archiveBranch"
            Invoke-GitCapture $projectRoot @("branch", $archiveBranch, $oldTip) | Out-Null
        }

        Write-Host "Removing previous managed branch: $WorktreeBranch"
        Invoke-GitCapture $projectRoot @("branch", "-D", $WorktreeBranch) | Out-Null
    }

    Write-Host ""
    Write-Host "Primary checkout and its '$Workspace' knowledge were preserved."
    Write-Host "Only the previous managed worktree/branch were reset."

    Write-Host ""
    Write-Host "== Bootstrap fresh OpenCode managed worktree =="

    $bootstrapArgs = @(
        "kit", "bootstrap-opencode",
        "--workspace", $Workspace,
        "--source", $resolvedSource,
        "--project-desktop",
        "--worktree",
        "--worktree-path", $resolvedWorktreePath,
        "--worktree-branch", $WorktreeBranch,
        "--worktree-base", $WorktreeBase
    )

    & selenium-pw-migrator @bootstrapArgs
    if ($LASTEXITCODE -ne 0) {
        throw "OpenCode managed worktree bootstrap failed with exit code $LASTEXITCODE"
    }

    $workspacePath = Join-Path $resolvedWorktreePath $Workspace
    $descriptorPath = Join-Path $workspacePath ".migration-kit/agent-launch.json"
    if (-not (Test-Path -LiteralPath $descriptorPath)) {
        throw "Managed agent launch descriptor was not created: $descriptorPath"
    }

    $descriptor = Get-Content -LiteralPath $descriptorPath -Raw | ConvertFrom-Json
    if ($descriptor.isolation -ne "managed-worktree") {
        throw "Unexpected agent isolation in descriptor: $($descriptor.isolation)"
    }
    if (-not (Test-PathsEqual ([string]$descriptor.workingDirectory) $resolvedWorktreePath)) {
        throw "agent-launch.json points at a different working directory: $($descriptor.workingDirectory)"
    }

    Write-Host ""
    Write-Host "== Verify fresh kit =="
    Push-Location $resolvedWorktreePath
    try {
        & selenium-pw-migrator kit doctor --workspace $Workspace
        if ($LASTEXITCODE -ne 0) {
            throw "kit doctor failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    Write-Host ""
    Write-Host "CLEANUP_AND_BOOTSTRAP_OPENCODE_KIT_PASS"
    Write-Host "Worktree:  $resolvedWorktreePath"
    Write-Host "Workspace: $workspacePath"
    Write-Host "Branch:    $WorktreeBranch"
    Write-Host ""
    Write-Host "Open this directory in OpenCode Desktop:"
    Write-Host "  $resolvedWorktreePath"
    Write-Host "Then run: /supervised-task"
}
finally {
    Set-Location $initialLocation
}
