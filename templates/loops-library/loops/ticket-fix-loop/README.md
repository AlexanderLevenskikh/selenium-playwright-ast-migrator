# Ticket Fix Loop

Use this loop to fix a ticket/bug/task.

The goal is:

```text
understand ticket
→ reproduce or locate failing behavior
→ add/update test where possible
→ fix
→ verify
→ self-check
→ report
```

## Success condition

`READY_FOR_ACCEPTANCE` when:

- ticket behavior is addressed;
- regression coverage exists when feasible;
- checks pass;
- runtime smoke passes when needed;
- remaining risks are documented.
