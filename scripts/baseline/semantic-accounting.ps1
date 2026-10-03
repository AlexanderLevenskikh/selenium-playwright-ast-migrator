# semantic-accounting.ps1 - Phase A semantic accounting harness.
#
# Normalizes the analyze report of the whole corpus into a per-file semantic ledger:
#   Semantic vs SyntaxFallback vs Unsupported actions, Mapped/Unmapped targets, TODOs,
#   and a derived "fully converted" flag (no TODO lexicon hit, no unmapped target).
# Also verifies the rollup invariant: Semantic + SyntaxFallback + Unsupported == ActionsFound
# (the flattened total). StructuralContainers is reported as an independent structure
# dimension. Prior to Phase A.1 the CLI counted only top-level actions while ReportBuilder
# counted the flattened set, so Semantic+SyntaxFallback exceeded ActionsFound by the number
# of nested actions; that inconsistency is now fixed and the invariant is checked below.
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

# Old reports (pre Phase A.1 metric fields) have no GeneratedTests/FullyConvertedTests;
# default them to 0 so the ledger still converges instead of carrying $null.
function IntOrZero($value) {
    if ($null -eq $value) { return 0 }
    return [int]$value
}

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
$invariantHolds = $true
$sumGeneratedTests = 0
$sumFullyConvertedTests = 0
foreach ($f in $r.PerFileReports) {
    $rel = $f.SourceFilePath -replace '^<USER_HOME>\\(Desktop\\)?', '' -replace '^.*\\corpus\\', 'corpus\'
    $fullyConverted = ($f.TodoComments -eq 0 -and $f.UnmappedTargets -eq 0 -and $f.UnsupportedCount -eq 0)
    $fileInvariant = ($f.SemanticActions + $f.SyntaxFallbackActions + $f.UnsupportedCount) -eq $f.TotalActions
    if (-not $fileInvariant) { $invariantHolds = $false }
    $genTests = IntOrZero $f.GeneratedTests
    $fullConvTests = IntOrZero $f.FullyConvertedTests
    $sumGeneratedTests += $genTests
    $sumFullyConvertedTests += $fullConvTests
    $ledger += [pscustomobject]@{
        File                  = $rel
        Tests                 = $f.TotalTests
        GeneratedTests        = $genTests
        FullyConvertedTests   = $fullConvTests
        Semantic              = $f.SemanticActions
        SyntaxFallback        = $f.SyntaxFallbackActions
        Unsupported           = $f.UnsupportedCount
        TotalActions          = $f.TotalActions
        StructuralContainers  = $f.StructuralContainers
        Mapped                = $f.MappedTargets
        Unmapped              = $f.UnmappedTargets
        TodoComments          = $f.TodoComments
        SuccessfullyConverted = $f.SuccessfullyConvertedTests
        FullyConverted        = $fullyConverted
        AccountingInvariant   = $fileInvariant
    }
}

$tot = [pscustomobject]@{
    FilesProcessed  = $r.FilesProcessed
    TestsFound      = $r.TestsFound
    ActionsFound    = $r.ActionsFound
    Semantic        = $r.SemanticActions
    SyntaxFallback  = $r.SyntaxFallbackActions
    Unsupported     = $r.UnsupportedActions
    StructuralContainers = $r.StructuralContainers
    Mapped          = $r.MappedTargets
    Unmapped        = $r.UnmappedTargets
    TodoComments    = $r.TodoComments
    GeneratedTests        = $sumGeneratedTests
    FullyConvertedTests   = $sumFullyConvertedTests
    StoriesConverted = ($ledger | Where-Object FullyConverted).Count
    AccountingInvariantHolds = $invariantHolds
    AccountedVsTotal = ($r.SemanticActions + $r.SyntaxFallbackActions + $r.UnsupportedActions) - $r.ActionsFound
}

$result = [ordered]@{
    SchemaVersion          = 'migrator-semantic-accounting/v2'
    SourceReport           = $report
    GeneratedAtUtc         = (Get-Date).ToUniversalTime().ToString('o')
    InvariantCheck         = "Semantic+SyntaxFallback+Unsupported == ActionsFound (flattened total); holds = $($tot.AccountingInvariantHolds); accounted-vs-total delta = $($tot.AccountedVsTotal). StructuralContainers is an independent structure dimension, not a confidence bucket. FullyConvertedTests (via ExecutableTargetSemantics) is the honest test-level signal; SuccessfullyConvertedTests is its legacy no-UnsupportedAction predecessor."
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
        "| Files | Tests | Actions | Semantic | SyntaxFallback | Unsupported | StructuralContainers | Mapped | Unmapped | TODO | Generated tests | Fully converted tests | Fully converted files |",
        '|---|---|---|---|---|---|---|---|---|---|---|---|---|',
        ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} |" -f `
            $tot.FilesProcessed,$tot.TestsFound,$tot.ActionsFound,$tot.Semantic,$tot.SyntaxFallback,
            $tot.Unsupported,$tot.StructuralContainers,$tot.Mapped,$tot.Unmapped,$tot.TodoComments,
            $tot.GeneratedTests,$tot.FullyConvertedTests,$tot.StoriesConverted),
        '',
        ('> Invariant: Semantic+SyntaxFallback+Unsupported == ActionsFound (flattened) - holds = {0} (accounted-vs-total delta = {1}). SuccessfullyConvertedTests is 1 for every story even when all assertions/locators became TODOs; the run-level quality gates (AssertionLoss, SemanticNoOp, MISSING_MAPPING) and FullyConvertedTests are the honest signal.' -f $tot.AccountingInvariantHolds, $tot.AccountedVsTotal),
        '',
        '## Per-file',
        '',
        '| File | Tests | Gen | FullConv | Sem | Syn | Unsup | Total | Struct | Mapped | Unmapped | TODO | Fully converted files | Invariant |',
        '|---|---|---|---|---|---|---|---|---|---|---|---|---|---|'
    )
    foreach ($e in $ledger) {
        $lines += ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} | {11} | {12} | {13} |" -f `
            $e.File,$e.Tests,$e.GeneratedTests,$e.FullyConvertedTests,$e.Semantic,$e.SyntaxFallback,$e.Unsupported,$e.TotalActions,$e.StructuralContainers,
            $e.Mapped,$e.Unmapped,$e.TodoComments, $(if ($e.FullyConverted) {'YES'} else {'no'}), $(if ($e.AccountingInvariant) {'ok'} else {'**** FAIL ****'}))
    }
    $lines | Set-Content -Path $md -Encoding utf8
    Write-Host "markdown -> $md"
}

Write-Host "ledger -> $OutputJson"
Write-Host ("files={0} tests={1} actions={2} semantic={3} syntax={4} unsupported={5} structuralContainers={6} mapped={7} unmapped={8} todos={9} generatedTests={10} fullyConvertedTests={11} fullyConvertedFiles={12} invariantHolds={13}" -f `
    $tot.FilesProcessed,$tot.TestsFound,$tot.ActionsFound,$tot.Semantic,$tot.SyntaxFallback,$tot.Unsupported,
    $tot.StructuralContainers,$tot.Mapped,$tot.Unmapped,$tot.TodoComments,$tot.GeneratedTests,$tot.FullyConvertedTests,
    $tot.StoriesConverted,$tot.AccountingInvariantHolds)
