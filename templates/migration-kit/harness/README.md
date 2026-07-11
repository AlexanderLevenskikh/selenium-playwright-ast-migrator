# Agent Harness Reference

This is a reference for the installed `migration/` workspace. It is not a replacement for the canonical guarded OpenCode Desktop runbook.

## The harness in one sentence

The harness is the migration agent's fenced workbench: policy, run files, prompts, traces, and deterministic gates.

## Source of truth order

1. `AGENT_CONTRACT.md`
2. `state/harness-policy.json`
3. `current-ticket.md`
4. `runs/<run-id>/Prompt.md`
5. `runs/<run-id>/Plan.md`
6. deterministic guard scripts
7. agent final message, only after final gate passes

## Autopilot behavior

## Agent skills layer

The Harness Kit ships a small skill layer under `agent-skills/`. Use `agent-skills/skill-map.md` to decide whether the current step needs `plow-ahead`, `read-the-damn-docs`, `agent-watchdog`, `efficient-frontier`, `quick-recap`, or `plan-arbiter`.

This keeps common behavior reusable without weakening the fenced workbench: skills cannot broaden allowed writes, bypass permission denials, or replace scope/final-gate evidence.


The agent may continue without interactive approvals when all of these are true:

- the write is under `migration/**`;
- the command is not matched by `deniedCommands` in `state/harness-policy.json`;
- no guard-sensitive file is being changed, changed kit-owned guard files match `.migration-kit/guard-checksums.json` after a trusted kit update, or `guard-checksums.json` changed only by metadata timestamp churn while all guard file hashes still match;
- the action advances the current run/ticket;
- the latest scope check is clean or the next action is fixing a scope violation.

The agent must stop with a concrete blocker when an action touches a denied category. The low-noise autopilot profile intentionally avoids interactive ask prompts.

## Required checkpoints

- before work: `check-harness-policy.ps1`
- after any meaningful write: `check-scope.ps1`
- before final claim: `check-final-gate.ps1`
- after a blocker: update `state/handoff.md` and `state/stop-policy-checklist.md`

## Non-goal

This harness does not guarantee that generated tests are correct. It guarantees that the agent's work is scoped, inspectable, resumable, and gated.


Applied skills are recorded with `scripts/record-agent-skill-profile.ps1` / `.sh` for common role profiles, or `scripts/write-agent-skill-usage.ps1` / `.sh` for custom one-off decisions, into `state/agent-skill-usage.jsonl` and `runs/<run-id>/skills/applied-skills.md`.


## Gate follow-up slicer

`migration/scripts/slice-gate-followups.ps1` / `.sh` converts final-gate and sentinel diagnostics into `state/backlog/gate-followup-tasks.jsonl`, `state/backlog/gate-followup-backlog.md`, and `current-ticket.md` before another wave starts.


## Current-ticket lifecycle

Use `migration/scripts/update-sentinel-finding-status.ps1` or `.sh` to record `OPEN`, `ASSIGNED`, `FIX_ATTEMPTED`, `VERIFIED`, `CLOSED`, `BLOCKED`, `NON_AGENT_EXECUTABLE`, or `ACCEPTED_RISK` transitions for sentinel findings. Final gate overlays these lifecycle statuses on top of `sentinel-findings.jsonl` so high/critical findings stop blocking only when they are verified/closed or explicitly classified as non-agent-executable/accepted risk.

Use `migration/scripts/update-current-ticket-status.ps1` or `.sh` to record ticket transitions. The latest status is `state/current-ticket-status.json`; the append-only audit log is `state/current-ticket-ledger.jsonl`. A non-terminal ticket (`READY`, `IN_PROGRESS`, or `REVIEW_READY`) has priority over wave selection.

### Wave quality budget

Run `migration/scripts/evaluate-wave-quality-budget.ps1` or `.sh` after each `runs/wave-*` execution. Final gate checks `wave-quality-budget/v1` evidence and blocks `BLOCKED_BY_WAVE_QUALITY_BUDGET` waves from continuing until mapping/research/config improvement evidence exists.

## Mapping/research memory evidence

If wave quality is blocked, final gate expects `mapping-research-memory/v1` evidence before the next wave. Run `migration/scripts/collect-mapping-research-memory.ps1` / `.sh` to create `state/mapping-research-memory.*` and `state/mapping-research-candidates.jsonl`, then route one bounded config/POM/recognizer or verify-harness ticket.

For sharing a migration gap with the migrator maintainer, run `migration/scripts/create-feedback-bundle.ps1` / `.sh`. The resulting `feedback-bundle/v1` zip is safe by default: it excludes project source and generated C# samples unless `-IncludeGeneratedSamples` is explicitly used.


## Artifact hygiene evidence

Run `migration/scripts/validate-installed-scripts.ps1 -Workspace migration` or `.sh` first, then run `migration/scripts/validate-run-artifacts.ps1` or `.sh` to create `artifact-hygiene/v1` reports in `state/artifact-hygiene.*` and `runs/<run-id>/artifact-hygiene.*`. Final gate invokes the same check. It cross-checks Plan.md sanitization, Documentation.md versus final gate status, run/wave identity in generated boards/status files, and honest session export status.

### Bounded remediation and fresh restart

Wavefront plans start with a one-test smoke wave. `plan --wave-profile auto` writes `wave-tuning.md/json` without invoking agents, then affinity-packs later tests by source file/POM context using same-file marginal complexity. `preflight-budget.json` uses soft targets for normal packing and a broader hard ceiling; `PASS`, `SOFT_LIMIT_EXCEEDED`, and `HEAVY_SINGLE_TEST` are executable, while only `BLOCKED` requires replan. Automatic post-final remediation is limited to four completed tickets per wave and two consecutive no-progress tickets. `wave-progress/v1` requires executable or assertion restoration; TODO deletion alone is not progress. When the budget is exhausted, final gate emits `FINAL_WITH_LIMITATIONS` and harness state `WAVE_REMEDIATION_BUDGET_EXHAUSTED`; the closed post-final loop must stop. Use `/supervised-task waves fresh` or `scripts/start-fresh-wavefront-run.ps1` / `.sh` to archive the pilot while preserving project memory.
