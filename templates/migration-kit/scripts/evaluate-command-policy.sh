#!/usr/bin/env bash
set -euo pipefail

# Stable output markers used by tests and by agent prompts:
# COMMAND_POLICY_SAFE
# COMMAND_POLICY_REVIEW_REQUIRED
# COMMAND_POLICY_FORBIDDEN

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ps_script="$script_dir/evaluate-command-policy.ps1"

if command -v pwsh >/dev/null 2>&1; then
  exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "$@"
elif command -v powershell.exe >/dev/null 2>&1; then
  exec powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "$@"
elif command -v powershell >/dev/null 2>&1; then
  exec powershell -NoProfile -ExecutionPolicy Bypass -File "$ps_script" "$@"
else
  echo "PowerShell was not found; cannot run evaluate-command-policy.ps1" >&2
  exit 127
fi
