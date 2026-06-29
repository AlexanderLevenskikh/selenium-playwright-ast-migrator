# LEGACY / COMPATIBILITY PROMPT

Use `.agent-loops/kickoff-prompt.txt` for new autopilot runs. This example is kept only for older agent-first docs and must not override the primary loop contract, `migration-artifact` source-edit boundary, or `CONTINUE_AUTONOMOUSLY` continuation rule.

# Start prompt — Strict Mode

Use this prompt when you want safe, conservative migration progress.

```text
Работай в Strict Mode.

Project paths:
- Selenium source tests: <SELENIUM_TESTS_PATH>
- Adapter config: <ADAPTER_CONFIG_PATH>
- Optional profile layers: <PROFILE_PATHS>
- Playwright .NET project for verify-project: <PLAYWRIGHT_DOTNET_PROJECT_PATH or empty>
- Playwright TypeScript project for verify-ts-project: <PLAYWRIGHT_TS_PROJECT_PATH or empty>
- Migration workspace: <MIGRATION_WORKSPACE_PATH>

Rules:
- Do not edit C# migrator code.
- Do not edit generated .cs/.ts files as the final solution.
- Do not edit the source Selenium project.
- Change only adapter-config/profile files and migration reports.
- Never invent selectors. Use POM/helper/source truth.
- Group TODO by full source expression and pattern, not by root `page`/`pagef`.
- If a recognizer/renderer/parser fix is required, create or update `migration/migrator-tickets.md`.

Start by reading:
- migration/agent-state.md
- migration/pre-stop-checklist.md
- latest orchestration-report.md
- latest explain-todo.md
- latest agent-next-task.md
- unmapped-targets.json
- unsupported-actions.json
- verify-project-report.md, if present
- migration-board.md/html, if present

Then do one small safe config/profile change and run:
1. config-validate
2. migrate or orchestrate
3. verify-project / verify-ts-project if applicable
4. config-diff
5. guard
6. explain-todo
7. migration-board

Report before/after metrics and stop if there is a generic blocker.
```
