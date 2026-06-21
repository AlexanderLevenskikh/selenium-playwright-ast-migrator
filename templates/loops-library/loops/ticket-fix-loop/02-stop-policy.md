# Ticket Fix Stop Policy

## Continue autonomously when

- tests fail with actionable output;
- several implementation approaches exist and one is safer;
- a regression test needs to be added;
- a helper needs a small extension;
- docs/config need small updates due to the fix;
- runtime smoke fails with actionable output;
- the bug can be narrowed by inspecting code/tests/logs.

## Stop only when

1. Ticket requirements are product/business ambiguous.
2. Required files/services/config are missing.
3. The same failure repeats after 3 serious attempts.
4. Fix requires broad architecture rewrite outside ticket scope.
5. Manual QA/risk acceptance is required and cannot be automated.
6. Environment prevents verification.
7. Max iterations reached.
8. Ticket is fixed and verified.

## Forbidden stop reasons

Do not stop with:

- "Which approach should I use?"
- "Should I continue?"
- "Tests failed; what now?"
- "I can add tests if you want."

Instead, choose the safest local action and continue.
