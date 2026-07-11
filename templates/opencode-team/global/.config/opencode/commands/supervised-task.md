---
description: Run the next migration task through the state-aware Harness Kit lifecycle, watchdog, review checkpoints, and final gate evidence.
agent: orchestrator
---

Requested task, optional:
$ARGUMENTS

Use the supervised Harness Kit workflow.

## One-command wavefront start (`/supervised-task waves`)

When `$ARGUMENTS` is exactly `waves`, `wave`, `wavefront`, `start waves`, or contains a clear request to start divide-and-conquer migration, enter **wavefront bootstrap mode**. When the arguments contain `waves fresh`, `fresh waves`, or `restart waves`, first enter **fresh-wavefront restart mode**. This mode is the preferred user-facing start path: the user should not need to run `kit init`, `bootstrap-opencode`, `kit doctor`, `migration plan`, or `migration run-wave` manually after installing/updating the tool.

In fresh-wavefront restart mode, run `migration/scripts/start-fresh-wavefront-run.ps1 -Workspace migration -Label pilot` or the `.sh` companion before replanning. The script must archive the current plan/runs/volatile state under `migration/archive/**`, preserve `migration/state/memory/**` and configured source scope, and write `migration/state/wavefront-restart.json`. Never delete an existing pilot without this archive receipt. Then continue in normal wavefront bootstrap mode.

In wavefront bootstrap mode:

0. First resolve the repository root with `git rev-parse --show-toplevel` when git is available. Treat that directory as the only valid project root for migration harness artifacts. All `migration/**`, `migration/plan/**`, and `migration/runs/**` paths must resolve under `<repo-root>/migration/**`, even if the current shell happens to be inside `Web/**`, a source project, or a target project. Do not `cd` into the Selenium source or Playwright target before running kit/plan/run-wave commands. If a nested workspace such as `Web/**/migration/**` appears, classify it as `NESTED_MIGRATION_WORKSPACE`, stop implementation, and route a cleanup/hardening task instead of continuing from the nested directory.
1. Do **not** run `selenium-pw-migrator --mode migrate` against the full source project before a wave workspace exists. A full-source migrate from a fresh workspace is a protocol violation when `waves` was requested.
2. Before repository-wide detection, read the configured bootstrap source from `migration/state/source-scope.json`, then `migration/.migration-kit/version.json`, then `migration/state/memory/project-profile.json`. When a non-placeholder source from `kit bootstrap-opencode --source ...` exists and resolves under the repository root, it is the authoritative Selenium source scope. Do not scan sibling `*FunctionalTests*` projects, do not build a table of alternative test projects, and do not widen the wave plan outside that configured source.
3. Inspect the repository and auto-detect only the missing parts, in this order:
   - source Selenium test project/path: only when no configured bootstrap source exists or the configured source is missing/placeholder; prefer `.csproj` files or directories containing Selenium/WebDriver references and test attributes; ignore `bin/`, `obj/`, `migration/`, generated reports, and archived workspaces;
   - target backend/framework: prefer existing Playwright .NET projects with `Microsoft.Playwright`, `Microsoft.Playwright.NUnit`, or xUnit/MSTest equivalents; otherwise infer `playwright-dotnet` and the source test framework (`nunit`, `xunit`, `mstest`) from package references/usings/attributes;
   - target project/output path if present; otherwise keep it as generated migration proposals under `migration/**`;
   - desired strategy: default to `wavefront` with `--wave-profile auto`, a one-test smoke validation, affinity-aware same-file/POM batching, soft planning targets, broad hard ceilings, and low-risk-first ordering. Do not guess fixed budgets when the read-only tuner can derive them from the current inventory.
4. Ask the user **only** when the configured source is absent/missing or detection is genuinely ambiguous. Ask one compact question with concrete options, for example: “I found two Selenium test projects and no configured bootstrap source: A and B. Which one should be migrated?” Do not ask broad preference questions when a safe configured source exists.
5. If `migration/AGENT_CONTRACT.md` or `migration/opencode-team/**` is missing, run:
   `selenium-pw-migrator kit bootstrap-opencode --workspace migration --source <configured-or-detected-source> --target-path <detected-target-or-placeholder> --opencode-install none`
   The CLI bootstrap is expected to apply repository-root `opencode.jsonc`, `.opencode/agents`, `.opencode/commands`, and `AGENTS.md` automatically. If it does not, run `migration/scripts/apply-opencode-project-config.ps1` or `.sh` as a repair step and report that as a tool defect.
6. Run `selenium-pw-migrator kit doctor --workspace migration`. If doctor reports only non-blocking warnings, continue. If source/config/workspace is missing, ask the smallest exact question needed.
7. If `migration/plan/waves.json` is missing or stale relative to the configured-or-detected source, run:
   `selenium-pw-migrator migration plan --input <configured-or-detected-source> --strategy wavefront --workspace migration --out migration/plan --wave-profile auto --smoke-wave-size 1 --prefer-low-risk-first true`
