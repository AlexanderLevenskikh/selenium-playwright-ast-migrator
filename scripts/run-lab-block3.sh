#!/usr/bin/env bash
set -euo pipefail

configuration="${CONFIGURATION:-Release}"
timeout_seconds="${TIMEOUT_SECONDS:-600}"
artifacts="${ARTIFACTS:-./artifacts/lab/block-03}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet build Migrator.sln -c "$configuration"
dotnet test Migrator.Tests/Migrator.Tests.csproj \
  -c "$configuration" \
  --no-build

rm -rf "$artifacts"
dotnet run --project Migrator.Cli \
  -c "$configuration" \
  --no-build \
  -- \
  lab run \
  --suite vertical \
  --corpus ./corpus/stable/vertical-slice \
  --out "$artifacts" \
  --timeout-seconds "$timeout_seconds" \
  --configuration "$configuration"

test -f "$artifacts/lab-summary.json"
python3 - "$artifacts/lab-summary.json" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as stream:
    report = json.load(stream)
summary = report["summary"]
assert summary["projects"] == 7, summary
for key in ("migratorFailures", "sourceInvalid", "infrastructureFailures", "regressions"):
    assert summary[key] == 0, summary
PY

echo "Block 3 passed: 7 source projects validated, existing migration run executed, suite statuses classified."
