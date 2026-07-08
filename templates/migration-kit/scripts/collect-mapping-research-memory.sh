#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PS_SCRIPT="$SCRIPT_DIR/collect-mapping-research-memory.ps1"

run_powershell_script() {
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

  script_name="$(basename "$PS_SCRIPT")"
  cat >&2 <<EOF2
PowerShell 7 (pwsh) is required to run ${script_name} from bash on macOS/Linux/WSL.
Install PowerShell 7: https://learn.microsoft.com/powershell/scripting/install/installing-powershell
EOF2
  exit 127
}

run_powershell_script "$@"
