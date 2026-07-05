# State-aware zero-argument dispatch for `/supervised-task`

`/supervised-task` is the tester-facing OpenCode entrypoint for guarded migration work. It should be safe to run without arguments.

```text
/supervised-task
```

The user should not need to know whether the current workspace is `FINAL`, `NOT FINAL`, `NOT RUNTIME READY`, or `CONTINUE_REQUIRED`. If `$ARGUMENTS` is empty or only whitespace, the command inspects the Harness Kit state and chooses the next bounded action; do not ask the user what to do next when state evidence is sufficient.

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
| `FINAL` / `HARNESS_CONTINUATION_FINAL` | Start a new bounded ticket from remaining risks instead of asking the user for a prompt. |
| Blocked or missing user input | Stop with an explicit `BLOCKED_*` reason and exact user actions. |

## Auto-selected ticket priority after FINAL

When the previous run is final, `/supervised-task` should not mutate that completed run blindly. It should create/update `migration/current-ticket.md` for the next bounded task using this priority order:

1. project-verify structural errors, missing project references, missing package references, missing usings, or generated test host configuration;
2. unmapped UiTargets that can be reduced using source-truth evidence under `migration/**`;
3. syntax-fallback / semantic context problems, especially when all actions are syntax-fallback and no Roslyn project context was used;
4. RequiredSideEffect helpers that need safe adapter-config mappings;
5. stale or incomplete migration documentation/evidence that affects review readiness.

The ticket should state that it was auto-selected by `/supervised-task`, list the evidence files read, and name the allowed roots and stop conditions.

## User experience

The intended loop for testers is:

```text
/supervised-task
```

After a final checkpoint, the next invocation of the same command starts the next bounded migration task. The tester should not have to write custom prompts like “investigate structural errors” unless they want to override the default priority.

## Safety boundaries

Auto-next does not grant broader permissions. It still must respect:

- `migration/state/harness-policy.json`;
- scope guard allowed roots;
- OpenCode permission profile;
- artifact-only mode unless the user explicitly changes policy;
- final gate evidence before reporting FINAL.

If external assemblies, credentials, package installs, network access, or product source edits are required, the agent must stop with `BLOCKED_USER_INPUT_REQUIRED` and list exact user actions.
