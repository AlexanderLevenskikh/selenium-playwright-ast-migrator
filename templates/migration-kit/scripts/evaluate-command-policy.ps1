param(
    [string]$Workspace = "migration",
    [Parameter(Mandatory = $true)][string]$Command,
    [string]$OutputJson = ""
)

$ErrorActionPreference = "Stop"

# Stable output markers used by tests and by agent prompts:
# COMMAND_POLICY_SAFE
# COMMAND_POLICY_REVIEW_REQUIRED
# COMMAND_POLICY_FORBIDDEN

function Get-WorkspacePath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Read-JsonOrNull([string]$Path) {
    try { return Get-Content -Raw -Path $Path | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
}

$workspacePath = Get-WorkspacePath $Workspace
$scopeContract = Read-JsonOrNull (Join-Path $workspacePath "state/scope-contract.json")
$policy = Read-JsonOrNull (Join-Path $workspacePath "state/harness-policy.json")
$commandText = $Command.Trim()
$lower = $commandText.ToLowerInvariant()
$decision = "safe"
$reasons = @()
$matchedPatterns = @()

$forbiddenPatterns = @()
if ($null -ne $scopeContract -and $scopeContract.PSObject.Properties["forbiddenCommandPatterns"]) {
    $forbiddenPatterns += @($scopeContract.forbiddenCommandPatterns | ForEach-Object { [string]$_ })
}
$forbiddenPatterns += @("git clean -fdx", "rm -rf", "Remove-Item -Recurse -Force", "dotnet test .", "dotnet test --no-filter")

foreach ($pattern in $forbiddenPatterns) {
    if (-not [string]::IsNullOrWhiteSpace($pattern) -and $lower.Contains($pattern.ToLowerInvariant())) {
        $matchedPatterns += $pattern
    }
}
if ($matchedPatterns.Count -gt 0) {
    $decision = "forbidden"
    $reasons += "matched forbidden command pattern(s): $($matchedPatterns -join ', ')"
}

$reviewPatterns = @("dotnet add package", "npm install", "yarn add", "pnpm add", "curl ", "wget ", "Invoke-WebRequest", "git push", "git reset", "git checkout")
foreach ($pattern in $reviewPatterns) {
    if ($lower.Contains($pattern.ToLowerInvariant()) -and $decision -ne "forbidden") {
        $decision = "review-required"
        $reasons += "matched review-required command pattern: $pattern"
    }
}

$safePatterns = @("git status", "git diff", "git log", "kit doctor", "verify-project", "check-harness-policy", "check-final-gate", "check-scope", "selenium-pw-migrator run", "report serve")
$safeMatch = @($safePatterns | Where-Object { $lower.Contains($_.ToLowerInvariant()) })
if ($decision -eq "safe" -and $safeMatch.Count -eq 0) {
    $decision = "review-required"
    $reasons += "command did not match known safe standard-migration command classes"
}


$result = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    command = $Command
    decision = $decision
    reasons = @($reasons)
    matchedForbiddenPatterns = @($matchedPatterns)
}

if (-not [string]::IsNullOrWhiteSpace($OutputJson)) {
    $path = if ([System.IO.Path]::IsPathRooted($OutputJson)) { $OutputJson } else { Join-Path $workspacePath $OutputJson }
    $dir = Split-Path -Parent $path
    if (-not [string]::IsNullOrWhiteSpace($dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $result | ConvertTo-Json -Depth 20 | Set-Content -Path $path -Encoding UTF8
}

Write-Host "COMMAND_POLICY_$($decision.ToUpperInvariant().Replace('-', '_'))"
foreach ($reason in $reasons) { Write-Host "- $reason" }
if ($decision -eq "forbidden") { exit 2 }
if ($decision -eq "review-required") { exit 1 }
exit 0
