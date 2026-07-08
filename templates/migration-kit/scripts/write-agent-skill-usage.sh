#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PS_SCRIPT="$SCRIPT_DIR/write-agent-skill-usage.ps1"

if command -v pwsh >/dev/null 2>&1; then
  exec pwsh -NoProfile -File "$PS_SCRIPT" "$@"
fi

if command -v powershell >/dev/null 2>&1; then
  exec powershell -NoProfile -ExecutionPolicy Bypass -File "$PS_SCRIPT" "$@"
fi

echo "PowerShell 7 (pwsh) is required to run write-agent-skill-usage.ps1 from bash." >&2
exit 127
