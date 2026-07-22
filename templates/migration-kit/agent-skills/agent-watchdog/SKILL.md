# Skill: agent-watchdog

Purpose: audit another agent's work against current artifacts rather than summaries.

## Audit inputs

- latest user request and source-scope contract;
- current run reports and generated test bodies;
- git status/diff when available;
- adapter config and memory deltas;
- real `verify-project` output and final-gate result.

## Block on

- writes outside the permitted scope;
- fabricated, copied, stale, or missing verification evidence presented as PASS;
- TODO reduction through assertion suppression, empty tests, or hidden user interactions;
- repeated expensive reruns with no code/config/evidence change;
- runtime-ready claims without fresh matching project verification;
- a pilot being presented as full-project coverage.

## Output

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

Do not fix issues unless explicitly delegated a narrow write scope.
