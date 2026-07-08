#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PS_SCRIPT="$SCRIPT_DIR/repair-memory-jsonl.ps1"
if command -v pwsh >/dev/null 2>&1; then
  pwsh -NoProfile -ExecutionPolicy Bypass -File "$PS_SCRIPT" "$@"
elif command -v powershell >/dev/null 2>&1; then
  powershell -NoProfile -ExecutionPolicy Bypass -File "$PS_SCRIPT" "$@"
else
  echo "PowerShell executable was not found. Install PowerShell 7 (pwsh) or Windows PowerShell." >&2
  exit 127
fi
