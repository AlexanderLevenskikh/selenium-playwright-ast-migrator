# Dependency Security Reviewer Loop

Use after implementation loop.

## Goal

Verify dependency/security changes independently.

## Trust only

- git diff;
- package.json;
- lockfile;
- command output;
- audit output;
- dependency tree;
- tests/build/typecheck/lint results.

Do not trust previous agent claims.

## Review steps

1. Inspect `package.json` diff.
2. Inspect lockfile diff for unrelated churn.
3. Check batch boundaries.
4. Verify vulnerabilities were actually reduced.
5. Run install/build/typecheck/test/audit when available.
6. Check for weakened tests/lint/audit thresholds.
7. Check unsafe `resolutions`/`overrides`.
8. Check whether code migrations are minimal and covered.
9. Return acceptance recommendation.

## Statuses

- `VERIFIED_OK`
- `VERIFIED_WITH_MINOR_FIXES`
- `VERIFICATION_FAILED`
- `TICKET_NEEDED`
- `BLOCKED_BY_ENVIRONMENT`
