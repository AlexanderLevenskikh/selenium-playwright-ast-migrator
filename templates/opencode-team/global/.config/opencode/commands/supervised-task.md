---
description: Run a task through orchestrator with watchdog and review checkpoints
agent: orchestrator
---

Task:
$ARGUMENTS

Use the supervised workflow:

1. Before planning, read:
   - AGENTS.md
   - migration/AGENT_CONTRACT.md
   - migration/state/final-gate.md
   - migration/state/stop-policy-checklist.md
2. Before each major action, state which AGENT_CONTRACT rule allows it.
3. Make a short plan.
4. Ask watchdog to check the plan.
5. Delegate implementation to executor only if needed.
6. Ask watchdog to check the result.
7. Ask reviewer to review the diff.
8. Run migration/scripts/check-scope.ps1 after any patch.
9. Apply only minimal fixes if needed.
10. Stop after at most 2 fix-review cycles unless the user asks to continue.
11. Do not issue FINAL unless `migration/scripts/check-final-gate.ps1 -RequireOpenCodeExport -RequireExplainTodo -RequireVerificationArtifacts` passes and migration/state/final-gate.md can be marked PASS with evidence. Otherwise say NOT FINAL - INVESTIGATION RESULT ONLY.
12. Final report:
   - changed files;
   - verification result;
   - scope guard result;
   - final gate result;
   - remaining risks;
   - anything intentionally not fixed.
