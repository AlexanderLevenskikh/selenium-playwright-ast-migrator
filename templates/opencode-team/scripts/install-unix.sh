#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SOURCE="$SCRIPT_DIR/../global/.config/opencode"
TARGET="$HOME/.config/opencode"

echo "Installing OpenCode agent team template..."
echo "Source: $SOURCE"
echo "Target: $TARGET"

mkdir -p "$TARGET"
cp -R "$SOURCE"/. "$TARGET"/

echo
echo "Done."
echo
echo "Next:"
echo "1. Copy project-template/AGENTS.md to the root of your repository."
echo "2. In opencode, try:"
echo "   /supervised-task inspect the current repository and report the safest first task"
