# Migration agent skill map

Use this map to load only the skill that matches the current situation. Do not paste all skills into every subtask.

## Default role mapping

| Situation | Primary skill | Typical owner |
|---|---|---|
| User asked for autonomous/bounded continuation | `plow-ahead/SKILL.md` | orchestrator, executor |
| Third-party dependency, SDK, CLI, framework, package, auth, browser, CI, or migration-guide work | `read-the-damn-docs/SKILL.md` | orchestrator, researcher, executor |
| Reviewing another agent's claims or final handoff | `agent-watchdog/SKILL.md` | watchdog |
| Broad task with many files/TODOs/waves | `efficient-frontier/SKILL.md` | orchestrator |
| Final answer or handoff | `quick-recap/SKILL.md` | orchestrator, reviewer |
| Two or more plausible plans disagree | `plan-arbiter/SKILL.md` | orchestrator, reviewer |

## Profile shortcuts

For common role starts, prefer the profile recorder so the run captures consistent usage evidence without long hand-written commands.

| Profile | Records | Typical command |
|---|---|---|
| `orchestrator` | `plow-ahead`, `efficient-frontier`, `quick-recap` | `migration/scripts/record-agent-skill-profile.ps1 -Profile orchestrator -Phase planning` |
| `supervised-task` | `plow-ahead`, `efficient-frontier`, `quick-recap` | `migration/scripts/record-agent-skill-profile.ps1 -Profile supervised-task -Phase dispatch` |
| `executor` | `plow-ahead` | `migration/scripts/record-agent-skill-profile.ps1 -Profile executor -Phase implementation` |
| `executor-docs-first` | `plow-ahead`, `read-the-damn-docs` | `migration/scripts/record-agent-skill-profile.ps1 -Profile executor-docs-first -Phase implementation` |
| `watchdog` | `agent-watchdog` | `migration/scripts/record-agent-skill-profile.ps1 -Profile watchdog -Phase review` |
| `reviewer` | `agent-watchdog`, `quick-recap` | `migration/scripts/record-agent-skill-profile.ps1 -Profile reviewer -Phase review` |
| `wave` | `efficient-frontier`, `plow-ahead` | `migration/scripts/record-agent-skill-profile.ps1 -Profile wave -Phase planning` |
| `plan-arbiter` | `plan-arbiter` | `migration/scripts/record-agent-skill-profile.ps1 -Profile plan-arbiter -Phase planning` |
| `final-handoff` | `quick-recap` | `migration/scripts/record-agent-skill-profile.ps1 -Profile final-handoff -Phase handoff` |

Use `write-agent-skill-usage.ps1` / `.sh` directly only when a custom one-off skill decision is more precise than a profile.

## Load order

1. Always read `migration/AGENT_CONTRACT.md` and `migration/state/harness-policy.json` first.
2. Read the active run files: `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and `trace.jsonl`.
3. Select one or two skills from this map based on the task.
4. Apply the skill only inside the current Harness Kit permissions and allowed writes.
5. Record common role usage with `migration/scripts/record-agent-skill-profile.ps1` / `.sh`; record custom one-off decisions with `migration/scripts/write-agent-skill-usage.ps1` / `.sh` whenever a skill materially changes planning, execution, review, or handoff.
6. Record material decisions in run documentation or harness events.

## Non-goals

- Skills are not new permissions.
- Skills are not a reason to skip watchdog/reviewer/final gate.
- Skills are not a replacement for source truth.
- Skills must not turn migration-artifact work into real project edits.
- Skills must be evidence-backed: no final handoff should claim skill-driven behavior without `runs/<run-id>/skills/applied-skills.md`.
