---
description: Run the next migration task through the state-aware Harness Kit lifecycle, watchdog, review checkpoints, and final gate evidence.
agent: orchestrator
---

Requested task, optional:
$ARGUMENTS

Use the supervised Harness Kit workflow.

## State-aware zero-argument dispatch

`/supervised-task` is the normal tester-facing entrypoint. It must work with no arguments. If `$ARGUMENTS` is empty or only whitespace, inspect workspace state and choose the safe behavior below; do not ask the user what to do next and do not require them to know Harness internals.

Before planning, always read:

- AGENTS.md
- migration/AGENT_CONTRACT.md
- migration/state/harness-policy.json
- migration/state/harness-run.json, if it exists
- migration/state/final-gate-result.json, if it exists
- migration/state/continuation-decision.json, if it exists
- migration/state/continuation-decision.md, if it exists
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
- migration/plan/plan.md, if it exists
- migration/plan/waves.json, if it exists
- migration/plan/memory-recall.md, if it exists

Then dispatch:

1. If `migration/state/continuation-decision.json` says `CONTINUE_REQUIRED`, Continue with that next bounded action. Do not produce a user-facing handoff first.
1a. If the active run is `FINAL_STOPPED_FOR_REVIEW` or the latest continuation decision is `FINAL` with `postSuccessPolicy: STOP_FOR_REVIEW`, and `$ARGUMENTS` is exactly or effectively `continue` with no specific bounded action, do not stop again and do not ask the user for a more detailed prompt. Start the closed post-final development loop: invoke `migration-researcher`, then `migration-research-lead`, then `migration-task-slicer` when research is approved. The researcher writes only under `migration/runs/<active-run-id>/research/**` plus allowed lifecycle continuation/trace files; the research lead may request one bounded revision instead of sending weak research to the user; the task slicer writes backlog/current-ticket artifacts only under `migration/**`.
1b. If `continuation-decision.json` says `FINAL_RESEARCH_COMPLETED` or `postFinalStage` is `RESEARCH_COMPLETED`, invoke `migration-research-lead` on `migration/runs/<active-run-id>/research/**`. If the lead returns `REQUEST_CHANGES`, send the revision back to `migration-researcher` once; if it returns `APPROVE`, invoke `migration-task-slicer`.
1c. If `continuation-decision.json` says `POST_FINAL_RESEARCH_APPROVED` or `postFinalStage` is `RESEARCH_APPROVED`, invoke `migration-task-slicer` and require it to create `migration/state/backlog/post-final-tasks.jsonl` plus `migration/current-ticket.md` before any implementation.
1d. If `continuation-decision.json` says `POST_FINAL_TASKS_READY`, `nextAction` is `RUN_NEXT_BOUNDED_TASK`, and `boundedAutoContinuation.allowed` is true for that exact action, delegate the selected `migration/current-ticket.md` to `executor` for exactly one bounded implementation task, then run watchdog/reviewer/scope/harness checks before any handoff.
2. If the latest final gate is non-final and current-ticket, verify output, handoff, or continuation decision names an allowed next config/scaffold/evidence action under `migration/**`, execute exactly one next bounded action.
2a. If no current ticket exists but `migration/plan/waves.json` exists, choose the next uncompleted wave as a bounded ticket. If `migration/runs/<wave-id>/input-scope.json` does not exist, first run `selenium-pw-migrator migration run-wave --plan migration/plan --wave <wave-id> --workspace migration --out migration/runs/<wave-id>`. Before implementation, run `selenium-pw-migrator memory explain --workspace migration`, `selenium-pw-migrator memory doctor --workspace migration`, and `selenium-pw-migrator memory recall --file <file> --workspace migration` for files in the wave. Treat the wave plan and run workspace as guidance, not permission to edit outside `migration/**`. Keep `config-delta.json` and `memory-delta.jsonl` wave-local until reviewed. When multiple reviewed deltas exist, run `selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge` and `selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge`; do not activate the candidate while `conflicts.jsonl` is non-empty.
3. If the latest final gate is `FINAL` / `HARNESS_CONTINUATION_FINAL` and `$ARGUMENTS` does not explicitly request `continue`, stop for review: summarize the completed checkpoint, final-gate evidence, changed artifacts, remaining risks, and one recommended continue command. Explicitly say: "I stopped because the SUCCESS checkpoint requires review before post-final research or another bounded ticket." Do not start another run/ticket and do not show a broad menu.
Do not show a broad menu after a successful checkpoint; give one recommended continue command instead: `/supervised-task continue`.
4. If `$ARGUMENTS` explicitly requests `continue` after a `FINAL` / `FINAL_STOPPED_FOR_REVIEW` checkpoint and does not name a concrete implementation task, run the low-prompt closed loop: `migration-researcher` → `migration-research-lead` → `migration-task-slicer` → one bounded `executor` task only if auto-continuation allows it. If the user names a specific test/helper/TODO, pass that as the research topic. If the user names a concrete implementation task, first require approved research/task slicing when research artifacts exist; otherwise start the smallest safe bounded implementation ticket from the recommended remaining risks.
5. If `continuation-decision.json` contains bounded auto-continuation for the exact next action, obey that budget; otherwise `FINAL` always means stop-for-review until explicit `continue`, and plain `continue` means post-final research first.
6. If there is no active run, create the first bounded migration run.
7. Stop only for `FINAL` stop-for-review, explicit `BLOCKED_*`, missing required user input, denied writes, or autonomous budget/plateau limits.

