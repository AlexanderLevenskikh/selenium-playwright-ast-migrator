param(
    [string]$Workspace = "migration",
    [Parameter(Mandatory = $true)][ValidateSet("decisions.jsonl", "warnings.jsonl", "antipatterns.jsonl", "final-gate-lessons.jsonl", "user-notes.jsonl")][string]$FileName,
    [string]$Reason = "repair invalid memory JSONL",
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"

function ConvertTo-CanonicalMemoryEntry([string]$Line, [string]$FileName, [int]$LineNumber) {
    $candidates = New-Object System.Collections.Generic.List[string]
    $candidates.Add($Line)

    # Common agent mistake: JSON object was written as an escaped shell/string payload.
    $unescaped = $Line.Trim()
    if ($unescaped.StartsWith('"') -and $unescaped.EndsWith('"')) {
        try {
            $inner = $unescaped | ConvertFrom-Json -ErrorAction Stop
            if ($inner -is [string]) { $candidates.Add($inner) }
        } catch { }
    }
    $candidates.Add(($Line -replace '\\:', ':' -replace '\\"', '"'))

    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
        try {
            $entry = $candidate | ConvertFrom-Json -ErrorAction Stop
            if ($entry -is [string]) {
                $entry = $entry | ConvertFrom-Json -ErrorAction Stop
            }
            foreach ($required in @("kind", "text", "source", "status")) {
                if (-not ($entry.PSObject.Properties.Name -contains $required)) {
                    throw "missing $required"
                }
            }
            return ($entry | ConvertTo-Json -Compress -Depth 20)
        } catch {
            # Try next candidate.
        }
    }

    throw "${FileName}:${LineNumber} cannot be repaired automatically. Preserve the original line and ask for a targeted repair."
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)
$memoryDir = Join-Path $workspacePath "state/memory"
$path = Join-Path $memoryDir $FileName
if (-not (Test-Path $path)) {
    throw "Memory file not found: $path"
}

$timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffffffZ")
$backupDir = Join-Path $memoryDir ".repair-backups/$timestamp"
$lines = Get-Content -Path $path
$fixed = New-Object System.Collections.Generic.List[string]
$lineNumber = 0
foreach ($line in $lines) {
    $lineNumber++
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $fixed.Add((ConvertTo-CanonicalMemoryEntry $line $FileName $lineNumber))
}

if ($WhatIf) {
    Write-Host "MEMORY_JSONL_REPAIR_DRY_RUN: $FileName lines=$($fixed.Count) reason=$Reason"
    exit 0
}

New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
Copy-Item -Path $path -Destination (Join-Path $backupDir $FileName) -Force
Set-Content -Path $path -Encoding UTF8 -Value $fixed
Write-Host "MEMORY_JSONL_REPAIRED: $FileName backup=$backupDir reason=$Reason"
