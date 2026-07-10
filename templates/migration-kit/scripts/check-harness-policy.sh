#!/usr/bin/env bash
set -euo pipefail

Workspace="migration"
RepoRoot="."
AllowNoGit=false
AllowGuardChanges=false
SkipGitStatus=false
AllowedRoots=()
ORIGINAL_ARGS=("$@")

while [[ $# -gt 0 ]]; do
  case "$1" in
    -Workspace|--workspace)
      Workspace="$2"
      shift 2
      ;;
    -RepoRoot|--repo-root)
      RepoRoot="$2"
      shift 2
      ;;
    -AllowedRoots|--allowed-roots)
      IFS=',' read -r -a parsed_roots <<< "$2"
      AllowedRoots+=("${parsed_roots[@]}")
      shift 2
      ;;
    -AllowNoGit|--allow-no-git)
      AllowNoGit=true
      shift
      ;;
    -AllowGuardChanges|--allow-guard-changes)
      AllowGuardChanges=true
      shift
      ;;
    -SkipGitStatus|--skip-git-status)
      SkipGitStatus=true
      shift
      ;;
    *)
      echo "Unknown argument: $1" >&2
      exit 64
      ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PS_SCRIPT="$SCRIPT_DIR/check-harness-policy.ps1"

find_python() {
  if command -v python3 >/dev/null 2>&1; then
    command -v python3
    return 0
  fi

  if command -v python >/dev/null 2>&1; then
    command -v python
    return 0
  fi

  return 1
}

PYTHON="$(find_python || true)"
if [[ -z "$PYTHON" ]]; then
  if command -v pwsh >/dev/null 2>&1; then
    exec pwsh -NoProfile -ExecutionPolicy Bypass -File "$PS_SCRIPT" "${ORIGINAL_ARGS[@]}"
  fi

  case "$(uname -s 2>/dev/null || echo unknown)" in
    MINGW*|MSYS*|CYGWIN*|Windows_NT)
      if command -v powershell >/dev/null 2>&1; then
        exec powershell -NoProfile -ExecutionPolicy Bypass -File "$PS_SCRIPT" "${ORIGINAL_ARGS[@]}"
      fi
      ;;
  esac

  cat >&2 <<EOF
Python 3 or PowerShell 7 (pwsh) is required to run check-harness-policy.sh on macOS/Linux/WSL.
Install PowerShell 7: https://learn.microsoft.com/powershell/scripting/install/installing-powershell
EOF
  exit 127
fi

abs_path() {
  "$PYTHON" - "$1" "$2" <<'PY'
import os
import sys

base, path = sys.argv[1], sys.argv[2]
if os.path.isabs(path):
    print(os.path.abspath(path))
else:
    print(os.path.abspath(os.path.join(base, path)))
PY
}

