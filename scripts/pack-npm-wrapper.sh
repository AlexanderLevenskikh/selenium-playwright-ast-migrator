#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: scripts/pack-npm-wrapper.sh <version> [output-dir]" >&2
  exit 2
fi

version="$1"
output_dir="${2:-artifacts/npm}"
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_dir="$repo_root/npm"
output_path="$repo_root/$output_dir"
staging_dir="$output_path/package"

command -v npm >/dev/null 2>&1 || { echo "npm is required to package the npm wrapper." >&2; exit 2; }
command -v node >/dev/null 2>&1 || { echo "node is required to package the npm wrapper." >&2; exit 2; }

rm -rf "$staging_dir"
mkdir -p "$staging_dir" "$output_path"
cp -R "$source_dir/bin" "$staging_dir/"
cp -R "$source_dir/scripts" "$staging_dir/"
cp "$source_dir/README.md" "$staging_dir/"
cp "$source_dir/package.json" "$staging_dir/"

node - "$staging_dir/package.json" "$version" <<'NODE'
const fs = require('fs');
const path = process.argv[2];
const version = process.argv[3];
const pkg = JSON.parse(fs.readFileSync(path, 'utf8'));
pkg.version = version;
fs.writeFileSync(path, JSON.stringify(pkg, null, 2) + '\n');
NODE

(cd "$staging_dir" && npm pack --pack-destination "$output_path")

echo "npm wrapper package artifacts: $output_path"
find "$output_path" -maxdepth 1 -type f -name '*.tgz' -printf '  %f\n' | sort
