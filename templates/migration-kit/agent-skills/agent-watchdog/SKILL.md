# Skill: agent-watchdog

Purpose: audit another agent's work by checking real artifacts, not summaries.

## Use when

- An executor, researcher, or orchestrator claims a task is done.
- A run is about to be handed to the user.
- A previous agent produced a plan, diff, migration run, research artifact, or final status.

## Audit inputs

Read or request:

- the user's latest request;
- active `harness-run.json` and `continuation-decision.json`;
- active run files: `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, `trace.jsonl`;
- `harness-events.jsonl`;
- git status/diff when available;
- generated reports, quality dashboards, config deltas, memory deltas, and final-gate result;
- verification logs the agent says it ran.

## Checks

Return `PASS`, `WARN`, or `BLOCK`.

Block on:

- forbidden writes outside the migration workspace;
- missing active run for implementation work;
- fake or missing verification evidence;
- TODO reduction through assertion suppression, empty tests, or hidden interactions;
- routine continuation questions despite an allowed next action;
- repeated expensive verification without a new diff/evidence;
- final-ready claims without final gate or explicit `NOT RUNTIME READY` evidence;
- state contradictions between run status, continuation decision, task-slice result, and final gate.

## Output format

```text
Verdict: PASS|WARN|BLOCK
Reason: one sentence
Evidence checked:
- ...
Gaps:
- ...
Next bounded action:
- ...
```

Do not fix issues unless the caller explicitly asked for a narrow watchdog fix and the write is permitted.