8. Read `migration/plan/wave-tuning.md` and verify that the recommended plan amortizes orchestration: only the smoke wave should be intentionally singleton, source files should not be fragmented without a hard-cap reason, and a project of roughly 80 tests should normally produce a single-digit or low-double-digit wave count rather than one wave per test.
9. If no active wave run exists, materialize the first pending wave before implementation:
   `selenium-pw-migrator migration run-wave --plan migration/plan --wave wave-001 --workspace migration --out migration/runs/wave-001 --execution-profile fast`
10. Treat `migration/runs/<wave-id>/wave-manifest.json` as the immutable selected-file/test contract, `execution-policy.json` as the role-routing contract, `run-context.json` as the immutable incremental/cache contract, and `input-scope.json` as descriptive scope evidence. Run `selenium-pw-migrator migration validate-wave --out migration/runs/<wave-id>` before implementation/review. If generated output is still a placeholder, execute the existing wave-local migrate script (`run-migrate.ps1` or `.sh`); do not rematerialize an existing run directory. After output changes, use the single validation host: `selenium-pw-migrator migration validate --out migration/runs/<wave-id> --validation-project <target-project>` (or `--validation-command <project-check>` when project auto-discovery is impossible). It owns plan → execute → evidence → exact-input cache → validation checkpoint. Use `migration validation-plan`/`record-validation` only for recovery or importing externally executed evidence; never fabricate a PASS.
11. Create/resume the Harness run and continue through the normal lifecycle using the wave workspace as the active ticket. After a successful validation, run `migration checkpoint-wave`, then `migration build-review-bundle`; after interruption run `migration resume-wave` and follow its single `nextAction` instead of repeating discovery or migration. A checkpoint is not `DONE`, cached validation never replaces required gates, and the review bundle never replaces final reviewer/sentinel/final gate. After each bounded implementation/fix cycle run `selenium-pw-migrator migration check-progress --out migration/runs/<wave-id> --max-identical-snapshots 3`. `NO_PROGRESS_DETECTED` requires watchdog/strategy change instead of another retry. All implementation writes remain under `migration/**` unless a later explicit policy grants more.

If the repository is fresh and `/supervised-task waves` has a configured bootstrap source or enough information to auto-detect the source path, the expected first meaningful actions are: bootstrap/update kit → doctor → wavefront plan → materialize `wave-001` → wave-local migration. Returning a generic “no task was provided” or running full-source migration is not allowed in this mode.


Compatibility wording: a fresh `FINAL` checkpoint must **stop for review** once; a persisted `FINAL_STOPPED_FOR_REVIEW` checkpoint normally resumes the closed post-final development loop. `FINAL_WITH_LIMITATIONS` / `WAVE_REMEDIATION_BUDGET_EXHAUSTED` is different: it is a hard autonomous stop and must not resume post-final work without explicit user approval or `waves fresh`.


## Permission and append-only state discipline

OpenCode permission denials are authoritative for every role. If an edit/write is denied, do not bypass it by running `bash`, PowerShell, Python, `sed`, `tee`, shell redirection, or any other shell write primitive. Stop with `BLOCKED_BY_OPENCODE_PERMISSION_DENIED` and report the denied path and intended change.

Do not manually overwrite append-only JSONL ledgers. Use `write-harness-event` for events/traces, `selenium-pw-migrator memory add` or `write-memory-entry` for memory additions, `repair-memory-jsonl` for memory-only repair, and `repair-jsonl-ledger` for an explicit controlled backlog/state/run JSONL repair with a backup. Run `validate-run-artifacts` before handoff; malformed JSONL is a blocking state defect. A final handoff is invalid when machine-readable state is contradictory, for example `task-slice-result` says `BLOCKED_NO_AGENT_EXECUTABLE_TASKS` while `continuation-decision.json` still says `CONTINUE_REQUIRED`.


## Session export and harness-sentinel

Every supervised run should leave a forensic session artifact. Before final handoff, create or update `migration/runs/<active-run-id>/opencode-session-export.md` with `migration/scripts/export-opencode-session.ps1` or `.sh`. If a native OpenCode transcript is unavailable, export an explicit `UNAVAILABLE_WITH_REASON` artifact and rely on `trace.jsonl`, `harness-events.jsonl`, and `session-observations.jsonl`; do not create an empty transcript template or invent transcript text.

