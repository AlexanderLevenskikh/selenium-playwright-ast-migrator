---
description: Run the next migration task through the state-aware Harness Kit lifecycle, watchdog, review checkpoints, and final gate evidence.
agent: orchestrator
---

Requested task, optional:
$ARGUMENTS

Use the supervised Harness Kit workflow.

## State-aware zero-argument dispatch

`/supervised-task` is the normal tester-facing entrypoint. It must work with no arguments. If `$ARGUMENTS` is empty or only whitespace, do not ask the user what to do next and do not require them to know Harness internals. Instead, inspect workspace state and choose the next bounded action.

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

Then dispatch:

1. If `migration/state/continuation-decision.json` says `CONTINUE_REQUIRED`, Continue with that next bounded action. Do not produce a user-facing handoff first.
2. If the latest final gate is non-final and current-ticket, verify output, handoff, or continuation decision names an allowed next config/scaffold/evidence action under `migration/**`, execute exactly one next bounded action.
3. If the latest final gate is `FINAL` / `HARNESS_CONTINUATION_FINAL`, do not continue the old run blindly and do not ask the user for a prompt. Start the next bounded ticket from remaining evidence and risks.
4. If there is no active run, create the first bounded migration run.
5. Stop only for `FINAL`, explicit `BLOCKED_*`, missing required user input, denied writes, or autonomous budget/plateau limits.

When choosing a new ticket after a FINAL checkpoint, use this priority order unless `$ARGUMENTS` names a more specific task:

1. project-verify structural errors, missing project references, missing package references, missing usings, or generated test host configuration;
2. unmapped UiTargets that can be reduced using source-truth evidence under `migration/**`;
3. syntax-fallback / semantic context problems, especially when all actions are syntax-fallback and no Roslyn project context was used;
4. RequiredSideEffect helpers that need safe adapter-config mappings;
5. stale or incomplete migration documentation/evidence that affects review readiness.

For an auto-selected ticket, write or update `migration/current-ticket.md` with the selected title, evidence files read, allowed roots, stop conditions, and verification plan. Record that the ticket was auto-selected by `/supervised-task` so a tester can audit why it was chosen.

If external assemblies, product project references, credentials, network access, package installation, or product source edits are required and cannot be safely inferred under `migration/**`, stop with `BLOCKED_USER_INPUT_REQUIRED` and list exact user actions.

## Lifecycle

1. Create or resume the active run:
   - if no matching active run exists, run `migration/scripts/new-harness-run.ps1` with the task title and goal;
   - if a matching run exists, read its `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md`, and `trace.jsonl`.
2. Before each major action, state which AGENT_CONTRACT or harness-policy rule allows it.
3. Make or update a short plan in the active run context.
4. Record important lifecycle events when practical with `migration/scripts/write-harness-event.ps1`, especially `plan-written`, `ticket-auto-selected`, `scope-check-pass`, `tests-pass`, `tests-failed`, `final-gate-pass`, and `handoff-written`.
5. Ask watchdog to check the plan.
6. Delegate implementation to executor only if needed, and include the active run id.
7. Ask watchdog to check the result.
8. Ask reviewer to review the diff and active run evidence.
9. Run `migration/scripts/check-scope.ps1` after any patch.
10. Run `migration/scripts/check-harness-policy.ps1` after any patch.
11. Apply only minimal fixes if needed.
12. Stop after at most 2 fix-review cycles unless the user asks to continue.
13. Do not ask routine continuation questions when the next action is allowed by harness-policy and OpenCode permissions.
14. Do not issue FINAL unless `migration/scripts/check-final-gate.ps1 -RequireOpenCodeExport -RequireExplainTodo -RequireVerificationArtifacts` passes and migration/state/final-gate.md can be marked PASS with evidence. Otherwise say NOT FINAL - INVESTIGATION RESULT ONLY.
15. After every non-final final-gate run, read `migration/state/continuation-decision.json` and `migration/state/continuation-decision.md`.
16. If continuation status is `CONTINUE_REQUIRED`, do not send a user-facing handoff yet. Execute exactly one next bounded action named by the decision/current ticket/handoff, then rerun scope, harness policy, verification, and final gate. A response that only repeats NOT FINAL / NOT RUNTIME READY while `CONTINUE_REQUIRED` exists is a protocol violation.
17. Do not stop just because the report is NOT FINAL / NOT RUNTIME READY if `current-ticket.md`, verify output, handoff, or `continuation-decision.json` names an allowed next config/scaffold/evidence action under `migration/**`. Stop only when `state/stop-policy-checklist.md` has a valid blocker, max iterations were reached, or the next action is denied by policy.
18. Final report:
   - active run id;
   - changed files;
   - verification result;
   - scope guard result;
   - harness-policy result;
   - final gate result;
   - trace/events status;
   - remaining risks;
   - anything intentionally not fixed.

Continuation rule: Continue with that next bounded action when `migration/state/continuation-decision.json` reports `CONTINUE_REQUIRED`.
