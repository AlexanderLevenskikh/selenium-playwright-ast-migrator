# SOURCE_ONLY_IDENTIFIER pattern backlog

`SOURCE_ONLY_IDENTIFIER(page/pagef/modal/lightbox/...)` is a symptom, not a final root cause.

Autopilot agents must not group TODO only by the root identifier.

## Required approach

1. Group TODO by full source expression.
2. Normalize repeated patterns.
3. Build a top-N backlog by frequency and migration impact.
4. For each pattern, inspect source truth: POM declarations, helper methods, selector builders, target Playwright architecture, existing config/profile rules.
5. Fix one coherent pattern at a time.
6. Add/update regression tests when the behavior is generic migrator behavior.
7. Run build/tests/verify.
8. Continue according to `migration/AGENT_CONTRACT.md` and `migration/state/stop-policy-checklist.md`.

## Hard rules

- Do not remove Selenium/POM roots from `SourceOnlyIdentifiers` just to reduce TODO.
- Do not add Selenium/POM roots to `TargetKnownIdentifiers` unless they really exist in target Playwright code.
- Do not create broad suppressions for assertions or user actions.
- Escalate concrete generic blockers, not the whole root variable.
