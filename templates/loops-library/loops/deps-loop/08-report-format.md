# Dependency Security Report Format

Use this when stopping or completing the loop.

```md
## Status

<READY_FOR_ACCEPTANCE | TICKET_NEEDED | BLOCKED_BY_ENVIRONMENT | BLOCKED_BY_MISSING_INPUT | MAX_ITERATIONS_REACHED>

## Task

<Original dependency/security task>

## Batches completed

| Batch | Packages | Reason | Result |
|---|---|---|---|
| 1 | ... | ... | ... |

## Files changed

- `package.json` — ...
- lockfile — ...
- other files — ...

## Verification commands run

```bash
...
```

## Audit/security result

Before:

- Critical:
- High:
- Total:

After:

- Critical:
- High:
- Total:

## Dependency tree notes

- Vulnerable package removed:
- Remaining vulnerable package:
- Reason:

## Code migrations

- ...

## Runtime smoke

- Required:
- Tool:
- Start command:
- Local URL:
- Scenario:
- Result:
- Console errors:
- Failed network requests:

## Remaining risks

- ...

## Next recommended step

- ...
```
