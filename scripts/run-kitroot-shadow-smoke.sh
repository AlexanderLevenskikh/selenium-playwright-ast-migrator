#!/usr/bin/env bash
set -euo pipefail

WORK_ROOT="${1:-.kitroot-smoke}"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PRODUCT_ROOT="$REPO_ROOT/$WORK_ROOT/product-repo"
LOG_DIR="$REPO_ROOT/$WORK_ROOT/logs"
LOG_PATH="$LOG_DIR/kitroot-shadow-smoke.log"

rm -rf "$REPO_ROOT/$WORK_ROOT"
mkdir -p "$PRODUCT_ROOT/templates/migration-kit" "$PRODUCT_ROOT/LegacyTests" "$LOG_DIR"

cat > "$PRODUCT_ROOT/templates/migration-kit/README.md" <<'EOF'
# PRODUCT_SHADOW_TEMPLATE_DO_NOT_USE

This fixture must never be copied into the installed migration workspace.
EOF
printf '%s
' '// smoke source placeholder' > "$PRODUCT_ROOT/LegacyTests/Smoke.cs"

PROJECT_PATH="$REPO_ROOT/Migrator.Cli/Migrator.Cli.csproj"
(
  cd "$PRODUCT_ROOT"
  dotnet run --project "$PROJECT_PATH" -- kit bootstrap-opencode --workspace migration --source ./LegacyTests --opencode-install none
) 2>&1 | tee "$LOG_PATH"

if grep -Fq "Kit root:     $PRODUCT_ROOT" "$LOG_PATH"; then
  echo "bootstrap-opencode used product repo templates as Kit root. This would shadow bundled templates." >&2
  exit 1
fi

required=(
  "AGENT_CONTRACT.md"
  "harness/README.md"
  "state/harness-policy.json"
  "scripts/check-harness-policy.ps1"
  "scripts/check-final-gate.ps1"
  "scripts/validate-run-artifacts.ps1"
  "opencode-team/global/.config/opencode/opencode.jsonc"
)

for relative in "${required[@]}"; do
  if [[ ! -e "$PRODUCT_ROOT/migration/$relative" ]]; then
    echo "Missing required bundled kit file: $relative" >&2
    exit 1
  fi
done

if grep -Fq "PRODUCT_SHADOW_TEMPLATE_DO_NOT_USE" "$PRODUCT_ROOT/migration/README.md"; then
  echo "Product shadow template was copied into migration workspace." >&2
  exit 1
fi

echo "KITROOT_SHADOW_SMOKE_PASS"
echo "Workspace: $PRODUCT_ROOT/migration"
echo "Log:       $LOG_PATH"
