# measure-baseline.ps1 - Phase A baseline measurement harness.
#
# Runs the three reproducible generator measurements that do NOT need a browser:
#   1. whole-corpus semantic accounting           (analyze, no config)
#   2. per-fixture analyze/migrate stage timing
#   3. run-level determinism  (run --twice --assert-identical) on p01 and p04
#
# Usage:  powershell -File scripts/baseline/measure-baseline.ps1 [-NoDeterminism]
# Output: artifacts/baseline/{accounting,perf,determinism}/...  and console summary.
#
# See docs/vnext/phase-a-findings.md (Performance and Determinism sections) for the
# interpretation of the numbers. This harness is a measurement instrument, not a gate.

param(
    [switch]$NoDeterminism
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$cli = Join-Path $repoRoot 'Migrator.Cli\bin\Debug\net10.0\Migrator.Cli.dll'
$corpus = Join-Path $repoRoot 'corpus\stable\vertical-slice'
$outRoot = Join-Path $repoRoot 'artifacts\baseline'

if (-not (Test-Path $cli)) { throw "CLI not built: $cli (run dotnet build first)" }

function Invoke-Cli {
    param([string[]]$ArgsList)
    & dotnet $cli @ArgsList
    if ($LASTEXITCODE -ne 0) { throw "CLI exited $LASTEXITCODE: dotnet $cli $($ArgsList -join ' ')" }
}

# The CLI re-bases --out under migration/ for the plain run/analyze paths but NOT for
# run --twice. Resolve the produced report wherever it landed.
function Find-Report {
    param([string]$OutDir)
    $candidates = @(
        (Join-Path $repoRoot $OutDir),
        (Join-Path $repoRoot "migration\$OutDir")
    )
    foreach ($c in $candidates) {
        $r = Get-ChildItem -Path $c -Recurse -Filter 'report.json' -ErrorAction SilentlyContinue |
             Select-Object -First 1
        if ($r) { return $r.FullName }
    }
    return $null
}

Write-Host "== whole-corpus analyze (no adapter-config) =="
$warm = Join-Path 'artifacts\baseline' 'perf\whole-corpus-warmup'
# warmup once so JIT/startup does not pollute the timing measurement
Invoke-Cli @('--mode','analyze','--input',$corpus,'--out',$warm,'--format','json')

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$perf = Join-Path 'artifacts\baseline' 'perf\whole-corpus'
Invoke-Cli @('--mode','analyze','--input',$corpus,'--out',$perf,'--format','json')
$sw.Stop()
Write-Host ("whole-corpus analyze: {0:N2}s (wall)" -f $sw.Elapsed.TotalSeconds)
$report = Find-Report $perf
if ($report) { Write-Host "report: $report" }
else { Write-Host "WARNING: report.json not found for $perf" }

Write-Host "== per-fixture stage timing (p01, p28) =="
foreach ($fx in @('p01-basic-id-login','p28-frames-popup-upload-download')) {
    $a = Join-Path 'artifacts\baseline' "perf\$fx-analyze"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-Cli @('--mode','analyze','--input',(Join-Path $corpus $fx),'--out',$a,'--format','json')
    $sw.Stop()
    Write-Host ("  {0,-45} analyze {1,5:N2}s" -f $fx,$sw.Elapsed.TotalSeconds)

    $m = Join-Path 'artifacts\baseline' "perf\$fx-migrate"
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    Invoke-Cli @('--mode','migrate','--input',(Join-Path $corpus $fx),'--out',$m,'--format','json')
    $sw.Stop()
    Write-Host ("  {0,-45} migrate {1,5:N2}s" -f $fx,$sw.Elapsed.TotalSeconds)
}

if (-not $NoDeterminism) {
    Write-Host "== determinism: run --twice --assert-identical (p01, p04) =="
    foreach ($fx in @('p01-basic-id-login','p04-findelements-count-text')) {
        $d = Join-Path 'artifacts\baseline' "determinism\$fx\run"
        & dotnet $cli run --twice --assert-identical --input (Join-Path $corpus $fx) --out $d
        # determinism harness exits 6 on mismatch (assert-identical) - acceptable,
        # content is captured in determinism-result.json under $d
        $res = Join-Path $repoRoot ("$d\determinism-result.json")
        if (-not (Test-Path $res)) { throw "determinism result missing: $res" }
        $json = Get-Content $res -Raw | ConvertFrom-Json
        Write-Host ("  {0,-45} Decision={1} exitA={2} exitB={3}" -f $fx,$json.Decision,$json.RunAExitCode,$json.RunBExitCode)
    }
}

Write-Host "done. See docs/vnext/phase-a-findings.md for interpretation."
