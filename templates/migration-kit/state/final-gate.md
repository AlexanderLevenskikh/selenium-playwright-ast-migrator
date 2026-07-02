# Final Gate

The agent may claim `FINAL` only when every applicable item is PASS.
Otherwise the report must say `NOT FINAL - INVESTIGATION RESULT ONLY`.

Do not fill this file by hand as proof. Run:

```powershell
./migration/scripts/check-final-gate.ps1 -Workspace migration
```

The script writes `state/final-gate-result.md/json`; those files are the gate evidence.

For strict forensic final checks, add optional switches:

```powershell
./migration/scripts/check-final-gate.ps1 -Workspace migration -RequireOpenCodeExport -RequireExplainTodo -RequireVerificationArtifacts
```

- [ ] PASS: scope guard shows no changed files outside the migration workspace.
- [ ] PASS: guard script checksums match `.migration-kit/guard-checksums.json`.
- [ ] PASS: latest run id is consistent across state files and reports.
- [ ] PASS: TODO decreased or was classified without new dangerous suppression categories.
- [ ] PASS: `EMPTY_TEST_AFTER_SUPPRESSION` is zero or each case is explicitly classified as non-meaningful source/setup-only.
- [ ] PASS: FluentAssertions/NUnit/business assertions were not suppressed to reduce TODO.
- [ ] PASS: generated tests preserve meaningful source-backed actions/assertions.
- [ ] PASS: config-validate passed, or an actual status/handoff file explicitly says `NOT RUNTIME READY`, `BLOCKED_BY_CONFIG`, or `BLOCKED_BY_DIAGNOSTICS`.
- [ ] PASS: project verify passed, or an actual status/handoff file explicitly says `NOT RUNTIME READY`.
- [ ] PASS: migration board / explain-todo / verification artifacts are updated for the latest run.
- [ ] PASS: OpenCode/session evidence bundle is exported when `-RequireOpenCodeExport` is used, or the exact reason is recorded.

## Evidence Links

- Scope guard:
- Guard checksums:
- Latest run:
- Config validate:
- Project verify:
- Board / explain TODO:
- OpenCode export:
