#!/usr/bin/env bash
set -euo pipefail

VERSION="${VERSION:-latest}"
BASE_URL="${BASE_URL:-https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download}"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.selenium-pw-migrator}"
RUNTIME="${RUNTIME:-}"

usage() {
  cat <<USAGE
Usage: install-standalone.sh [--version <version>] [--base-url <url>] [--install-dir <dir>] [--runtime <rid>]

Environment variables are also supported: VERSION, BASE_URL, INSTALL_DIR, RUNTIME.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --base-url) BASE_URL="$2"; shift 2 ;;
    --install-dir) INSTALL_DIR="$2"; shift 2 ;;
    --runtime) RUNTIME="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done

if [[ -z "$RUNTIME" ]]; then
  os="$(uname -s | tr '[:upper:]' '[:lower:]')"
  arch="$(uname -m)"
  case "$os" in
    linux*) os_part="linux" ;;
    darwin*) os_part="osx" ;;
    *) echo "Unsupported OS: $os" >&2; exit 2 ;;
  esac
  case "$arch" in
    x86_64|amd64) arch_part="x64" ;;
    arm64|aarch64) arch_part="arm64" ;;
    *) echo "Unsupported architecture: $arch" >&2; exit 2 ;;
  esac
  RUNTIME="$os_part-$arch_part"
fi

if [[ "$VERSION" != "latest" && "$BASE_URL" == */releases/latest/download* ]]; then
  BASE_URL="${BASE_URL%/releases/latest/download*}/releases/download/v$VERSION"
fi
BASE_URL="${BASE_URL%/}"

archive_name="selenium-pw-migrator-$RUNTIME.tar.gz"
if [[ "$VERSION" != "latest" ]]; then
  archive_name="selenium-pw-migrator-$VERSION-$RUNTIME.tar.gz"
fi

bin_dir="$INSTALL_DIR/bin"
tmp_dir="$(mktemp -d)"
cleanup() { rm -rf "$tmp_dir"; }
trap cleanup EXIT

mkdir -p "$bin_dir"
archive_url="$BASE_URL/$archive_name"
archive_path="$tmp_dir/$archive_name"

echo "Downloading $archive_url"
if command -v curl >/dev/null 2>&1; then
  curl -fsSL "$archive_url" -o "$archive_path"
elif command -v wget >/dev/null 2>&1; then
  wget -q "$archive_url" -O "$archive_path"
else
  echo "curl or wget is required" >&2
  exit 2
fi

checksums_url="$BASE_URL/checksums.sha256"
checksums_path="$tmp_dir/checksums.sha256"
if command -v curl >/dev/null 2>&1 && curl -fsSL "$checksums_url" -o "$checksums_path"; then
  expected="$(grep "  $archive_name$" "$checksums_path" | awk '{print $1}' || true)"
  if [[ -n "$expected" ]]; then
    if command -v sha256sum >/dev/null 2>&1; then
      actual="$(sha256sum "$archive_path" | awk '{print $1}')"
    elif command -v shasum >/dev/null 2>&1; then
      actual="$(shasum -a 256 "$archive_path" | awk '{print $1}')"
    else
      actual=""
    fi
    if [[ -n "$actual" && "$actual" != "$expected" ]]; then
      echo "Checksum mismatch for $archive_name" >&2
      exit 1
    fi
    [[ -n "$actual" ]] && echo "Checksum verified."
  fi
else
  echo "Checksum verification skipped: checksums.sha256 not available."
fi

extract_dir="$tmp_dir/extract"
mkdir -p "$extract_dir"
tar -xzf "$archive_path" -C "$extract_dir"
rm -rf "$bin_dir"/*
cp -R "$extract_dir"/. "$bin_dir"/
chmod +x "$bin_dir/selenium-pw-migrator" 2>/dev/null || true

echo "Installed Selenium Playwright Migrator to: $bin_dir"
echo "Run: $bin_dir/selenium-pw-migrator --version"
echo "To use from any terminal, add this to your shell profile:"
echo "  export PATH=\"$bin_dir:\$PATH\""
