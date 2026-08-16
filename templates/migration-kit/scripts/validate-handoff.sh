#!/usr/bin/env bash
set -euo pipefail
if ! command -v pwsh >/dev/null 2>&1; then echo "PowerShell 7 (pwsh) is required." >&2; exit 127; fi
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# COMPLETE handoffs are validated against the recorded final-gate proof by the PowerShell contract.
exec pwsh -NoProfile -File "$SCRIPT_DIR/validate-handoff.ps1" "$@"
