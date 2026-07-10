# Final Gate

The agent may claim `FINAL` only when every applicable item is PASS.
Otherwise the report must say `NOT FINAL - INVESTIGATION RESULT ONLY`, then follow `state/continuation-decision.json`. If it says `CONTINUE_REQUIRED`, the agent must continue before sending a user-facing handoff.

When the gate passes with `FINAL`, the current supervised-task run creates a successful checkpoint: stop once, report evidence, and name one recommended next command: `/supervised-task continue`. Do not start another run automatically in the same fresh checkpoint. On any later `/supervised-task` invocation where `harness-run.json` is already `FINAL_STOPPED_FOR_REVIEW`, resume the closed post-final research/research-lead/task-slicing/change-review loop automatically; explicit continue remains supported but is not required. Implementation starts only after approved research/current-ticket, change-review approval, a concrete implementation request, or bounded auto-continuation.

Do not fill this file by hand as proof. Run:

```powershell
./migration/scripts/check-final-gate.ps1 -Workspace migration
```

The script writes `state/final-gate-result.md/json` and `state/continuation-decision.md/json`; those files are the gate evidence and continuation decision. If the gate/sentinel diagnostics are blocking and there is no bounded ticket yet, the continuation decision may name `migration/scripts/slice-gate-followups.ps1`; run it to create `state/backlog/gate-followup-tasks.jsonl`, `state/backlog/gate-followup-backlog.md`, and `current-ticket.md` before another wave.

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


When gate/sentinel diagnostics create `current-ticket.md`, the next loop must track the ticket with `state/current-ticket-status.json`. A current ticket is considered active until `DONE` with validation evidence or `BLOCKED` with a concrete reason; do not start another wave first.

High/critical sentinel findings are evaluated through the finding lifecycle overlay. `sentinel-findings.jsonl` is immutable evidence; status changes live in `state/sentinel-finding-ledger.jsonl` / `runs/<run-id>/sentinel/sentinel-finding-lifecycle.jsonl`. A high/critical agent-executable finding blocks final gate until `update-sentinel-finding-status` records `VERIFIED`, `CLOSED`, `NON_AGENT_EXECUTABLE`, or `ACCEPTED_RISK` with evidence.

## Wave quality budget

Final gate includes `wave-quality-budget`. If `runs/wave-*` artifacts exist, `state/wave-quality-budget.json` or `runs/<run-id>/wave-quality-budget.json` must exist with schema `wave-quality-budget/v1`. `PASS` means the next wave may be considered after all normal gates pass. `BLOCKED_BY_WAVE_QUALITY_BUDGET` means the supervisor must switch to mapping/research/config improvement before another wave.


## Mapping/research memory gate

When `wave-quality-budget/v1` is `BLOCKED_BY_WAVE_QUALITY_BUDGET`, the final gate requires `mapping-research-memory/v1` evidence before another wave. Generate it with `migration/scripts/collect-mapping-research-memory.ps1` / `.sh`; it must summarize TODO clusters, syntax-fallback clusters, unmapped targets, unresolved symbols, verify blockers, and candidate bounded improvements.


## Artifact hygiene gate

Final gate includes `artifact-hygiene`. It invokes `migration/scripts/validate-run-artifacts.ps1` when present and expects schema `artifact-hygiene/v1`. The hygiene report blocks final handoff when existing artifacts contradict the gate/state evidence: polluted `Plan.md`, optimistic `Documentation.md` while blocked, missing run/wave identity on generated boards/status reports, or fake session export status.

### Bounded remediation and fresh restart

Wavefront plans start with a one-test smoke wave and use `preflight-budget.json` to enforce test/file/action/complexity limits. Automatic post-final remediation is limited to four completed tickets per wave and two consecutive no-progress tickets. `wave-progress/v1` requires executable or assertion restoration; TODO deletion alone is not progress. When the budget is exhausted, final gate emits `FINAL_WITH_LIMITATIONS` and harness state `WAVE_REMEDIATION_BUDGET_EXHAUSTED`; the closed post-final loop must stop. Use `/supervised-task waves fresh` or `scripts/start-fresh-wavefront-run.ps1` / `.sh` to archive the pilot while preserving project memory.
