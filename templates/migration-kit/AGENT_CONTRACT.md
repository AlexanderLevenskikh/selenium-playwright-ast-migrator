# Agent Contract

This file is the short operational contract for every migration checkpoint.
Before each major action, restate which rule allows the action.

1. Allowed writes: `migration/**` only by default. Source/test-file writes are allowed only when `state/scope-contract.json` explicitly grants `allowedSourceRoots` / `allowedFiles`; the scope contract is machine authority, so do not widen it mentally.
1a. Before planning, read `state/scope-contract.json`. Do not run broad tests or inspect/edit outside its roots. If the contract omits `allowedSourceRoots`, stop for review or regenerate the kit with `--source`.
1b. Before bounded implementation, create or resume a claim with `scripts/new-claim.ps1` / `.sh` when parallel wave work is possible. Keep heartbeat current and complete the claim with evidence.
2. POM means generated/shadow/proposal POM under `migration/**`, not the real POM project.
3. Real target project, production POM, Playwright test project, `.csproj`, `nuget.config`, and root-level generated file edits are forbidden in artifact-only mode.
4. TODO reduction via suppression is failure, especially FluentAssertions/NUnit/business assertion suppression.
5. `0 TODO` is not success without scope-clean, quality-gate, and verification evidence.
6. If blocked, write a classified blocker/proposal artifact; do not ask vague continuation questions.
7. Runtime-ready/final claims require project verify evidence, or the report must say `NOT RUNTIME READY`.
8. The agent final answer is only another artifact until `state/final-gate.md` is PASS.
9. After a non-final final gate, read `state/continuation-decision.json`; if it says `CONTINUE_REQUIRED`, execute exactly one next bounded action before any user-facing handoff.

9a. Before planning or reviewing a supervised task, read `agent-skills/skill-map.md` and load only the relevant `SKILL.md` contracts. Skills guide behavior; they never override allowed writes, OpenCode permissions, scope guard, harness policy, or final gate.
9b. If a skill materially affects planning, implementation, review, or handoff, record common role profiles with `scripts/record-agent-skill-profile.ps1` / `.sh` and custom decisions with `scripts/write-agent-skill-usage.ps1` / `.sh`; final gate treats missing latest-run skill evidence as a broken handoff for skill-enabled workspaces.
9c. Keep script changes paired across platforms: any new or changed migration-kit `.ps1` lifecycle script needs a same-name `.sh` companion. A thin Unix wrapper around PowerShell is acceptable when the PowerShell script remains the source of truth; on macOS/Linux/WSL it must require PowerShell 7 (`pwsh`) with a clear install hint, while `powershell.exe` fallback is only for Windows-like Bash shells.

10. After a successful FINAL/PASS checkpoint, stop and report. Do not start another run or ticket unless the user explicitly says continue or state/continuation-decision.json grants bounded auto-continuation for that exact next action.

11. Post-final research is not a terminal human handoff. `MANUAL_REVIEW` and `Developer action` mean “needs source-backed review and task slicing” until `migration-research-lead` and `migration-task-slicer` classify the work as non-agent-executable.
12. After reviewed post-final research, create bounded tickets before asking the user to manually continue: researcher → research lead → task slicer → supervisor/executor is the default closed loop when writes stay under `migration/**`.

## Project-scoped migration memory

Before planning a bounded action:

- Read `state/memory/memory-summary.md`.
- Read `plan/plan.md`, `plan/waves.json`, and `plan/memory-recall.md` when a divide-and-conquer wave plan exists.
- Resolve the repository root before wave planning/execution. `migration/**`, `migration/plan/**`, and `migration/runs/**` are repository-root artifacts, not source-project-relative artifacts. Do not run kit/plan/run-wave from `Web/**` or any source/target subdirectory. A nested workspace such as `Web/**/migration/**` is `NESTED_MIGRATION_WORKSPACE` and must be reported/repaired before implementation continues.
- If no wave run workspace exists for the selected wave, prepare it from the repository root with `selenium-pw-migrator migration run-wave --plan migration/plan --wave <wave-id> --workspace migration --out migration/runs/<wave-id> --execution-profile fast` before implementation.
- Treat `runs/<wave-id>/wave-manifest.json` as immutable authority for selected files/tests, `execution-policy.json` as the role-routing contract, and `run-context.json` as the immutable incremental/cache contract. Run `migration validate-wave --out migration/runs/<wave-id>` before implementation/review. Existing run directories are validated/reused, never recopied; execute them through `run-migrate.ps1`/`.sh`.
- If files in scope are known, run `selenium-pw-migrator memory explain --workspace migration` and `selenium-pw-migrator memory recall --file <file> --workspace migration` for every scoped file. Recall writes machine-readable receipts to `state/memory/recall-index.json` and `recall-ledger.jsonl`; merely reading the memory files is not equivalent evidence.
- Treat memory as guidance, not authority: apply a remembered rule only when its scope/conditions match the current evidence.

After implementation/review:

- Record durable decisions, warnings, rejected approaches, and final-gate lessons with `selenium-pw-migrator memory add ...` or `migration/scripts/write-memory-entry.ps1`/`.sh`; do not hand-write or overwrite JSONL memory files.
- Emit or update wave-local `config-delta.json` and `memory-delta.jsonl`; do not silently rewrite the global adapter config as a memory side effect.
- To combine reviewed wave deltas, run `selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge`, then `selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge`.
- Treat `migration/config-merge/adapter-config.merged.json` as a candidate only; do not replace the active adapter config until Reviewer, Watchdog, and Final Gate accept `merge-report.md`, `validate-merge-report.md`, and empty `conflicts.jsonl`.
- Keep `migration/runs/<wave-id>/wave-manifest.json`, `execution-policy.json`, `run-context.json`, `input-scope.json`, `wave-validation.json`, `validation-plan.json`, `validation-host-result.json`, `validation-result.json`, `latest-checkpoint.json`, `resume-decision.json`, `review/review-bundle.json`, `agent-next-action.json`, `agent-role-events.jsonl`, `agent-budget-result.json`, `agent-lifecycle-performance.json`, `performance-trace.json`, `run-summary.md`, and `wave-status.json` as evidence for the bounded wave.
- After generated output changes, run the single validation host: `migration validate --out <wave-run> --validation-project <target-project>` or provide one explicit `--validation-command`. The host plans impact, executes internal/project checks, records stdout/stderr and exact-input PASS evidence, materializes cache hits, and writes the validation checkpoint. `validation-plan`/`record-validation` are recovery-only. A reusable PASS without executed command evidence is invalid; failed/stale validation is never cached.
- After successful validation, run `migration checkpoint-wave`, then `migration build-review-bundle`. After interruption use `migration resume-wave` and follow its single next action. Checkpoints never mean `DONE`, and review bundles never replace reviewer, sentinel, or final gate.
- After each bounded fix/review cycle run `migration check-progress --out migration/runs/<wave-id> --max-identical-snapshots 3`. `NO_PROGRESS_DETECTED` requires a watchdog/strategy change and forbids another identical retry.
- Resolve role routing with `migration next-agent-action --out migration/runs/<wave-id>` and execute exactly one emitted action. Record every dispatched role through `migration record-agent-role` with `STARTED` and terminal evidence; never hand-edit `agent-role-events.jsonl` or reuse a stale role receipt. `migration check-agent-budget` is mandatory before automatic continuation. Fast routing may omit pre-execution roles, but final reviewer, final sentinel, scope/harness checks, and final gate remain mandatory.
- Run `selenium-pw-migrator memory doctor --workspace migration` and ensure `config-delta-merge` final-gate check is clean before final-gate handoff.

Memory safety rules:

- Memory cannot justify assertion suppression.
- Memory cannot justify hiding over-suppressed user interactions.
- Selector knowledge must have evidence before it is reused.
- POM uncertainty stays reviewable until a target mapping exists.