`harness-sentinel` is the process tester / forensic reviewer. Invoke it after gate failure, permission denial, post-final research approval, and before final handoff. It must finish by writing `migration/runs/<run-id>/sentinel/sentinel-inspection.json` through `migration/scripts/complete-sentinel-inspection.*`. If it records open high/critical agent-executable findings, do not hand them to the user as generic advice. Route them to `migration-task-slicer` for a bounded process-hardening ticket, then continue the normal reviewer/gate loop. When a finding is assigned to a ticket, call `migration/scripts/update-sentinel-finding-status.ps1 -FindingId <id> -Status ASSIGNED -TicketId <ticket>` / `.sh`; after implementation use `FIX_ATTEMPTED`, and only reviewer/final-gate evidence may move it to `VERIFIED` or `CLOSED`.

`/supervised-task sentinel`, `/supervised-task inspect`, and `/supervised-task qa` explicitly run this process test: export session evidence if needed, invoke `harness-sentinel`, complete `sentinel-inspection.json`, and report findings or the next bounded hardening task.

## State-aware zero-argument dispatch

`/supervised-task` is the normal tester-facing entrypoint. It must work with no arguments. If `$ARGUMENTS` is empty or only whitespace, inspect workspace state and choose the safe behavior below; do not ask the user what to do next and do not require them to know Harness internals.

Before planning, record the default dispatch skill profile when practical:

```powershell
migration/scripts/record-agent-skill-profile.ps1 -Profile supervised-task -Phase dispatch -Trigger supervised-task -Detail "Loaded supervised-task dispatch profile."
```

Use the `.sh` companion on Unix-like shells. Then read:

