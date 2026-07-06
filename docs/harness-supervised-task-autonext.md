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
| `FINAL_STOPPED_FOR_REVIEW` plus plain explicit `continue` | Invoke `migration-researcher` for post-final TODO/source-truth research. Do not ask for a long supervisor prompt. |
| `FINAL_RESEARCH_COMPLETED` | Invoke `migration-change-reviewer` to validate research and extract one bounded implementation ticket recommendation. |

Do not show a broad menu when state is clear. After `FINAL`, stop for review unless the user explicitly requests continue. Plain `continue` means research first, not immediate implementation.
| Blocked or missing user input | Stop with an explicit `BLOCKED_*` reason and exact user actions. |

## Explicit continue after SUCCESS

A successful iteration is a checkpoint, not permission to keep migrating forever.

After `FINAL`, zero-argument `/supervised-task` must not start a new ticket. It should print a compact handoff like:

```text
run-002 is FINAL/PASS. I stopped for review.
Recommended next step: post-final research of remaining TODO/source-truth risks.
To continue:
  /supervised-task continue
```

The agent may continue past a successful checkpoint only when:

1. the user explicitly asks to continue; or
2. `migration/state/continuation-decision.json` grants bounded auto-continuation for this exact next action.

No extra prompt is required when the user says `continue`; the agent should invoke `migration-researcher` first. The researcher writes `migration/runs/<active-run>/research/**`, then `migration-change-reviewer` validates findings before implementation.

## Ticket priority after research review

After research is reviewed, choose the next bounded implementation task using this priority order unless the user names a more specific task:

1. project-verify structural errors, missing project references, missing package references, missing usings, or generated test host configuration;
2. unmapped UiTargets that can be reduced using source-truth evidence under `migration/**`;
3. syntax-fallback / semantic context problems, especially when all actions are syntax-fallback and no Roslyn project context was used;
4. RequiredSideEffect helpers that need safe adapter-config mappings;
5. stale or incomplete migration documentation/evidence that affects review readiness.

The ticket should state that it was selected from post-final research or explicit `/supervised-task continue <task>`, list the evidence files read, and name the allowed roots and stop conditions.

## Safety boundaries

Explicit continue does not grant broader permissions. It still must respect:

- `migration/state/harness-policy.json`;
- scope guard allowed roots;
- OpenCode permission profile;
- artifact-only mode unless the user explicitly changes policy;
- final gate evidence before reporting FINAL.

If external assemblies, credentials, package installs, network access, or product source edits are required, the agent must stop with `BLOCKED_USER_INPUT_REQUIRED` and list exact user actions.

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. Reports should say why work stopped: the SUCCESS checkpoint requires review, and the next action starts with `To continue, run: /supervised-task continue`, which triggers post-final research by default.

