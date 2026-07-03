#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: scripts/push-tool.sh <source> [version]" >&2
  exit 2
fi

SOURCE="$1"
VERSION="${2:-0.0.0-preview.1}"
PACKAGE_ID="${PACKAGE_ID:-SeleniumPlaywrightMigrator}"
API_KEY="${NUGET_API_KEY:-}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PACKAGE="$ROOT/artifacts/nuget/$PACKAGE_ID.$VERSION.nupkg"

if [[ ! -f "$PACKAGE" ]]; then
  echo "Package not found: $PACKAGE. Run scripts/pack-tool.sh first." >&2
  exit 1
fi

is_nuget_org_source() {
  case "$SOURCE" in
    *nuget.org*) return 0 ;;
    *) return 1 ;;
  esac
}

if [[ -z "$API_KEY" ]]; then
  if is_nuget_org_source; then
    cat >&2 <<'MESSAGE'
NUGET_API_KEY is required to publish to nuget.org.
For GitHub Actions Trusted Publishing, run NuGet/login@v1 before this script and pass:
  NUGET_API_KEY: ${{ steps.nuget-login.outputs.NUGET_API_KEY }}
For classic API key publishing, set the NUGET_API_KEY environment variable.
MESSAGE
    exit 1
  fi

  echo "Publishing $PACKAGE to $SOURCE without an explicit API key..."
  dotnet nuget push "$PACKAGE" --source "$SOURCE" --skip-duplicate
  exit $?
fi

echo "Publishing $PACKAGE to $SOURCE..."
dotnet nuget push "$PACKAGE" --source "$SOURCE" --api-key "$API_KEY" --skip-duplicate
