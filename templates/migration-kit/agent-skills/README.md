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

A skill is useful only when the run can prove it was actually applied. For common role starts, prefer the profile recorder:

```powershell
.\migration\scripts\record-agent-skill-profile.ps1 -Profile supervised-task -Phase dispatch -Detail "Loaded the default supervised-task skill set before planning."
```

```bash
./migration/scripts/record-agent-skill-profile.sh -Profile executor-docs-first -Phase implementation -Detail "Task touches package/CI behavior, so docs-first is required."
```

### Profile shortcuts

- `orchestrator` / `supervised-task` — `plow-ahead`, `efficient-frontier`, `quick-recap`.
- `executor` — `plow-ahead`.
- `executor-docs-first` — `plow-ahead`, `read-the-damn-docs`.
- `watchdog` — `agent-watchdog`.
- `reviewer` — `agent-watchdog`, `quick-recap`.
- `wave` — `efficient-frontier`, `plow-ahead`.
- `plan-arbiter` — `plan-arbiter`.
- `final-handoff` — `quick-recap`.

For custom one-off decisions, record directly with the kit-owned writer:

```powershell
.\migration\scripts\write-agent-skill-usage.ps1 -SkillName plow-ahead -Trigger "autonomous continuation" -Phase planning -Detail "Chose the smallest bounded wave instead of asking the user."
```

```bash
./migration/scripts/write-agent-skill-usage.sh -SkillName quick-recap -Trigger "final handoff" -Phase handoff -Detail "Prepared GREEN/YELLOW/RED recap with gate evidence."
```

Both scripts append JSONL evidence to `state/agent-skill-usage.jsonl` and `runs/<run-id>/skills/agent-skill-usage.jsonl`, updates `runs/<run-id>/skills/applied-skills.md`, and emits a harness trace event. In skill-enabled workspaces, final gate requires latest-run skill usage evidence before a final handoff.

