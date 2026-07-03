#!/usr/bin/env bash
set -euo pipefail

VERSION="${1:-0.0.0-preview.1}"
PACKAGE_ID="${PACKAGE_ID:-SeleniumPlaywrightMigrator}"
PACKAGE_DIRECTORY="${PACKAGE_DIRECTORY:-artifacts/nuget}"
INPUT="${INPUT:-Migrator.Tests/TestFiles}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
SOURCE="$PACKAGE_DIRECTORY"
INPUT_PATH="$INPUT"

if [[ "$SOURCE" != /* ]]; then
  SOURCE="$ROOT/$SOURCE"
fi

if [[ "$INPUT_PATH" != /* ]]; then
  INPUT_PATH="$ROOT/$INPUT_PATH"
fi

if [[ ! -d "$SOURCE" ]]; then
  echo "Package source not found: $SOURCE. Run scripts/pack-tool.sh first." >&2
  exit 1
fi

if [[ ! -e "$INPUT_PATH" ]]; then
  echo "Smoke input path not found: $INPUT_PATH" >&2
  exit 1
fi

TEMP_DIR="$(mktemp -d)"
cleanup() {
  rm -rf "$TEMP_DIR"
}
trap cleanup EXIT

pushd "$TEMP_DIR" >/dev/null

dotnet new tool-manifest
dotnet tool install "$PACKAGE_ID" --version "$VERSION" --add-source "$SOURCE" --ignore-failed-sources

dotnet tool run selenium-pw-migrator -- --help

doctor_out="$TEMP_DIR/doctor"
dotnet tool run selenium-pw-migrator -- --mode doctor --input "$INPUT_PATH" --out "$doctor_out" --format both

if [[ ! -f "$doctor_out/doctor-report.md" ]]; then
  echo "Doctor smoke did not produce expected report: $doctor_out/doctor-report.md" >&2
  exit 1
fi

popd >/dev/null

echo "Local dotnet-tool smoke passed for $PACKAGE_ID $VERSION"