13. Once the active run is persisted as `FINAL_STOPPED_FOR_REVIEW`, `/supervised-task` must resume the closed post-final loop even with zero arguments. It must not stop merely because research already exists, TODOs are marked manual, or the stop checklist names missing source truth. Existing research must be reviewed, revised if needed, sliced into tickets, reviewed, and one bounded migration-artifact executor task must run unless `BLOCKED_NO_AGENT_EXECUTABLE_TASKS` or a concrete reviewer/policy blocker is written. Explicit `/supervised-task continue` remains supported but is not required for this persisted state.

14. If the same Goal/Progress/Next Steps block would be repeated without a new file change, command result, reviewer decision, or gate result, run `migration/scripts/check-loop-guard.ps1` / `.sh`. If it reports `LOOP_GUARD_BLOCKED`, stop and report the blocker instead of continuing the loop.

14a. Record run evidence with `migration/scripts/record-run-evidence.ps1` / `.sh`; do not hand-edit `runs/*/evidence/index.json`. Evidence index hashes and `runs/*/events.jsonl` hash-chain are checked by final gate when present.
14b. For long waves or context compaction, write `state/memory/compaction-receipts.jsonl` with `migration/scripts/write-memory-compaction-receipt.ps1` / `.sh`; preserve current ticket, scope contract, failing tests, and blockers.
14c. Use `migration/scripts/evaluate-command-policy.ps1` / `.sh` before any non-obvious shell command. `COMMAND_POLICY_FORBIDDEN` means stop for review; do not try an equivalent command spelling.
14d. `claim-doctor` only diagnoses expired leases. Move reviewed abandoned claims with `migration/scripts/move-stale-claims.ps1` / `.sh`, which writes `state/claims/stale-ledger.jsonl`.


## Permission and state-integrity rules

14. OpenCode permission denials are authoritative. If an edit/write tool is denied, do not retry the same write through `bash`, PowerShell, Python, `sed`, `tee`, shell redirection, or any other alternate tool. Stop with `BLOCKED_BY_OPENCODE_PERMISSION_DENIED` and report the denied path and intended change.
15. JSONL ledgers are controlled append-only state; treat them as append-only JSONL ledgers. Do not manually overwrite `state/harness-events.jsonl`, `runs/*/trace.jsonl`, `state/memory/*.jsonl`, or `state/backlog/*.jsonl` with ad-hoc shell writes. Use `write-harness-event` for events/traces, `record-agent-skill-profile` or `write-agent-skill-usage` for applied skill evidence, `selenium-pw-migrator memory add` or `write-memory-entry` for memory additions, `repair-memory-jsonl` for memory-only repair, and `repair-jsonl-ledger` for explicit controlled state/backlog/run repair with a backup. `validate-run-artifacts` must parse every non-empty controlled JSONL line before handoff.
16. Machine-readable state must be consistent before handoff. If `task-slice-result` or reviewer/gate evidence says `BLOCKED_NO_AGENT_EXECUTABLE_TASKS`, `state/continuation-decision.json` must not remain `CONTINUE_REQUIRED`.



## Session export and sentinel rules

17. Each supervised run should produce a forensic session artifact at `runs/<run-id>/opencode-session-export.md`. Use `scripts/export-opencode-session.ps1`/`.sh`; if a native OpenCode transcript is unavailable, create a best-effort export and preserve observable session excerpts in `runs/<run-id>/session-observations.jsonl`. Do not invent transcript content.
18. `harness-sentinel` is the process tester. It scans session exports, trace/events, machine-readable state, prompts, and OpenCode config for process bugs such as `PERMISSION_BYPASS_ATTEMPT`, `APPEND_ONLY_VIOLATION`, `STATE_CONTRADICTION`, `PREMATURE_DONE`, `HUMAN_HANDOFF_WITHOUT_BLOCKER`, `FULL_MIGRATION_IN_WAVE_MODE`, and `STALE_ROOT_OPENCODE_CONFIG`.
19. Open high/critical sentinel findings that are agent-executable must be routed to `migration-task-slicer` as bounded process-hardening tasks before a final handoff. Sentinel recommendations are not vague advice to the user unless they are explicitly non-agent-executable. If final gate/sentinel diagnostics exist but no bounded ticket exists yet, run `migration/scripts/slice-gate-followups.ps1` / `.sh` to create `state/backlog/gate-followup-tasks.jsonl`, `state/backlog/gate-followup-backlog.md`, and `current-ticket.md`.


