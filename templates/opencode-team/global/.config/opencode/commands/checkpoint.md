---
description: Ask watchdog to audit current task state
agent: watchdog
subtask: true
---

Audit the current task state.

Check:
- alignment with the user's latest request;
- AGENTS.md compliance;
- git diff scope;
- unsafe commands;
- missing verification;
- hallucinated claims;
- whether the executor/orchestrator is drifting;
- whether it is safe to continue.

Return PASS / WARN / BLOCK.
