# Agent orchestration primitives

Migrator Kit intentionally stays boring: it does not embed a heavyweight external orchestrator. Instead, every supervised/waves run gets a few machine-readable rails that make weak or noisy agents safer.

Lifecycle:

```text
plan -> claim -> execute bounded work -> collect evidence -> verify -> final gate -> continuation decision
```

## Scope contract

`migration/state/scope-contract.json` is the authoritative machine-readable boundary for a migration wave. `kit init`, `kit update`, `kit bootstrap-agent`, and `kit bootstrap-opencode` write it from `--source`.

The contract records:

- `workspaceRoot`: migration-owned state/evidence area, normally `migration`;
- `allowedSourceRoots`: the Selenium source root passed via `--source`;
- `allowedFiles`: optional exact-file override for tightly bounded tickets;
- `forbiddenRoots`: roots that must fail even if another rule would allow them;
- `allowedCommandKinds` and `forbiddenCommandPatterns` for prompt-level command discipline;
- `maxChangedFiles` and `requiresEvidence` for final-gate review.

If `--source` is omitted, the contract does **not** silently widen to the repository root. It writes an empty `allowedSourceRoots` array plus a warning. Regenerate the kit with an explicit source root before running migration waves that edit source-scoped artifacts.

## How agents get bounded work

Before planning, agents must read:

```text
AGENTS.md
migration/AGENT_CONTRACT.md
migration/state/harness-policy.json
migration/state/scope-contract.json
migration/state/harness-run.json, if it exists
migration/state/continuation-decision.json, if it exists
migration/state/final-gate-result.json, if it exists
migration/state/claims/active/*.json, if any exist
```

A wave executor may edit only files allowed by `scope-contract.json` and `harness-policy.json`. If it needs broader access, it parks the work with a blocker/review note instead of making the change.

## Claims and leases

The file-based claim MVP lives under:

```text
migration/state/claims/active/*.json
migration/state/claims/completed/*.json
migration/state/claims/stale/*.json
```

PowerShell:

```powershell
migration/scripts/new-claim.ps1 -Workspace migration -Ticket wave-003-batch-02 -Agent opencode-worker-01 -ClaimedFiles Web/Tests/CatalogTests.cs
migration/scripts/update-claim-heartbeat.ps1 -Workspace migration -Claim claim-wave-003-batch-02-opencode-worker-01
migration/scripts/complete-claim.ps1 -Workspace migration -Claim claim-wave-003-batch-02-opencode-worker-01 -Outcome completed -Evidence migration/runs/run-001/evidence/diff.patch
migration/scripts/claim-doctor.ps1 -Workspace migration
```

Bash wrappers with the same names are available for Unix-like shells. Duplicate active claims for the same ticket fail. File-level conflicts fail when two active claims overlap in `claimedFiles`. `claimedSymbols` is already part of the schema, but full Roslyn symbol extraction is future work.

Expired claims are not deleted automatically. `claim-doctor` reports them; move a claim to `state/claims/stale` only after review or a final-decision artifact.

## Safe autopilot actions

`migration/state/harness-policy.json` now has an `autopilot` block:

- safe: read-only inspection, scoped tests, migration state/evidence writes, heartbeat updates;
- review-required: dependency changes, network access, CI/harness template changes, mass formatting;
- forbidden: destructive deletes, `.git` edits, broad out-of-scope writes, disabling gates/tests, assertion suppression without an explicit task.

The policy is documentation plus machine-readable input. The deterministic enforcement still happens in scope guard, harness policy, and final gate.

## Final gate failures

`migration/scripts/check-final-gate.ps1` reads `scope-contract.json`, extends routine allowed roots with `allowedSourceRoots`, and writes a top-level `scopeContract` block to `migration/state/final-gate-result.json`.

Typical failure:

```json
{
  "scopeContract": {
    "status": "FAIL",
    "outOfScopeFiles": ["Migrator.Core/Renderer/PlaywrightDotNetRenderer.cs"],
    "reason": "Changed file is outside allowedSourceRoots/allowedFiles for this migration wave."
  }
}
```

Fix the failure by reverting the out-of-scope change, narrowing the ticket, or stopping for review and regenerating a contract that explicitly allows the broader task.

## Evidence bundle

