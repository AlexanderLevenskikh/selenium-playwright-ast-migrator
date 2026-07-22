#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="."
WORKSPACE="migration"
PERMISSION_PROFILE="LowNoise"
FORCE="false"
DRY_RUN="false"
SKIP_PROJECT_AGENTS="false"

usage() {
  cat <<USAGE
Usage: apply-opencode-project-config.sh [options]

Copies the bundled OpenCode team from migration/opencode-team into the repository root:
  opencode.jsonc
  .opencode/agents/*
  .opencode/commands/*

Options:
  --repo-root PATH              Repository root to install into. Default: .
  --workspace PATH              Migration workspace. Default: migration
  --permission-profile NAME     LowNoise or TrustedProject. Default: LowNoise
  --force                       Overwrite AGENTS.md too, after backup.
  --dry-run                     Print actions without writing.
  --skip-project-agents         Do not copy project-template/AGENTS.md.
  -h, --help                    Show this help.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --repo-root)
      REPO_ROOT="${2:?--repo-root requires a path}"
      shift 2
      ;;
    --workspace)
      WORKSPACE="${2:?--workspace requires a path}"
      shift 2
      ;;
    --permission-profile)
      PERMISSION_PROFILE="${2:?--permission-profile requires LowNoise or TrustedProject}"
      shift 2
      ;;
    --force)
      FORCE="true"
      shift
      ;;
    --dry-run)
      DRY_RUN="true"
      shift
      ;;
    --skip-project-agents)
      SKIP_PROJECT_AGENTS="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if [[ "$PERMISSION_PROFILE" != "LowNoise" && "$PERMISSION_PROFILE" != "TrustedProject" ]]; then
  echo "--permission-profile must be LowNoise or TrustedProject" >&2
  exit 2
fi

abs_path() {
  local path="$1"
  if [[ "$path" = /* ]]; then
    (cd "$path" 2>/dev/null && pwd -P) || { mkdir -p "$(dirname "$path")"; cd "$(dirname "$path")" && printf '%s/%s\n' "$(pwd -P)" "$(basename "$path")"; }
  else
    (cd "$path" 2>/dev/null && pwd -P) || { mkdir -p "$(dirname "$path")"; cd "$(dirname "$path")" && printf '%s/%s\n' "$(pwd -P)" "$(basename "$path")"; }
  fi
}

copy_file_safe() {
  local source_file="$1"
  local destination_file="$2"
  local overwrite="$3"

  if [[ -e "$destination_file" && "$overwrite" != "true" ]]; then
    echo "Keeping existing file: $destination_file"
    return
  fi

  if [[ "$DRY_RUN" == "true" ]]; then
    echo "DRY RUN: copy file $source_file to $destination_file"
    return
  fi

  mkdir -p "$(dirname "$destination_file")"
  cp -f "$source_file" "$destination_file"
}

backup_path_if_exists() {
  local path_to_backup="$1"
  local backup_root="$2"
  if [[ ! -e "$path_to_backup" ]]; then
    return
  fi

  if [[ "$DRY_RUN" == "true" ]]; then
    echo "DRY RUN: backup $path_to_backup to $backup_root"
    return
  fi

  mkdir -p "$backup_root"
  local destination="$backup_root/$(basename "$path_to_backup")"
  if [[ -e "$destination" ]]; then
    destination="$destination.$(date +%s%N)"
  fi
  cp -R "$path_to_backup" "$destination"
  echo "Backed up existing $(basename "$path_to_backup") to $destination"
}

copy_directory_contents() {
  local source_dir="$1"
  local destination_dir="$2"
  if [[ "$DRY_RUN" == "true" ]]; then
    echo "DRY RUN: copy directory contents from $source_dir to $destination_dir"
    return
  fi

  if [[ ! -d "$source_dir" ]]; then
    echo "Source directory does not exist: $source_dir" >&2
    exit 2
  fi

  mkdir -p "$destination_dir"
  cp -R "$source_dir"/. "$destination_dir"/
}

REPO_ROOT_FULL="$(abs_path "$REPO_ROOT")"
WORKSPACE_FULL="$(abs_path "$WORKSPACE")"
SOURCE_ROOT="$WORKSPACE_FULL/opencode-team/global/.config/opencode"
PROJECT_AGENTS_TEMPLATE="$WORKSPACE_FULL/opencode-team/project-template/AGENTS.md"

if [[ ! -d "$SOURCE_ROOT" ]]; then
  echo "OpenCode team template was not found at '$SOURCE_ROOT'. Run 'selenium-pw-migrator kit bootstrap-opencode --workspace $WORKSPACE --opencode-install none' first, or install the workspace with team templates." >&2
  exit 2
fi

CONFIG_FILE_NAME="opencode.jsonc"
if [[ "$PERMISSION_PROFILE" == "TrustedProject" ]]; then
  CONFIG_FILE_NAME="opencode.trusted-project.jsonc"
fi
CONFIG_SOURCE="$SOURCE_ROOT/$CONFIG_FILE_NAME"
if [[ ! -f "$CONFIG_SOURCE" ]]; then
  echo "OpenCode config profile was not found: $CONFIG_SOURCE" >&2
  exit 2
fi

TARGET_OPENCODE="$REPO_ROOT_FULL/.opencode"
TARGET_AGENTS="$TARGET_OPENCODE/agents"
TARGET_COMMANDS="$TARGET_OPENCODE/commands"
BACKUP_ROOT="$WORKSPACE_FULL/.migration-kit/opencode-backups/$(date +%Y%m%d-%H%M%S)"

echo "Applying OpenCode project config to repository root..."
echo "Repo root:          $REPO_ROOT_FULL"
echo "Workspace:          $WORKSPACE_FULL"
echo "Source:             $SOURCE_ROOT"
echo "Permission profile: $PERMISSION_PROFILE"
echo "Force overwrite:    $FORCE"
echo "Dry run:            $DRY_RUN"
echo

backup_path_if_exists "$REPO_ROOT_FULL/opencode.jsonc" "$BACKUP_ROOT"
backup_path_if_exists "$TARGET_AGENTS" "$BACKUP_ROOT"
backup_path_if_exists "$TARGET_COMMANDS" "$BACKUP_ROOT"

copy_file_safe "$CONFIG_SOURCE" "$REPO_ROOT_FULL/opencode.jsonc" "true"
copy_directory_contents "$SOURCE_ROOT/agents" "$TARGET_AGENTS"
copy_directory_contents "$SOURCE_ROOT/commands" "$TARGET_COMMANDS"

if [[ "$SKIP_PROJECT_AGENTS" != "true" && -f "$PROJECT_AGENTS_TEMPLATE" ]]; then
  copy_file_safe "$PROJECT_AGENTS_TEMPLATE" "$REPO_ROOT_FULL/AGENTS.md" "$FORCE"
elif [[ "$SKIP_PROJECT_AGENTS" == "true" ]]; then
  echo "Skipped root AGENTS.md installation."
fi

echo
echo "OPENCODE_PROJECT_CONFIG_APPLIED"
echo "Installed:"
echo "  $REPO_ROOT_FULL/opencode.jsonc"
echo "  $TARGET_AGENTS"
echo "  $TARGET_COMMANDS"
echo
echo "Next: open this repository folder in OpenCode and run /supervised-task."
