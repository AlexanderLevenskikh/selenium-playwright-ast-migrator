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
## Usage evidence

A skill is useful only when the run can prove it was actually applied. When a skill materially changes planning, execution, review, or handoff, record it with the kit-owned writer:

```powershell
.\migration\scripts\write-agent-skill-usage.ps1 -SkillName plow-ahead -Trigger "autonomous continuation" -Phase planning -Detail "Chose the smallest bounded wave instead of asking the user."
```

```bash
./migration/scripts/write-agent-skill-usage.sh -SkillName quick-recap -Trigger "final handoff" -Phase handoff -Detail "Prepared GREEN/YELLOW/RED recap with gate evidence."
```

This appends JSONL evidence to `state/agent-skill-usage.jsonl` and `runs/<run-id>/skills/agent-skill-usage.jsonl`, updates `runs/<run-id>/skills/applied-skills.md`, and emits a harness trace event. In skill-enabled workspaces, final gate requires latest-run skill usage evidence before a final handoff.

