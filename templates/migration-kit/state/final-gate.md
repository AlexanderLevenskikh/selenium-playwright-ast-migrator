# Final Gate

The agent may claim `FINAL` only when every applicable item is PASS.
Otherwise the report must say `NOT FINAL - INVESTIGATION RESULT ONLY`, then follow `state/continuation-decision.json`. If it says `CONTINUE_REQUIRED`, the agent must continue before sending a user-facing handoff.

When the gate passes with `FINAL`, the default is a successful checkpoint: stop, report evidence, and name one recommended next command: `/supervised-task continue`. Do not start another run automatically. A plain explicit continue starts the closed post-final research/research-lead/task-slicing loop; implementation starts only after approved research/current-ticket, a concrete implementation request, or bounded auto-continuation.

Do not fill this file by hand as proof. Run:

```powershell
./migration/scripts/check-final-gate.ps1 -Workspace migration
```

The script writes `state/final-gate-result.md/json` and `state/continuation-decision.md/json`; those files are the gate evidence and continuation decision.

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
- Continuation decision:
