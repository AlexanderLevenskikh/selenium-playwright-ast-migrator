param(
    [string]$Workspace = "migration"
)

$ErrorActionPreference = "Stop"

function Get-WorkspacePath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

$workspacePath = Get-WorkspacePath $Workspace
$activeDir = Join-Path $workspacePath "state/claims/active"
$stateDir = Join-Path $workspacePath "state/claims"
New-Item -ItemType Directory -Force -Path $activeDir, $stateDir | Out-Null
$now = [DateTimeOffset]::UtcNow
$expired = @()
$invalid = @()

foreach ($file in @(Get-ChildItem -Path $activeDir -Filter "*.json" -File -ErrorAction SilentlyContinue)) {
    try {
        $claim = Get-Content -Raw -Path $file.FullName | ConvertFrom-Json -ErrorAction Stop
        $expires = [DateTimeOffset]::Parse([string]$claim.expiresAtUtc)
        if ($expires -lt $now) {
            $expired += [pscustomobject]@{
                path = [string]$file.FullName
                claimId = [string]$claim.claimId
                ticketId = [string]$claim.ticketId
                expiresAtUtc = $expires.ToString("o")
            }
        }
    }
    catch {
        $invalid += "{0}: {1}" -f $file.FullName, $_.Exception.Message
    }
}

$status = if ($expired.Count -eq 0 -and $invalid.Count -eq 0) { "PASS" } else { "WARN" }
$report = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = $now.ToString("o")
    status = $status
    expiredClaims = @($expired)
    invalidClaims = @($invalid)
    guidance = "Expired claims are not deleted automatically. Move them to state/claims/stale only after human or final-decision review."
}
$reportPath = Join-Path $stateDir "claim-doctor-result.json"
$report | ConvertTo-Json -Depth 20 | Set-Content -Path $reportPath -Encoding UTF8
Write-Host "CLAIM_DOCTOR_$status"
Write-Host "Report: $reportPath"
if ($status -eq "PASS") { exit 0 }
exit 1
