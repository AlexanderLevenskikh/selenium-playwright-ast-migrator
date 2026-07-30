#!/usr/bin/env bash
set -euo pipefail
configuration="${CONFIGURATION:-Release}"
timeout_seconds="${TIMEOUT_SECONDS:-600}"
artifacts="${ARTIFACTS:-./artifacts/lab/block-04}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

dotnet build Migrator.sln -c "$configuration"
dotnet test Migrator.Tests/Migrator.Tests.csproj -c "$configuration" --no-build

playwright_script="Migrator.Tests/bin/$configuration/net10.0/playwright.ps1"
if [[ "${SKIP_BROWSER_INSTALL:-0}" != "1" ]]; then
  if command -v pwsh >/dev/null 2>&1; then
    pwsh -NoProfile -File "$playwright_script" install chromium
  else
    echo "pwsh is required to install Playwright Chromium; set SKIP_BROWSER_INSTALL=1 only when browsers are already installed." >&2
    exit 13
  fi
fi

rm -rf "$artifacts"
set +e
dotnet run --project Migrator.Cli -c "$configuration" --no-build -- \
  lab run \
  --suite vertical \
  --corpus ./corpus/stable/vertical-slice \
  --out "$artifacts" \
  --timeout-seconds "$timeout_seconds" \
  --configuration "$configuration"
lab_exit=$?
set -e

python3 - "$artifacts/lab-summary.json" "$lab_exit" <<'PY'
import json,sys
path,exit_code=sys.argv[1],int(sys.argv[2])
with open(path,encoding='utf-8') as f: data=json.load(f)
assert data['summary']['projects']==7, data['summary']
mismatches=[p for p in data['projects'] if p['actualStatus'] != p['expectedStatus']]
if exit_code or mismatches:
    for p in mismatches:
        print(f"{p['id']}: expected {p['expectedStatus']}, actual {p['actualStatus']}")
    raise SystemExit('Block 4 did not reach its final acceptance state; inspect lab-summary.md')
PY

echo "Block 4 passed: verify-project, target Playwright runtime, semantic oracle, and quality budgets matched all 7 scenario contracts."
