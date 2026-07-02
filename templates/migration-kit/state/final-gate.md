# Final Gate

The agent may claim `FINAL` only when every applicable item is PASS.
Otherwise the report must say `NOT FINAL - INVESTIGATION RESULT ONLY`.

- [ ] PASS: scope guard shows no changed files outside the migration workspace.
- [ ] PASS: latest run id is consistent across state files and reports.
- [ ] PASS: TODO decreased or was classified without new dangerous suppression categories.
- [ ] PASS: `EMPTY_TEST_AFTER_SUPPRESSION` is zero or each case is explicitly classified as non-meaningful source/setup-only.
- [ ] PASS: FluentAssertions/NUnit/business assertions were not suppressed to reduce TODO.
- [ ] PASS: generated tests preserve meaningful source-backed actions/assertions.
- [ ] PASS: config-validate passed, or the final report records the exact blocking diagnostics.
- [ ] PASS: project verify passed, or the final report explicitly says `NOT RUNTIME READY`.
- [ ] PASS: migration board / explain-todo / verification artifacts are updated for the latest run.
- [ ] PASS: OpenCode/session evidence bundle is exported or the exact reason is recorded.

## Evidence Links

- Scope guard:
- Latest run:
- Config validate:
- Project verify:
- Board / explain TODO:
- OpenCode export:
