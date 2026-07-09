param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [Parameter(Mandatory = $true)][string]$Phase,
    [Parameter(Mandatory = $true)][string]$Action,
    [string]$Status = "info",
    [string]$Detail = "",
    [string]$DataJson = "",
    [string[]]$Artifacts = @()
)

$ErrorActionPreference = "Stop"

function Read-LatestRunId([string]$WorkspacePath) {
    $agentState = Join-Path $WorkspacePath "agent-state.md"
    if (-not (Test-Path $agentState)) { return "" }
    $text = Get-Content -Raw -Path $agentState
    $m = [regex]::Match($text, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
    if ($m.Success) { return $m.Groups[1].Value }
    return ""
}

function Get-Sha256Text([string]$Text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Get-LastJsonLine([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    $lines = @(Get-Content -Path $Path -ErrorAction SilentlyContinue | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($lines.Count -eq 0) { return $null }
    return $lines[$lines.Count - 1]
}

function Get-NextEventId([string]$Path) {
    $count = 0
    if (Test-Path $Path) {
        $count = @(Get-Content -Path $Path -ErrorAction SilentlyContinue | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }).Count
    }
    return "evt-{0:000000}" -f ($count + 1)
}

function Resolve-ArtifactRecord([string]$WorkspacePath, [string]$Artifact) {
    if ([string]::IsNullOrWhiteSpace($Artifact)) { return $null }
    $full = if ([System.IO.Path]::IsPathRooted($Artifact)) { [System.IO.Path]::GetFullPath($Artifact) } else { [System.IO.Path]::GetFullPath((Join-Path $WorkspacePath $Artifact)) }
    $relative = $Artifact.Replace("\\", "/")
    if ([System.IO.Path]::IsPathRooted($Artifact)) {
        try {
            $workspaceUri = New-Object System.Uri(($WorkspacePath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar))
            $fileUri = New-Object System.Uri($full)
            $relative = [System.Uri]::UnescapeDataString($workspaceUri.MakeRelativeUri($fileUri).ToString()).Replace("\\", "/")
        } catch { $relative = $Artifact.Replace("\\", "/") }
    }
    $hash = if (Test-Path $full) { (Get-FileHash -Algorithm SHA256 -LiteralPath $full).Hash.ToLowerInvariant() } else { "" }
    return [ordered]@{ path = $relative; sha256 = $hash; exists = [bool](Test-Path $full) }
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)

if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = Read-LatestRunId $workspacePath }
if ([string]::IsNullOrWhiteSpace($RunId)) { throw "RunId was not provided and could not be read from agent-state.md." }

$data = $null
if (-not [string]::IsNullOrWhiteSpace($DataJson)) {
    try { $data = $DataJson | ConvertFrom-Json -ErrorAction Stop }
    catch {
        Write-Warning "DataJson was not valid JSON; storing it as rawDataJson metadata. Received: $DataJson"
        $data = [ordered]@{ rawDataJson = $DataJson; dataJsonParseStatus = "invalid-json-preserved" }
    }
}

$runEventsPath = Join-Path $workspacePath "runs/$RunId/events.jsonl"
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $runEventsPath) | Out-Null
$lastLine = Get-LastJsonLine $runEventsPath
$prevHash = $null
if ($null -ne $lastLine) {
    try {
        $last = $lastLine | ConvertFrom-Json -ErrorAction Stop
        if ($last.PSObject.Properties["eventHash"]) { $prevHash = [string]$last.eventHash }
    } catch { $prevHash = Get-Sha256Text $lastLine }
}

$artifactRecords = @($Artifacts | ForEach-Object { Resolve-ArtifactRecord $workspacePath $_ } | Where-Object { $null -ne $_ })
$createdAt = [DateTimeOffset]::UtcNow.ToString("o")
$eventPayload = [ordered]@{
    schemaVersion = 1
    eventId = Get-NextEventId $runEventsPath
    runId = $RunId
    createdAtUtc = $createdAt
    kind = "$Phase.$Action"
    phase = $Phase
    action = $Action
    status = $Status
    summary = $Detail
    artifacts = @($artifactRecords)
    data = $data
    prevEventHash = $prevHash
}
$payloadJson = $eventPayload | ConvertTo-Json -Compress -Depth 30
$eventPayload["eventHash"] = Get-Sha256Text $payloadJson
$hashChainedLine = $eventPayload | ConvertTo-Json -Compress -Depth 30

New-Item -ItemType Directory -Force -Path (Join-Path $workspacePath "state") | Out-Null
Add-Content -Path (Join-Path $workspacePath "state/harness-events.jsonl") -Encoding UTF8 -Value $hashChainedLine
Add-Content -Path $runEventsPath -Encoding UTF8 -Value $hashChainedLine

$tracePath = Join-Path $workspacePath "runs/$RunId/trace.jsonl"
if (Test-Path (Split-Path -Parent $tracePath)) { Add-Content -Path $tracePath -Encoding UTF8 -Value $hashChainedLine }

Write-Host "HARNESS_EVENT_RECORDED: $RunId $Phase/$Action $Status"
Write-Host "EventHash: $($eventPayload.eventHash)"
