#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.0.0-preview.1}"
PACKAGE_ID="${PACKAGE_ID:-SeleniumPlaywrightMigrator}"
CONFIGURATION="${CONFIGURATION:-Release}"
OUTPUT="${OUTPUT:-artifacts/nuget}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

mkdir -p "$ROOT/$OUTPUT"
BUILD_DATE_UTC="$(date -u +%Y-%m-%dT%H:%M:%SZ)"

dotnet pack "$ROOT/Migrator.Cli/Migrator.Cli.csproj" \
  -c "$CONFIGURATION" \
  -o "$ROOT/$OUTPUT" \
  /p:Version="$VERSION" \
  /p:MigratorDistribution=dotnet-tool \
  /p:MigratorBuildDateUtc="$BUILD_DATE_UTC"

echo "Package output: $ROOT/$OUTPUT"
test -f "$ROOT/$OUTPUT/$PACKAGE_ID.$VERSION.nupkg"
