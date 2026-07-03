---
description: Run a task through orchestrator with Harness Kit lifecycle, watchdog, review checkpoints, and final gate evidence.
agent: orchestrator
---

Task:
$ARGUMENTS

Use the supervised Harness Kit workflow:

1. Before planning, read:
   - AGENTS.md
   - migration/AGENT_CONTRACT.md
   - migration/state/harness-policy.json
   - migration/state/harness-run.json, if it exists
   - migration/state/final-gate.md
   - migration/state/stop-policy-checklist.md
2. Create or resume the active run:
   - if no matching active run exists, run `migration/scripts/new-harness-run.ps1` with the task title and goal;
   - if a matching run exists, read its `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and `trace.jsonl`.
3. Before each major action, state which AGENT_CONTRACT or harness-policy rule allows it.
4. Make or update a short plan in the active run context.
5. Record important lifecycle events when practical with `migration/scripts/write-harness-event.ps1`, especially `plan-written`, `scope-check-pass`, `tests-pass`, `tests-failed`, `final-gate-pass`, and `handoff-written`.
6. Ask watchdog to check the plan.
7. Delegate implementation to executor only if needed, and include the active run id.
8. Ask watchdog to check the result.
9. Ask reviewer to review the diff and active run evidence.
10. Run `migration/scripts/check-scope.ps1` after any patch.
11. Run `migration/scripts/check-harness-policy.ps1` after any patch.
12. Apply only minimal fixes if needed.
13. Stop after at most 2 fix-review cycles unless the user asks to continue.
14. Do not ask routine continuation questions when the next action is allowed by harness-policy and OpenCode permissions.
15. Do not issue FINAL unless `migration/scripts/check-final-gate.ps1 -RequireOpenCodeExport -RequireExplainTodo -RequireVerificationArtifacts` passes and migration/state/final-gate.md can be marked PASS with evidence. Otherwise say NOT FINAL - INVESTIGATION RESULT ONLY.
16. Final report:
   - active run id;
   - changed files;
   - verification result;
   - scope guard result;
   - harness-policy result;
   - final gate result;
   - trace/events status;
   - remaining risks;
   - anything intentionally not fixed.
