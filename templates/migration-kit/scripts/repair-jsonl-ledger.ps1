<#
.SYNOPSIS
Validate or explicitly repair a controlled migration JSONL ledger.

.DESCRIPTION
The script never guesses how to reconstruct a truncated JSON object. By default it only
reports invalid lines. With -DropInvalidLines it creates a timestamped backup, removes
only malformed non-empty lines, writes the ledger atomically, and appends a repair receipt.
The target must be a .jsonl file under migration/state/** or migration/runs/**.
#>
param(
    [string]$Workspace = "migration",
    [Parameter(Mandatory = $true)]
    [string]$Path,
    [switch]$DropInvalidLines,
    [string]$Actor = "operator",
    [string]$Reason = "explicit invalid JSONL repair"
)

$ErrorActionPreference = "Stop"

function Get-Sha256([string]$Text) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
        return ([System.BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Write-TextAtomic([string]$TargetPath, [string]$Text) {
    $tempPath = "$TargetPath.tmp-$([Guid]::NewGuid().ToString('N'))"
    try {
        [System.IO.File]::WriteAllText($tempPath, $Text, [System.Text.UTF8Encoding]::new($false))
        Move-Item -Force -Path $tempPath -Destination $TargetPath
    }
    finally {
        if (Test-Path $tempPath) { Remove-Item -Force -Path $tempPath -ErrorAction SilentlyContinue }
    }
}

$workspacePath = [System.IO.Path]::GetFullPath($Workspace).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$targetPath = if ([System.IO.Path]::IsPathRooted($Path)) { [System.IO.Path]::GetFullPath($Path) } else { [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path)) }
if (-not (Test-Path $workspacePath)) { throw "Workspace not found: $workspacePath" }
if (-not (Test-Path $targetPath)) { throw "JSONL ledger not found: $targetPath" }
if (-not $targetPath.EndsWith(".jsonl", [StringComparison]::OrdinalIgnoreCase)) { throw "Target must be a .jsonl file: $targetPath" }

$stateRoot = (Join-Path $workspacePath "state") + [System.IO.Path]::DirectorySeparatorChar
$runsRoot = (Join-Path $workspacePath "runs") + [System.IO.Path]::DirectorySeparatorChar
$allowed = $targetPath.StartsWith($stateRoot, [StringComparison]::OrdinalIgnoreCase) -or $targetPath.StartsWith($runsRoot, [StringComparison]::OrdinalIgnoreCase)
if (-not $allowed) { throw "JSONL repair is limited to workspace state/** or runs/**: $targetPath" }

$lines = @([System.IO.File]::ReadAllLines($targetPath))
$validLines = New-Object System.Collections.Generic.List[string]
$invalid = New-Object System.Collections.Generic.List[object]
for ($index = 0; $index -lt $lines.Count; $index++) {
    $line = [string]$lines[$index]
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    try {
        $entry = $line | ConvertFrom-Json -ErrorAction Stop
        if ($null -eq $entry -or ($entry -isnot [System.Management.Automation.PSCustomObject] -and $entry -isnot [System.Collections.IDictionary])) {
            throw "JSONL root must be an object"
        }
        $validLines.Add($line) | Out-Null
    }
    catch {
        $invalid.Add([pscustomobject][ordered]@{
            line = $index + 1
            sha256 = Get-Sha256 $line
            error = $_.Exception.Message
        }) | Out-Null
    }
}

if ($invalid.Count -eq 0) {
    Write-Host "JSONL_LEDGER_VALID"
    Write-Host "Path: $targetPath"
    Write-Host "Records: $($validLines.Count)"
    exit 0
}

Write-Host "JSONL_LEDGER_INVALID"
Write-Host "Path: $targetPath"
foreach ($item in $invalid) { Write-Host "Invalid line $($item.line): $($item.error)" }
if (-not $DropInvalidLines) {
    Write-Host "No changes made. Re-run with -DropInvalidLines only after confirming malformed records are recoverable from other evidence."
    exit 1
}

$timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$relative = $targetPath.Substring($workspacePath.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$backupPath = Join-Path $workspacePath ("state/jsonl-repair-backups/{0}/{1}" -f $timestamp, $relative)
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backupPath) | Out-Null
Copy-Item -Force -Path $targetPath -Destination $backupPath

$replacement = if ($validLines.Count -eq 0) { "" } else { ($validLines -join [Environment]::NewLine) + [Environment]::NewLine }
Write-TextAtomic $targetPath $replacement

$receipt = [pscustomobject][ordered]@{
    schemaVersion = "jsonl-repair-receipt/v1"
    event = "INVALID_JSONL_LINES_DROPPED"
    path = $relative.Replace("\\", "/")
    backup = $backupPath.Substring($workspacePath.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Replace("\\", "/")
    actor = $Actor
    reason = $Reason
    originalLineCount = $lines.Count
    retainedRecordCount = $validLines.Count
    droppedLineCount = $invalid.Count
    dropped = $invalid.ToArray()
    repairedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}
$repairLedger = Join-Path $workspacePath "state/jsonl-repair-ledger.jsonl"
Add-Content -Path $repairLedger -Encoding UTF8 -Value ($receipt | ConvertTo-Json -Depth 20 -Compress)

Write-Host "JSONL_LEDGER_REPAIRED"
Write-Host "Path: $targetPath"
Write-Host "Backup: $backupPath"
Write-Host "Dropped invalid lines: $($invalid.Count)"
