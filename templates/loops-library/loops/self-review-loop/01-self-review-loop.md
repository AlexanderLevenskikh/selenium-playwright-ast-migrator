# Self Review / Pre-Acceptance Loop

You are preparing current changes for human acceptance.

## Goal

Review the current diff, fix serious issues, verify, and produce a final acceptance-ready report.

## Core loop

1. Inspect original task/context.
2. Inspect current git diff.
3. Run available verification commands.
4. Review the diff for correctness, tests, runtime risks, security, maintainability, and scope creep.
5. Classify findings as `BLOCKER`, `MAJOR`, `MINOR`, `QUESTION`.
6. Automatically fix `BLOCKER` and `MAJOR` findings when local and safe.
7. Do not automatically fix `MINOR` findings unless trivial and low-risk.
8. Rerun verification commands after fixes.
9. Perform one final review pass.
10. Stop with final report.

## Hard limits

- Maximum review-fix cycles: 2.
- Do not broaden the original task.
- Do not rewrite architecture unless the original task requires it.
- Do not perform unrelated cleanup.
- Do not weaken tests.
- Do not ask the user to choose between technical options.

## Success

Return `READY_FOR_ACCEPTANCE` when:

- build/typecheck/tests pass as applicable;
- runtime smoke passes if required;
- final review has no `BLOCKER` or `MAJOR` findings;
- remaining `MINOR`/`QUESTION` items are documented.

## If serious issues remain

If `BLOCKER`/`MAJOR` findings remain after 2 cycles, stop with:

- `NEEDS_HUMAN_REVIEW`, if decision is needed;
- `TICKET_NEEDED`, if follow-up work is substantial;
- `MAX_ITERATIONS_REACHED`, if time/cycle limit hit but path is clear.
