#!/usr/bin/env bash
set -euo pipefail

package="selenium-pw-migrator@preview"
registry="https://registry.npmjs.org/"
standalone_base_url=""
runtime=""
keep_temp="false"
command_args=(--version)

usage() {
  cat <<'USAGE'
Usage: scripts/smoke-npm-registry-install.sh [options] [-- <cli-args>]

Options:
  --package <spec>             npm package spec, default: selenium-pw-migrator@preview
  --registry <url>             npm registry URL, default: https://registry.npmjs.org/
  --standalone-base-url <url>  standalone release asset base URL for Nexus/static mirrors
  --runtime <rid>              runtime override, for example win-x64 or linux-x64
  --keep-temp                  keep the temporary smoke project
  -h, --help                   show help

Examples:
  scripts/smoke-npm-registry-install.sh --package selenium-pw-migrator@preview
  scripts/smoke-npm-registry-install.sh \
    --registry https://nexus.example/repository/npm-group/ \
    --standalone-base-url https://nexus.example/repository/migrator-releases/v0.0.0-preview.8 \
    --package selenium-pw-migrator@0.0.0-preview.8
USAGE
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --package)
      package="${2:-}"
      shift 2
      ;;
    --registry)
      registry="${2:-}"
      shift 2
      ;;
    --standalone-base-url)
      standalone_base_url="${2:-}"
      shift 2
      ;;
    --runtime)
      runtime="${2:-}"
      shift 2
      ;;
    --keep-temp)
      keep_temp="true"
      shift
      ;;
    -h|--help)
      usage
      exit 0
      ;;
    --)
      shift
      command_args=("$@")
      break
      ;;
    *)
      echo "Unknown option: $1" >&2
      usage >&2
      exit 2
      ;;
  esac
done

if ! command -v npm >/dev/null 2>&1; then
  echo "npm is required for registry install smoke." >&2
  exit 2
fi

smoke_dir="$(mktemp -d "${TMPDIR:-/tmp}/selenium-pw-migrator-npm-smoke.XXXXXX")"
cleanup() {
  if [[ "$keep_temp" == "true" ]]; then
    echo "Keeping npm registry smoke directory: $smoke_dir"
  else
    rm -rf "$smoke_dir"
  fi
}
trap cleanup EXIT

cd "$smoke_dir"
echo "npm registry smoke directory: $smoke_dir"
echo "Package: $package"
echo "Registry: $registry"

npm init -y >/dev/null

install_args=(install "$package" "--registry=$registry")
if [[ -n "$standalone_base_url" ]]; then
  install_args+=("--selenium-pw-migrator-base-url=$standalone_base_url")
  echo "Standalone base URL: $standalone_base_url"
fi
if [[ -n "$runtime" ]]; then
  install_args+=("--selenium-pw-migrator-runtime=$runtime")
  echo "Runtime override: $runtime"
fi

npm "${install_args[@]}"

bin_path="./node_modules/.bin/selenium-pw-migrator"
if [[ ! -x "$bin_path" ]]; then
  echo "Installed npm wrapper binary was not found or is not executable: $bin_path" >&2
  exit 2
fi

echo "Running: $bin_path ${command_args[*]}"
"$bin_path" "${command_args[@]}"
