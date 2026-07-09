#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if command -v pwsh >/dev/null 2>&1; then
  exec pwsh -NoProfile -ExecutionPolicy Bypass -File "${SCRIPT_DIR}/complete-claim.ps1" "$@"
fi
exec powershell -NoProfile -ExecutionPolicy Bypass -File "${SCRIPT_DIR}/complete-claim.ps1" "$@"
