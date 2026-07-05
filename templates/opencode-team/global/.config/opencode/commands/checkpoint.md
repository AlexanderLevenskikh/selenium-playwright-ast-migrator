---
description: Ask watchdog to audit current task state and Harness Kit lifecycle evidence.
agent: watchdog
subtask: true
---

Audit the current task state.

Check:
- alignment with the user's latest request;
- AGENTS.md compliance;
- `migration/state/harness-policy.json` compliance;
- whether an active Harness Kit run exists or should exist;
- whether `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and `trace.jsonl` are present for the active run;
- git diff scope;
- unsafe commands;
- missing verification;
- repeated expensive verification commands without a new diff/evidence;
- hallucinated claims;
- whether the executor/orchestrator is drifting;
- whether routine continuation questions were asked despite an allowed next action;
- whether it is safe to continue.
- if the task involves CLI installation, whether diagnostics started with PATH resolution (`Get-Command`/`where.exe`/`which -a`) rather than `dotnet tool list` only.

Return PASS / WARN / BLOCK.
