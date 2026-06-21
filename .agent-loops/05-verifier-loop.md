# Migrator Independent Verifier Loop

This loop verifies migrator changes independently.

Use it after an implementation loop or when the user asks for review/verification.

## Goal

Verify that the migrator changes are correct, covered, and do not introduce regressions.

## Core rule

Trust command output, repository diff, tests, snapshots, and generated reports.

Do not trust previous agent claims.

## Verification steps

1. Inspect git diff.
2. Identify intended behavior changes.
3. Check whether every behavior change has regression coverage.
4. Run build/tests.
5. Run verify/migrate command if a real input path is available.
6. Inspect generated output or reports for regressions.
7. Classify the result.

## Commands

Run when applicable:

```bash
dotnet build
dotnet test Migrator.Tests
```

If real input exists:

```bash
dotnet run --project Migrator.Cli -- --mode verify --input <SOURCE_SELENIUM_TESTS> --out <VERIFY_OUT>
```

Use actual project paths from the repository.

## Allowed fixes during verifier loop

The verifier may fix:

- missed compile errors;
- broken tests;
- snapshot expectations that are objectively stale because of the intended change;
- missing regression tests;
- small local mistakes introduced by the implementer.

The verifier must stop if fixing requires:

- broad redesign;
- product/business knowledge;
- changing the selected scope;
- weakening tests;
- accepting unsafe generated code.

## Verifier statuses

Return exactly one:

- `VERIFIED_OK`
- `VERIFIED_WITH_MINOR_FIXES`
- `VERIFICATION_FAILED`
- `TICKET_NEEDED`
- `BLOCKED_BY_ENVIRONMENT`

## Verifier report

Always include:

1. Status.
2. Commands run.
3. Test/build result.
4. Diff summary.
5. Coverage assessment.
6. Risks.
7. Acceptance recommendation.

## Verify continuation behavior

The verifier should check whether the implementer stopped too early.

If a batch is `READY_FOR_ACCEPTANCE` but the migration board still has actionable categories, the verifier should report:

```text
Batch status: READY_FOR_ACCEPTANCE
Overall loop recommendation: CONTINUE_AUTONOMOUSLY
```

A green build/project verify proves compile safety, not migration completeness.
