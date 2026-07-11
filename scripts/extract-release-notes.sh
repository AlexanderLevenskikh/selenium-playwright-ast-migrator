#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 || $# -gt 3 ]]; then
  echo "Usage: scripts/extract-release-notes.sh <version> <output-path> [--allow-fallback]" >&2
  exit 2
fi

VERSION="$1"
OUT="$2"
ALLOW_FALLBACK="${3:-}"
if [[ -n "$ALLOW_FALLBACK" && "$ALLOW_FALLBACK" != "--allow-fallback" ]]; then
  echo "Unknown option: $ALLOW_FALLBACK" >&2
  exit 2
fi
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
EXPLICIT_NOTES="$ROOT/docs/release-notes/v$VERSION.md"
CHANGELOG="$ROOT/CHANGELOG.md"

mkdir -p "$(dirname "$OUT")"

write_fallback() {
  local reason="$1"
  cat > "$OUT" <<MESSAGE
# Selenium Playwright Migrator $VERSION

$reason
MESSAGE
}

if [[ -f "$EXPLICIT_NOTES" ]]; then
  cp "$EXPLICIT_NOTES" "$OUT"
  echo "Using release notes from docs/release-notes/v$VERSION.md"
  exit 0
fi

if [[ ! -f "$CHANGELOG" ]]; then
  if [[ "$ALLOW_FALLBACK" == "--allow-fallback" ]]; then
    write_fallback "No CHANGELOG.md was found in the repository."
    echo "CHANGELOG.md was not found; wrote explicitly allowed fallback release notes."
    exit 0
  fi
  echo "RELEASE_NOTES_NOT_FOUND: CHANGELOG.md does not exist and no dedicated notes file was found for $VERSION." >&2
  exit 1
fi

status=0
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
    sub(/\r$/, "", line);
    if (line == "## [" version "]" || line == "## " version) {
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

if [[ "$status" != "0" ]]; then
  rm -f "$OUT.tmp"
  if [[ "$ALLOW_FALLBACK" == "--allow-fallback" ]]; then
    write_fallback "No dedicated release notes file or matching CHANGELOG.md section was found for this version."
    echo "No release notes section found for $VERSION; wrote explicitly allowed fallback release notes."
    exit 0
  fi
  echo "RELEASE_NOTES_NOT_FOUND: add docs/release-notes/v$VERSION.md or a matching CHANGELOG.md section before publishing." >&2
  exit 1
fi

{
  echo "# Selenium Playwright Migrator $VERSION"
  echo
  cat "$OUT.tmp"
} > "$OUT"
rm -f "$OUT.tmp"
echo "Extracted release notes from CHANGELOG.md for $VERSION"
