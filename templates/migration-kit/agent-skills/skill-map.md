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

## Load order

1. Always read `migration/AGENT_CONTRACT.md` and `migration/state/harness-policy.json` first.
2. Read the active run files: `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and `trace.jsonl`.
3. Select one or two skills from this map based on the task.
4. Apply the skill only inside the current Harness Kit permissions and allowed writes.
5. Record material decisions in run documentation or harness events.

## Non-goals

- Skills are not new permissions.
- Skills are not a reason to skip watchdog/reviewer/final gate.
- Skills are not a replacement for source truth.
- Skills must not turn migration-artifact work into real project edits.