Sentinel inspections must be finalized with `migration/scripts/complete-sentinel-inspection.ps1` or `.sh`; final gate treats a missing active-run `sentinel-inspection.json` as a process defect. Sentinel findings have their own lifecycle: `OPEN -> ASSIGNED -> FIX_ATTEMPTED -> VERIFIED -> CLOSED`, recorded by `migration/scripts/update-sentinel-finding-status.ps1` / `.sh` in `state/sentinel-finding-ledger.jsonl` and `runs/<run-id>/sentinel/sentinel-finding-lifecycle.jsonl`. Do not mutate `sentinel-findings.jsonl`; it is forensic evidence.


Final gate reconciles `migration/state/harness-run.json` after every run: gate failure writes `BLOCKED_BY_GATE`/the concrete continuation status and real `latestChecks`; a supervisor must not continue from stale `CONTINUE_AUTONOMOUSLY` state after a failed gate.


Wave scope is file-based, not single-test-based: report `sourceFiles`, estimated/actual test count, migrated action count, and TODO count explicitly. Do not describe a wave as “3 tests” when the input scope is 3 files containing more tests.

TODO reduction is not quality evidence by itself. Remove a TODO/unresolved-symbol marker only when active equivalent code exists or source-backed evidence proves the marker obsolete; leaving the declaration/action commented out while deleting the warning is forbidden evidence manipulation.


## Current-ticket executor loop

When `migration/current-ticket.md` exists, it is the active bounded task. `/supervised-task` must read the active `execution-policy.json`, execute exactly one bounded `executor` task, and run `migration-change-reviewer` before execution when `standard`/`audit` or a deterministic trigger requires it; final review is mandatory in every profile. Track state in `migration/state/current-ticket-status.json` and append transitions to `migration/state/current-ticket-ledger.jsonl` with `migration/scripts/update-current-ticket-status.ps1` / `.sh`. Valid statuses are `READY`, `IN_PROGRESS`, `REVIEW_READY`, `DONE`, and `BLOCKED`. After executor, run `migration check-progress`; `NO_PROGRESS_DETECTED` requires watchdog/strategy change. After reviewer validation, set `DONE` before running the fresh final gate; the status script synchronizes task-slice, continuation, harness-run, and wave state. Never mark `DONE` after final gate. Do not start another wave while the current-ticket lifecycle is active unless the ticket is `DONE` with validation evidence or `BLOCKED` with a concrete non-agent-executable reason.

Wave quality budget is mandatory for wave runs: after materializing or executing `runs/wave-*`, run `migration/scripts/evaluate-wave-quality-budget.ps1` / `.sh` before the next wave. If it writes `BLOCKED_BY_WAVE_QUALITY_BUDGET`, do not start another wave; route into mapping/research memory and one bounded config/POM/recognizer improvement ticket.

## Mapping/research memory loop

When `evaluate-wave-quality-budget` reports `BLOCKED_BY_WAVE_QUALITY_BUDGET`, do not start another wave. First run `migration/scripts/collect-mapping-research-memory.ps1` / `.sh`. It writes `mapping-research-memory/v1`, `state/mapping-research-memory.json`, `state/mapping-research-memory.md`, `state/mapping-research-candidates.jsonl`, and `runs/<run-id>/research/mapping-research-memory.*`. Use those artifacts to slice exactly one bounded config/POM/recognizer or verify-harness improvement ticket.

