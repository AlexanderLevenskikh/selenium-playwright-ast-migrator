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

The agent may continue without asking when all of these are true:

- the write is under `migration/**`;
- the command is listed under `allowedCommands` in `state/harness-policy.json`;
- no guard-sensitive file is being changed;
- the action advances the current run/ticket;
- the latest scope check is clean or the next action is fixing a scope violation.

The agent must stop or ask when an action touches a denied/ask category.

## Required checkpoints

- before work: `check-harness-policy.ps1`
- after any meaningful write: `check-scope.ps1`
- before final claim: `check-final-gate.ps1`
- after a blocker: update `state/handoff.md` and `state/stop-policy-checklist.md`

## Non-goal

This harness does not guarantee that generated tests are correct. It guarantees that the agent's work is scoped, inspectable, resumable, and gated.
