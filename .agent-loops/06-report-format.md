# Migrator Report Format

Use this report format when stopping or completing a loop.

## Final report

```md
## Status

<READY_FOR_ACCEPTANCE | TICKET_NEEDED | BLOCKED_BY_ENVIRONMENT | BLOCKED_BY_MISSING_INPUT | MAX_ITERATIONS_REACHED>

## What was fixed

- ...

## Files changed

- `path/to/file.cs` — reason
- `path/to/test.cs` — reason

## Tests added/updated

- `TestName` — what it covers

## Commands run

```bash
dotnet build
dotnet test Migrator.Tests
```

## Verification result

- Build: pass/fail
- Tests: pass/fail
- Verify/migrate: pass/fail/not run
- Generated compile-safety: pass/fail/not checked

## Remaining risks

- ...

## Next recommended step

- ...
```

## If continuing autonomously

Do not produce a long user-facing report.

Keep a short internal note and continue.

## If ticket is needed

Use `07-ticket-needed-template.md`.
