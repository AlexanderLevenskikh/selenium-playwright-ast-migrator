#!/usr/bin/env bash
set -uo pipefail

configuration="${CONFIGURATION:-Release}"
timeout_seconds="${TIMEOUT_SECONDS:-600}"
artifacts="${ARTIFACTS:-./artifacts/lab/block-06}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

set -e
dotnet build Migrator.sln -c "$configuration"
dotnet test Migrator.Tests/Migrator.Tests.csproj -c "$configuration" --no-build
rm -rf "$artifacts"
mkdir -p "$artifacts"
dotnet run --project Migrator.Cli -c "$configuration" --no-build -- lab validate --corpus ./corpus/stable/vertical-slice --out "$artifacts/contracts" --fail-on-planned
set +e

failures=0
run_suite() {
  local suite="$1" expected="$2" out="$artifacts/$suite"
  dotnet run --project Migrator.Cli -c "$configuration" --no-build -- lab run --suite "$suite" --corpus ./corpus/stable/vertical-slice --out "$out" --timeout-seconds "$timeout_seconds" --configuration "$configuration"
  local exit_code=$?
  python - "$out/lab-summary.json" "$expected" "$suite" <<'PY'
import json,sys
path,expected,suite=sys.argv[1],int(sys.argv[2]),sys.argv[3]
try: data=json.load(open(path,encoding='utf-8-sig'))
except Exception as e: print(f'{suite}: cannot read summary: {e}'); sys.exit(1)
projects=data.get('projects',[])
issues=[]
if len(projects)!=expected: issues.append(f'expected {expected} scenarios, got {len(projects)}')
for p in projects:
 if p.get('actualStatus')!=p.get('expectedStatus'):
  issues.append(f"{p.get('id')}: expected {p.get('expectedStatus')}, actual {p.get('actualStatus')}")
for issue in issues: print(f'{suite}: {issue}')
sys.exit(1 if issues else 0)
PY
  local summary_exit=$?
  if [[ $exit_code -ne 0 || $summary_exit -ne 0 ]]; then failures=1; fi
}

run_suite smoke 7
run_suite pr 18
run_suite nightly 30

dotnet run --project Migrator.Cli -c "$configuration" --no-build -- lab run --corpus ./corpus/stable/vertical-slice --feature WebDriverWait,CustomWait --out "$artifacts/feature-waits" --timeout-seconds "$timeout_seconds" --configuration "$configuration"
feature_exit=$?
python - "$artifacts/feature-waits/lab-summary.json" <<'PY'
import json,sys
x=json.load(open(sys.argv[1],encoding='utf-8-sig'))
actual=sorted(p['id'] for p in x['projects'])
expected=sorted(['p15-webdriverwait-visible','p16-wait-disappear-negative','p17-custom-wait-state'])
issues=[p for p in x['projects'] if p['actualStatus']!=p['expectedStatus']]
if actual!=expected or issues:
 print('feature-waits mismatch:',actual,issues)
 sys.exit(1)
PY
[[ $? -ne 0 || $feature_exit -ne 0 ]] && failures=1

for required in corpus/stable/vertical-slice/coverage-matrix.json docs/lab/STABLE_CORPUS_MATRIX.ru.md "$artifacts/smoke/lab-summary.html" "$artifacts/pr/lab-summary.html" "$artifacts/nightly/lab-summary.html"; do
  [[ -f "$required" ]] || { echo "Missing Block 6 artifact: $required"; failures=1; }
done

if [[ $failures -ne 0 ]]; then
  echo "Block 6 did not reach its final acceptance state; inspect $artifacts/nightly/lab-summary.md"
  exit 10
fi

echo "Block 6 passed: 30 READY stable scenarios, smoke/PR/nightly suites, feature selection, and expected negative contracts are verified."
