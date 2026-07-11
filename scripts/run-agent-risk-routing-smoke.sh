#!/usr/bin/env bash
set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
# Install PowerShell 7: https://learn.microsoft.com/powershell/scripting/install/installing-powershell
# Windows fallback: run scripts/run-agent-risk-routing-smoke.ps1 directly from Windows PowerShell.
if command -v pwsh >/dev/null 2>&1; then
  exec pwsh -NoProfile -File "$SCRIPT_DIR/run-agent-risk-routing-smoke.ps1" -Root "$ROOT" "$@"
fi
echo "PowerShell 7 is required on macOS/Linux/WSL. Install PowerShell 7: https://learn.microsoft.com/powershell/scripting/install/installing-powershell" >&2
exit 127
