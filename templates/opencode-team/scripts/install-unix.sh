#!/usr/bin/env bash
set -euo pipefail

# TrustedProject example:
#   --permission-profile TrustedProject
#   install-unix.sh --mode ProjectLocal --permission-profile TrustedProject

MODE="ProjectLocal"
TARGET=""
PERMISSION_PROFILE="LowNoise"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --mode)
      MODE="${2:?--mode requires ProjectLocal or Global}"
      shift 2
      ;;
    --target)
      TARGET="${2:?--target requires a path}"
      shift 2
      ;;
    --permission-profile)
      PERMISSION_PROFILE="${2:?--permission-profile requires LowNoise or TrustedProject}"
      shift 2
      ;;
    --help|-h)
      echo "Usage: install-unix.sh [--mode ProjectLocal|Global] [--target PATH] [--permission-profile LowNoise|TrustedProject]"
      exit 0
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 2
      ;;
  esac
done

if [[ "$MODE" != "ProjectLocal" && "$MODE" != "Global" ]]; then
  echo "--mode must be ProjectLocal or Global" >&2
  exit 2
fi

if [[ "$PERMISSION_PROFILE" != "LowNoise" && "$PERMISSION_PROFILE" != "TrustedProject" ]]; then
  echo "--permission-profile must be LowNoise or TrustedProject" >&2
  exit 2
fi

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE="$SCRIPT_DIR/../global/.config/opencode"
if [[ -z "$TARGET" ]]; then
  if [[ "$MODE" == "Global" ]]; then
    TARGET="$HOME/.config/opencode"
  else
    TARGET="$PWD/.opencode-migrator"
  fi
fi

echo "Installing OpenCode agent team template..."
echo "Mode:   $MODE"
echo "Source: $SOURCE"
echo "Target: $TARGET"
echo "Permission profile: $PERMISSION_PROFILE"
echo

if [[ "$MODE" == "Global" ]]; then
  echo "WARNING: Global mode affects all OpenCode sessions for this user. Use it only if you want artifact-only migration behavior globally." >&2
else
  echo "ProjectLocal mode is recommended. Start OpenCode for migration sessions with this config only."
fi

mkdir -p "$TARGET"
cp -R "$SOURCE"/. "$TARGET"/
if [[ "$PERMISSION_PROFILE" == "TrustedProject" ]]; then
  cp "$SOURCE/opencode.trusted-project.jsonc" "$TARGET/opencode.jsonc"
else
  cp "$SOURCE/opencode.jsonc" "$TARGET/opencode.jsonc"
fi

echo
echo "Done."
echo
if [[ "$PERMISSION_PROFILE" == "TrustedProject" ]]; then
  echo "TrustedProject profile disables routine approval prompts inside this project; external directories remain blocked."
  echo
fi

echo "Next:"
echo "1. For repository-root config, prefer: selenium-pw-migrator kit bootstrap-opencode --opencode-install auto (it copies AGENTS.md when missing)."
if [[ "$MODE" == "ProjectLocal" ]]; then
  echo "2. Use this config only for migration sessions, for example:"
  echo "   OPENCODE_CONFIG=\"$TARGET/opencode.jsonc\" opencode"
else
  echo "2. In opencode, try:"
  echo "   /supervised-task"
fi
