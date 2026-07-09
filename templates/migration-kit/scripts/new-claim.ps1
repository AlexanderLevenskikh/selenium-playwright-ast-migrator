param(
    [string]$Workspace = "migration",
    [Alias("TicketId")]
    [string]$Ticket = "",
    [Alias("AgentId")]
    [string]$Agent = "agent-01",
    [string]$RunId = "",
    [int]$TtlMinutes = 20,
    [string[]]$ClaimedFiles = @(),
    [string[]]$ClaimedSymbols = @(),
    [switch]$AllowExpiredDuplicate
)

$ErrorActionPreference = "Stop"

function Get-WorkspacePath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Normalize-ClaimIdPart([string]$Value) {
    $v = if ([string]::IsNullOrWhiteSpace($Value)) { "unknown" } else { $Value.Trim() }
    $v = $v -replace '[^0-9A-Za-z._-]+', '-'
    $v = $v.Trim('-')
    if ([string]::IsNullOrWhiteSpace($v)) { return "unknown" }
    return $v
}

function Find-LatestRunId([string]$WorkspacePath) {
    $agentState = Join-Path $WorkspacePath "agent-state.md"
    if (Test-Path $agentState) {
        $text = Get-Content -Raw -Path $agentState
        $m = [regex]::Match($text, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    return "run-001"
}

function Read-JsonOrNull([string]$Path) {
    try { return Get-Content -Raw -Path $Path | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
}

function Normalize-PathValue([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }
    $p = $Path.Replace("\", "/")
    while ($p.StartsWith("./", [StringComparison]::Ordinal)) { $p = $p.Substring(2) }
    return $p.TrimEnd("/")
}

if ([string]::IsNullOrWhiteSpace($Ticket)) { throw "-Ticket is required" }
if ($TtlMinutes -lt 1) { throw "-TtlMinutes must be positive" }

$workspacePath = Get-WorkspacePath $Workspace
$activeDir = Join-Path $workspacePath "state/claims/active"
$completedDir = Join-Path $workspacePath "state/claims/completed"
$staleDir = Join-Path $workspacePath "state/claims/stale"
New-Item -ItemType Directory -Force -Path $activeDir, $completedDir, $staleDir | Out-Null

if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = Find-LatestRunId $workspacePath }
$now = [DateTimeOffset]::UtcNow
$claimedFilesNormalized = @($ClaimedFiles | ForEach-Object { Normalize-PathValue $_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
$claimedSymbolsNormalized = @($ClaimedSymbols | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)

foreach ($file in @(Get-ChildItem -Path $activeDir -Filter "*.json" -File -ErrorAction SilentlyContinue)) {
    $existing = Read-JsonOrNull $file.FullName
    if ($null -eq $existing) { continue }
    $sameTicket = ([string]$existing.ticketId -eq $Ticket)
    $expires = $null
    try { $expires = [DateTimeOffset]::Parse([string]$existing.expiresAtUtc) } catch { $expires = $null }
    $expired = $expires -ne $null -and $expires -lt $now
    if ($sameTicket -and (-not $expired -or -not $AllowExpiredDuplicate)) {
        throw "Active claim already exists for ticket '$Ticket': $($file.FullName)"
    }

    $existingFiles = @($existing.claimedFiles | ForEach-Object { Normalize-PathValue ([string]$_) } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    $overlap = @($claimedFilesNormalized | Where-Object { $existingFiles -contains $_ })
    if ($overlap.Count -gt 0 -and (-not $expired -or -not $AllowExpiredDuplicate)) {
        throw "Active claim file conflict with $($file.Name): $($overlap -join ', ')"
    }
}

$claimId = "claim-$(Normalize-ClaimIdPart $Ticket)-$(Normalize-ClaimIdPart $Agent)"
$claimPath = Join-Path $activeDir "$claimId.json"
if (Test-Path $claimPath) { throw "Claim file already exists: $claimPath" }

$claim = [ordered]@{
    schemaVersion = 1
    claimId = $claimId
    runId = $RunId
    ticketId = $Ticket
    agentId = $Agent
    status = "active"
    createdAtUtc = $now.ToString("o")
    heartbeatAtUtc = $now.ToString("o")
    expiresAtUtc = $now.AddMinutes($TtlMinutes).ToString("o")
    scopeContractPath = "state/scope-contract.json"
    claimedFiles = @($claimedFilesNormalized)
    claimedSymbols = @($claimedSymbolsNormalized)
    notes = "Wave migration batch claim. Symbol locks are declarative placeholders until Roslyn extraction is wired."
}

$claim | ConvertTo-Json -Depth 20 | Set-Content -Path $claimPath -Encoding UTF8
Write-Host "CLAIM_CREATED: $claimId"
Write-Host "Claim: $claimPath"
