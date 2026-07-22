#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if ! command -v pwsh >/dev/null 2>&1; then
  echo "PowerShell 7 (pwsh) is required." >&2
  exit 2
fi
exec pwsh -NoProfile -File "$SCRIPT_DIR/run-standard-migration-smoke.ps1" "$@"
