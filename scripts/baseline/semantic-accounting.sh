#!/usr/bin/env bash
# semantic-accounting.sh - cross-platform companion of scripts/baseline/semantic-accounting.ps1
# Usage:
#   bash scripts/baseline/semantic-accounting.sh
#   bash scripts/baseline/semantic-accounting.sh --report path/to/report.json --markdown
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLI="$REPO_ROOT/Migrator.Cli/bin/Debug/net10.0/Migrator.Cli.dll"
CORPUS="$REPO_ROOT/corpus/stable/vertical-slice"
REPORT=""
MARKDOWN=0

while [[ $# -gt 0 ]]; do
  case "$1" in
    --report) REPORT="$2"; shift 2 ;;
    --markdown) MARKDOWN=1; shift ;;
    *) echo "unknown option: $1" >&2; exit 2 ;;
  esac
done

if [[ -z "$REPORT" ]]; then
  if [[ ! -f "$CLI" ]]; then echo "CLI not built: $CLI" >&2; exit 1; fi
  OUT="artifacts/baseline/accounting/analyze"
  dotnet "$CLI" --mode analyze --input "$CORPUS" --out "$OUT" --format json
  REPORT=""
  for cand in "$REPO_ROOT/$OUT" "$REPO_ROOT/migration/$OUT"; do
    if [[ -z "$REPORT" ]]; then REPORT="$(find "$cand" -name report.json 2>/dev/null | head -1 || true)"; fi
  done
  [[ -z "$REPORT" ]] && { echo "analyze report not found under $OUT" >&2; exit 1; }
fi

# Minimal normalized totals using python3 if present (the PS1 harness is the canonical
# implementation; this shell companion reproduces the rollup numbers for CI).
python3 - "$REPORT" <<'PY'
import json, sys, os
r = json.load(open(sys.argv[1], encoding='utf-8'))
led = r.get("PerFileReports", [])
fc = sum(1 for f in led if f.get("TodoComments",0)==0 and f.get("UnmappedTargets",0)==0 and f.get("UnsupportedCount",0)==0)
tot = {
  "FilesProcessed": r.get("FilesProcessed"), "TestsFound": r.get("TestsFound"), "ActionsFound": r.get("ActionsFound"),
  "Semantic": r.get("SemanticActions"), "SyntaxFallback": r.get("SyntaxFallbackActions"),
  "Unsupported": r.get("UnsupportedActions"), "Mapped": r.get("MappedTargets"),
  "Unmapped": r.get("UnmappedTargets"), "TodoComments": r.get("TodoComments"),
  "FullyConvertedFiles": fc,
  "ReportDoubleCountDelta": (r.get("SemanticActions",0)+r.get("SyntaxFallbackActions",0))-r.get("ActionsFound",0),
}
print(json.dumps(tot, indent=2, ensure_ascii=False))
PY
