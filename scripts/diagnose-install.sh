#!/usr/bin/env bash
set -u

command_name="selenium-pw-migrator"
skip_package_managers="false"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --command-name)
      command_name="${2:-}"
      shift 2
      ;;
    --skip-package-managers)
      skip_package_managers="true"
      shift
      ;;
    -h|--help)
      cat <<HELP
Diagnose which selenium-pw-migrator installation the current shell resolves.

The migrator can be installed as a standalone CLI, npm wrapper, dotnet global tool,
or dotnet local tool. Do not start diagnostics with dotnet tool list only: first
inspect PATH resolution and then inspect package-manager state.

Usage:
  scripts/diagnose-install.sh [--command-name selenium-pw-migrator] [--skip-package-managers]
HELP
      exit 0
      ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2
      ;;
  esac
done

section() {
  printf '\n== %s ==\n' "$1"
}

best_effort() {
  local label="$1"
  shift
  printf '\n> %s\n' "$label"
  "$@" || echo "WARN: command failed: $label" >&2
}

echo "Selenium Playwright Migrator installation diagnostics"
echo "Command: $command_name"
echo "Rule: inspect actual PATH resolution before package-manager lists."

section "Resolved commands"
best_effort "command -v $command_name" command -v "$command_name"
best_effort "which -a $command_name" which -a "$command_name"

section "Version metadata"
best_effort "$command_name --version" "$command_name" --version

if [[ "$skip_package_managers" != "true" ]]; then
  section "dotnet tool state"
  best_effort "dotnet tool list --global" dotnet tool list --global
  best_effort "dotnet tool list --local" dotnet tool list --local

  section "npm wrapper state"
  best_effort "npm list -g selenium-pw-migrator --depth=0" npm list -g selenium-pw-migrator --depth=0
  best_effort "npm config get registry" npm config get registry
  best_effort "npm config get prefix" npm config get prefix
  best_effort "npm config get selenium-pw-migrator-base-url" npm config get selenium-pw-migrator-base-url
fi

section "How to read this output"
echo "The first resolved command is what this shell will run."
echo "Use --version metadata to identify the distribution: standalone, npm wrapper payload, or dotnet tool."
echo "If multiple commands exist, fix PATH priority before reinstalling anything."
