param(
    [string]$Workspace = "migration",
    [string]$Claim = "",
    [string]$Reason = "expired claim reviewed and moved stale",
    [switch]$ExpiredOnly,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

function Get-WorkspacePath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Set-ObjectProperty([object]$Object, [string]$Name, [object]$Value) {
    if ($Object.PSObject.Properties[$Name]) { $Object.$Name = $Value }
    else { $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value }
}

$workspacePath = Get-WorkspacePath $Workspace
$activeDir = Join-Path $workspacePath "state/claims/active"
$staleDir = Join-Path $workspacePath "state/claims/stale"
$ledgerPath = Join-Path $workspacePath "state/claims/stale-ledger.jsonl"
New-Item -ItemType Directory -Force -Path $activeDir, $staleDir | Out-Null

$now = [DateTimeOffset]::UtcNow
$moved = @()
$skipped = @()

$files = if ([string]::IsNullOrWhiteSpace($Claim)) {
    @(Get-ChildItem -Path $activeDir -Filter "*.json" -File -ErrorAction SilentlyContinue)
} else {
    $claimName = if ($Claim.EndsWith(".json", [StringComparison]::OrdinalIgnoreCase)) { [System.IO.Path]::GetFileNameWithoutExtension($Claim) } else { $Claim }
    @(Get-Item -LiteralPath (Join-Path $activeDir "$claimName.json") -ErrorAction Stop)
}

foreach ($file in $files) {
    $claimObject = Get-Content -Raw -Path $file.FullName | ConvertFrom-Json -ErrorAction Stop
    $expires = $null
    try { $expires = [DateTimeOffset]::Parse([string]$claimObject.expiresAtUtc) } catch { $expires = $null }
    $expired = $expires -ne $null -and $expires -lt $now
    if ($ExpiredOnly -and -not $expired) {
        $skipped += [pscustomobject]@{ claimId = [string]$claimObject.claimId; reason = "not expired" }
        continue
    }

    Set-ObjectProperty $claimObject "status" "stale"
    Set-ObjectProperty $claimObject "staleAtUtc" ($now.ToString("o"))
    Set-ObjectProperty $claimObject "staleReason" $Reason
    Set-ObjectProperty $claimObject "expiredWhenMoved" ([bool]$expired)

    $destination = Join-Path $staleDir $file.Name
    if (-not $WhatIf) {
        $claimObject | ConvertTo-Json -Depth 20 | Set-Content -Path $destination -Encoding UTF8
        Remove-Item -LiteralPath $file.FullName
        $ledger = [ordered]@{
            schemaVersion = 1
            movedAtUtc = $now.ToString("o")
            claimId = [string]$claimObject.claimId
            ticketId = [string]$claimObject.ticketId
            runId = [string]$claimObject.runId
            expiredWhenMoved = [bool]$expired
            reason = $Reason
            destination = "state/claims/stale/$($file.Name)"
        }
        Add-Content -Path $ledgerPath -Encoding UTF8 -Value ($ledger | ConvertTo-Json -Compress -Depth 10)
    }

    $moved += [pscustomobject]@{ claimId = [string]$claimObject.claimId; destination = $destination; expired = [bool]$expired }
}

$status = if ($moved.Count -gt 0) { "MOVED" } else { "NOOP" }
Write-Host "STALE_CLAIMS_${status}: moved=$($moved.Count) skipped=$($skipped.Count)"
foreach ($entry in $moved) { Write-Host "  $($entry.claimId) -> $($entry.destination)" }
if ($WhatIf) { Write-Host "WhatIf: no files were changed." }
