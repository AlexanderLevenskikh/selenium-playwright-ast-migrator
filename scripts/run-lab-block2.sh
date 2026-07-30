#!/usr/bin/env bash
set -euo pipefail

configuration="${1:-Release}"
port="${MIGRATOR_LAB_PORT:-0}"
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
corpus="$root/corpus/stable/vertical-slice"
artifacts="$root/artifacts/lab/block-02"
ready_file="$artifacts/lab-app-ready.json"
cli_dll="$root/Migrator.Cli/bin/$configuration/net10.0/Migrator.Cli.dll"

mkdir -p "$artifacts"
rm -f "$ready_file" "$artifacts/lab-app.stdout.log" "$artifacts/lab-app.stderr.log"

dotnet build "$root/Migrator.sln" -c "$configuration" --nologo

dotnet "$cli_dll" lab app serve --port "$port" --ready-file "$ready_file" \
  >"$artifacts/lab-app.stdout.log" 2>"$artifacts/lab-app.stderr.log" &
server_pid=$!
trap 'kill "$server_pid" 2>/dev/null || true' EXIT

for _ in $(seq 1 150); do
  [[ -f "$ready_file" ]] && break
  if ! kill -0 "$server_pid" 2>/dev/null; then
    cat "$artifacts/lab-app.stderr.log" >&2 || true
    exit 1
  fi
  sleep 0.2
done
[[ -f "$ready_file" ]] || { echo "Timed out waiting for LabApp." >&2; exit 1; }

base_url="$(python3 -c 'import json,sys; print(json.load(open(sys.argv[1]))["baseUrl"])' "$ready_file")"
export MIGRATOR_LAB_APP_URL="$base_url"
curl --fail --silent --show-error "${base_url}health" >/dev/null

projects=(
  "p01-basic-id-login/Scenario.csproj"
  "p04-findelements-count-text/Scenario.csproj"
  "p09-helper-extension-mapping/Scenario.csproj"
  "p15-webdriverwait-visible/Scenario.csproj"
  "p23-cpm-isolation/Scenario.csproj"
  "p24a-transitive-warning-isolated/Tests/Tests.csproj"
  "p26-jsexecutor-unsupported/Scenario.csproj"
)

for project in "${projects[@]}"; do
  echo "Testing fixture: $project"
  dotnet test "$corpus/$project" -c "$configuration" --nologo
done

dotnet "$cli_dll" lab validate \
  --corpus "$corpus" \
  --out "$artifacts/contracts" \
  --fail-on-planned

echo "Block 2 passed: 7 ready fixtures, LabApp health OK, all source tests passed."
