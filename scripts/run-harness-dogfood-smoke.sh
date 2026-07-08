#!/usr/bin/env sh
set -eu
SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
PS_SCRIPT="$SCRIPT_DIR/run-harness-dogfood-smoke.ps1"

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
PowerShell 7 (pwsh) is required to run run-harness-dogfood-smoke.ps1 from shell on macOS/Linux/WSL.
Install PowerShell 7: https://learn.microsoft.com/powershell/scripting/install/installing-powershell
EOF2
exit 127
