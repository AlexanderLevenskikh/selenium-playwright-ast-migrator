#!/usr/bin/env bash
set -euo pipefail

root="${1:-.}"
cd "$root"

if ! command -v pwsh >/dev/null 2>&1; then
  echo "pwsh is required to parse PowerShell scripts." >&2
  exit 1
fi

pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/validate-scripts.ps1 -Root . -SkipShell

mapfile -d '' shell_scripts < <(
  find scripts templates .github/workflows \
    -type f -name '*.sh' \
    -not -path '*/bin/*' \
    -not -path '*/obj/*' \
    -not -path '*/artifacts/*' \
    -not -path '*/.dogfood/*' \
    -not -path '*/npm/native/*' \
    -not -path '*/TestResults/*' \
    -not -path '*/playwright-report/*' \
    -print0
)

printf 'Shell scripts: %s\n' "${#shell_scripts[@]}"
for script in "${shell_scripts[@]}"; do
  printf 'SH   %s\n' "$script"
  bash -n -- "$script"
done

printf 'SCRIPT_VALIDATE_SH_PASS: checked %s shell script(s).\n' "${#shell_scripts[@]}"
