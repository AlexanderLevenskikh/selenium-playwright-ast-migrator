[CmdletBinding()]
param(
    [string]$Workspace = "migration",
    [string]$RepoRoot = ".",
    [string[]]$AllowedRoots = @(),
    [switch]$SkipGitStatus
)
$ErrorActionPreference = "Stop"
$workspaceFull = [IO.Path]::GetFullPath($Workspace).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$repoFull = [IO.Path]::GetFullPath($RepoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
$repoPrefix = $repoFull + [IO.Path]::DirectorySeparatorChar
if (-not ($workspaceFull.Equals($repoFull, [StringComparison]::OrdinalIgnoreCase) -or
          $workspaceFull.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase))) {
    throw "WORKSPACE_SCOPE_VIOLATION: migration workspace must be inside the repository root"
}
$scopePath = Join-Path $workspaceFull "state/source-scope.json"
if (-not (Test-Path -LiteralPath $scopePath)) { throw "SOURCE_SCOPE_MISSING: $scopePath" }
Write-Host "STANDARD_RUN_POLICY_PASS"