The current implementation keeps existing run artifacts and final-gate reports. The expected P1 layout is:

```text
migration/runs/<run-id>/
  run.json
  scope-contract.json
  events.jsonl
  evidence/
    diff.patch
    test-output.txt
    verify-project.json
    kit-doctor.txt
    final-gate-result.json
```

The hash-chained event journal is documented as future work. Do not claim HMAC-grade auditability yet.

## Stale claim repair

1. Run `migration/scripts/claim-doctor.ps1 -Workspace migration`.
2. Inspect expired active claims and the current run/ticket state.
3. Move genuinely abandoned claims from `active` to `stale` in a review-backed maintenance ticket, or complete them with evidence if the work actually finished.
4. Re-run `claim-doctor` and final gate.

## Scoped tests

Prefer tests that include a project, file, category, or fully-qualified filter. Avoid broad commands such as `dotnet test .` or `dotnet test --no-filter` in waves unless a human explicitly widens the contract.


## Loop guard

`migration/scripts/check-loop-guard.ps1` / `.sh` records a normalized fingerprint of the active `Goal` + lifecycle stage + next action in `migration/state/loop-guard.json`. If the same dispatch is repeated three times without a new concrete action, the guard reports `LOOP_GUARD_BLOCKED` and the agent must stop instead of printing another copied `Goal / Progress / Next Steps` block.

Use it before post-final/current-ticket dispatch when the next response would otherwise repeat the same plan:

```powershell
migration/scripts/check-loop-guard.ps1 -Workspace migration -RunId run-001 -TicketId post-final-019 -Goal "Execute post-final-019" -Stage execute -NextAction "Run executor for current-ticket"
```

A loop-guard block is a bounded stop condition, not a success report. The final response should cite `migration/state/loop-guard.json` and explain which concrete action is missing or blocked.

## Evidence bundle v2 and event hash-chain

Use `migration/scripts/record-run-evidence.ps1` / `.sh` to add run evidence instead of copying files by hand. The script copies or writes an artifact under `migration/runs/<run-id>/evidence/`, records `sha256`, and updates `evidence/index.json`.

Examples:

```powershell
migration/scripts/record-run-evidence.ps1 -Workspace migration -RunId run-001 -Kind diff -SourcePath migration/runs/run-001/evidence/diff.patch -Required
migration/scripts/record-run-evidence.ps1 -Workspace migration -RunId run-001 -Kind test-output -Reason "No scoped tests were applicable; config-only ticket."
```

`migration/scripts/write-harness-event.ps1` now appends hash-chained events to both `state/harness-events.jsonl` and `runs/<run-id>/events.jsonl`. Final gate validates `runs/<run-id>/evidence/index.json` when present: artifact paths must exist, SHA-256 hashes must match, and the event journal must have a continuous `prevEventHash -> eventHash` chain.

This is a tamper-evident development trail, not a cryptographic HMAC audit log.

## Memory compaction receipts

Long waves can write a compact handoff with:

```powershell
migration/scripts/write-memory-compaction-receipt.ps1 -Workspace migration -RunId run-001 -Summary "Current bounded task, failing tests, scope, and next action."
```

It updates:

```text
migration/state/memory/quick-recap.md
migration/state/memory/current-ticket.md
migration/state/memory/last-errors.md
migration/state/memory/compaction-receipts.jsonl
```

The JSONL receipt records source artifacts and the facts that must survive context compaction.

## Stale claim recovery

`claim-doctor` only reports expired claims. To move reviewed abandoned claims to `state/claims/stale`, use:

```powershell
migration/scripts/move-stale-claims.ps1 -Workspace migration -ExpiredOnly -Reason "Reviewed after crash; no owner heartbeat."
```

This writes `state/claims/stale-ledger.jsonl`. Do not move active claims to stale without a concrete review reason.

## Command policy preflight

Before running a command that is not obviously safe, classify it:

```powershell
migration/scripts/evaluate-command-policy.ps1 -Workspace migration -Command "dotnet test ."
```

The classifier returns `COMMAND_POLICY_SAFE`, `COMMAND_POLICY_REVIEW_REQUIRED`, or `COMMAND_POLICY_FORBIDDEN`. Scope guard/final gate still enforce consequences; this preflight is a low-friction early warning.
