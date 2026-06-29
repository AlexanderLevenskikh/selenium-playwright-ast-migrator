# LEGACY / COMPATIBILITY PROMPT

Use `.agent-loops/kickoff-prompt.txt` for new autopilot runs. This example is kept only for older agent-first docs and must not override the primary loop contract, `migration-artifact` source-edit boundary, or `CONTINUE_AUTONOMOUSLY` continuation rule.

# Start prompt — Creative Mode

Use this prompt when you want the agent to explore patterns and make progress, while still keeping hard safety boundaries.

```text
Работай в Creative Mode.

Project paths:
- Selenium source tests: <SELENIUM_TESTS_PATH>
- Adapter config: <ADAPTER_CONFIG_PATH>
- Optional profile layers: <PROFILE_PATHS>
- Playwright .NET project for verify-project: <PLAYWRIGHT_DOTNET_PROJECT_PATH or empty>
- Playwright TypeScript project for TS draft/verify-ts-project: <PLAYWRIGHT_TS_PROJECT_PATH or empty>
- Existing Playwright TS tests to match style: <PLAYWRIGHT_TS_TESTS_PATH or empty>
- Migration workspace: <MIGRATION_WORKSPACE_PATH>

Goal:
Максимально продвигай миграцию через pattern mining, гипотезы и маленькие безопасные эксперименты. Creative Mode разрешает творчески искать strategy, но не разрешает творчески выдумывать факты.

Hard rules:
- Locator must be evidence-based.
- POM property name is not selector.
- Do not use `SideMenuDocumentsAgreements` as `data-tid` just because it is a property name.
- Inspect POM property/helper implementation, for example `CreateControlByTid(...)` and `WithDataTestId(...)`.
- Attribute can be `data-tid`, `data-test`, or `data-test-id`; choose only from source truth or existing target code.
- Do not promote `page`, `pagef`, `modal`, `lightbox`, `WebDriver` to target-known identifiers unless they really exist in target Playwright code.
- Do not call a compile-ready draft “finished”. Distinguish generated / compile-ready / list-ready / smoke-ready / runtime-proven / behavior-reviewed.

Workflow:
1. Analyze latest reports and baseline metrics.
2. Mine repeated TODO patterns by full source expression, not root identifier.
3. Generate 2–3 hypotheses.
4. Run one small safe experiment.
5. Validate with config-validate, migrate/orchestrate, verify, guard, config-diff, explain-todo, migration-board.
6. Keep if metrics improve, rollback if they regress.
7. Create `migration/migrator-tickets.md` entries for parser/recognizer/renderer blockers.

WaitPolicy:
- Elide Selenium actionability waits when Playwright auto-wait covers them.
- Preserve/convert product-state waits: loader, table loaded, modal opened, toast, URL, download, server/DB refresh.
- Do not convert Thread.Sleep/WaitForTimeout blindly; create WAIT_REQUIRES_STATE_ASSERTION TODO/ticket.

TypeScript draft rules:
- Work only inside an existing Playwright TS project or `migration/ts-draft` unless explicitly allowed.
- Reuse existing fixtures/helpers/style.
- Verify with TypeScript compile and `playwright test --list` when possible.
- For every TS file report source count, generated count, skipped count, locator confidence, and runtime readiness.

Continue without asking while the safety loop is green and metrics improve. Ask the user when a generic blocker, selector uncertainty, runtime behavior mismatch, or C# migrator change is required.
```
