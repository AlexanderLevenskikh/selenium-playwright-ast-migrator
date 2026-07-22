<# Collect reusable root-cause evidence from the latest standard migration run. #>
param(
    [string]$Workspace = "migration",
    [string]$RunId = "",
    [int]$MaxItems = 20,
    [switch]$CreateCurrentTicket
)
$ErrorActionPreference = "Stop"
function Resolve-FullPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
}
function Read-Text([string]$Path) { if (Test-Path $Path) { return Get-Content -Raw $Path -ErrorAction SilentlyContinue }; return "" }
$workspacePath = Resolve-FullPath $Workspace
$runsPath = Join-Path $workspacePath "runs"
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $latest = Get-ChildItem $runsPath -Directory -Filter "run-*" -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if ($null -ne $latest) { $RunId = $latest.Name }
}
if ([string]::IsNullOrWhiteSpace($RunId)) { throw "No standard run was found under $runsPath" }
$runRoot = Join-Path $runsPath $RunId
if (-not (Test-Path $runRoot)) { throw "Run not found: $runRoot" }
$files = Get-ChildItem $runRoot -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.Extension -in @('.json','.md','.txt','.csv') }
$allText = ($files | ForEach-Object { Read-Text $_.FullName }) -join "`n"
$patterns = [ordered]@{
    unresolvedSymbols = '(?im)\bUNRESOLVED_SYMBOL\b[^\r\n]*'
    unmappedActions = '(?im)\bUNMAPPED(?:_TARGET|_ACTION)?\b[^\r\n]*'
    todoClusters = '(?im)\bTODO\b[^\r\n]*'
    syntaxFallbacks = '(?im)\bSYNTAX_FALLBACK\b[^\r\n]*'
    verificationBlockers = '(?im)\b(?:VERIFY_PROJECT|VALIDATION)[^\r\n]*(?:FAIL|BLOCK|MISSING|ERROR)[^\r\n]*'
}
$clusters = [ordered]@{}
foreach ($key in $patterns.Keys) {
    $items = [regex]::Matches($allText, $patterns[$key]) | ForEach-Object { $_.Value.Trim() } | Where-Object { $_ } | Group-Object | Sort-Object Count -Descending | Select-Object -First $MaxItems
    $clusters[$key] = @($items | ForEach-Object { [ordered]@{ text = $_.Name; count = $_.Count } })
}
$candidates = New-Object System.Collections.Generic.List[object]
foreach ($key in $clusters.Keys) {
    foreach ($item in @($clusters[$key])) {
        [void]$candidates.Add([ordered]@{
            category = $key
            title = "Resolve repeated $key pattern"
            evidence = $item.text
            occurrences = $item.count
            validation = "Rerun the complete standard pipeline and require this pattern count to decrease without behavior loss."
        })
    }
}
$candidates = @($candidates | Sort-Object @{Expression='occurrences';Descending=$true} | Select-Object -First $MaxItems)
$payload = [ordered]@{
    schemaVersion = "mapping-research-memory/v2"
    runId = $RunId
    runRoot = $runRoot
    collectedAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
    scannedFiles = $files.Count
    clusters = $clusters
    candidates = $candidates
    nextAction = if ($candidates.Count -gt 0) { "Implement exactly one highest-payoff root-cause improvement, then rerun the complete standard flow." } else { "No repeated actionable pattern detected; review the concrete blocker manually." }
}
$stateDir = Join-Path $workspacePath "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$jsonPath = Join-Path $stateDir "mapping-research-memory.json"
$payload | ConvertTo-Json -Depth 30 | Set-Content $jsonPath -Encoding UTF8
$candidatePath = Join-Path $stateDir "mapping-research-candidates.jsonl"
Remove-Item $candidatePath -Force -ErrorAction SilentlyContinue
foreach ($candidate in $candidates) { Add-Content $candidatePath -Encoding UTF8 -Value ($candidate | ConvertTo-Json -Depth 10 -Compress) }
$md = @("# Mapping research memory", "", "Run: ``$RunId``", "", "## Candidates", "")
if ($candidates.Count -eq 0) { $md += "- No repeated actionable pattern detected." } else { foreach ($c in $candidates) { $md += "- **$($c.category)** ($($c.occurrences)): $($c.evidence)" } }
$md += @("", "## Next action", "", $payload.nextAction)
$md -join "`n" | Set-Content (Join-Path $stateDir "mapping-research-memory.md") -Encoding UTF8
$researchDir = Join-Path $runRoot "research"
New-Item -ItemType Directory -Force -Path $researchDir | Out-Null
Copy-Item $jsonPath (Join-Path $researchDir "mapping-research-memory.json") -Force
Copy-Item (Join-Path $stateDir "mapping-research-memory.md") (Join-Path $researchDir "mapping-research-memory.md") -Force
if ($CreateCurrentTicket -and $candidates.Count -gt 0) {
    @("# Current migration ticket", "", "Implement one root-cause improvement:", "", "- $($candidates[0].title)", "- Evidence: $($candidates[0].evidence)", "- Validation: $($candidates[0].validation)") -join "`n" | Set-Content (Join-Path $workspacePath "current-ticket.md") -Encoding UTF8
}
Write-Host "MAPPING_RESEARCH_MEMORY_COLLECTED: $jsonPath"
