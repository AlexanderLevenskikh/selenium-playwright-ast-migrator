param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [Parameter(Mandatory = $true)][string]$Category,
    [ValidateSet("info", "low", "medium", "high", "critical")][string]$Severity = "medium",
    [Parameter(Mandatory = $true)][string]$Summary,
    [string]$Evidence = "",
    [string]$RecommendedAction = "",
    [bool]$AgentExecutable = $true,
    [string]$Status = "open",
    [string]$DataJson = "",
    [string]$FindingJsonPath = "",
    [switch]$ReadFindingJsonFromStdin
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

function Get-ObjectProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties | Where-Object { $_.Name.Equals($Name, [StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($property -eq $null) { return $null }
    return $property.Value
}

function Read-FindingOverride([string]$Path, [bool]$FromStdin) {
    $jsonText = ""
    if ($FromStdin) {
        $jsonText = [Console]::In.ReadToEnd()
    }
    elseif (-not [string]::IsNullOrWhiteSpace($Path)) {
        if (-not (Test-Path $Path)) { throw "FindingJsonPath does not exist: $Path" }
        $jsonText = Get-Content -Raw -Path $Path
    }

    if ([string]::IsNullOrWhiteSpace($jsonText)) { return $null }
    try { return $jsonText | ConvertFrom-Json -ErrorAction Stop } catch { throw "Finding JSON must be valid JSON. Error: $($_.Exception.Message)" }
}

function Normalize-RelativePath([string]$Path) {
    return ($Path -replace "\\", "/").TrimStart("./")
}

function Get-PathEvidenceItems([string]$WorkspacePath, $Data) {
    $items = New-Object System.Collections.Generic.List[object]
    if ($null -eq $Data) { return $items }
    foreach ($name in @("path", "paths", "pathEvidence", "filesystemPaths")) {
        $value = Get-ObjectProperty $Data $name
        if ($null -eq $value) { continue }
        foreach ($item in @($value)) {
            if ($null -eq $item) { continue }
            if ($item -is [string]) {
                $pathValue = [string]$item
                if ([string]::IsNullOrWhiteSpace($pathValue)) { continue }
                $full = if ([System.IO.Path]::IsPathRooted($pathValue)) { $pathValue } else { Join-Path (Split-Path -Parent $WorkspacePath) $pathValue }
                $items.Add([ordered]@{ path = (Normalize-RelativePath $pathValue); exists = (Test-Path $full); observedAtUtc = [DateTimeOffset]::UtcNow.ToString("o") })
            }
            else {
                $pathValue = [string](Get-ObjectProperty $item "path")
                if ([string]::IsNullOrWhiteSpace($pathValue)) { continue }
                $existsValue = Get-ObjectProperty $item "exists"
                $exists = if ($null -eq $existsValue) {
                    $full = if ([System.IO.Path]::IsPathRooted($pathValue)) { $pathValue } else { Join-Path (Split-Path -Parent $WorkspacePath) $pathValue }
                    Test-Path $full
                } else { [bool]$existsValue }
                $items.Add([ordered]@{ path = (Normalize-RelativePath $pathValue); exists = $exists; observedAtUtc = [DateTimeOffset]::UtcNow.ToString("o") })
            }
        }
    }
    return $items
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)

if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = Read-LatestRunId $workspacePath
}
if ([string]::IsNullOrWhiteSpace($RunId)) {
    throw "RunId was not provided and could not be read from agent-state.md."
}

$override = Read-FindingOverride $FindingJsonPath ([bool]$ReadFindingJsonFromStdin)
if ($override -ne $null) {
    foreach ($name in @("Category", "category")) { $value = Get-ObjectProperty $override $name; if ($null -ne $value) { $Category = [string]$value; break } }
    foreach ($name in @("Severity", "severity")) { $value = Get-ObjectProperty $override $name; if ($null -ne $value) { $Severity = [string]$value; break } }
    foreach ($name in @("Summary", "summary")) { $value = Get-ObjectProperty $override $name; if ($null -ne $value) { $Summary = [string]$value; break } }
    foreach ($name in @("Evidence", "evidence")) { $value = Get-ObjectProperty $override $name; if ($null -ne $value) { $Evidence = (@($value) -join "; "); break } }
    foreach ($name in @("RecommendedAction", "recommendedAction")) { $value = Get-ObjectProperty $override $name; if ($null -ne $value) { $RecommendedAction = [string]$value; break } }
    foreach ($name in @("AgentExecutable", "agentExecutable")) { $value = Get-ObjectProperty $override $name; if ($null -ne $value) { $AgentExecutable = [bool]$value; break } }
    foreach ($name in @("Status", "status")) { $value = Get-ObjectProperty $override $name; if ($null -ne $value) { $Status = [string]$value; break } }
    foreach ($name in @("Data", "data")) { $value = Get-ObjectProperty $override $name; if ($null -ne $value) { $DataJson = ($value | ConvertTo-Json -Depth 20 -Compress); break } }
}

$data = $null
if (-not [string]::IsNullOrWhiteSpace($DataJson)) {
    try {
        $data = $DataJson | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "DataJson must be valid JSON. Error: $($_.Exception.Message)"
    }
}

$evidenceItems = @()
if (-not [string]::IsNullOrWhiteSpace($Evidence)) {
    $evidenceItems = @($Evidence -split ';' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
}

$pathEvidence = @(Get-PathEvidenceItems $workspacePath $data)
if (($Severity -match '^(?i:high|critical)$') -and $pathEvidence.Count -gt 0) {
    $existingPaths = @($pathEvidence | Where-Object { $_.exists })
    if ($existingPaths.Count -eq 0) {
        $Category = "STALE_GATE_EVIDENCE"
        $Severity = "medium"
        if ([string]::IsNullOrWhiteSpace($RecommendedAction)) {
            $RecommendedAction = "Re-run the gate and sentinel inspection, or remove stale gate evidence before routing this as a blocking filesystem defect."
        }
    }
}

$finding = [ordered]@{
    schemaVersion = 1
    findingId = "sentinel-" + ([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())
    runId = $RunId
    category = $Category
    severity = $Severity
    status = $Status
    summary = $Summary
    evidence = $evidenceItems
    recommendedAction = $RecommendedAction
    agentExecutable = [bool]$AgentExecutable
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}
if ($pathEvidence.Count -gt 0) {
    $finding.pathEvidence = @($pathEvidence)
}
if ($null -ne $data) {
    $finding.data = $data
}

$runSentinelDir = Join-Path $workspacePath "runs/$RunId/sentinel"
New-Item -ItemType Directory -Force -Path $runSentinelDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $workspacePath "state") | Out-Null

$line = $finding | ConvertTo-Json -Compress -Depth 20
Add-Content -Path (Join-Path $runSentinelDir "sentinel-findings.jsonl") -Encoding UTF8 -Value $line
Add-Content -Path (Join-Path $workspacePath "state/sentinel-ledger.jsonl") -Encoding UTF8 -Value $line

# Seed the append-only sentinel finding lifecycle ledger. Later status changes must use
# update-sentinel-finding-status instead of mutating sentinel-findings.jsonl.
$lifecycleStatus = ([string]$Status).ToUpperInvariant().Replace("-", "_")
if ($lifecycleStatus -eq "OPEN" -or $lifecycleStatus -eq "") { $lifecycleStatus = "OPEN" }
$lifecycleEvent = [pscustomobject][ordered]@{
    schemaVersion = "sentinel-finding-lifecycle/v1"
    event = "SENTINEL_FINDING_STATUS_UPDATED"
    findingId = [string]$finding.findingId
    runId = $RunId
    category = $Category
    severity = $Severity
    status = $lifecycleStatus
    previousStatus = $null
    ticketId = ""
    source = "write-sentinel-finding"
    actor = "harness-sentinel"
    summary = "Initial sentinel finding status."
    evidence = $Evidence
    result = "finding-created"
    updatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}
$lifecycleLine = $lifecycleEvent | ConvertTo-Json -Compress -Depth 20
Add-Content -Path (Join-Path $runSentinelDir "sentinel-finding-lifecycle.jsonl") -Encoding UTF8 -Value $lifecycleLine
Add-Content -Path (Join-Path $workspacePath "state/sentinel-finding-ledger.jsonl") -Encoding UTF8 -Value $lifecycleLine

$observationPath = Join-Path $workspacePath "runs/$RunId/session-observations.jsonl"
if (Test-Path (Split-Path -Parent $observationPath)) {
    Add-Content -Path $observationPath -Encoding UTF8 -Value $line
}

Write-Host "SENTINEL_FINDING_RECORDED: $RunId $Category $Severity"
