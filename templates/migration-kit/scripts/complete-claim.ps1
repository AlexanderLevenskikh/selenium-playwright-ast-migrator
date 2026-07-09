param(
    [string]$Workspace = "migration",
    [Alias("ClaimId")]
    [string]$Claim = "",
    [string]$Outcome = "completed",
    [string[]]$Evidence = @()
)

$ErrorActionPreference = "Stop"

function Get-WorkspacePath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Set-ObjectProperty([object]$Object, [string]$Name, [object]$Value) {
    if ($Object.PSObject.Properties[$Name]) {
        $Object.$Name = $Value
    } else {
        $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
    }
}

if ([string]::IsNullOrWhiteSpace($Claim)) { throw "-Claim is required" }

$workspacePath = Get-WorkspacePath $Workspace
$claimName = if ($Claim.EndsWith(".json", [StringComparison]::OrdinalIgnoreCase)) { [System.IO.Path]::GetFileNameWithoutExtension($Claim) } else { $Claim }
$activePath = Join-Path $workspacePath "state/claims/active/$claimName.json"
$completedDir = Join-Path $workspacePath "state/claims/completed"
New-Item -ItemType Directory -Force -Path $completedDir | Out-Null
if (-not (Test-Path $activePath)) { throw "Active claim not found: $activePath" }

$claimObject = Get-Content -Raw -Path $activePath | ConvertFrom-Json -ErrorAction Stop
Set-ObjectProperty $claimObject "status" "completed"
Set-ObjectProperty $claimObject "completedAtUtc" ([DateTimeOffset]::UtcNow.ToString("o"))
Set-ObjectProperty $claimObject "outcome" $Outcome
Set-ObjectProperty $claimObject "evidence" @($Evidence)
$completedPath = Join-Path $completedDir "$claimName.json"
$claimObject | ConvertTo-Json -Depth 20 | Set-Content -Path $completedPath -Encoding UTF8
Remove-Item -LiteralPath $activePath
Write-Host "CLAIM_COMPLETED: $claimName"
Write-Host "Claim: $completedPath"
