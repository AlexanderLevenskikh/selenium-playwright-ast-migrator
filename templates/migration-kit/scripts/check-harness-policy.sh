#!/usr/bin/env bash
set -euo pipefail
if ! command -v pwsh >/dev/null 2>&1; then echo "PowerShell 7 (pwsh) is required." >&2; exit 127; fi
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# PowerShell implementation validates residual-identity liveness policy on every platform.
exec pwsh -NoProfile -File "$SCRIPT_DIR/check-harness-policy.ps1" "$@"
