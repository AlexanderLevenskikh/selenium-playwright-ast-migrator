# Finding Severity

Use this severity model in review and self-review loops.

## BLOCKER

Must fix before acceptance.

Examples:

- build/test failure;
- obvious runtime break;
- security issue introduced;
- data loss risk;
- broken public API;
- incorrect business behavior;
- missing required migration;
- dangerous hidden failure.

## MAJOR

Should fix before acceptance if local and safe.

Examples:

- likely regression;
- missing important test;
- incorrect edge-case behavior;
- poor error handling in changed code;
- significant maintainability risk;
- scope creep causing risk.

## MINOR

Can be reported without blocking acceptance.

Examples:

- naming polish;
- small duplication;
- small readability issue;
- minor docs wording;
- non-critical cleanup.

## QUESTION

Needs clarification only if it blocks correctness.

If answer can be inferred from code/tests/docs, infer and continue.
If it requires product/business decision, stop with `NEEDS_HUMAN_REVIEW`.
