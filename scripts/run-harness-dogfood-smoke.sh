#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$script_dir/run-harness-dogfood-smoke.ps1" "$@"
