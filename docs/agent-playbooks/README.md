# Agent Playbooks

Procedural guides for AI agents working with the Migrator.

Each playbook describes a specific task: what input you need, step-by-step actions, what NOT to do, and acceptance criteria.


## Choose an agent mode first

- Use [Strict Mode](../agent-modes.md#strict-mode) for safe config-only iterations, final review, and MR preparation.
- Use [Creative Mode](../agent-modes.md#creative-mode) for pattern mining, TypeScript drafts, and blocker discovery.
- Start prompts live in `examples/agent-first/start-strict.md` and `examples/agent-first/start-creative.md`.

## Available playbooks

| Playbook | Task |
|---|---|
| [Add UiTarget Mapping](add-ui-target-mapping.md) | Map an unmapped page element to a Playwright locator |
| [Add Method Mapping](add-method-mapping.md) | Map a project-specific helper method |
| [Add Parameterized Method Mapping](add-parameterized-method-mapping.md) | Map a helper with varying arguments |
| [Add Profile Scope](add-profile-scope.md) | Configure file-specific overrides |
| [Add Table/List Mapping](add-table-list-mapping.md) | Map table rows or list items |
| [Use Propose Report](use-propose-report.md) | Consume and apply mapping proposals |
| [Runtime Proof](runtime-proof.md) | Safely attempt runtime verification |
| [Classify Failures](classify-failures.md) | Categorize test failures |
| [Safety Rules](safety-rules.md) | Hard rules — read before starting any task |
| [Source-only Pattern Backlog](source-only-pattern-backlog.md) | Turn `SOURCE_ONLY_IDENTIFIER(page/pagef)` floods into actionable source-pattern backlog |
| [Run Agent Migration Iteration](run-agent-migration-iteration.md) | One safe config/profile iteration |
| [Escalate to Developer](escalate-to-developer.md) | Create a developer escalation package |
| [Reuse Existing Profile](reuse-existing-profile.md) | Apply a base migration profile to a similar project |
| [Runtime Smoke One Test](runtime-smoke-one-test.md) | Runtime-proof one selected smoke candidate |

## General principles

1. **Source truth only**: All selectors must come from verified source code, not guesses.
2. **One change at a time**: Apply one mapping, re-run, verify improvement.
3. **No config mutation by tool**: The tool does not modify `adapter-config.json`. You edit the config, then re-run.
4. **Report metrics**: Always report before/after metrics for each change.
5. **Pattern before escalation**: If TODO are dominated by `SOURCE_ONLY_IDENTIFIER`, group by full source expression/pattern before creating an escalation.
6. **Never auto-apply**: Proposals are suggestions, not instructions to blindly apply.

## Workflow for agents

```
1. Read safety rules (safety-rules.md)
2. Read the relevant playbook
3. Run the required tool command
4. Read the output artifacts
5. Apply one config change
6. Re-run and verify
7. Report results
```

## Agent-first workflow

Перед сложной миграцией агент должен читать:

- `docs/agent-first-workflow.md`
- `docs/agent-command-set.md`
- `docs/agent-first-checklist.md`
- `docs/escalation-reports.md`

Эти документы задают роли, безопасные команды, условия остановки и формат эскалации.
