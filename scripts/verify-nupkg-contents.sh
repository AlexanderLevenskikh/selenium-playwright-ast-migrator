#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 1 ]]; then
  echo "Usage: scripts/verify-nupkg-contents.sh <package-path>" >&2
  exit 2
fi

PACKAGE_PATH="$1"

if [[ ! -f "$PACKAGE_PATH" ]]; then
  echo "Package not found: $PACKAGE_PATH" >&2
  exit 1
fi

python3 - "$PACKAGE_PATH" <<'PY'
import re
import sys
import zipfile
from pathlib import PurePosixPath

package_path = sys.argv[1]
with zipfile.ZipFile(package_path) as zf:
    entries = [name.replace('\\', '/') for name in zf.namelist()]

required_exact = [
    'README_TOOL.md',
    'LICENSE',
    'SECURITY.md',
    'CONTRIBUTING.md',
    'CHANGELOG.md',
    'assets/icon.png',
    'schemas/adapter-config.schema.json',
]
for required in required_exact:
    if required not in entries:
        raise SystemExit(f'Package is missing required public file: {required}')

required_patterns = [
    r'^tools/net10\.0/any/Migrator\.Cli\.(exe|dll)$',
    r'^tools/net10\.0/any/Migrator\.Core\.dll$',
    r'^tools/net10\.0/any/Migrator\.Roslyn\.dll$',
    r'^tools/net10\.0/any/Migrator\.PlaywrightDotNet\.dll$',
    r'^tools/net10\.0/any/Migrator\.PlaywrightTypeScript\.dll$',
    r'^tools/net10\.0/any/Migrator\.SeleniumCSharp\.dll$',
    r'^templates/migration-kit/README\.md$',
    r'^templates/migration-kit/prompts/kickoff-prompt\.txt$',
    r'^templates/migration-kit/agent-skills/skill-map\.md$',
    r'^templates/migration-kit/agent-skills/manifest\.json$',
    r'^templates/migration-kit/scripts/write-agent-skill-usage\.ps1$',
    r'^templates/migration-kit/scripts/write-agent-skill-usage\.sh$',
    r'^templates/migration-kit/scripts/record-agent-skill-profile\.ps1$',
    r'^templates/migration-kit/scripts/record-agent-skill-profile\.sh$',
    r'^templates/migration-kit/scripts/slice-gate-followups\.ps1$',
    r'^templates/migration-kit/scripts/slice-gate-followups\.sh$',
    r'^templates/migration-kit/scripts/evaluate-wave-quality-budget\.ps1$',
    r'^templates/migration-kit/scripts/evaluate-wave-quality-budget\.sh$',
    r'^templates/migration-kit/scripts/start-fresh-wavefront-run\.ps1$',
    r'^templates/migration-kit/scripts/start-fresh-wavefront-run\.sh$',
    r'^templates/migration-kit/scripts/collect-mapping-research-memory\.ps1$',
    r'^templates/migration-kit/scripts/collect-mapping-research-memory\.sh$',
    r'^templates/migration-kit/scripts/create-feedback-bundle\.ps1$',
    r'^templates/migration-kit/scripts/create-feedback-bundle\.sh$',
    r'^templates/migration-kit/scripts/validate-installed-scripts\.ps1$',
    r'^templates/migration-kit/scripts/validate-installed-scripts\.sh$',
    r'^templates/migration-kit/scripts/validate-run-artifacts\.ps1$',
    r'^templates/migration-kit/scripts/validate-run-artifacts\.sh$',
    r'^templates/migration-kit/scripts/repair-jsonl-ledger\.ps1$',
    r'^templates/migration-kit/scripts/repair-jsonl-ledger\.sh$',
    r'^templates/migration-kit/scripts/update-current-ticket-status\.ps1$',
    r'^templates/migration-kit/scripts/update-current-ticket-status\.sh$',
    r'^templates/migration-kit/scripts/update-sentinel-finding-status\.ps1$',
    r'^templates/migration-kit/scripts/update-sentinel-finding-status\.sh$',
    r'^scripts/install-migration-kit\.ps1$',
]
for pattern in required_patterns:
    if not any(re.search(pattern, entry) for entry in entries):
        raise SystemExit(f'Package is missing an entry matching: {pattern}')

forbidden_patterns = [
    r'(^|/)\.agent-state(/|$)',
    r'(^|/)\.migration(/|$)',
    r'(^|/)migration(/|$)',
    r'(^|/)artifacts(/|$)',
    r'(^|/)bin(/|$)',
    r'(^|/)obj(/|$)',
    r'(^|/)TestResults(/|$)',
    r'(^|/)\.git(/|$)',
    r'(^|/)\.vs(/|$)',
    r'(^|/)\.idea(/|$)',
    r'(^|/)node_modules(/|$)',
    r'(^|/)\.env(\.|/|$)',
    r'(^|/)NuGet\.config$',
    r'\.local\.json$',
    r'\.(zip|7z|rar)$',
]
for entry in entries:
    for pattern in forbidden_patterns:
        if re.search(pattern, entry):
            raise SystemExit(f'Package contains forbidden local/private artifact: {entry}')

if not any(entry.endswith('.nuspec') for entry in entries):
    raise SystemExit('Package does not contain a .nuspec file.')

print(f'Package content verification passed: {package_path}')
print(f'Entries checked: {len(entries)}')
PY
