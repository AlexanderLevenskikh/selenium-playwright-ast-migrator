#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.0.0-preview.1}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

if ! command -v pwsh >/dev/null 2>&1; then
  echo "pwsh is required to run scripts/package-standalone.ps1" >&2
  exit 2
fi

pwsh -NoProfile -ExecutionPolicy Bypass -File "$ROOT/scripts/package-standalone.ps1" \
  -Version "$VERSION"
