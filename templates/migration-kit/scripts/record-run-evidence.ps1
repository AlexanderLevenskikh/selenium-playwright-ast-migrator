param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [Parameter(Mandatory = $true)][string]$Kind,
    [string]$SourcePath = "",
    [string]$Content = "",
    [string]$TargetName = "",
    [string]$Status = "recorded",
    [string]$Reason = "",
    [switch]$Required
)

$ErrorActionPreference = "Stop"

function Get-WorkspacePath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}

function Set-Utf8NoBom([string]$Path, [string]$Value) {
    $dir = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $utf8 = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Value, $utf8)
}

function Read-LatestRunId([string]$WorkspacePath) {
    $agentState = Join-Path $WorkspacePath "agent-state.md"
    if (Test-Path $agentState) {
        $text = Get-Content -Raw -Path $agentState
        $m = [regex]::Match($text, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
        if ($m.Success) { return $m.Groups[1].Value }
    }
    return ""
}

function Normalize-FileName([string]$Value) {
    $name = if ([string]::IsNullOrWhiteSpace($Value)) { "evidence" } else { $Value.Trim() }
    $name = $name -replace '[\\/:*?"<>|]+', '-'
    $name = $name.Trim('-', ' ', '.')
    if ([string]::IsNullOrWhiteSpace($name)) { return "evidence.txt" }
    return $name
}

function Read-IndexOrNew([string]$Path, [string]$RunId) {
    if (Test-Path $Path) {
        try { return Get-Content -Raw -Path $Path | ConvertFrom-Json -ErrorAction Stop } catch { }
    }

    return [pscustomobject]@{
        schemaVersion = 2
        runId = $RunId
        generatedAtUtc = ""
        artifacts = @()
        requiredEvidence = [pscustomobject]@{}
        notes = "Evidence bundle index with sha256 validation at runs/<run-id>/evidence/index.json."
    }
}

function Set-ObjectProperty([object]$Object, [string]$Name, [object]$Value) {
    if ($Object.PSObject.Properties[$Name]) { $Object.$Name = $Value }
    else { $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value }
}

$workspacePath = Get-WorkspacePath $Workspace
if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = Read-LatestRunId $workspacePath }
if ([string]::IsNullOrWhiteSpace($RunId)) { throw "RunId was not provided and could not be read from agent-state.md." }

$runPath = Join-Path $workspacePath "runs/$RunId"
$evidenceDir = Join-Path $runPath "evidence"
New-Item -ItemType Directory -Force -Path $evidenceDir | Out-Null

$normalizedKind = Normalize-FileName $Kind
$targetFileName = if (-not [string]::IsNullOrWhiteSpace($TargetName)) { Normalize-FileName $TargetName } elseif (-not [string]::IsNullOrWhiteSpace($SourcePath)) { Normalize-FileName ([System.IO.Path]::GetFileName($SourcePath)) } elseif (-not [string]::IsNullOrWhiteSpace($Reason)) { "$normalizedKind-not-run-reason.json" } else { "$normalizedKind.txt" }
$targetPath = Join-Path $evidenceDir $targetFileName

if (-not [string]::IsNullOrWhiteSpace($SourcePath)) {
    $sourceFullPath = if ([System.IO.Path]::IsPathRooted($SourcePath)) { [System.IO.Path]::GetFullPath($SourcePath) } else { [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $SourcePath)) }
    if (-not (Test-Path $sourceFullPath)) { throw "Evidence source path not found: $sourceFullPath" }
    Copy-Item -LiteralPath $sourceFullPath -Destination $targetPath -Force
}
elseif (-not [string]::IsNullOrWhiteSpace($Content)) {
    Set-Utf8NoBom $targetPath $Content
}
elseif (-not [string]::IsNullOrWhiteSpace($Reason)) {
    $reasonPayload = [ordered]@{
        schemaVersion = 1
        runId = $RunId
        kind = $Kind
        status = "not-run"
        reason = $Reason
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    }
    Set-Utf8NoBom $targetPath ($reasonPayload | ConvertTo-Json -Depth 10)
}
else {
    throw "Provide -SourcePath, -Content, or -Reason."
}

$relativePath = "runs/$RunId/evidence/$targetFileName"
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $targetPath).Hash.ToLowerInvariant()
$length = (Get-Item -LiteralPath $targetPath).Length
$recordedAt = [DateTimeOffset]::UtcNow.ToString("o")

$indexPath = Join-Path $evidenceDir "index.json"
$index = Read-IndexOrNew $indexPath $RunId
Set-ObjectProperty $index "schemaVersion" 2
Set-ObjectProperty $index "runId" $RunId
Set-ObjectProperty $index "generatedAtUtc" $recordedAt
if (-not $index.PSObject.Properties["artifacts"] -or $null -eq $index.artifacts) { Set-ObjectProperty $index "artifacts" @() }
if (-not $index.PSObject.Properties["requiredEvidence"] -or $null -eq $index.requiredEvidence) { Set-ObjectProperty $index "requiredEvidence" ([pscustomobject]@{}) }

$newArtifact = [ordered]@{
    path = $relativePath
    kind = $Kind
    sha256 = $hash
    bytes = $length
    recordedAtUtc = $recordedAt
    required = [bool]$Required
    status = $Status
}
if (-not [string]::IsNullOrWhiteSpace($Reason)) { $newArtifact["reason"] = $Reason }
if (-not [string]::IsNullOrWhiteSpace($SourcePath)) { $newArtifact["sourcePath"] = $SourcePath }

$artifacts = @($index.artifacts | Where-Object { [string]$_.path -ne $relativePath })
$artifacts += [pscustomobject]$newArtifact
$artifacts = @($artifacts | Sort-Object { [string]$_.path })
Set-ObjectProperty $index "artifacts" @($artifacts)

if ($Required) {
    $required = $index.requiredEvidence
    if ($required.PSObject.Properties[$Kind]) { $required.$Kind = $relativePath }
    else { $required | Add-Member -NotePropertyName $Kind -NotePropertyValue $relativePath }
}

Set-Utf8NoBom $indexPath ($index | ConvertTo-Json -Depth 30)

$eventScript = Join-Path $workspacePath "scripts/write-harness-event.ps1"
if (Test-Path $eventScript) {
    $data = ([ordered]@{ artifact = $relativePath; kind = $Kind; sha256 = $hash; required = [bool]$Required } | ConvertTo-Json -Compress -Depth 10)
    & $eventScript -Workspace $workspacePath -RunId $RunId -Phase "evidence" -Action "record" -Status $Status -Detail "Recorded $Kind evidence: $relativePath" -DataJson $data -Artifacts @($relativePath) | Out-Null
}

Write-Host "EVIDENCE_RECORDED: $relativePath"
Write-Host "sha256: $hash"
