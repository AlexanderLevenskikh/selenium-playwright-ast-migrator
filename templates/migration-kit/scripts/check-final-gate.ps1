[CmdletBinding()]
param(
    [string]$Workspace = "migration",
    [Alias("Run")]
    [string]$RunPath = "",
    [string]$RepoRoot = ".",
    [switch]$AllowMissingVerification
)

$ErrorActionPreference = "Stop"
$workspaceFull = [IO.Path]::GetFullPath($Workspace)
if ([string]::IsNullOrWhiteSpace($RunPath)) {
    $runsRoot = Join-Path $workspaceFull "runs"
    $latest = Get-ChildItem -Path $runsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -eq $latest) { throw "STANDARD_RUN_MISSING: no run directory exists under $runsRoot" }
    $runFull = $latest.FullName
} else {
    $runFull = [IO.Path]::GetFullPath($RunPath)
}

$failures = [System.Collections.Generic.List[string]]::new()
$reportPath = Join-Path $runFull "orchestration-report.json"
$generatedReportPath = Join-Path $runFull "generated/report.json"
if (-not (Test-Path -LiteralPath $reportPath)) { $failures.Add("orchestration-report.json is missing") }
if (-not (Test-Path -LiteralPath $generatedReportPath)) { $failures.Add("generated/report.json is missing") }

$verificationPath = Join-Path $runFull "verify-project/project-verify-report.json"
$verificationStatus = "NOT_RUN"
if (Test-Path -LiteralPath $verificationPath) {
    try {
        $verify = Get-Content -LiteralPath $verificationPath -Raw | ConvertFrom-Json
        $verificationStatus = [string]$verify.Status
        if ([string]::IsNullOrWhiteSpace($verificationStatus) -and $null -ne $verify.status) { $verificationStatus = [string]$verify.status }
        if ($verificationStatus -notmatch '^(PASS|PASSED)$') { $failures.Add("verify-project status is $verificationStatus") }
    } catch {
        $failures.Add("verify-project report is invalid JSON")
    }
} elseif (-not $AllowMissingVerification) {
    $failures.Add("verify-project artifacts are required but missing")
}

$status = if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }
$stateDir = Join-Path $workspaceFull "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$result = [ordered]@{
    schemaVersion = "standard-run-final-gate/v1"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    status = $status
    runPath = $runFull
    verificationStatus = $verificationStatus
    failures = @($failures)
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stateDir "final-gate-result.json") -Encoding utf8

$md = @("# Standard run final gate", "", "- Status: ``$status``", "- Run: ``$runFull``", "- Verification: ``$verificationStatus``")
if ($failures.Count -gt 0) { $md += ""; $md += "## Failures"; $md += @($failures | ForEach-Object { "- $_" }) }
$md -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $stateDir "final-gate.md") -Encoding utf8

if ($status -eq "PASS") { Write-Host "STANDARD_RUN_FINAL_GATE_PASS"; exit 0 }
Write-Error ("STANDARD_RUN_FINAL_GATE_FAIL: " + ($failures -join "; "))
exit 2
