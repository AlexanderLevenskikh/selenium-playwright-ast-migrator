#!/usr/bin/env bash
set -euo pipefail

version="0.0.0-preview.8"
configuration="Release"
skip_dotnet_tests="false"
run_packaging_smoke="false"
run_npm_registry_smoke="false"
npm_package="selenium-pw-migrator@preview"
npm_registry=""
standalone_base_url=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --version)
      version="$2"; shift 2 ;;
    --configuration)
      configuration="$2"; shift 2 ;;
    --skip-dotnet-tests)
      skip_dotnet_tests="true"; shift ;;
    --run-packaging-smoke)
      run_packaging_smoke="true"; shift ;;
    --run-npm-registry-smoke)
      run_npm_registry_smoke="true"; shift ;;
    --npm-package)
      npm_package="$2"; shift 2 ;;
    --npm-registry)
      npm_registry="$2"; shift 2 ;;
    --standalone-base-url)
      standalone_base_url="$2"; shift 2 ;;
    -h|--help)
      cat <<HELP
Run final distribution sanity checks before publishing or piloting the migrator.

Usage:
  scripts/verify-distribution-final.sh [--version 0.0.0-preview.8] [--skip-dotnet-tests]
  scripts/verify-distribution-final.sh --run-packaging-smoke
  scripts/verify-distribution-final.sh --run-npm-registry-smoke --npm-package selenium-pw-migrator@preview --npm-registry https://nexus.example/repository/npm-group/ --standalone-base-url https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
HELP
      exit 0 ;;
    *)
      echo "Unknown option: $1" >&2
      exit 2 ;;
  esac
done

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"

step() {
  printf '\n== %s ==\n' "$1"
}

step "Git diff whitespace check"
git diff --check

step "Shell script executable bits"
bad="$(git ls-files -s -- '*.sh' | awk '$1 != "100755" { print $1 " " $4 }')"
if [[ -n "$bad" ]]; then
  echo "$bad" | sed 's/^/Non-executable shell script: /'
  echo "Run: git ls-files '*.sh' | xargs git update-index --chmod=+x" >&2
  exit 1
fi
echo "All tracked .sh files are executable in git."

step "Node syntax checks"
if command -v node >/dev/null 2>&1; then
  node -c npm/scripts/install.js
  node -c npm/bin/selenium-pw-migrator.js
else
  echo "node not found; skipping npm wrapper syntax checks."
fi

step "Bash syntax checks"
bash -n scripts/pack-npm-wrapper.sh
bash -n scripts/publish-npm-wrapper.sh
bash -n scripts/smoke-npm-registry-install.sh
bash -n scripts/diagnose-install.sh
bash -n scripts/verify-distribution-final.sh

if [[ "$skip_dotnet_tests" != "true" ]]; then
  step "dotnet test"
  dotnet test Migrator.sln -c "$configuration"
fi

step "Install diagnostics script smoke"
scripts/diagnose-install.sh --skip-package-managers || true

if [[ "$run_packaging_smoke" == "true" ]]; then
  step "Standalone + npm wrapper local packaging smoke"
  pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/package-standalone.ps1 -Version "$version" -Runtimes win-x64
  pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/smoke-npm-wrapper.ps1 \
    -Version "$version" \
    -Runtime win-x64 \
    -ArchivePath "artifacts/release/selenium-pw-migrator-$version-win-x64.zip" \
    -ChecksumsPath "artifacts/release/checksums.sha256"
  pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/pack-npm-wrapper.ps1 -Version "$version"
fi

if [[ "$run_npm_registry_smoke" == "true" ]]; then
  step "Published npm registry/Nexus install smoke"
  args=(--package "$npm_package")
  if [[ -n "$npm_registry" ]]; then
    args+=(--registry "$npm_registry")
  fi
  if [[ -n "$standalone_base_url" ]]; then
    args+=(--standalone-base-url "$standalone_base_url")
  fi
  scripts/smoke-npm-registry-install.sh "${args[@]}"
fi

echo
echo "Final distribution verification completed."
