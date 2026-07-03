#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: scripts/push-tool.sh <source> [version]" >&2
  exit 2
fi

SOURCE="$1"
VERSION="${2:-0.0.0}"
PACKAGE_ID="${PACKAGE_ID:-SeleniumPlaywrightMigrator}"
API_KEY="${NUGET_API_KEY:-}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGE="$ROOT/artifacts/nuget/$PACKAGE_ID.$VERSION.nupkg"

if [[ ! -f "$PACKAGE" ]]; then
  echo "Package not found: $PACKAGE. Run scripts/pack-tool.sh first." >&2
  exit 1
fi

if [[ -n "$API_KEY" ]]; then
  dotnet nuget push "$PACKAGE" --source "$SOURCE" --api-key "$API_KEY" --skip-duplicate
else
  dotnet nuget push "$PACKAGE" --source "$SOURCE" --skip-duplicate
fi
