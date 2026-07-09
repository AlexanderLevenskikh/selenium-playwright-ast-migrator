param(
    [string]$Workspace = "migration",
    [Alias("ClaimId")]
    [string]$Claim = "",
    [int]$TtlMinutes = 20
)

$ErrorActionPreference = "Stop"

function Get-WorkspacePath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

if ([string]::IsNullOrWhiteSpace($Claim)) { throw "-Claim is required" }
if ($TtlMinutes -lt 1) { throw "-TtlMinutes must be positive" }

$workspacePath = Get-WorkspacePath $Workspace
$claimName = if ($Claim.EndsWith(".json", [StringComparison]::OrdinalIgnoreCase)) { [System.IO.Path]::GetFileNameWithoutExtension($Claim) } else { $Claim }
$claimPath = Join-Path $workspacePath "state/claims/active/$claimName.json"
if (-not (Test-Path $claimPath)) { throw "Active claim not found: $claimPath" }

$claimObject = Get-Content -Raw -Path $claimPath | ConvertFrom-Json -ErrorAction Stop
$now = [DateTimeOffset]::UtcNow
$claimObject.heartbeatAtUtc = $now.ToString("o")
$claimObject.expiresAtUtc = $now.AddMinutes($TtlMinutes).ToString("o")
$claimObject | ConvertTo-Json -Depth 20 | Set-Content -Path $claimPath -Encoding UTF8
Write-Host "CLAIM_HEARTBEAT_UPDATED: $claimName"
Write-Host "Claim: $claimPath"
