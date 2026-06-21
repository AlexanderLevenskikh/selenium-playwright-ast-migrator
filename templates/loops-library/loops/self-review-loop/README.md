# Self Review / Pre-Acceptance Loop

Use this after an agent has implemented something.

The goal is to make the agent review its own work, fix serious issues, verify again, and then stop.

This is not an infinite polish loop.

## Default hard limit

```text
Maximum review-fix cycles: 2
```

## Success condition

`READY_FOR_ACCEPTANCE` when:

- checks pass;
- final review has no `BLOCKER` or `MAJOR` findings;
- remaining `MINOR`/`QUESTION` items are reported;
- scope did not grow unexpectedly.
