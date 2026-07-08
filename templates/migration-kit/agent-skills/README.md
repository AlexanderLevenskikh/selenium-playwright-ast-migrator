# Agent skills

This directory contains small, composable `SKILL.md` contracts for migration agents.

They are intentionally lightweight: the Harness Kit remains the source of truth for state, scope, and gates, while skills describe *how* an agent should behave in a common situation.

Read `skill-map.md` first. It tells each role which skills to load.

## Installed skills

- `plow-ahead` — continue through routine ambiguity without noisy user prompts.
- `read-the-damn-docs` — require authoritative docs or local source evidence before dependency/API work.
- `agent-watchdog` — audit another agent's claims against real diffs, run state, and verification evidence.
- `efficient-frontier` — reserve the strongest model/role for judgment; delegate bounded heavy work.
- `quick-recap` — finish with a concise green/yellow/red status signal.
- `plan-arbiter` — compare competing plans and choose one executable direction.

## Safety boundary

Skills never override:

- `migration/AGENT_CONTRACT.md`;
- `migration/state/harness-policy.json`;
- OpenCode permissions;
- scope/final-gate scripts;
- explicit user constraints.

If a skill and a gate disagree, the gate wins.
