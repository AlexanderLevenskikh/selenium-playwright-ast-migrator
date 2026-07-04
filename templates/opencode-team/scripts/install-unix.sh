#!/usr/bin/env bash
set -euo pipefail

MODE="ProjectLocal"
TARGET=""

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
    --help|-h)
      echo "Usage: install-unix.sh [--mode ProjectLocal|Global] [--target PATH]"
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
echo

if [[ "$MODE" == "Global" ]]; then
  echo "WARNING: Global mode affects all OpenCode sessions for this user. Use it only if you want artifact-only migration behavior globally." >&2
else
  echo "ProjectLocal mode is recommended. Start OpenCode for migration sessions with this config only."
fi

mkdir -p "$TARGET"
cp -R "$SOURCE"/. "$TARGET"/

echo
echo "Done."
echo
echo "Next:"
echo "1. Copy project-template/AGENTS.md to the root of your repository if needed."
if [[ "$MODE" == "ProjectLocal" ]]; then
  echo "2. Use this config only for migration sessions, for example:"
  echo "   OPENCODE_CONFIG=\"$TARGET/opencode.jsonc\" opencode"
else
  echo "2. In opencode, try:"
  echo "   /supervised-task inspect the current repository and report the safest first task"
fi
