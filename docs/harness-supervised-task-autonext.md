# State-aware zero-argument dispatch for `/supervised-task`

`/supervised-task` is the tester-facing OpenCode entrypoint for guarded migration work. It is safe to run without arguments:

```text
/supervised-task
```

The user should not need to know whether the current workspace is `NOT FINAL`, `NOT RUNTIME READY`, `CONTINUE_REQUIRED`, or `FINAL`. If state evidence is present, do not ask the user what to do next. If `$ARGUMENTS` is empty or only whitespace, the command inspects the Harness Kit state and chooses the safe behavior. It must not ask a broad “what do you want to do?” menu when state evidence is sufficient.

## Dispatch rules

When `/supervised-task` starts, the orchestrator reads:

```text
AGENTS.md
migration/AGENT_CONTRACT.md
migration/state/harness-policy.json
migration/state/final-gate-result.json
migration/state/continuation-decision.json
migration/state/handoff.md
migration/current-ticket.md
migration/agent-state.md
migration/state/stop-policy-checklist.md
```

Then it dispatches:

| State | Behavior |
| --- | --- |
| No active run | Create the first bounded migration run. |
| `CONTINUE_REQUIRED` | Execute the named next bounded action before any user-facing handoff. |
| Non-final with allowed next action | Execute exactly one next config/scaffold/evidence action under `migration/**`. |
| `FINAL` / `HARNESS_CONTINUATION_FINAL` and no explicit `continue` | Stop for review: report status, evidence, artifacts, remaining risks, and one recommended continue command. Do not mutate the completed run. |

Do not show a broad menu when state is clear. After `FINAL`, stop for review unless the user explicitly requests continue.

| `FINAL` / `HARNESS_CONTINUATION_FINAL` plus explicit `continue` | Start exactly one new bounded ticket from remaining risks. |
| Blocked or missing user input | Stop with an explicit `BLOCKED_*` reason and exact user actions. |

## Explicit continue after SUCCESS

A successful iteration is a checkpoint, not permission to keep migrating forever.

After `FINAL`, zero-argument `/supervised-task` must not start a new ticket. It should print a compact handoff like:

```text
run-002 is FINAL/PASS. I stopped for review.
Recommended next bounded action: fix remaining unmapped UiTargets.
To continue:
  /supervised-task continue fix remaining unmapped UiTargets from run-002
```

The agent may continue past a successful checkpoint only when:

1. the user explicitly asks to continue; or
2. `migration/state/continuation-decision.json` grants bounded auto-continuation for this exact next action.

No extra prompt is required when the user says `continue`; the agent should choose the next bounded ticket from the evidence and stop again after the next SUCCESS checkpoint.

## Ticket priority for explicit continue

When the user explicitly says `continue` after a FINAL checkpoint, choose the next bounded task using this priority order unless the user names a more specific task:

1. project-verify structural errors, missing project references, missing package references, missing usings, or generated test host configuration;
2. unmapped UiTargets that can be reduced using source-truth evidence under `migration/**`;
3. syntax-fallback / semantic context problems, especially when all actions are syntax-fallback and no Roslyn project context was used;
4. RequiredSideEffect helpers that need safe adapter-config mappings;
5. stale or incomplete migration documentation/evidence that affects review readiness.

The ticket should state that it was selected by explicit `/supervised-task continue`, list the evidence files read, and name the allowed roots and stop conditions.

## Safety boundaries

Explicit continue does not grant broader permissions. It still must respect:

- `migration/state/harness-policy.json`;
- scope guard allowed roots;
- OpenCode permission profile;
- artifact-only mode unless the user explicitly changes policy;
- final gate evidence before reporting FINAL.

If external assemblies, credentials, package installs, network access, or product source edits are required, the agent must stop with `BLOCKED_USER_INPUT_REQUIRED` and list exact user actions.

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. Reports should say why work stopped: the SUCCESS checkpoint requires review, and the next action starts only with `To continue, run: /supervised-task continue <next bounded action>`.

