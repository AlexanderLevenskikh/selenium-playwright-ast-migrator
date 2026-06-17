#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.6.0-preview.1}"
PACKAGE_ID="${PACKAGE_ID:-SeleniumPlaywrightAstMigrator}"
CONFIGURATION="${CONFIGURATION:-Release}"
OUTPUT="${OUTPUT:-artifacts/nuget}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

mkdir -p "$ROOT/$OUTPUT"

dotnet pack "$ROOT/Migrator.Cli/Migrator.Cli.csproj" \
  -c "$CONFIGURATION" \
  -o "$ROOT/$OUTPUT" \
  /p:PackageId="$PACKAGE_ID" \
  /p:Version="$VERSION"

echo "Package output: $ROOT/$OUTPUT"
