> **Legacy/background note:** Do not use this document as the current guarded OpenCode Desktop launch procedure. For current migration-agent runs, start with `docs/guarded-opencode-desktop-runbook.ru.md`.

# Agent modes: Strict and Creative

The migrator is designed to work well with AI agents, but different migration phases need different levels of freedom.

## Strict Mode

Use Strict Mode when correctness matters more than exploration:

- preparing a merge request;
- stabilizing a project profile;
- reviewing final generated tests;
- working near release branches;
- validating CI results.

Strict Mode rules:

- change only adapter config/profile files unless explicitly allowed;
- never edit generated tests as the final solution;
- never edit the source Selenium project;
- never invent selectors;
- run the safety loop after every change;
- stop on generic migrator blockers and create a ticket.

Primary prompt: `.agent-loops/kickoff-prompt.txt`. Legacy example: [`examples/agent-first/start-strict.md`](../examples/agent-first/start-strict.md).

## Creative Mode

Use Creative Mode for exploration and pattern mining:

- discovering repeated TODO patterns;
- drafting TypeScript tests in an existing Playwright project;
- finding config/profile opportunities;
- creating high-quality migrator tickets;
- reducing unknowns before a stricter pass.

Creative Mode can propose hypotheses and run small experiments, but it is still bounded:

- selectors must be evidence-based;
- PageObject property names are not selectors;
- source-side Selenium roots must not become target-known identifiers without proof;
- readiness must not be overstated;
- generated/compile-ready/runtime-proven are different states.

Primary prompt: `.agent-loops/kickoff-prompt.txt`. Legacy example: [`examples/agent-first/start-creative.md`](../examples/agent-first/start-creative.md).

## Required project inputs

Before starting, collect paths and write them into the prompt or `migration/agent-state.md`:

| Input | Example | Required for |
|---|---|---|
| Selenium source tests | `C:\repo\selenium_tests\Project.E2ETests` | all migration modes |
| Adapter config | `migration\adapter-config.json` | config-driven migration |
| Profile layers | `profiles\base.adapter.json`, `profiles\projects\billing.adapter.json` | reusable migration profiles |
| Playwright .NET project | `C:\repo\playwright-dotnet-tests` | `verify-project` |
| Playwright TS project | `C:\repo\front` | `--target ts`, `verify-ts-project` |
| Output workspace | `migration\orchestration-1` | all runs |

## Safety loop

After every meaningful change:

```text
config-validate
migrate / orchestrate
verify-project / verify-ts-project
config-diff
guard
explain-todo
migration-board
```

If a change improves metrics and does not introduce compile/guard regressions, keep it. Otherwise rollback and record the hypothesis.
