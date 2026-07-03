#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "Usage: scripts/extract-release-notes.sh <version> <output-path>" >&2
  exit 2
fi

VERSION="$1"
OUT="$2"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXPLICIT_NOTES="$ROOT/docs/release-notes/v$VERSION.md"
CHANGELOG="$ROOT/CHANGELOG.md"

mkdir -p "$(dirname "$OUT")"

if [[ -f "$EXPLICIT_NOTES" ]]; then
  cp "$EXPLICIT_NOTES" "$OUT"
  echo "Using release notes from docs/release-notes/v$VERSION.md"
  exit 0
fi

if [[ ! -f "$CHANGELOG" ]]; then
  cat > "$OUT" <<MESSAGE
# Selenium Playwright Migrator $VERSION

No CHANGELOG.md was found in the repository.
MESSAGE
  echo "CHANGELOG.md was not found; wrote fallback release notes."
  exit 0
fi

awk -v version="$VERSION" '
  BEGIN {
    in_section = 0;
    found = 0;
  }
  /^##[[:space:]]+/ {
    if (in_section == 1) {
      exit;
    }

    line = $0;
    if (line ~ "^##[[:space:]]+\\[" version "\\]" || line ~ "^##[[:space:]]+" version "([[:space:]]|$)") {
      in_section = 1;
      found = 1;
      next;
    }
  }
  in_section == 1 {
    print;
  }
  END {
    if (found == 0) {
      exit 42;
    }
  }
' "$CHANGELOG" > "$OUT.tmp" || status=$?

status="${status:-0}"
if [[ "$status" != "0" ]]; then
  cat > "$OUT" <<MESSAGE
# Selenium Playwright Migrator $VERSION

No dedicated release notes file or matching CHANGELOG.md section was found for this version.
MESSAGE
  rm -f "$OUT.tmp"
  echo "No release notes section found for $VERSION; wrote fallback release notes."
  exit 0
fi

{
  echo "# Selenium Playwright Migrator $VERSION"
  echo
  cat "$OUT.tmp"
} > "$OUT"
rm -f "$OUT.tmp"
echo "Extracted release notes from CHANGELOG.md for $VERSION"
