#!/usr/bin/env bash
set -euo pipefail

workspace="migration"
source_path="<SOURCE_SELENIUM_PROJECT_PATH>"
target_path="<TARGET_PROJECT_OR_OUTPUT_PATH>"
config_path="migration/profiles/adapter-config.json"
output_path="migration/runs/run-001"
tool_command="selenium-pw-migrator"
mode="init"
extra_args=()

usage() {
  cat <<'USAGE'
Usage:
  scripts/install-migration-kit.sh [options]

This is a thin cross-platform wrapper over:
  selenium-pw-migrator kit init/update

Options:
  --workspace <path>        Migration workspace root. Default: migration
  --source <path>           Source Selenium tests/project path.
  --target-path <path>      Target project/output path metadata.
  --config <path>           Adapter config path. Default: migration/profiles/adapter-config.json
  --out <path>              Default run output path. Default: migration/runs/run-001
  --tool-command <cmd>      Migrator command/binary. Default: selenium-pw-migrator
  --update                  Run kit update instead of init.
  --backup                  Snapshot existing workspace before update/init.
  --force                   Overwrite kit-owned files.
  --with-team               Install optional OpenCode team templates.
  --with-loop-library       Install optional reusable loop library.
  --no-codex-files          Do not install migration/codex files.
  --no-root-agent-files     Do not copy .agent-loops into project root.
  -h, --help                Show help.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --workspace) workspace="$2"; shift 2 ;;
    --source) source_path="$2"; shift 2 ;;
    --target-path) target_path="$2"; shift 2 ;;
    --config) config_path="$2"; shift 2 ;;
    --out|--output) output_path="$2"; shift 2 ;;
    --tool-command) tool_command="$2"; shift 2 ;;
    --update) mode="update"; shift ;;
    --backup|--force|--with-team|--with-loop-library|--no-codex-files|--no-root-agent-files)
      extra_args+=("$1"); shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage; exit 2 ;;
  esac
done

exec "$tool_command" kit "$mode" \
  --workspace "$workspace" \
  --source "$source_path" \
  --target-path "$target_path" \
  --config "$config_path" \
  --out "$output_path" \
  --tool-command "$tool_command" \
  "${extra_args[@]}"
