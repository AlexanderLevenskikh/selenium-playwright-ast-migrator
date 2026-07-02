# Agent loop hardening

This page summarizes the public autopilot-loop hardening rules. It exists so users and contributors can see which prompt is canonical and which files are compatibility wrappers.

## Canonical prompt

Use `.agent-loops/kickoff-prompt.txt` as the single primary loop prompt for new autopilot runs.

Secondary prompts are allowed only for bounded situations:

- `.agent-loops/resume-prompt.txt` — resume from saved state;
- `.agent-loops/strict-ticket-prompt.txt` — restricted ticket/workspace mode;
- `.agent-loops/09-continue-after-compile-fix-prompt.txt` — continuation after a compile-fix checkpoint;
- `templates/migration-kit/prompts/*` — migration-kit wrappers that delegate back to the primary loop contract;
- `examples/agent-first/start-strict.md` and `examples/agent-first/start-creative.md` — legacy examples, not the canonical prompt.

Secondary prompts must not introduce conflicting rules.

## Continuation rule

If the current status is `CONTINUE_AUTONOMOUSLY`, the agent continues without asking the user whether to continue.

A green build or project verify is a checkpoint, not the end of a migration, while reports still contain actionable TODOs, unsupported actions, unmapped targets, empty tests, or runtime candidates.

## Source-edit boundary

Default mode is `migration-artifact`.

In `migration-artifact` mode the agent may work with allowed migration artifacts, config/profile files, generated output folders, reports, boards, POM/helper evidence, and verification outputs. It must not edit migrator repository source code.

Migrator source-code edits are allowed only when the prompt explicitly says:

```text
Mode: migrator-code
Repository source edits are allowed.
```

and the repository source tree is listed under allowed write paths.

If a compiled tool path is provided, the agent must run that tool directly and must not search for `Migrator.Cli`, `.sln`, `.csproj`, or migrator source folders.

For installed-tool OpenCode runs, use an artifact-only boundary:

- allowed writes: `migration/**`;
- proposal-only writes for forbidden real project changes: `migration/proposals/**`;
- forbidden writes: real target project, production POM project, Playwright test project, `.csproj`, `nuget.config`, root-level generated files, and migrator source code.

`Write POM` means generated/shadow/proposal POM under `migration/**`, not editing the real POM project.

## OpenCode hardening

OpenCode should be configured as an execution sandbox, not just a chat prompt:

- `permission.edit`: deny `*`, allow only `migration/**`;
- `permission.task`: deny broad/general/build agents; allow only migration-specific reviewer/watchdog/executor agents;
- `permission.question`, `external_directory`, and `doom_loop`: ask;
- subagent prompts must repeat inherited path boundaries and evidence gates.

If path-level edit permissions are unavailable or unreliable, run `migration/scripts/check-scope.ps1` after every patch and before any final answer.

Do not install the OpenCode team template globally unless you want these
artifact-only rules in every OpenCode session. Prefer project-local install or a
session-specific `OPENCODE_CONFIG` value for migration runs.

## Metric integrity

TODO reduction is progress only when it represents real migration:

- source-backed mappings;
- helper/POM evidence;
- generated POM proposals;
- raw locators from proven selectors;
- explicit TODO classification.

TODO reduction is invalid when caused by broad suppression, FluentAssertions/NUnit/business assertion suppression, empty tests, weakened assertions, dummy target-known identifiers, or real project edits.

`0 TODO` is not success unless scope guard, quality gates, verification evidence, and meaningful generated test bodies all pass.

## Stop-policy checklist

Before any stop, blocker, or handoff, use `.agent-loops/15-stop-policy-checklist.md`.

The checklist requires evidence for:

- current mode;
- selected batch goal;
- allowed input/write paths;
- commands run or why commands were impossible;
- artifacts inspected;
- files changed;
- one valid final status;
- one concrete next action.

If the checklist does not prove a hard stop and another safe step remains, the agent should continue instead of asking the user what to do next.

## Multi-agent mode

Use `.agent-loops/14-multi-agent-loop.md` for multi-agent workflows.

Rules:

- one coordinator owns state, merge decisions, and final verification;
- sub-agents get non-overlapping write paths;
- every sub-agent inherits the same mode/path boundary;
- migration agents cannot edit migrator source in `migration-artifact` mode;
- migrator-code agents are allowed only in explicit `migrator-code` mode;
- every sub-agent handoff must include evidence, commands, changed files, metrics, and stop-policy checklist result.