- AGENTS.md
- migration/AGENT_CONTRACT.md
- migration/state/harness-policy.json
- migration/state/scope-contract.json
- migration/state/claims/active/*.json, if any exist
- migration/agent-skills/skill-map.md, if it exists
- migration/state/harness-run.json, if it exists
- migration/state/final-gate-result.json, if it exists
- migration/state/continuation-decision.json, if it exists
- migration/state/continuation-decision.md, if it exists
- migration/state/wave-quality-budget.json, if it exists
- migration/state/wavefront-restart.json, if it exists
- migration/state/final-gate.md
- migration/state/handoff.md, if it exists
- migration/current-ticket.md, if it exists
- migration/agent-state.md, if it exists
- migration/state/stop-policy-checklist.md
- migration/state/memory/memory-summary.md, if it exists
- migration/state/memory/decisions.jsonl, if it exists
- migration/state/memory/warnings.jsonl, if it exists
- migration/state/memory/antipatterns.jsonl, if it exists
- migration/state/memory/final-gate-lessons.jsonl, if it exists
- migration/state/sentinel-ledger.jsonl, if it exists
- migration/runs/<active-run-id>/opencode-session-export.md, if it exists
- migration/runs/<active-run-id>/session-observations.jsonl, if it exists
- migration/runs/<active-run-id>/sentinel/sentinel-report.md, if it exists
- migration/runs/<active-run-id>/sentinel/sentinel-findings.jsonl, if it exists
- migration/plan/plan.md, if it exists
- migration/plan/waves.json, if it exists
- migration/plan/memory-recall.md, if it exists

Then dispatch:

0. If `$ARGUMENTS` is `sentinel`, `inspect`, `qa`, or clearly asks for process testing, export/update `opencode-session-export.md`, invoke `harness-sentinel`, and route open high/critical agent-executable findings to `migration-task-slicer` before any final handoff. If final-gate/sentinel diagnostics exist but no `current-ticket.md` exists yet, run `migration/scripts/slice-gate-followups.ps1` / `.sh` to write `state/backlog/gate-followup-tasks.jsonl`, `state/backlog/gate-followup-backlog.md`, and `current-ticket.md`.
0a. If `migration/state/harness-run.json` says `WAVE_REMEDIATION_BUDGET_EXHAUSTED`, `continuation-decision.json` says `FINAL_WITH_LIMITATIONS`, or `wave-quality-budget.json` says `REMEDIATION_BUDGET_EXHAUSTED`, stop and report the remaining limitations. Do not invoke researcher, task slicer, reviewer, or executor automatically. Recommend exactly one command: `/supervised-task waves fresh`, unless the user explicitly authorizes extending the remediation budget.
1. If `migration/state/continuation-decision.json` says `CONTINUE_REQUIRED`, Continue with that next bounded action. Do not produce a user-facing handoff first.
1a. If the persisted active run is `FINAL_STOPPED_FOR_REVIEW`, always resume the closed post-final development loop, even when `$ARGUMENTS` is empty and even when old research or stop-policy checklist entries already exist. Do not stop again, do not ask the user for a more detailed prompt, and do not claim that no bounded action exists. Start or resume: `migration-researcher` → `migration-research-lead` → `migration-task-slicer` → execution-policy resolution → exactly one bounded `executor` task when a ticket is approved. Run `migration-change-reviewer` before execution when `standard`/`audit` or a policy trigger requires it; final review is never skipped. The researcher writes only under `migration/runs/<active-run-id>/research/**` plus allowed lifecycle continuation/trace files; the research lead may request one bounded revision instead of sending weak research to the user; the task slicer writes backlog/current-ticket artifacts only under `migration/**`; the change reviewer validates scope, evidence, and no assertion suppression before executor work.
1b. If `continuation-decision.json` says `FINAL_RESEARCH_COMPLETED` or `postFinalStage` is `RESEARCH_COMPLETED`, invoke `migration-research-lead` on `migration/runs/<active-run-id>/research/**`. If the lead returns `REQUEST_CHANGES`, send the revision back to `migration-researcher` once; if it returns `APPROVE`, invoke `migration-task-slicer`.
1c. If `continuation-decision.json` says `POST_FINAL_RESEARCH_APPROVED` or `postFinalStage` is `RESEARCH_APPROVED`, invoke `migration-task-slicer` and require it to create `migration/state/backlog/post-final-tasks.jsonl` plus `migration/current-ticket.md` before any implementation.
1d. If `continuation-decision.json` says `POST_FINAL_TASKS_READY` and `nextAction` is `RUN_NEXT_BOUNDED_TASK`, read the active `execution-policy.json`; route `migration/current-ticket.md` through `migration-change-reviewer` before execution only when the profile/trigger requires it, then delegate exactly one bounded implementation task to `executor` under `migration/**`. `boundedAutoContinuation.allowed` may further constrain the budget, but persisted `FINAL_STOPPED_FOR_REVIEW` is enough for this one selected task unless reviewer or policy blocks it. Then run watchdog/reviewer/scope/harness checks before any handoff.

1e. If `migration/current-ticket.md` exists and `migration/state/current-ticket-status.json` is missing or says `READY`, `IN_PROGRESS`, or `REVIEW_READY`, this current-ticket loop has priority over selecting another wave. Run `migration/scripts/update-current-ticket-status.ps1 -Status IN_PROGRESS -Source supervised-task` / `.sh` when starting execution, read `execution-policy.json`, run pre-execution `migration-change-reviewer` only when required by profile/trigger, then delegate exactly one bounded `executor` task under the ticket's allowed roots. After the executor patch, update the ticket to `REVIEW_READY`, run `migration check-progress`, mandatory final reviewer/scope/harness/artifact-hygiene checks, and watchdog only when policy/no-progress requires it, then call `update-current-ticket-status ... -Status DONE -Evidence <validation>` before the final gate. The DONE transition atomically synchronizes `task-slice-result.json`, `continuation-decision.json`, `harness-run.json`, and the active `wave-status.json`; run a fresh final gate immediately afterwards. Never mark DONE after final gate, because that makes the gate stale. Do not start another wave while the current-ticket lifecycle is active.
2. If the latest final gate is non-final and current-ticket, verify output, handoff, or continuation decision names an allowed next config/scaffold/evidence action under `migration/**`, execute exactly one next bounded action. If the named action is `slice-gate-followups`, run the slicer first, then route the generated `migration/current-ticket.md` through `migration-change-reviewer`.
2a. If no current ticket exists but `migration/plan/waves.json` exists, choose the next uncompleted wave as a bounded ticket. If `migration/runs/<wave-id>/input-scope.json` does not exist, first run `selenium-pw-migrator migration run-wave --plan migration/plan --wave <wave-id> --workspace migration --out migration/runs/<wave-id> --execution-profile fast`. Before implementation, run `selenium-pw-migrator memory explain --workspace migration`, `selenium-pw-migrator memory doctor --workspace migration`, and `selenium-pw-migrator memory recall --file <file> --workspace migration` for every file in the wave/current-ticket scope. `memory recall` records receipts in `state/memory/recall-index.json` and `recall-ledger.jsonl`; final gate requires current-wave coverage when active memory exists. Treat the wave plan and run workspace as guidance, not permission to edit outside `migration/**`. Keep `config-delta.json` and `memory-delta.jsonl` wave-local until reviewed. When multiple reviewed deltas exist, run `selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge` and `selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge`; do not activate the candidate while `conflicts.jsonl` is non-empty.
3. If the latest final gate has just produced a fresh `FINAL` / `HARNESS_CONTINUATION_FINAL` checkpoint in the current supervised-task run, stop for review once: summarize the completed checkpoint, final-gate evidence, changed artifacts, remaining risks, and one recommended continue command. If `migration/state/harness-run.json` already contains `FINAL_STOPPED_FOR_REVIEW` when this command starts, this is no longer a fresh checkpoint; resume the closed post-final loop from step 1a instead. Explicitly say only for the fresh checkpoint: "I stopped because the SUCCESS checkpoint requires review before post-final research or another bounded ticket." Do not show a broad menu.
Do not show a broad menu after a successful checkpoint; give one recommended continue command instead: `/supervised-task continue`.
4. If `$ARGUMENTS` explicitly requests `continue` after a `FINAL` / `FINAL_STOPPED_FOR_REVIEW` checkpoint, use the same low-prompt closed loop as step 1a. Explicit `continue` is still supported, but it is no longer required once the persisted run status is `FINAL_STOPPED_FOR_REVIEW`; zero-argument `/supervised-task` must also resume that loop. If the user names a specific test/helper/TODO, pass that as the research topic. If the user names a concrete implementation task, first require approved research/task slicing when research artifacts exist; otherwise start the smallest safe bounded implementation ticket from the recommended remaining risks.
5. If `continuation-decision.json` contains bounded auto-continuation for the exact next action, obey that budget. Otherwise a fresh `FINAL` checkpoint stops once for review, while a persisted `FINAL_STOPPED_FOR_REVIEW` status always resumes the closed post-final loop; plain `continue` and zero-argument `/supervised-task` both mean post-final research/review/task slicing first.
6. If there is no active run and wavefront bootstrap mode is not requested, create the first bounded migration run. If wavefront bootstrap mode is requested or `migration/plan/waves.json` exists, create/materialize the first bounded wave first and never start a full-source migration run.
7. Stop only for a fresh `FINAL` stop-for-review, `FINAL_WITH_LIMITATIONS` / `WAVE_REMEDIATION_BUDGET_EXHAUSTED`, explicit `BLOCKED_*`, missing required user input, denied writes, loop-guard block, or autonomous budget/plateau limits. Persisted `FINAL_STOPPED_FOR_REVIEW` is not a stop condition; it is the closed-loop dispatch state.
8. Before repeating a post-final/current-ticket dispatch summary with the same Goal/Progress/Next Steps, run `migration/scripts/check-loop-guard.ps1 -Workspace migration -RunId <active-run-id> -TicketId <ticket-id> -Goal <goal> -Stage <research|slice|review|execute> -NextAction <next concrete command-or-agent>` or the `.sh` wrapper. If it prints `LOOP_GUARD_BLOCKED`, stop immediately with that status and do not print another copied lifecycle block. A valid response must either execute one new bounded action, write a concrete blocker, or report `LOOP_GUARD_BLOCKED` with `migration/state/loop-guard.json` evidence.

Before entering or repeating any post-final loop, run `migration/scripts/evaluate-wave-quality-budget.ps1 -Workspace migration` or `.sh`. If it writes `REMEDIATION_BUDGET_EXHAUSTED`, stop with `FINAL_WITH_LIMITATIONS`; do not slice another ticket. The default automatic budget is at most four completed post-final tickets per wave and at most two consecutive tickets without measurable generated-code progress. TODO deletion without executable restoration is not progress.

When the persisted active run is `FINAL_STOPPED_FOR_REVIEW`, prefer the closed post-final loop over immediate implementation whether `$ARGUMENTS` is empty or explicitly says `continue`, unless the user names a concrete implementation task. Use this priority order:

1. If `migration/current-ticket.md` or `migration/state/backlog/post-final-tasks.jsonl` already exists, do not re-run broad research first. If `migration/state/current-ticket-status.json` is absent or not terminal, run `migration/scripts/update-current-ticket-status.ps1 -Status IN_PROGRESS -Source supervised-task` / `.sh`, route the selected ticket through `migration-change-reviewer`, then delegate exactly one bounded executor task. Do not start another wave while the current-ticket lifecycle is active.
2. If approved research review exists, invoke `migration-task-slicer`, then route the selected ticket through `migration-change-reviewer`, then delegate exactly one bounded executor task.
3. If any research exists under `migration/runs/*/research/**`, including legacy `post-final-analysis.md`, invoke `migration-research-lead` first. If it requests revision, invoke `migration-researcher` once; if it approves, invoke `migration-task-slicer`.
4. If no research exists and no specific task is named, invoke `migration-researcher` in `pattern-scout` mode over the active run's remaining TODOs and require `research-summary.md` plus `todo-inventory.json`.
5. If a specific test/helper/TODO is named, invoke `migration-researcher` in `source-truth` mode for that topic and still require actionability classification.
6. After research approval, invoke `migration-task-slicer` to create `migration/state/backlog/post-final-tasks.jsonl`, `migration/state/backlog/post-final-backlog.md`, and `migration/current-ticket.md`.
7. Route the sliced current ticket through `migration-change-reviewer` before implementation; it must reject unsafe scope, missing evidence, assertion suppression, or human-only decisions disguised as executor work.
8. Only after slicing, select implementation work in this order: project-verify structural errors; unmapped UiTargets with source-truth evidence; syntax-fallback / semantic context problems; RequiredSideEffect helpers with safe mapping evidence; stale/incomplete migration documentation/evidence that affects review readiness.
9. After task slicing and change-review approval, delegate exactly one selected ticket to `executor` under `migration/**`. If `boundedAutoContinuation.allowed` is present, obey its budget; if it is absent, the persisted `FINAL_STOPPED_FOR_REVIEW` state or explicit plain `continue` grants only this one bounded executor task. Stop only when the reviewer/policy blocks the ticket.

For an implementation ticket selected after research review, `migration-task-slicer` must write or update `migration/current-ticket.md` with the selected title, evidence files read, allowed roots, stop conditions, and verification plan. Record whether the ticket came from post-final research or explicit `/supervised-task continue <task>` so a tester can audit why it was started.

If external assemblies, product project references, credentials, network access, package installation, or product source edits are required and cannot be safely inferred under `migration/**`, stop with `BLOCKED_USER_INPUT_REQUIRED` and list exact user actions.

### Post-final continue dispatch matrix

When the active run is already `FINAL_STOPPED_FOR_REVIEW`, with or without an explicit `continue` argument, first verify that remediation budget is not exhausted. When it is still available, do not produce a terminal “no work available” report until this matrix has been exhausted in order. Re-read `migration/state/continuation-decision.json` and the filesystem after every subagent step. If `mustContinueBeforeUserMessage` is `true`, continue the matrix inside the same supervised-task run instead of handing off to the user.

1. If `migration/current-ticket.md` exists and `migration/state/continuation-decision.json` says `POST_FINAL_TASKS_READY`, route the current ticket through `migration-change-reviewer`, then delegate exactly one bounded executor task. The persisted `FINAL_STOPPED_FOR_REVIEW` state is sufficient consent for one bounded executor task under `migration/**`; explicit `continue` is helpful but not required for this one task.
2. Else if `migration/state/backlog/post-final-tasks.jsonl` exists, select or refresh `migration/current-ticket.md`, route it through `migration-change-reviewer`, then delegate exactly one bounded executor task.
3. Else if `migration/runs/*/research/research-review.json` or `research-review.md` exists with `APPROVE`, invoke `migration-task-slicer` and continue to step 1.
4. Else if any post-final research artifact exists under `migration/runs/*/research/**` (including legacy `post-final-analysis.md`), invoke `migration-research-lead`. If the lead requests revision, invoke `migration-researcher` once with the requested fixes, then invoke `migration-research-lead` again. If approved, invoke `migration-task-slicer` and continue to step 1.
5. Else invoke `migration-researcher`, then `migration-research-lead`, then `migration-task-slicer`, then continue to step 1.

The following final-report claims are forbidden after post-final loop dispatch unless `migration-task-slicer` wrote `BLOCKED_NO_AGENT_EXECUTABLE_TASKS` or `migration-change-reviewer` wrote a concrete blocker:

- “No bounded next action exists.”
- “Zero bounded actions remain.”
- “The migration lifecycle is complete.”
- “Further continue commands produce no new work.”
- “417 TODOs require manual developer work.”
- “Post-final research already complete” as a reason to stop.

`stop-policy-checklist.md` stop reasons are not self-executing after post-final loop dispatch. “Source truth missing”, “selector evidence missing”, “manual work”, or “source edits forbidden” may be used as a blocker only after the researcher/research-lead/task-slicer path classifies the exact remaining work as `BLOCKED_BY_MISSING_SOURCE_TRUTH`, `BLOCKED_BY_SCOPE`, `HUMAN_DECISION_REQUIRED`, or `BLOCKED_NO_AGENT_EXECUTABLE_TASKS` with evidence. Artifact-only mode forbids real product source edits, but it permits bounded updates under `migration/**`, including research, backlog/current-ticket, generated migration proposals, migration-run migrated scaffolds, and review documentation when the selected ticket allows those paths.


### Start-workspace no-menu fallback

If `migration/next-commands.md`, `migration/state/start-dispatch.json`, or `migration/current-ticket.md` exists but a full Harness Kit state is not complete yet, do not ask the user to choose among broad repository tasks. Treat the start artifacts as the active ticket and execute the first safe migration setup action from this priority order:

1. run `selenium-pw-migrator doctor install` when install diagnostics are missing;
2. run `selenium-pw-migrator kit bootstrap-agent ...` from `migration/next-commands.md` when `migration/AGENT_CONTRACT.md` is missing;
3. run `selenium-pw-migrator pilot ...` when `migration/pilot/selected-input` or `migration/pilot/pilot-selection.json` is missing;
4. create/resume the Harness run and continue from `migration/current-ticket.md`.

Do not offer options such as README updates, package maintenance, broad refactors, or unrelated repository cleanup unless the user explicitly asks for them. Ask a question only when the source path, workspace path, or required write permission is missing or contradictory.

## Lifecycle

1. Create or resume the active run:
   - if no matching active run exists, run `migration/scripts/new-harness-run.ps1` with the task title and goal;
   - if a matching run exists, read its `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and `trace.jsonl`.
2. Before each major action, state which AGENT_CONTRACT or harness-policy rule allows it.
3. Read project-scoped memory before planning: `migration/state/memory/memory-summary.md` when present, plus `selenium-pw-migrator memory explain --workspace migration`. For every explicit file in the next bounded scope, run `selenium-pw-migrator memory recall --file <file> --workspace migration`; reading the summary alone is not recall evidence.
4. Make or update a short plan in the active run context.
5. Record important lifecycle events when practical with `migration/scripts/write-harness-event.ps1`, especially `plan-written`, `post-final-research-started`, `post-final-research-reviewed`, `post-final-research-revision-requested`, `post-final-tasks-sliced`, `explicit-continue-ticket-selected`, `scope-check-pass`, `tests-pass`, `tests-failed`, `final-gate-pass`, and `handoff-written`.
6. Read the active wave `execution-policy.json` when present. Ask watchdog to check the plan only for `audit`, a policy trigger, scope/policy risk, or a prior `NO_PROGRESS_DETECTED`; do not spend a watchdog turn on an unchanged low-risk fast-path plan.
6a. For broad wave/TODO work, apply `migration/agent-skills/efficient-frontier/SKILL.md` to split only bounded independent work packets.
7. Delegate implementation to executor only if needed, and include the active run id.
8. Run `migration check-progress` after the bounded result/fix. Ask watchdog to check the result when the policy requires it, the detector returns `NO_PROGRESS_DETECTED`, verification repeats without diff, or scope/policy evidence is suspicious.
8a. Before the user-facing handoff, apply `migration/agent-skills/quick-recap/SKILL.md` so the result is GREEN/YELLOW/RED with evidence.
9. Ask reviewer to review the diff and active run evidence before final handoff; fast profile may defer this review until the bounded result exists, but never skips final review.
10. Run `selenium-pw-migrator migration validate-wave --out migration/runs/<wave-id>` and `migration/scripts/check-scope.ps1` after any patch that can affect wave scope or selected inputs.
11. Run `migration/scripts/check-harness-policy.ps1` after any patch.
12. Run `selenium-pw-migrator memory doctor --workspace migration` before final-gate handoff when the CLI is available. For wave work, include `migration/runs/<wave-id>/config-delta.json`, `memory-delta.jsonl`, and `wave-status.json` in the handoff evidence.
13. Apply only minimal fixes if needed.
14. Stop after at most 2 fix-review cycles unless the user asks to continue.
15. Do not ask routine continuation questions when the next action is allowed by harness-policy and OpenCode permissions.
16. Do not issue FINAL unless `migration/scripts/check-final-gate.ps1 -RequireOpenCodeExport -RequireExplainTodo -RequireVerificationArtifacts` passes and migration/state/final-gate.md can be marked PASS with evidence. Otherwise say NOT FINAL - INVESTIGATION RESULT ONLY.
17. After every non-final final-gate run, read `migration/state/continuation-decision.json` and `migration/state/continuation-decision.md`.
18. If continuation status is `CONTINUE_REQUIRED`, do not send a user-facing handoff yet. Execute exactly one next bounded action named by the decision/current ticket/handoff, then rerun scope, harness policy, verification, and final gate. A response that only repeats NOT FINAL / NOT RUNTIME READY while `CONTINUE_REQUIRED` exists is a protocol violation.
19. After every fresh successful `FINAL` / PASS checkpoint produced in the current run, stop once and report. On any later `/supervised-task` invocation where `harness-run.json` is already `FINAL_STOPPED_FOR_REVIEW`, resume the closed post-final loop automatically; starting implementation still requires approved research, task slicing into `migration/current-ticket.md`, change-review approval, or bounded auto-continuation in `continuation-decision.json`.
20. Final report:
   - active run id;
   - changed files;
   - verification result;
   - scope guard result;
   - harness-policy result;
   - final gate result;
   - trace/events status;
   - remaining risks;
   - one recommended continue command in the form `To continue, run: /supervised-task continue`;
   - anything intentionally not fixed.

After a SUCCESS checkpoint report, never leave the user guessing why no work was done: state that the run is complete, `harness-run.json` should be `FINAL_STOPPED_FOR_REVIEW` when present, and provide exactly one command: `To continue, run: /supervised-task continue`.

Continuation rule: Continue with the next bounded action when `migration/state/continuation-decision.json` reports `CONTINUE_REQUIRED`, the persisted active run is `FINAL_STOPPED_FOR_REVIEW`, the user explicitly requests plain `continue`, or bounded auto-continuation allows this exact action. A `Developer action` in research is not a terminal human handoff until `migration-task-slicer` classifies it as non-agent-executable.


## Semantic TODO cleanup rule

Do not use a reduced TODO count as proof of migration quality. A ticket may remove an unresolved-symbol/TODO marker only when the same patch introduces or identifies active equivalent code, proves the marker obsolete with source-backed evidence, and preserves assertions/interactions. Deleting a TODO while the corresponding declaration/action remains commented out is evidence manipulation and must be rejected by reviewer/sentinel.

## Final handoff process-test gate

Before any final answer, confirm:

- `migration/runs/<active-run-id>/opencode-session-export.md` exists or the report explains why native transcript export was unavailable;
- `harness-sentinel` has inspected the latest run after the last material state change and wrote `sentinel-inspection.json`;
- no open high/critical agent-executable sentinel finding remains untriaged;
- any sentinel hardening recommendation was either routed to `migration-task-slicer` or explicitly classified as informational/non-blocking.

A sentinel finding is not a license to broaden implementation. It becomes a bounded process-hardening task under `migration/**` and must pass the same change-reviewer/watchdog/final-gate path.

## Permission denial discipline

OpenCode permission denials are authoritative. If an edit/write tool is denied, do not retry the same write through bash, PowerShell, Python, sed, tee, shell redirection, or any alternate shell write primitive. Stop with `BLOCKED_BY_OPENCODE_PERMISSION_DENIED` and report the denied path and intended change. Append-only JSONL files must not be overwritten; use `write-memory-entry` or `repair-memory-jsonl` when applicable.


Final gate reconciles `migration/state/harness-run.json` after every run: gate failure writes `BLOCKED_BY_GATE`/the concrete continuation status and real `latestChecks`; a supervisor must not continue from stale `CONTINUE_AUTONOMOUSLY` state after a failed gate.


Wave scope is file-based, not single-test-based: report `sourceFiles`, estimated/actual test count, migrated action count, and TODO count explicitly. Do not describe a wave as “3 tests” when the input scope is 3 files containing more tests.


Wave quality budget rule: after any `runs/wave-*` execution, run `migration/scripts/evaluate-wave-quality-budget.ps1` / `.sh` before selecting another wave. If the report says `BLOCKED_BY_WAVE_QUALITY_BUDGET`, do not start a new wave; route to mapping/research memory and one bounded config/POM/recognizer improvement ticket.

## Mapping/research memory loop

When `evaluate-wave-quality-budget` reports `BLOCKED_BY_WAVE_QUALITY_BUDGET`, do not select another wave. Run `migration/scripts/collect-mapping-research-memory.ps1` / `.sh` first. Use `mapping-research-memory/v1`, `state/mapping-research-memory.json`, and `state/mapping-research-candidates.jsonl` to route one bounded config/POM/recognizer or verify-harness improvement ticket.


## Artifact hygiene

Before final handoff or another wave after material state changes, run `migration/scripts/validate-installed-scripts.ps1 -Workspace migration` / `.sh` when installed, then run or honor final-gate execution of `migration/scripts/validate-run-artifacts.ps1` / `.sh`. `artifact-hygiene/v1` must pass: Plan.md is sanitized, Documentation.md does not contradict final gate, generated boards carry run/wave identity, session export status is explicit, every controlled JSONL line parses, current-ticket/task-slice/continuation/harness state agrees, and `wave-status.json` is not left `prepared` after generated output exists.
For user-shareable feedback, run `migration/scripts/create-feedback-bundle.ps1` / `.sh` instead of collecting the repository. The `feedback-bundle/v1` packer excludes project source by default, writes `state/feedback-bundles/*/manifest.json`, and requires manifest review before sharing.


## Scope-contract discipline

- Do not leave `scope-contract.json`: no broad `dotnet test .`, no repo-wide migration scans, and no edits under `Migrator.*` during a migration wave unless a review-backed contract explicitly allows it.
- If a required action is outside `allowedSourceRoots`/`workspaceRoot`, write a blocker and stop for review instead of silently doing it.
- Create or resume a claim with `migration/scripts/new-claim.*` before parallel wave execution; heartbeat during long work and complete the claim with evidence.


## Evidence and command policy rails

- Record material artifacts with `migration/scripts/record-run-evidence.ps1` / `.sh`; do not hand-edit `runs/*/evidence/index.json`.
- Record material lifecycle events with `migration/scripts/write-harness-event.ps1` / `.sh`; `runs/*/events.jsonl` is hash-chained.
- For long runs, write compaction receipts with `migration/scripts/write-memory-compaction-receipt.ps1` / `.sh`.
- Before ambiguous shell execution, classify it with `migration/scripts/evaluate-command-policy.ps1` / `.sh`; stop on `COMMAND_POLICY_FORBIDDEN`.
- Use `migration/scripts/move-stale-claims.ps1` / `.sh` only after reviewing an expired/abandoned claim.
