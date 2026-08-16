#!/usr/bin/env bash
set -euo pipefail
if ! command -v pwsh >/dev/null 2>&1; then echo "PowerShell 7 (pwsh) is required." >&2; exit 127; fi
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# Forward every contract argument to the canonical PowerShell implementation.
# Durable autonomy-state ledger/recovery and proof-bound completion are implemented there for every platform.
exec pwsh -NoProfile -File "$SCRIPT_DIR/update-autonomy-state.ps1" "$@"
