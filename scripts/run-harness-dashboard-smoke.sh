#!/usr/bin/env sh
set -eu
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
if command -v pwsh >/dev/null 2>&1; then
  exec pwsh "$REPO_ROOT/scripts/run-harness-dashboard-smoke.ps1" "$@"
fi
if command -v powershell >/dev/null 2>&1; then
  exec powershell "$REPO_ROOT/scripts/run-harness-dashboard-smoke.ps1" "$@"
fi
echo "PowerShell Core (pwsh) or Windows PowerShell is required." >&2
exit 127