normalize_path() {
  local path="$1"
  path="${path//\\//}"
  while [[ "$path" == ./* ]]; do
    path="${path#./}"
  done
  echo "$path"
}

read_text_if_exists() {
  local path="$1"
  if [[ -f "$path" ]]; then
    cat "$path"
  fi
}

test_glob_path() {
  "$PYTHON" - "$1" "$2" <<'PY'
import re
import sys

path = sys.argv[1].replace("\\", "/").lstrip("./")
pattern = sys.argv[2].replace("\\", "/").lstrip("./")
regex = "^" + re.escape(pattern).replace(r"\*\*", ".*").replace(r"\*", "[^/]*").replace(r"\?", ".") + "$"
sys.exit(0 if re.match(regex, path) else 1)
PY
}

test_any_pattern() {
  local path="$1"
  shift
  local pattern
  for pattern in "$@"; do
    if test_glob_path "$path" "$pattern"; then
      return 0
    fi
  done

  return 1
}

convert_to_workspace_relative_path() {
  local path
  local workspace_prefix
  path="$(normalize_path "$1")"
  workspace_prefix="$(normalize_path "$2")"
  workspace_prefix="${workspace_prefix%/}"
  if [[ -n "$workspace_prefix" && "$path" == "$workspace_prefix/"* ]]; then
    echo "${path#"$workspace_prefix/"}"
  else
    echo "$path"
  fi
}

sha256_file() {
  local path="$1"
  if command -v sha256sum >/dev/null 2>&1; then
    sha256sum "$path" | awk '{ print toupper($1) }'
    return 0
  fi

  if command -v shasum >/dev/null 2>&1; then
    shasum -a 256 "$path" | awk '{ print toupper($1) }'
    return 0
  fi

  "$PYTHON" - "$path" <<'PY'
import hashlib
import sys

with open(sys.argv[1], "rb") as f:
    print(hashlib.sha256(f.read()).hexdigest().upper())
PY
}

json_scalar() {
  "$PYTHON" - "$1" "$2" <<'PY'
import json
import sys

path, key = sys.argv[1], sys.argv[2]
with open(path, encoding="utf-8-sig") as f:
    data = json.load(f)
value = data
for part in key.split("."):
    value = value.get(part) if isinstance(value, dict) else None
print("" if value is None else value)
PY
}

json_array_lines() {
  "$PYTHON" - "$1" "$2" <<'PY'
import json
import sys

path, key = sys.argv[1], sys.argv[2]
with open(path, encoding="utf-8-sig") as f:
    data = json.load(f)
value = data
for part in key.split("."):
    value = value.get(part) if isinstance(value, dict) else []
for item in (value or []):
    print(item)
PY
}


scope_contract_allowed_roots() {
  local workspace_path="$1"
  local contract_path="$workspace_path/state/scope-contract.json"
  [[ -f "$contract_path" ]] || return 0
  "$PYTHON" - "$contract_path" <<'PY_SCOPE_ROOTS'
import json
import sys


def norm(value):
    text = str(value or "").replace("\\", "/").strip()
    while text.startswith("./"):
        text = text[2:]
    return text.strip("/")

with open(sys.argv[1], encoding="utf-8-sig") as f:
    contract = json.load(f)

roots = []
for root in contract.get("allowedSourceRoots") or []:
    normalized = norm(root)
    if normalized and normalized not in (".", "/"):
        roots.append(normalized)

if not roots:
    source_root = norm(contract.get("sourceRoot"))
    warnings = contract.get("warnings") or []
    if source_root and source_root not in (".", "/") and not warnings:
        roots.append(source_root)

for root in sorted(set(roots)):
    print(root)
PY_SCOPE_ROOTS
}

test_scope_contract_changed_paths() {
  local workspace_path="$1"
  shift
  local contract_path="$workspace_path/state/scope-contract.json"
  if [[ ! -f "$contract_path" ]]; then
    echo "scope-contract.json not found; skipped for backward compatibility"
    return 0
  fi

  "$PYTHON" - "$contract_path" "$@" <<'PY_SCOPE_CHECK'
import json
import sys


def norm(value):
    text = str(value or "").replace("\\", "/").strip()
    while text.startswith("./"):
        text = text[2:]
    return text.strip("/")

contract_path = sys.argv[1]
changed = [norm(p) for p in sys.argv[2:] if norm(p)]
try:
    with open(contract_path, encoding="utf-8-sig") as f:
        contract = json.load(f)
except Exception as exc:
    print(f"invalid scope-contract.json: {exc}")
    sys.exit(1)

workspace_root = norm(contract.get("workspaceRoot") or "migration")
allowed_files = {norm(p) for p in (contract.get("allowedFiles") or []) if norm(p)}
allowed_roots = {norm(p) for p in (contract.get("allowedSourceRoots") or []) if norm(p) and norm(p) not in (".", "/")}
forbidden_roots = {norm(p) for p in (contract.get("forbiddenRoots") or []) if norm(p)}
max_changed = int(contract.get("maxChangedFiles") or 0)

def under(path, root):
    return path == root or path.startswith(root + "/")

forbidden_hits = []
out_of_scope = []
for changed_path in changed:
    if any(under(changed_path, root) for root in forbidden_roots):
        forbidden_hits.append(changed_path)
        continue
    if workspace_root and under(changed_path, workspace_root):
        continue
    if allowed_files:
        if changed_path in allowed_files:
            continue
    elif any(under(changed_path, root) for root in allowed_roots):
        continue
    out_of_scope.append(changed_path)

reasons = []
if max_changed > 0 and len(changed) > max_changed:
    reasons.append(f"changed files {len(changed)} exceed maxChangedFiles {max_changed}")
if forbidden_hits:
    reasons.append("forbidden root hits: " + ", ".join(forbidden_hits))
if out_of_scope:
    reasons.append("out-of-scope files: " + ", ".join(out_of_scope))

if reasons:
    print("; ".join(reasons))
    sys.exit(1)

print(f"scope contract passed; changedFilesChecked={len(changed)}")
PY_SCOPE_CHECK
}

read_guard_checksum_index() {
  local workspace_path="$1"
  local checksum_path="$workspace_path/.migration-kit/guard-checksums.json"
  if [[ ! -f "$checksum_path" ]]; then
    echo "missing $checksum_path" >&2
    return 1
  fi

  "$PYTHON" - "$checksum_path" <<'PY'
import json
import sys

with open(sys.argv[1], encoding="utf-8-sig") as f:
    data = json.load(f)
for entry in data.get("files", []):
    path = str(entry.get("path", "")).replace("\\", "/")
    sha = str(entry.get("sha256", "")).upper()
    if path and sha:
        print(f"{path}\t{sha}")
PY
}

required_guard_checksum_files() {
  cat <<'EOF'
scripts/check-scope.ps1
scripts/check-scope.sh
scripts/check-final-gate.ps1
scripts/check-final-gate.sh
scripts/check-harness-policy.ps1
scripts/check-harness-policy.sh
scripts/new-claim.ps1
scripts/new-claim.sh
scripts/update-claim-heartbeat.ps1
scripts/update-claim-heartbeat.sh
scripts/complete-claim.ps1
scripts/complete-claim.sh
scripts/claim-doctor.ps1
scripts/claim-doctor.sh
scripts/build-harness-dashboard.ps1
scripts/build-harness-dashboard.sh
scripts/export-opencode-session.ps1
scripts/export-opencode-session.sh
scripts/slice-gate-followups.ps1
scripts/slice-gate-followups.sh
scripts/evaluate-wave-quality-budget.ps1
scripts/evaluate-wave-quality-budget.sh
scripts/repair-jsonl-ledger.ps1
scripts/repair-jsonl-ledger.sh
scripts/start-fresh-wavefront-run.ps1
scripts/start-fresh-wavefront-run.sh
scripts/collect-mapping-research-memory.ps1
scripts/collect-mapping-research-memory.sh
scripts/create-feedback-bundle.ps1
scripts/create-feedback-bundle.sh
scripts/write-sentinel-finding.ps1
scripts/write-sentinel-finding.sh
scripts/complete-sentinel-inspection.ps1
scripts/complete-sentinel-inspection.sh
scripts/update-current-ticket-status.ps1
scripts/update-current-ticket-status.sh
scripts/update-sentinel-finding-status.ps1
scripts/update-sentinel-finding-status.sh
EOF
}

load_expected_checksums() {
  local workspace_path="$1"
  EXPECTED=()
  while IFS=$'\t' read -r path sha; do
    [[ -z "${path:-}" ]] && continue
    EXPECTED+=("$path=$sha")
  done < <(read_guard_checksum_index "$workspace_path")
}

expected_checksum_for() {
  local target="$1"
  local entry
  for entry in "${EXPECTED[@]:-}"; do
    if [[ "${entry%%=*}" == "$target" ]]; then
      echo "${entry#*=}"
      return 0
    fi
  done

  return 1
}

expected_paths_sorted() {
  local entry
  for entry in "${EXPECTED[@]:-}"; do
    echo "${entry%%=*}"
  done | sort -u
}

test_guard_checksum_index_matches_current_files() {
  local workspace_path="$1"
  local mismatches=()
  local required
  while IFS= read -r required; do
    if ! expected_checksum_for "$required" >/dev/null; then
      mismatches+=("$required missing checksum baseline")
    fi
  done < <(required_guard_checksum_files)

  local relative
  while IFS= read -r relative; do
    [[ -z "$relative" ]] && continue
    local full_path="$workspace_path/$relative"
    if [[ ! -f "$full_path" ]]; then
      mismatches+=("$relative missing on disk")
      continue
    fi

    local actual expected
    actual="$(sha256_file "$full_path")"
    expected="$(expected_checksum_for "$relative")"
    if [[ "$actual" != "$expected" ]]; then
      mismatches+=("$relative checksum mismatch")
    fi
  done < <(expected_paths_sorted)

  if [[ ${#mismatches[@]} -gt 0 ]]; then
    (IFS='; '; echo "${mismatches[*]}")
    return 1
  fi

  echo "all guard file hashes match guard-checksums baseline"
  return 0
}

test_guard_sensitive_changes_match_checksum_baseline() {
  local workspace_path="$1"
  local workspace_root_name="$2"
  shift 2
  local guard_changes=("$@")

  if [[ ${#guard_changes[@]} -eq 0 ]]; then
    echo "no guard-sensitive changes"
    return 0
  fi

  local checksum_path_pattern
  checksum_path_pattern="$(normalize_path "$workspace_root_name")"
  checksum_path_pattern="${checksum_path_pattern%/}/.migration-kit/guard-checksums.json"

  local checksum_changed=false
  local guard_file_changes=()
  local path normalized
  for path in "${guard_changes[@]}"; do
    normalized="$(normalize_path "$path")"
    if [[ "${normalized,,}" == "${checksum_path_pattern,,}" ]]; then
      checksum_changed=true
      continue
    fi

    guard_file_changes+=("$normalized")
  done

  local load_error
  if ! load_error="$(load_expected_checksums "$workspace_path" 2>&1)"; then
    echo "$load_error"
    return 1
  fi

  if [[ "$checksum_changed" == true && ${#guard_file_changes[@]} -eq 0 ]]; then
    local current_detail
    if current_detail="$(test_guard_checksum_index_matches_current_files "$workspace_path")"; then
      echo "guard-checksums.json metadata-only change accepted; $current_detail"
      return 0
    fi

    echo "guard-checksums.json changed without changed guard scripts; $current_detail"
    return 1
  fi

  local mismatches=()
  local changed_path relative full_path actual expected
  for changed_path in "${guard_file_changes[@]}"; do
    relative="$(convert_to_workspace_relative_path "$changed_path" "$workspace_root_name")"
    if ! expected="$(expected_checksum_for "$relative")"; then
      mismatches+=("$changed_path missing checksum baseline")
      continue
    fi

    full_path="$workspace_path/$relative"
    if [[ ! -f "$full_path" ]]; then
      mismatches+=("$changed_path missing on disk")
      continue
    fi

    actual="$(sha256_file "$full_path")"
    if [[ "$actual" != "$expected" ]]; then
      mismatches+=("$changed_path checksum mismatch")
    fi
  done

  if [[ ${#mismatches[@]} -gt 0 ]]; then
    (IFS='; '; echo "${mismatches[*]}")
    return 1
  fi

  local changed_summary
  changed_summary="$(printf '%s\n' "${guard_changes[@]}" | sort -u | paste -sd ', ' -)"
  echo "guard-sensitive changes match guard-checksums baseline; changed: $changed_summary"
  return 0
}

expand_allowed_root_patterns() {
  local root normalized
  for root in "$@"; do
    [[ -z "${root// }" ]] && continue
    normalized="$(normalize_path "$root")"
    normalized="${normalized%/}"
    [[ -z "$normalized" ]] && continue
    echo "$normalized"
    if [[ "$normalized" != */** ]]; then
      echo "$normalized/**"
    fi
  done | sort -u
}

