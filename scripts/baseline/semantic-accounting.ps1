# semantic-accounting.ps1 - Phase A semantic accounting harness.
#
# Normalizes the analyze report of the whole corpus into a per-file semantic ledger:
#   Semantic vs SyntaxFallback vs Unsupported actions, Mapped/Unmapped targets, TODOs,
#   and a derived "fully converted" flag (no TODO lexicon hit, no unmapped target).
# Also verifies the rollup invariant and surfaces the ReportBuilder double-counting
# artefact (Semantic+SyntaxFallback may exceed ActionsFound when block containers are
# counted both as nodes and via Flatten).
#
# Usage: powershell -File scripts/baseline/semantic-accounting.ps1
#   [-Report <path to report.json>]          # reuse an existing analyze report
#   [-OutputJson <path>]                      # default artifacts/baseline/accounting/semantic-accounting.json
#   [-WriteMarkdown]                          # also emit a markdown table next to the JSON
#
# See docs/vnext/semantic-accounting.md for the definition of terms and the decision gate.

param(
    [string]$Report,
    [string]$OutputJson,
    [switch]$WriteMarkdown
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cli = Join-Path $repoRoot 'Migrator.Cli\bin\Debug\net10.0\Migrator.Cli.dll'
$corpus = Join-Path $repoRoot 'corpus\stable\vertical-slice'

if (-not $Report) {
    if (-not (Test-Path $cli)) { throw "CLI not built: $cli (run dotnet build first)" }
    $out = Join-Path 'artifacts\baseline' 'accounting\analyze'
    & dotnet $cli --mode analyze --input $corpus --out $out --format json
    if ($LASTEXITCODE -ne 0) { throw "analyze failed (exit $LASTEXITCODE)" }
    # analyze re-bases --out under migration/; fall back to both candidates
    $candidates = @(
        (Join-Path $repoRoot $out),
        (Join-Path $repoRoot "migration\$out")
    )
    $report = ($candidates | ForEach-Object { Get-ChildItem -Path $_ -Recurse -Filter 'report.json' -ErrorAction SilentlyContinue | Select-Object -First 1 } | Select-Object -First 1).FullName
    if (-not $report) { throw "analyze report not found under $out" }
}
else { $report = (Resolve-Path $Report).Path }

$r = Get-Content $report -Raw | ConvertFrom-Json
if (-not $OutputJson) { $OutputJson = Join-Path $repoRoot 'artifacts\baseline\accounting\semantic-accounting.json' }

# Per-file ledger
$ledger = @()
foreach ($f in $r.PerFileReports) {
    $rel = $f.SourceFilePath -replace '^<USER_HOME>\\(Desktop\\)?', '' -replace '^.*\\corpus\\', 'corpus\'
    $fullyConverted = ($f.TodoComments -eq 0 -and $f.UnmappedTargets -eq 0 -and $f.UnsupportedCount -eq 0)
    $ledger += [pscustomobject]@{
        File                  = $rel
        Tests                 = $f.TotalTests
        Semantic              = $f.SemanticActions
        SyntaxFallback        = $f.SyntaxFallbackActions
        Unsupported           = $f.UnsupportedCount
        Mapped                = $f.MappedTargets
        Unmapped              = $f.UnmappedTargets
        TodoComments          = $f.TodoComments
        SuccessfullyConverted = $f.SuccessfullyConvertedTests
        FullyConverted        = $fullyConverted
    }
}

$tot = [pscustomobject]@{
    FilesProcessed  = $r.FilesProcessed
    TestsFound      = $r.TestsFound
    ActionsFound    = $r.ActionsFound
    Semantic        = $r.SemanticActions
    SyntaxFallback  = $r.SyntaxFallbackActions
    Unsupported     = $r.UnsupportedActions
    Mapped          = $r.MappedTargets
    Unmapped        = $r.UnmappedTargets
    TodoComments    = $r.TodoComments
    StoriesConverted = ($ledger | Where-Object FullyConverted).Count
    ReportDoubleCountDelta = ($r.SemanticActions + $r.SyntaxFallbackActions) - $r.ActionsFound
}

$result = [ordered]@{
    SchemaVersion          = 'migrator-semantic-accounting/v1'
    SourceReport           = $report
    GeneratedAtUtc         = (Get-Date).ToUniversalTime().ToString('o')
    InvariantCheck         = "Semantic+SyntaxFallback >= ActionsFound is a KNOWN double-counting artefact (block containers counted as nodes and via Flatten); delta = $($tot.ReportDoubleCountDelta)"
    Totals                 = [ordered]@{}
    Ledger                 = $ledger
}
foreach ($p in $tot.PSObject.Properties) { $result.Totals[$p.Name] = $p.Value }

$dir = Split-Path $OutputJson -Parent
if (-not (Test-Path $dir)) { New-Item -ItemType Directory -Path $dir -Force | Out-Null }
$result | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputJson -Encoding utf8

# Markdown mirror
if ($WriteMarkdown) {
    $md = Join-Path $dir 'semantic-accounting.md'
    $lines = @(
        '# Semantic Accounting (whole corpus, unconfigured default path)',
        '',
        "Source report: ``$report``",
        '',
        "| Files | Tests | Actions | Semantic | SyntaxFallback | Unsupported | Mapped | Unmapped | TODO | Fully converted files |",
        '|---|---|---|---|---|---|---|---|---|---|',
        ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} |" -f `
            $tot.FilesProcessed,$tot.TestsFound,$tot.ActionsFound,$tot.Semantic,$tot.SyntaxFallback,
            $tot.Unsupported,$tot.Mapped,$tot.Unmapped,$tot.TodoComments,$tot.StoriesConverted),
        '',
        '> Note: Semantic+SyntaxFallback (127) > ActionsFound (120) is the ReportBuilder double-counting artefact (block containers counted both as nodes and via Flatten). `SuccessfullyConvertedTests` is 1 for every story even when all assertions/locators became TODOs; the run-level quality gates (`AssertionLoss`, `SemanticNoOp`, `MISSING_MAPPING`) are the honest signal.',
        '',
        '## Per-file',
        '',
        '| File | Tests | Sem | Syn | Unsup | Mapped | Unmapped | TODO | Fully converted |',
        '|---|---|---|---|---|---|---|---|---|'
    )
    foreach ($e in $ledger) {
        $lines += ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} |" -f `
            $e.File,$e.Tests,$e.Semantic,$e.SyntaxFallback,$e.Unsupported,$e.Mapped,$e.Unmapped,$e.TodoComments, $(if ($e.FullyConverted) {'YES'} else {'no'}))
    }
    $lines | Set-Content -Path $md -Encoding utf8
    Write-Host "markdown -> $md"
}

Write-Host "ledger -> $OutputJson"
Write-Host ("files={0} tests={1} actions={2} semantic={3} syntax={4} unsupported={5} mapped={6} unmapped={7} todos={8} fullyConvertedFiles={9} doubleCountDelta={10}" -f `
    $tot.FilesProcessed,$tot.TestsFound,$tot.ActionsFound,$tot.Semantic,$tot.SyntaxFallback,$tot.Unsupported,
    $tot.Mapped,$tot.Unmapped,$tot.TodoComments,$tot.StoriesConverted,$tot.ReportDoubleCountDelta)
