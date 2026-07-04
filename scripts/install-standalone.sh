#!/usr/bin/env bash
set -euo pipefail

VERSION="${VERSION:-latest}"
BASE_URL="${BASE_URL:-https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download}"
INSTALL_DIR="${INSTALL_DIR:-$HOME/.selenium-pw-migrator}"
RUNTIME="${RUNTIME:-}"
ARCHIVE_PATH="${ARCHIVE_PATH:-}"
CHECKSUMS_PATH="${CHECKSUMS_PATH:-}"
UNINSTALL="${UNINSTALL:-false}"

usage() {
  cat <<USAGE
Usage: install-standalone.sh [--version <version>] [--base-url <url>] [--install-dir <dir>] [--runtime <rid>] [--archive-path <path>] [--checksums-path <path>] [--uninstall]

Modes:
  Remote release:  --version <version> --base-url <release-artifacts-url>
  Local archive:   --archive-path <archive.tar.gz> [--checksums-path <checksums.sha256>]
  Uninstall:       --uninstall [--install-dir <dir>]

Environment variables are also supported: VERSION, BASE_URL, INSTALL_DIR, RUNTIME, ARCHIVE_PATH, CHECKSUMS_PATH, UNINSTALL.
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version) VERSION="$2"; shift 2 ;;
    --base-url) BASE_URL="$2"; shift 2 ;;
    --install-dir) INSTALL_DIR="$2"; shift 2 ;;
    --runtime) RUNTIME="$2"; shift 2 ;;
    --archive-path) ARCHIVE_PATH="$2"; shift 2 ;;
    --checksums-path) CHECKSUMS_PATH="$2"; shift 2 ;;
    --uninstall) UNINSTALL="true"; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 2 ;;
  esac
done


normalize_path_for_compare() {
  local value="$1"
  value="${value%/}"
  printf '%s' "$value"
}

assert_safe_install_dir() {
  local dir="$1"
  if [[ -z "$dir" ]]; then
    echo "InstallDir cannot be empty." >&2
    exit 1
  fi

  local full_dir=""
  full_dir="$(cd "$(dirname "$dir")" 2>/dev/null && pwd -P)/$(basename "$dir")" || full_dir="$dir"
  local home_norm="$(normalize_path_for_compare "$HOME")"
  local dir_norm="$(normalize_path_for_compare "$full_dir")"

  if [[ "$dir_norm" == "$home_norm" || "$dir_norm" == "/" ]]; then
    echo "Refusing to uninstall from unsafe install directory: $full_dir" >&2
    exit 1
  fi
}

uninstall_standalone() {
  assert_safe_install_dir "$INSTALL_DIR"
  local bin_dir="$INSTALL_DIR/bin"

  if [[ -d "$INSTALL_DIR" ]]; then
    rm -rf "$INSTALL_DIR"
    echo "Removed Selenium Playwright Migrator standalone installation: $INSTALL_DIR"
  else
    echo "Standalone installation directory was already absent: $INSTALL_DIR"
  fi

  echo "Remove this directory from your shell profile PATH entry if it is present:"
  echo "  $bin_dir"
  echo "For example, edit ~/.bashrc, ~/.zshrc, ~/.profile, or ~/.config/fish/config.fish."
}

if [[ "$UNINSTALL" == "true" ]]; then
  uninstall_standalone
  exit 0
fi

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

verify_checksum() {
  local archive_path="$1"
  local checksums_path="$2"
  local expected_archive_name="$3"

  if [[ -z "$checksums_path" ]]; then
    return 0
  fi
  if [[ ! -f "$checksums_path" ]]; then
    echo "Checksums file was not found: $checksums_path" >&2
    exit 1
  fi

  local expected=""
  expected="$(grep -E "[[:space:]]${expected_archive_name//./\\.}$" "$checksums_path" | awk '{print $1}' | head -n 1 || true)"
  if [[ -z "$expected" ]]; then
    echo "No checksum entry for $expected_archive_name in $checksums_path" >&2
    exit 1
  fi

  local actual=""
  if command -v sha256sum >/dev/null 2>&1; then
    actual="$(sha256sum "$archive_path" | awk '{print $1}')"
  elif command -v shasum >/dev/null 2>&1; then
    actual="$(shasum -a 256 "$archive_path" | awk '{print $1}')"
  else
    echo "sha256sum or shasum is required to verify checksum" >&2
    exit 2
  fi

  actual_lc="$(printf '%s' "$actual" | tr '[:upper:]' '[:lower:]')"
  expected_lc="$(printf '%s' "$expected" | tr '[:upper:]' '[:lower:]')"
  if [[ "$actual_lc" != "$expected_lc" ]]; then
    echo "Checksum mismatch for $expected_archive_name. Expected $expected, actual $actual." >&2
    exit 1
  fi

  echo "Checksum verified."
}

archive_path="$tmp_dir/$archive_name"

if [[ -n "$ARCHIVE_PATH" ]]; then
  if [[ ! -f "$ARCHIVE_PATH" ]]; then
    echo "ArchivePath was not found: $ARCHIVE_PATH" >&2
    exit 1
  fi
  archive_path="$ARCHIVE_PATH"
  archive_name="$(basename "$archive_path")"
  echo "Using local archive: $archive_path"
  if [[ -n "$CHECKSUMS_PATH" ]]; then
    verify_checksum "$archive_path" "$CHECKSUMS_PATH" "$archive_name"
  fi
else
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
  if command -v curl >/dev/null 2>&1; then
    if curl -fsSL "$checksums_url" -o "$checksums_path"; then
      verify_checksum "$archive_path" "$checksums_path" "$archive_name"
    else
      echo "Checksum verification skipped: checksums.sha256 not available at $checksums_url."
    fi
  elif command -v wget >/dev/null 2>&1; then
    if wget -q "$checksums_url" -O "$checksums_path"; then
      verify_checksum "$archive_path" "$checksums_path" "$archive_name"
    else
      echo "Checksum verification skipped: checksums.sha256 not available at $checksums_url."
    fi
  fi
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