project_local_opencode_patterns() {
  cat <<'EOF'
AGENTS.md
opencode.jsonc
.opencode
.opencode/**
.opencode-migrator
.opencode-migrator/**
opencode
opencode/**
EOF
}

find_latest_run_id() {
  local workspace_path="$1"
  local agent_state="$workspace_path/agent-state.md"
  [[ -f "$agent_state" ]] || return 0
  "$PYTHON" - "$agent_state" <<'PY'
import re
import sys

text = open(sys.argv[1], encoding="utf-8-sig").read()
match = re.search(r"(?im)^\s*Latest run\s*:\s*(run-[0-9A-Za-z][0-9A-Za-z._-]*)\s*$", text)
print(match.group(1) if match else "")
PY
}

git_changed_paths() {
  local root="$1"
  (
    cd "$root"
    local git_root
    if ! git_root="$(git rev-parse --show-toplevel 2>/dev/null)" || [[ -z "$git_root" ]]; then
      if [[ "$AllowNoGit" == true ]]; then
        return 0
      fi
      echo "No git repository found at $root." >&2
      return 1
    fi

    local tokens=()
    while IFS= read -r -d '' token; do
      tokens+=("$token")
    done < <(git status --porcelain=v1 -z --untracked-files=all)

    local paths=()
    local i=0
    while [[ $i -lt ${#tokens[@]} ]]; do
      local entry="${tokens[$i]}"
      if [[ -z "$entry" || ${#entry} -lt 4 ]]; then
        i=$((i + 1))
        continue
      fi

      local xy="${entry:0:2}"
      local path="${entry:3}"
      [[ -n "${path// }" ]] && paths+=("$(normalize_path "$path")")
      if [[ "$xy" == *R* || "$xy" == *C* ]]; then
        if [[ $((i + 1)) -lt ${#tokens[@]} && -n "${tokens[$((i + 1))]}" ]]; then
          paths+=("$(normalize_path "${tokens[$((i + 1))]}")")
          i=$((i + 1))
        fi
      fi
      i=$((i + 1))
    done

    printf '%s\n' "${paths[@]}" | sort -u
  )
}

open_code_policy_ok() {
  "$PYTHON" - "$1" <<'PY'
import re
import sys

text = open(sys.argv[1], encoding="utf-8-sig").read()
has_deny_all = re.search(r'"edit"\s*:\s*\{[\s\S]*?"\*"\s*:\s*"deny"', text) is not None
has_migration = re.search(r'"migration/\*\*"\s*:\s*"allow"', text) is not None
trusted = (
    re.search(r'"edit"\s*:\s*"allow"', text) is not None
    and re.search(r'"bash"\s*:\s*"allow"', text) is not None
    and re.search(r'"external_directory"\s*:\s*"deny"', text) is not None
)
print(f"{str(has_deny_all).lower()}\t{str(has_migration).lower()}\t{str(trusted).lower()}")
sys.exit(0 if ((has_deny_all and has_migration) or trusted) else 1)
PY
}

RESULTS_TSV="$(mktemp)"
trap 'rm -f "$RESULTS_TSV"' EXIT

add_result() {
  local name="$1"
  local passed="$2"
  local detail="$3"
  printf '%s\t%s\t%s\n' "$name" "$passed" "$detail" >> "$RESULTS_TSV"
}

repoRootPath="$(abs_path "$(pwd)" "$RepoRoot")"
workspacePath="$(abs_path "$repoRootPath" "$Workspace")"

policyPath="$workspacePath/state/harness-policy.json"
policy_loaded=false
if [[ ! -f "$policyPath" ]]; then
  add_result "policy-file" "false" "missing $policyPath"
else
  if "$PYTHON" -m json.tool "$policyPath" >/dev/null 2>&1; then
    policy_loaded=true
    add_result "policy-file" "true" "loaded $policyPath"
  else
    add_result "policy-file" "false" "invalid JSON"
  fi
fi

if [[ "$policy_loaded" == true ]]; then
  schemaVersion="$(json_scalar "$policyPath" "schemaVersion")"
  mode="$(json_scalar "$policyPath" "mode")"
  if [[ "${schemaVersion:-0}" -ge 1 && -n "$mode" ]]; then
    add_result "policy-schema" "true" "schemaVersion=$schemaVersion; mode=$mode"
  else
    add_result "policy-schema" "false" "schemaVersion=$schemaVersion; mode=$mode"
  fi

  missing_required=()
  while IFS= read -r relative; do
    [[ -z "$relative" ]] && continue
    [[ -e "$workspacePath/$relative" ]] || missing_required+=("$relative")
  done < <(json_array_lines "$policyPath" "requiredFiles")

  if [[ ${#missing_required[@]} -eq 0 ]]; then
    add_result "required-files" "true" "all required files exist"
  else
    add_result "required-files" "false" "missing: $(IFS=', '; echo "${missing_required[*]}")"
  fi

  latestRunId="$(find_latest_run_id "$workspacePath")"
  runPath="$workspacePath/runs/$latestRunId"
  runFiles=("Prompt.md" "Plan.md" "Implement.md" "Documentation.md" "trace.jsonl")
  missing_run_files=()
  if [[ -z "$latestRunId" ]]; then
    missing_run_files+=("agent-state.md Latest run line")
  else
    for file in "${runFiles[@]}"; do
      [[ -f "$runPath/$file" ]] || missing_run_files+=("runs/$latestRunId/$file")
    done
  fi

  if [[ ${#missing_run_files[@]} -eq 0 ]]; then
    add_result "active-run-files" "true" "latest run $latestRunId is resumable"
  else
    add_result "active-run-files" "false" "missing: $(IFS=', '; echo "${missing_run_files[*]}")"
  fi

  changed=()
  if [[ "$SkipGitStatus" == true ]]; then
    add_result "git-status-readable" "true" "skipped by -SkipGitStatus"
    add_result "changed-paths-allowed" "true" "skipped by -SkipGitStatus"
    add_result "scope-contract" "true" "skipped by -SkipGitStatus"
    add_result "guard-sensitive-clean" "true" "skipped by -SkipGitStatus"
  else
    if mapfile -t changed < <(git_changed_paths "$repoRootPath"); then
      add_result "git-status-readable" "true" "changed paths: ${#changed[@]}"
    else
      add_result "git-status-readable" "false" "No git repository found at $repoRootPath."
      changed=()
    fi

    if [[ ${#changed[@]} -gt 0 ]]; then
      mapfile -t policy_allowed_writes < <(json_array_lines "$policyPath" "allowedWrites")
      mapfile -t scope_contract_roots < <(scope_contract_allowed_roots "$workspacePath")
      allowed_roots_for_expand=("${AllowedRoots[@]}")
      if [[ ${#allowed_roots_for_expand[@]} -eq 0 ]]; then
        allowed_roots_for_expand=("$Workspace")
      fi
      mapfile -t expanded_allowed_roots < <(expand_allowed_root_patterns "${allowed_roots_for_expand[@]}" "${scope_contract_roots[@]}")
      mapfile -t project_local_patterns < <(project_local_opencode_patterns)
      effective_allowed=("${policy_allowed_writes[@]}" "${expanded_allowed_roots[@]}" "${project_local_patterns[@]}")

      outside_allowed=()
      for path in "${changed[@]}"; do
        if ! test_any_pattern "$path" "${effective_allowed[@]}"; then
          outside_allowed+=("$path")
        fi
      done

      if [[ ${#outside_allowed[@]} -eq 0 ]]; then
        add_result "changed-paths-allowed" "true" "all changed paths match allowedWrites/AllowedRoots/scope-contract"
      else
        add_result "changed-paths-allowed" "false" "outside allowedWrites/AllowedRoots/scope-contract: $(IFS=', '; echo "${outside_allowed[*]}")"
      fi

      scope_contract_detail=""
      if scope_contract_detail="$(test_scope_contract_changed_paths "$workspacePath" "${changed[@]}")"; then
        add_result "scope-contract" "true" "$scope_contract_detail"
      else
        add_result "scope-contract" "false" "$scope_contract_detail"
      fi

      mapfile -t guard_sensitive_writes < <(json_array_lines "$policyPath" "guardSensitiveWrites")
      project_local_changes=()
      guard_changes=()
      for path in "${changed[@]}"; do
        if test_any_pattern "$path" "${project_local_patterns[@]}"; then
          project_local_changes+=("$path")
          continue
        fi

        if test_any_pattern "$path" "${guard_sensitive_writes[@]}"; then
          guard_changes+=("$path")
        fi
      done

      baseline_ok=false
      baseline_detail=""
      if [[ ${#guard_changes[@]} -gt 0 && "$AllowGuardChanges" != true ]]; then
        if baseline_detail="$(test_guard_sensitive_changes_match_checksum_baseline "$workspacePath" "$Workspace" "${guard_changes[@]}")"; then
          baseline_ok=true
        fi
      fi

      if [[ ${#guard_changes[@]} -eq 0 ]]; then
        if [[ ${#project_local_changes[@]} -gt 0 ]]; then
          add_result "guard-sensitive-clean" "true" "no guard-sensitive changed paths; ignored project-local OpenCode config: $(printf '%s\n' "${project_local_changes[@]}" | sort -u | paste -sd ', ' -)"
        else
          add_result "guard-sensitive-clean" "true" "no guard-sensitive changed paths"
        fi
      elif [[ "$AllowGuardChanges" == true ]]; then
        add_result "guard-sensitive-clean" "true" "guard-sensitive changes allowed by -AllowGuardChanges: $(IFS=', '; echo "${guard_changes[*]}")"
      elif [[ "$baseline_ok" == true ]]; then
        add_result "guard-sensitive-clean" "true" "$baseline_detail"
      else
        add_result "guard-sensitive-clean" "false" "guard-sensitive changes: $(IFS=', '; echo "${guard_changes[*]}"); baseline check: $baseline_detail"
      fi
    else
      add_result "changed-paths-allowed" "true" "no git changes detected or git skipped"
      add_result "scope-contract" "true" "no git changes detected or git skipped"
      add_result "guard-sensitive-clean" "true" "no guard-sensitive changed paths detected"
    fi
  fi

  openCodeCandidates=(
    "$repoRootPath/opencode.jsonc"
    "$repoRootPath/.opencode-migrator/opencode.jsonc"
    "$repoRootPath/.opencode/opencode.jsonc"
  )
  openCodeConfig=""
  for candidate in "${openCodeCandidates[@]}"; do
    if [[ -f "$candidate" ]]; then
      openCodeConfig="$candidate"
      break
    fi
  done

  if [[ -n "$openCodeConfig" ]]; then
    if policy_fields="$(open_code_policy_ok "$openCodeConfig")"; then
      IFS=$'\t' read -r hasDenyAllEdit hasMigrationAllow hasTrustedProjectProfile <<< "$policy_fields"
      add_result "opencode-edit-policy" "true" "config=$openCodeConfig; denyAllEdit=$hasDenyAllEdit; migrationAllow=$hasMigrationAllow; trustedProject=$hasTrustedProjectProfile"
    else
      IFS=$'\t' read -r hasDenyAllEdit hasMigrationAllow hasTrustedProjectProfile <<< "$policy_fields"
      add_result "opencode-edit-policy" "false" "config=$openCodeConfig; denyAllEdit=$hasDenyAllEdit; migrationAllow=$hasMigrationAllow; trustedProject=$hasTrustedProjectProfile"
    fi
  else
    add_result "opencode-edit-policy" "true" "no project OpenCode config found; skip template-level check"
  fi
fi

failed_count="$(awk -F '\t' '$2 != "true" { count++ } END { print count + 0 }' "$RESULTS_TSV")"
if [[ "$failed_count" -eq 0 ]]; then
  status="PASS"
else
  status="FAIL"
fi

stateDir="$workspacePath/state"
mkdir -p "$stateDir"
jsonPath="$stateDir/harness-policy-result.json"
mdPath="$stateDir/harness-policy-result.md"

"$PYTHON" - "$RESULTS_TSV" "$jsonPath" "$status" "$workspacePath" <<'PY'
import datetime
import json
import sys

results_path, json_path, status, workspace = sys.argv[1:5]
checks = []
with open(results_path, encoding="utf-8") as f:
    for line in f:
        name, passed, detail = line.rstrip("\n").split("\t", 2)
        checks.append({"name": name, "passed": passed == "true", "detail": detail})

report = {
    "generatedAtUtc": datetime.datetime.now(datetime.timezone.utc).isoformat().replace("+00:00", "Z"),
    "status": status,
    "workspace": workspace,
    "checks": checks,
}
with open(json_path, "w", encoding="utf-8") as f:
    json.dump(report, f, indent=2)
    f.write("\n")
PY

{
  echo "# Harness Policy Result"
  echo
  echo "Status: **$status**"
  echo
  while IFS=$'\t' read -r name passed detail; do
    if [[ "$passed" == "true" ]]; then
      check_status="PASS"
    else
      check_status="FAIL"
    fi
    echo "- ${check_status}: $name - $detail"
  done < "$RESULTS_TSV"
} > "$mdPath"

echo "HARNESS_POLICY_$status"
echo "Report: $mdPath"
if [[ "$status" == "PASS" ]]; then
  exit 0
fi
exit 1
