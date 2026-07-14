# Quality–profit arbitration

Use this skill only at a bounded wave boundary.

## Objective

Choose the highest-value safe next action without weakening deterministic quality gates.

## Non-negotiable rule

Hard invariants are facts, not preferences. Never accept or defer a wave while the deterministic packet reports a failed hard gate. Never edit metric or gate evidence to improve a score.

## Decision method

For every candidate, estimate:

```text
expected payoff = occurrences × affected tests × blocking severity × confidence / estimated remediation cost
```

Prefer reusable root fixes that unlock many tests. Defer only genuinely non-blocking review debt. Stop honestly when the bounded remediation budget or no-progress threshold is exhausted.

Allowed decisions:

- `ACCEPT_WAVE`
- `REMEDIATE_CURRENT_WAVE`
- `SPLIT_WAVE`
- `DEFER_SOFT_DEBT`
- `STOP_BUDGET_EXHAUSTED`
- `REQUEST_HUMAN_DECISION`

`fast` reduces ceremony and validation breadth, never hard quality thresholds.
