#!/usr/bin/env bash
# measure-baseline.sh - cross-platform companion of scripts/baseline/measure-baseline.ps1
# Requires a .NET SDK (>= net10.0) with Migrator.Cli already built: `dotnet build`.
# Usage: bash scripts/baseline/measure-baseline.sh [--no-determinism]

set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
CLI="$REPO_ROOT/Migrator.Cli/bin/Debug/net10.0/Migrator.Cli.dll"
CORPUS="$REPO_ROOT/corpus/stable/vertical-slice"
OUT="$REPO_ROOT/artifacts/baseline"
NO_DETERMINISM=0
[[ "${1:-}" == "--no-determinism" ]] && NO_DETERMINISM=1

if [[ ! -f "$CLI" ]]; then
  echo "CLI not built: $CLI (run: dotnet build)" >&2
  exit 1
fi

invoke() { dotnet "$CLI" "$@"; }

echo "== whole-corpus analyze (no adapter-config) =="
invoke --mode analyze --input "$CORPUS" --out "artifacts/baseline/perf/whole-corpus-warmup" --format json
START=$(date +%s.%N)
invoke --mode analyze --input "$CORPUS" --out "artifacts/baseline/perf/whole-corpus" --format json
END=$(date +%s.%N)
echo "whole-corpus analyze: $(echo "$END $START" | awk '{printf "%.2fs", $1-$2}') (wall)"

echo "== per-fixture stage timing (p01, p28) =="
for FX in p01-basic-id-login p28-frames-popup-upload-download; do
  START=$(date +%s.%N)
  invoke --mode analyze --input "$CORPUS/$FX" --out "artifacts/baseline/perf/$FX-analyze" --format json
  END=$(date +%s.%N)
  echo "  $FX  analyze $(echo "$END $START" | awk '{printf "%.2fs", $1-$2}')"
  START=$(date +%s.%N)
  invoke --mode migrate --input "$CORPUS/$FX" --out "artifacts/baseline/perf/$FX-migrate" --format json
  END=$(date +%s.%N)
  echo "  $FX  migrate $(echo "$END $START" | awk '{printf "%.2fs", $1-$2}')"
done

if [[ $NO_DETERMINISM -eq 0 ]]; then
  echo "== determinism: run --twice --assert-identical (p01, p04) =="
  for FX in p01-basic-id-login p04-findelements-count-text; do
    D="$OUT/determinism/$FX/run"
    invoke run --twice --assert-identical --input "$CORPUS/$FX" --out "$D" || true
    if [[ ! -f "$REPO_ROOT/$D/determinism-result.json" ]]; then
      echo "determinism result missing: $D" >&2
      exit 1
    fi
    DEC=$(grep -o '"Decision": *"[^"]*"' "$REPO_ROOT/$D/determinism-result.json" | head -1)
    echo "  $FX  $DEC"
  done
fi

echo "done. See docs/vnext/phase-a-findings.md for interpretation."
