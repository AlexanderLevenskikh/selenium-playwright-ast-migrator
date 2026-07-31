#!/usr/bin/env bash
set -euo pipefail

configuration="${CONFIGURATION:-Release}"
timeout_seconds="${TIMEOUT_SECONDS:-600}"
artifacts="${ARTIFACTS:-./artifacts/lab/block-05}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet build Migrator.sln -c "$configuration"
dotnet test Migrator.Tests/Migrator.Tests.csproj -c "$configuration" --no-build
rm -rf "$artifacts"
current="$artifacts/current"
baseline="$artifacts/baseline-main"
replay="$artifacts/replay-p15"
same_diff="$artifacts/diff-same"

dotnet run --project Migrator.Cli -c "$configuration" --no-build -- lab run --suite vertical --corpus ./corpus/stable/vertical-slice --out "$current" --timeout-seconds "$timeout_seconds" --configuration "$configuration"
dotnet run --project Migrator.Cli -c "$configuration" --no-build -- lab baseline --input "$current" --out "$baseline" --label main
dotnet run --project Migrator.Cli -c "$configuration" --no-build -- lab replay --project p15-webdriverwait-visible --corpus ./corpus/stable/vertical-slice --out "$replay" --timeout-seconds "$timeout_seconds" --configuration "$configuration"
dotnet run --project Migrator.Cli -c "$configuration" --no-build -- lab diff --baseline "$baseline" --current "$current" --out "$same_diff" --duration-regression-percent 20

test -f "$current/lab-summary.html"
test -f "$baseline/lab-baseline.json"
test -f "$replay/lab-summary.html"
test -f "$same_diff/lab-diff.html"
echo "Block 5 passed: HTML report, single-scenario replay, normalized baseline, and clean diff are verified."