When `$ARGUMENTS` explicitly requests `continue` after a FINAL checkpoint, prefer the closed post-final loop over immediate implementation unless the user names a concrete implementation task. Use this priority order:

1. If no specific task is named, invoke `migration-researcher` in `pattern-scout` mode over the active run's remaining TODOs and require `research-summary.md` plus `todo-inventory.json`.
2. If a specific test/helper/TODO is named, invoke `migration-researcher` in `source-truth` mode for that topic and still require actionability classification.
3. Invoke `migration-research-lead` after research. It may approve, block, or request one bounded revision. Do not treat weak research as a human handoff.
4. After research approval, invoke `migration-task-slicer` to create `migration/state/backlog/post-final-tasks.jsonl`, `migration/state/backlog/post-final-backlog.md`, and `migration/current-ticket.md`.
5. Only after slicing, select implementation work in this order: project-verify structural errors; unmapped UiTargets with source-truth evidence; syntax-fallback / semantic context problems; RequiredSideEffect helpers with safe mapping evidence; stale/incomplete migration documentation/evidence that affects review readiness.
6. If `boundedAutoContinuation.allowed` permits `RUN_NEXT_BOUNDED_TASK`, delegate exactly one selected ticket to `executor`; otherwise stop with the current ticket and backlog evidence.

For an implementation ticket selected after research review, `migration-task-slicer` must write or update `migration/current-ticket.md` with the selected title, evidence files read, allowed roots, stop conditions, and verification plan. Record whether the ticket came from post-final research or explicit `/supervised-task continue <task>` so a tester can audit why it was started.

If external assemblies, product project references, credentials, network access, package installation, or product source edits are required and cannot be safely inferred under `migration/**`, stop with `BLOCKED_USER_INPUT_REQUIRED` and list exact user actions.

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
3. Read project-scoped memory before planning: `migration/state/memory/memory-summary.md` when present, plus `selenium-pw-migrator memory explain --workspace migration` when the next bounded action depends on prior decisions.
4. Make or update a short plan in the active run context.
5. Record important lifecycle events when practical with `migration/scripts/write-harness-event.ps1`, especially `plan-written`, `post-final-research-started`, `post-final-research-reviewed`, `post-final-research-revision-requested`, `post-final-tasks-sliced`, `explicit-continue-ticket-selected`, `scope-check-pass`, `tests-pass`, `tests-failed`, `final-gate-pass`, and `handoff-written`.
6. Ask watchdog to check the plan.
7. Delegate implementation to executor only if needed, and include the active run id.
8. Ask watchdog to check the result.
9. Ask reviewer to review the diff and active run evidence.
10. Run `migration/scripts/check-scope.ps1` after any patch.
11. Run `migration/scripts/check-harness-policy.ps1` after any patch.
12. Run `selenium-pw-migrator memory doctor --workspace migration` before final-gate handoff when the CLI is available. For wave work, include `migration/runs/<wave-id>/config-delta.json`, `memory-delta.jsonl`, and `wave-status.json` in the handoff evidence.
13. Apply only minimal fixes if needed.
14. Stop after at most 2 fix-review cycles unless the user asks to continue.
15. Do not ask routine continuation questions when the next action is allowed by harness-policy and OpenCode permissions.
16. Do not issue FINAL unless `migration/scripts/check-final-gate.ps1 -RequireOpenCodeExport -RequireExplainTodo -RequireVerificationArtifacts` passes and migration/state/final-gate.md can be marked PASS with evidence. Otherwise say NOT FINAL - INVESTIGATION RESULT ONLY.
17. After every non-final final-gate run, read `migration/state/continuation-decision.json` and `migration/state/continuation-decision.md`.
18. If continuation status is `CONTINUE_REQUIRED`, do not send a user-facing handoff yet. Execute exactly one next bounded action named by the decision/current ticket/handoff, then rerun scope, harness policy, verification, and final gate. A response that only repeats NOT FINAL / NOT RUNTIME READY while `CONTINUE_REQUIRED` exists is a protocol violation.
19. After every successful `FINAL` / PASS checkpoint, stop and report. Starting post-final research requires only explicit user `continue`; starting implementation requires approved research, task slicing into `migration/current-ticket.md`, an explicit implementation task, or bounded auto-continuation in `continuation-decision.json`.
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

Continuation rule: Continue with the next bounded action only when `migration/state/continuation-decision.json` reports `CONTINUE_REQUIRED`, the closed post-final research/review/task-slicing loop is requested by plain `continue`, or bounded auto-continuation allows this exact action. A `Developer action` in research is not a terminal human handoff until `migration-task-slicer` classifies it as non-agent-executable.
