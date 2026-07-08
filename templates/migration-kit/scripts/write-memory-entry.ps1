param(
    [string]$Workspace = "migration",
    [Parameter(Mandatory = $true)][string]$Kind,
    [Parameter(Mandatory = $true)][string]$Text,
    [string]$Source = "agent",
    [string]$Status = "active",
    [string]$FileName = "",
    [string]$DataJson = ""
)

$ErrorActionPreference = "Stop"

function Resolve-MemoryFileName([string]$KindValue, [string]$ExplicitFileName) {
    if (-not [string]::IsNullOrWhiteSpace($ExplicitFileName)) {
        $allowed = @("decisions.jsonl", "warnings.jsonl", "antipatterns.jsonl", "final-gate-lessons.jsonl", "user-notes.jsonl")
        if ($allowed -notcontains $ExplicitFileName) {
            throw "Unsupported memory JSONL file '$ExplicitFileName'. Allowed: $($allowed -join ', ')"
        }
        return $ExplicitFileName
    }

    switch ($KindValue.ToLowerInvariant()) {
        { $_ -in @("decision", "preference", "constraint") } { return "decisions.jsonl" }
        "warning" { return "warnings.jsonl" }
        "antipattern" { return "antipatterns.jsonl" }
        "final-gate-lesson" { return "final-gate-lessons.jsonl" }
        default { return "user-notes.jsonl" }
    }
}

$workspacePath = if ([System.IO.Path]::IsPathRooted($Workspace)) { $Workspace } else { Join-Path (Get-Location) $Workspace }
$workspacePath = [System.IO.Path]::GetFullPath($workspacePath)
$memoryDir = Join-Path $workspacePath "state/memory"
New-Item -ItemType Directory -Force -Path $memoryDir | Out-Null

$data = $null
if (-not [string]::IsNullOrWhiteSpace($DataJson)) {
    try {
        $data = $DataJson | ConvertFrom-Json -ErrorAction Stop
    } catch {
        throw "DataJson must be valid JSON. Use a plain Text value when structured metadata is not needed. Error: $($_.Exception.Message)"
    }
}

$entry = [ordered]@{
    kind = $Kind
    text = $Text
    source = $Source
    status = $Status
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
}
if ($null -ne $data) {
    $entry.data = $data
}

$file = Resolve-MemoryFileName $Kind $FileName
$path = Join-Path $memoryDir $file
$line = $entry | ConvertTo-Json -Compress -Depth 20
Add-Content -Path $path -Encoding UTF8 -Value $line
Write-Host "MEMORY_ENTRY_APPENDED: $file $Kind"
