---
description: Run a task through orchestrator with watchdog and review checkpoints
agent: orchestrator
---

Task:
$ARGUMENTS

Use the supervised workflow:

1. Read AGENTS.md and relevant files.
2. Make a short plan.
3. Ask watchdog to check the plan.
4. Delegate implementation to executor only if needed.
5. Ask watchdog to check the result.
6. Ask reviewer to review the diff.
7. Apply only minimal fixes if needed.
8. Stop after at most 2 fix-review cycles unless the user asks to continue.
9. Final report:
   - changed files;
   - verification result;
   - remaining risks;
   - anything intentionally not fixed.
