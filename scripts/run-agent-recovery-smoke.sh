#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PS_SCRIPT="$SCRIPT_DIR/run-agent-recovery-smoke.ps1"

if command -v pwsh >/dev/null 2>&1; then
  exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$PS_SCRIPT" "$@"
fi

case "$(uname -s 2>/dev/null || echo unknown)" in
  MINGW*|MSYS*|CYGWIN*|Windows_NT)
    if command -v powershell >/dev/null 2>&1; then
      exec powershell -NoProfile -ExecutionPolicy Bypass -File "$PS_SCRIPT" "$@"
    fi
    ;;
esac

cat >&2 <<'MESSAGE'
PowerShell was not found.
Install PowerShell 7: https://learn.microsoft.com/powershell/scripting/install/installing-powershell
On macOS/Linux/WSL the executable must be available as `pwsh`.
On MINGW/MSYS/CYGWIN/Windows_NT, Windows PowerShell is accepted as a compatibility fallback.
MESSAGE
exit 127
