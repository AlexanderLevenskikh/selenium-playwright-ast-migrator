#!/usr/bin/env bash
set -euo pipefail

version="${1:-}"
package_path="${2:-}"
registry="${NPM_REGISTRY:-https://registry.npmjs.org/}"
access="${NPM_ACCESS:-public}"
dry_run="${NPM_DRY_RUN:-true}"
tag="${NPM_TAG:-preview}"
provenance="${NPM_PROVENANCE:-false}"

if [[ -z "$version" ]]; then
  echo "Usage: scripts/publish-npm-wrapper.sh <version> [package-path]" >&2
  echo "Set NPM_DRY_RUN=false to publish for real." >&2
  exit 2
fi

if [[ -z "$package_path" ]]; then
  package_path="artifacts/npm/selenium-pw-migrator-${version}.tgz"
fi

expected_name="selenium-pw-migrator-${version}.tgz"
actual_name="$(basename "$package_path")"
if [[ "$actual_name" != "$expected_name" ]]; then
  echo "npm wrapper package name mismatch. Expected '${expected_name}', actual '${actual_name}'." >&2
  exit 2
fi

if [[ ! -f "$package_path" ]]; then
  echo "npm wrapper package was not found: $package_path" >&2
  exit 2
fi

args=(publish "$package_path" --registry "$registry" --access "$access" --tag "$tag")
if [[ "$dry_run" == "true" || "$dry_run" == "1" ]]; then
  args+=(--dry-run)
elif [[ "$provenance" == "true" || "$provenance" == "1" ]]; then
  args+=(--provenance)
fi

echo "Publishing npm wrapper package: $package_path"
echo "Registry: $registry"
echo "Tag: $tag"
echo "Dry run: $dry_run"
echo "Provenance: $provenance"
npm "${args[@]}"
