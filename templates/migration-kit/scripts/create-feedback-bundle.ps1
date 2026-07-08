<#
.SYNOPSIS
Create a redacted migration feedback bundle for sharing migrator improvement evidence.

.DESCRIPTION
create-feedback-bundle collects migration-kit reports, gate evidence, wave quality budget,
mapping/research memory, verify-project harness snapshots, sentinel findings, and related
forensic artifacts into a safe feedback-bundle/v1 package under state/feedback-bundles.
By default it excludes project source files and generated C# samples. Use -IncludeGeneratedSamples only when the user has
reviewed the generated snippets and wants to include a small capped sample set.
#>
param(
    [string]$Workspace = "migration",
    [string]$OutDir = "",
    [string]$BundleName = "",
    [switch]$IncludeGeneratedSamples,
    [int]$MaxGeneratedSamples = 5,
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"

function Read-TextIfExists([string]$Path) {
    if (Test-Path $Path) { return Get-Content -Raw -Path $Path -ErrorAction SilentlyContinue }
    return ""
}

function Read-LatestRunId([string]$WorkspacePath) {
    $stateRun = Join-Path $WorkspacePath "state/harness-run.json"
    if (Test-Path $stateRun) {
        try {
            $json = Get-Content -Raw -Path $stateRun | ConvertFrom-Json -ErrorAction Stop
            if (-not [string]::IsNullOrWhiteSpace([string]$json.runId)) { return [string]$json.runId }
        } catch { }
    }

    $agentState = Join-Path $WorkspacePath "agent-state.md"
    $text = Read-TextIfExists $agentState
    $m = [regex]::Match($text, '(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$')
    if ($m.Success) { return $m.Groups[1].Value }

    $runsPath = Join-Path $WorkspacePath "runs"
    if (Test-Path $runsPath) {
        $latest = Get-ChildItem -Path $runsPath -Directory -Filter "run-*" -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1
        if ($latest -ne $null) { return $latest.Name }
    }

    return ""
}

function New-DirectoryClean([string]$Path) {
    if (Test-Path $Path) { Remove-Item -Recurse -Force -Path $Path }
    New-Item -ItemType Directory -Force -Path $Path | Out-Null
}

function Convert-ToRelativePath([string]$Root, [string]$Path) {
    $rootFull = [System.IO.Path]::GetFullPath($Root).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $pathFull = [System.IO.Path]::GetFullPath($Path)
    if ($pathFull.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $pathFull.Substring($rootFull.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar).Replace('\\', '/')
    }
    return $pathFull.Replace('\\', '/')
}

function Test-SensitiveContent([string]$Path, [ref]$Reasons) {
    $name = [System.IO.Path]::GetFileName($Path)
    if ($name -match '(?i)^\.env(\.|$)|id_rsa|id_dsa|\.pfx$|\.pem$|\.key$') {
        $Reasons.Value.Add("sensitive filename pattern: $name") | Out-Null
        return $true
    }

    $size = (Get-Item $Path).Length
    if ($size -gt 5MB) {
        $Reasons.Value.Add("file larger than 5MB") | Out-Null
        return $true
    }

    $text = Read-TextIfExists $Path
    if ([string]::IsNullOrEmpty($text)) { return $false }

    $patterns = @(
        '(?i)\b(password|passwd|secret|api[_-]?key|token|client[_-]?secret|connectionstring)\b\s*[:=]',
        '(?i)Authorization:\s*Bearer\s+[A-Za-z0-9._~+/-]+=*',
        '-----BEGIN\s+(RSA\s+)?PRIVATE\s+KEY-----',
        '(?i)AccountKey=|SharedAccessKey=',
        '(?i)aws_access_key_id|aws_secret_access_key'
    )
    foreach ($pattern in $patterns) {
        if ($text -match $pattern) {
            $Reasons.Value.Add("content matched redaction pattern: $pattern") | Out-Null
            return $true
        }
    }

    return $false
}

function Copy-SafeFile(
    [string]$WorkspacePath,
    [string]$BundleRoot,
    [string]$SourcePath,
    [string]$DestinationRelative,
    [System.Collections.Generic.List[object]]$Included,
    [System.Collections.Generic.List[object]]$Excluded,
    [string]$Kind
) {
    if (-not (Test-Path $SourcePath)) { return }
    if ((Get-Item $SourcePath).PSIsContainer) { return }

    $reasons = New-Object System.Collections.Generic.List[string]
    if (Test-SensitiveContent $SourcePath ([ref]$reasons)) {
        $Excluded.Add([ordered]@{
            path = Convert-ToRelativePath $WorkspacePath $SourcePath
            kind = $Kind
            reason = (@($reasons) -join '; ')
        }) | Out-Null
        return
    }

    $destination = Join-Path $BundleRoot $DestinationRelative
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -Force -Path $SourcePath -Destination $destination
    $Included.Add([ordered]@{
        path = Convert-ToRelativePath $WorkspacePath $SourcePath
        bundlePath = $DestinationRelative.Replace('\\', '/')
        kind = $Kind
        bytes = (Get-Item $SourcePath).Length
    }) | Out-Null
}

function Add-GlobFiles(
    [string]$WorkspacePath,
    [string]$BundleRoot,
    [string]$Pattern,
    [System.Collections.Generic.List[object]]$Included,
    [System.Collections.Generic.List[object]]$Excluded,
    [string]$Kind,
    [int]$Limit = 200
) {
    $files = @(Get-ChildItem -Path $WorkspacePath -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { (Convert-ToRelativePath $WorkspacePath $_.FullName) -like $Pattern } |
        Sort-Object FullName |
        Select-Object -First $Limit)
    foreach ($file in $files) {
        $relative = Convert-ToRelativePath $WorkspacePath $file.FullName
        Copy-SafeFile $WorkspacePath $BundleRoot $file.FullName $relative $Included $Excluded $Kind
    }
}

$workspacePath = [System.IO.Path]::GetFullPath($Workspace)
if (-not (Test-Path $workspacePath)) { throw "Workspace not found: $workspacePath" }

$stateDir = Join-Path $workspacePath "state"
if ([string]::IsNullOrWhiteSpace($OutDir)) { $OutDir = Join-Path $stateDir "feedback-bundles" }
$outDirFull = [System.IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $outDirFull | Out-Null

$runId = Read-LatestRunId $workspacePath
$timestamp = [DateTimeOffset]::UtcNow.ToString("yyyyMMdd-HHmmss")
if ([string]::IsNullOrWhiteSpace($BundleName)) {
    $safeRun = if ([string]::IsNullOrWhiteSpace($runId)) { "no-run" } else { $runId }
    $BundleName = "migration-feedback-$safeRun-$timestamp"
}
$BundleName = ($BundleName -replace '[^A-Za-z0-9._-]', '-')
$bundleRoot = Join-Path $outDirFull $BundleName
New-DirectoryClean $bundleRoot

$included = New-Object System.Collections.Generic.List[object]
$excluded = New-Object System.Collections.Generic.List[object]

# High-value state/report artifacts. These are migration evidence only; project source is not included.
$explicit = @(
    "agent-state.md",
    "current-ticket.md",
    "state/final-gate-result.json",
    "state/continuation-decision.json",
    "state/harness-run.json",
    "state/current-ticket-status.json",
    "state/current-ticket-ledger.jsonl",
    "state/sentinel-finding-status.json",
    "state/sentinel-finding-ledger.jsonl",
    "state/wave-quality-budget.json",
    "state/wave-quality-budget.md",
    "state/mapping-research-memory.json",
    "state/mapping-research-memory.md",
    "state/mapping-research-candidates.jsonl",
    "state/artifact-hygiene.json",
    "state/artifact-hygiene.md",
    "state/sentinel-ledger.jsonl",
    "state/agent-skill-usage.jsonl"
)
foreach ($relative in $explicit) {
    Copy-SafeFile $workspacePath $bundleRoot (Join-Path $workspacePath $relative) $relative $included $excluded "state-evidence"
}

Add-GlobFiles $workspacePath $bundleRoot "state/backlog/*.md" $included $excluded "backlog" 50
Add-GlobFiles $workspacePath $bundleRoot "state/backlog/*.json" $included $excluded "backlog" 50
Add-GlobFiles $workspacePath $bundleRoot "state/backlog/*.jsonl" $included $excluded "backlog" 50

# Run and wave evidence. Keep reports/snapshots, not source trees.
Add-GlobFiles $workspacePath $bundleRoot "runs/*/Documentation.md" $included $excluded "run-documentation" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/artifact-hygiene.*" $included $excluded "artifact-hygiene" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/research/mapping-research-memory.*" $included $excluded "mapping-research" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/sentinel/sentinel-report.md" $included $excluded "sentinel" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/sentinel/sentinel-findings.jsonl" $included $excluded "sentinel" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/sentinel/sentinel-inspection.json" $included $excluded "sentinel" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/sentinel/sentinel-finding-status.json" $included $excluded "sentinel" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/sentinel/sentinel-finding-lifecycle.jsonl" $included $excluded "sentinel" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/opencode-session-export.json" $included $excluded "session-export" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/opencode-session-export.md" $included $excluded "session-export" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/project-verify-report.json" $included $excluded "verify-project" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/project-verify-report.md" $included $excluded "verify-project" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/project-verify-harness.csproj" $included $excluded "verify-project-harness" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/*/wave-quality-budget.*" $included $excluded "wave-quality" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/wave-*/generated/migration-board.md" $included $excluded "wave-dashboard" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/wave-*/generated/migration-board.json" $included $excluded "wave-dashboard" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/wave-*/generated/explain-todo.md" $included $excluded "todo-explanation" 50
Add-GlobFiles $workspacePath $bundleRoot "runs/wave-*/wave-status.json" $included $excluded "wave-status" 50

if ($IncludeGeneratedSamples) {
    $sampleCount = 0
    $generatedFiles = @(Get-ChildItem -Path $workspacePath -Recurse -File -Filter "*.cs" -ErrorAction SilentlyContinue |
        Where-Object { (Convert-ToRelativePath $workspacePath $_.FullName) -like "runs/wave-*/generated/*.cs" } |
        Sort-Object Length -Descending |
        Select-Object -First $MaxGeneratedSamples)
    foreach ($file in $generatedFiles) {
        $sampleCount += 1
        $relative = Convert-ToRelativePath $workspacePath $file.FullName
        Copy-SafeFile $workspacePath $bundleRoot $file.FullName ("generated-samples/" + ($relative -replace '/', '__')) $included $excluded "generated-sample-opt-in"
    }
} else {
    $excluded.Add([ordered]@{
        path = "runs/wave-*/generated/*.cs"
        kind = "generated-sample-opt-in"
        reason = "generated C# samples excluded by default; rerun with -IncludeGeneratedSamples after review"
    }) | Out-Null
}

$manifest = [ordered]@{
    schemaVersion = "feedback-bundle/v1"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    sourceWorkspace = (Split-Path -Leaf $workspacePath)
    runId = $runId
    bundleName = $BundleName
    safeByDefault = $true
    includesProjectSourceByDefault = $false
    includeGeneratedSamples = [bool]$IncludeGeneratedSamples
    maxGeneratedSamples = $MaxGeneratedSamples
    redactionChecks = [ordered]@{
        sensitiveFilenamePatterns = @(".env", "id_rsa", "*.pem", "*.key", "*.pfx")
        sensitiveContentPatterns = @("password/secret/token/api-key assignments", "Authorization: Bearer", "private keys", "cloud shared keys")
        largeFileLimitBytes = 5MB
    }
    included = @($included)
    excluded = @($excluded)
    recommendedRecipientReview = @(
        "Open manifest.json and confirm included file list before sharing.",
        "Do not add project source files unless explicitly requested and reviewed.",
        "Prefer sharing mapping-research-memory.json, project-verify-report.json, and project-verify-harness.csproj over private test source."
    )
}

$manifestPath = Join-Path $bundleRoot "manifest.json"
$manifest | ConvertTo-Json -Depth 40 | Set-Content -Path $manifestPath -Encoding UTF8

$readme = @"
# Migration Feedback Bundle

Schema: `feedback-bundle/v1`
Generated at UTC: `$($manifest.generatedAtUtc)`
Run id: `$runId`

This bundle is designed for sharing migrator improvement evidence without sending the whole private project.
By default it excludes project source files and generated C# samples.

## Review before sharing

1. Open `manifest.json`.
2. Review every item in `included`.
3. Check `excluded` for anything the packer intentionally skipped.
4. Share the zip only after this review.

## Most useful files for the migrator author

- `state/mapping-research-memory.json`
- `state/mapping-research-candidates.jsonl`
- `state/wave-quality-budget.json`
- `runs/*/project-verify-report.json`
- `runs/*/project-verify-harness.csproj`
- `runs/wave-*/generated/migration-board.md`
- `runs/wave-*/generated/explain-todo.md`

## Generated samples

Generated `.cs` samples are excluded by default. To include a small capped set after review, rerun:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration -IncludeGeneratedSamples -MaxGeneratedSamples 3
```
"@
Set-Content -Path (Join-Path $bundleRoot "README.md") -Value $readme -Encoding UTF8

$zipPath = Join-Path $outDirFull ($BundleName + ".zip")
if (-not $NoZip) {
    if (Test-Path $zipPath) { Remove-Item -Force -Path $zipPath }
    Compress-Archive -Path (Join-Path $bundleRoot "*") -DestinationPath $zipPath -Force
}

$stateSummary = [ordered]@{
    schemaVersion = "feedback-bundle/v1"
    generatedAtUtc = $manifest.generatedAtUtc
    runId = $runId
    bundleDirectory = $bundleRoot
    bundleZip = if ($NoZip) { $null } else { $zipPath }
    includedCount = @($included).Count
    excludedCount = @($excluded).Count
    includeGeneratedSamples = [bool]$IncludeGeneratedSamples
}
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$stateSummary | ConvertTo-Json -Depth 20 | Set-Content -Path (Join-Path $stateDir "feedback-bundle.json") -Encoding UTF8

Write-Host "FEEDBACK_BUNDLE_CREATED"
Write-Host "Schema: feedback-bundle/v1"
Write-Host "Directory: $bundleRoot"
if (-not $NoZip) { Write-Host "Zip: $zipPath" }
Write-Host "Included files: $(@($included).Count)"
Write-Host "Excluded/skipped items: $(@($excluded).Count)"