If the user wants to share a failing/noisy migration with the migrator author, run `migration/scripts/create-feedback-bundle.ps1` / `.sh` instead of collecting arbitrary source files. `feedback-bundle/v1` is safe by default: reports/evidence only, generated `.cs` samples opt-in via `-IncludeGeneratedSamples`, and manifest review required before sharing.


## Artifact hygiene loop

- Validate installed workspace script syntax with `scripts/validate-installed-scripts.ps1` / `.sh` before artifact hygiene when available.
Before final handoff or another wave after material state changes, ensure `migration/scripts/validate-run-artifacts.ps1` / `.sh` has produced `artifact-hygiene/v1`. Do not publish user-facing reports that claim `complete`, `success`, or green status while `final-gate-result.json` is blocked. `Plan.md` must remain a plan, not raw shell/write transcript; session export status must be explicit; generated boards/status artifacts must include run/wave identity.

## Bounded production waves

- Production wavefront plans begin with a one-test `smoke-validation` wave. Later waves are affinity-packed by source file/POM context. Repeated tests from the same file use marginal complexity so role overhead is amortized instead of producing one wave per test.
- Use `--execution-profile fast` by default. `standard` requires executor + reviewer; `audit` requires executor + reviewer + watchdog + sentinel. In fast/standard, watchdog and pre-final sentinel work are event-driven, but final review, final sentinel inspection, and final gate remain mandatory.
- Prefer `migration plan --wave-profile auto`; read `plan/wave-tuning.md` before execution. The tuner is planning-only and must not invoke agents.
- Read `runs/<wave-id>/preflight-budget.json` before execution. `PASS`, `SOFT_LIMIT_EXCEEDED`, and `HEAVY_SINGLE_TEST` are executable. Only `BLOCKED` crosses the broad hard ceiling and requires replan.
- Automatic post-final remediation is capped at four completed tickets per wave and two consecutive no-progress tickets. `update-current-ticket-status` records `wave-progress/v1`; TODO deletion without executable restoration is no progress.
- `REMEDIATION_BUDGET_EXHAUSTED` produces `FINAL_WITH_LIMITATIONS` and stops the closed loop. Do not create another ticket until the user explicitly extends the budget or starts `/supervised-task waves fresh`.
- A fresh restart must use `scripts/start-fresh-wavefront-run.ps1` / `.sh`, archive the pilot under `archive/**`, and preserve `state/memory/**` plus configured source scope.

- Run `assess-agent-risk` before automatic role dispatch. Treat `agent-risk-assessment.json` as runtime-owned deterministic evidence; never edit its score, reasons, budget, or `assessmentFingerprint` manually.
- A `RUN_ROLE` authorization is valid only while its `riskAssessmentFingerprint` matches the current assessment. Evidence changes invalidate the authorization and require a fresh `next-agent-action`.
- `critical` risk or `automaticContinuationAllowed=false` requires `HUMAN_REVIEW_REQUIRED`. Do not downgrade risk, increase adaptive budgets, or skip final reviewer/final sentinel/final gate.

## Durable role recovery

- `record-agent-role --role-status STARTED` owns `agent-role-lease.json`; long roles renew it with `heartbeat-agent-role`.
- After interruption, run `plan-agent-recovery` before selecting another role.
- `WAIT_FOR_ROLE` forbids duplicate dispatch. `SAFE_REPAIR_AVAILABLE` permits only the emitted `recover-agent-runtime` command. `BLOCKED` requires human review.
- Safe recovery may rebuild only the derived ledger head, append a FAILED terminal event for a stale role, archive an orphan lease, or quarantine incomplete atomic temp files.
- Never delete, truncate, or rewrite malformed `agent-role-events.jsonl`; a broken hash chain is blocking evidence.
- Recovery does not reset role budgets and does not replace reviewer, sentinel, scope checks, validation, or final gate.
