#!/usr/bin/env sh
set -eu
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPO_ROOT=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
PS_SCRIPT="$REPO_ROOT/scripts/run-harness-dashboard-smoke.ps1"

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

cat >&2 <<EOF2
PowerShell 7 (pwsh) is required to run run-harness-dashboard-smoke.ps1 from shell on macOS/Linux/WSL.
Install PowerShell 7: https://learn.microsoft.com/powershell/scripting/install/installing-powershell
EOF2
exit 127
