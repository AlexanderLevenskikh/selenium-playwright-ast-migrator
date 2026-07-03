---
description: Create or resume a Migrator Harness Kit run before autopilot work.
agent: orchestrator
---

Task:
$ARGUMENTS

Prepare a Harness Kit run for this task.

1. Read `migration/state/harness-policy.json` and `migration/state/harness-run.json` if present.
2. If there is no matching active run, create one with `migration/scripts/new-harness-run.ps1` using a concise task title and goal derived from `$ARGUMENTS`.
3. If there is a matching active run, resume it instead of creating a duplicate.
4. Read the active run files:
   - `migration/runs/<run-id>/Prompt.md`
   - `migration/runs/<run-id>/Plan.md`
   - `migration/runs/<run-id>/Implement.md`
   - `migration/runs/<run-id>/Documentation.md`
   - `migration/runs/<run-id>/trace.jsonl`
5. Report:
   - active run id;
   - whether it was created or resumed;
   - which run files are present;
   - the next allowed action under `harness-policy.json`.
6. Do not implement the task unless the user explicitly requested implementation in this command.
