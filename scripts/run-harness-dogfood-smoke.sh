#!/usr/bin/env sh
set -eu
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
if command -v pwsh >/dev/null 2>&1; then
  exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$SCRIPT_DIR/run-harness-dogfood-smoke.ps1" "$@"
fi
if command -v powershell >/dev/null 2>&1; then
  exec powershell -NoProfile -ExecutionPolicy Bypass -File "$SCRIPT_DIR/run-harness-dogfood-smoke.ps1" "$@"
fi
echo "PowerShell Core (pwsh) or Windows PowerShell is required." >&2
exit 127
