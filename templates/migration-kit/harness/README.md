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


Applied skills are recorded with `scripts/write-agent-skill-usage.ps1` / `.sh` into `state/agent-skill-usage.jsonl` and `runs/<run-id>/skills/applied-skills.md`.
