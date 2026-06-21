# Migrator — Autopilot AGENTS.md

## Purpose

Migrator is a .NET 8 CLI tool for converting Selenium C# / NUnit tests to Playwright tests.

Pipeline: **parse → recognize → IR → adapt/configure → render → report → verify**.

This repository is configured for **Autopilot Loop** work: an agent should continue through small verified iterations until the selected migration block is done or a real stop condition is reached.

## Autopilot-first rule

For development of the migrator itself, the primary workflow is now:

1. Read `.agent-loops/README.md`.
2. Read every file in `.agent-loops/`.
3. Read this `AGENTS.md`.
4. Start `Migrator Autopilot Loop`.
5. Do not ask the user to choose between technical implementation options.
6. Continue if status is `CONTINUE_AUTONOMOUSLY`.
7. Stop only according to `.agent-loops/03-stop-policy.md`.

The old human-checkpoint workflow is intentionally removed from this package.

Forbidden stop phrases:

- “Which option do you prefer?”
- “Should I use approach A or B?”
- “Do you want me to continue?”
- “I can continue if you want.”
- “There are several possible implementations.”

Choose the safest option and continue.

## Architecture

| Project | Responsibility |
|---|---|
| `Migrator.Core` | IR models, interfaces, pipeline, reports. No Roslyn/Selenium/Playwright implementation dependencies. |
| `Migrator.Roslyn` | Roslyn parser and recognizers. |
| `Migrator.SeleniumCSharp` | Selenium C# adapter and config-driven source mapping. |
| `Migrator.PlaywrightDotNet` | Playwright .NET renderer. |
| `Migrator.PlaywrightTypeScript` | Experimental Playwright TypeScript renderer. |
| `Migrator.Cli` | CLI entry point and command modes. |
| `Migrator.Tests` | Regression tests, snapshots, compile-smoke checks. |

Core must stay generic. Do not put framework-specific recognizer or renderer logic into `Migrator.Core`.

## Key files

- `Migrator.Roslyn/RoslynTestFileParser.cs` — parser and recognizer registration.
- `Migrator.Roslyn/Recognizers/` — Selenium/NUnit/FluentAssertions recognizers.
- `Migrator.PlaywrightDotNet/PlaywrightDotNetRenderer.cs` — C# Playwright .NET generation.
- `Migrator.SeleniumCSharp/DefaultProjectAdapter.cs` — JSON profile/config mapping.
- `Migrator.Core/Models/TargetExpression.cs` — target expression model.
- `Migrator.Core/Models/UnsupportedAction.cs` — unsupported action model.
- `Migrator.Core/VerifyRunner.cs` — verification pipeline.
- `Migrator.Tests/SnapshotTests.cs` — snapshot coverage.
- `Migrator.Tests/TicketRegressionTests.cs` — regression coverage for known migration tickets.
- `Migrator.Tests/CompileChecker.cs` — compile-smoke checks.

## Mandatory checks

Run these whenever applicable:

```bash
dotnet build
dotnet test Migrator.Tests/Migrator.Tests.csproj
```

If a real source input is available, also run a verify/orchestrate command, for example:

```bash
dotnet run --project Migrator.Cli -- --mode verify --input <SOURCE_SELENIUM_TESTS> --out <VERIFY_OUT>
```

Use actual repository paths. Inspect the repo instead of asking the user when paths are discoverable.

## Engineering rules

- Prefer Roslyn semantic model over regex/string parsing.
- Use syntax fallback only when semantic data is unavailable.
- Never transform C# string literals, comments, CSS selectors, XPath selectors, URLs, verbatim strings, or interpolated string content as executable code.
- Generated Playwright code should compile whenever possible.
- Unsupported cases must produce explicit TODO diagnostics rather than unsafe generated code.
- Do not weaken tests to make them pass.
- Add/update regression tests for behavior changes.
- Snapshot changes must be intentional and explained.
- Prefer small local fixes over broad rewrites.
- Keep renderer output stable; avoid unrelated formatting churn.

## Config/profile rules

- Keep project-specific knowledge in `adapter-config.json` or migration profiles, not in generic renderer logic.
- Do not invent selectors.
- PageObject property names are not selectors.
- Source truth must come from POM/helper code, config, or existing target Playwright code.
- Use `TargetKnownTypes` / `TargetKnownIdentifiers` only for symbols that really exist in target code.
- Keep Selenium-only roots in `SourceOnlyIdentifiers` when they are not target-side symbols.
- Do not reduce TODO count through unsafe broad suppression.

## Work queue

Follow `.agent-loops/04-work-queue.md`.

Default priority:

1. Build errors in the migrator itself.
2. Failing `Migrator.Tests` tests.
3. Compile errors in generated Playwright output.
4. High-frequency `UnsupportedAction` categories.
5. Unresolved target expressions.
6. TODOs that block compilation.
7. TODOs that preserve compilation but reduce migration quality.
8. Page object field/property transfer gaps.
9. Renderer/snapshot mismatches.
10. Adapter config improvements.
11. Report/diagnostic improvements.
12. Cleanup/refactoring.

## Main CLI modes

| Mode | Purpose |
|---|---|
| `doctor` | Preflight checks: input, config, project files, tooling, source truth hints. |
| `analyze` | Parse Selenium files and produce reports without final generated code. |
| `migrate` | Generate Playwright .NET or TS tests. |
| `verify` | Lightweight generated-code verification. |
| `verify-project` | Compile generated Playwright .NET tests against a real project/harness. |
| `verify-ts-project` | Type-check generated Playwright TS tests inside an existing TS project. |
| `orchestrate` | Run analyze → migrate → verify → reports for Playwright .NET. |
| `index-pom` | Mine Selenium PageObjects and helper selectors. |
| `profile-match` | Estimate whether existing profiles can be reused for a new project. |
| `config-validate` | Validate profile safety and common mistakes. |
| `config-diff` | Review config changes. |
| `guard` | Compare before/after migration metrics and catch regressions. |
| `explain-todo` | Turn TODO markers into prioritized root-cause insights. |
| `smoke-plan` | Rank generated tests by runtime readiness. |
| `runtime-classify` | Classify Playwright runtime failures after smoke runs. |
| `migration-board` | Generate an HTML dashboard from migration artifacts. |
| `config-schema` | Export JSON Schema for adapter config. |

## Autopilot reports

When stopping, use `.agent-loops/06-report-format.md`.

Allowed final statuses:

- `READY_FOR_ACCEPTANCE`
- `TICKET_NEEDED`
- `BLOCKED_BY_ENVIRONMENT`
- `BLOCKED_BY_MISSING_INPUT`
- `MAX_ITERATIONS_REACHED`

If the status would be `CONTINUE_AUTONOMOUSLY`, do not stop. Continue.

## Documentation index

Start here:

- `.agent-loops/README.md`
- `docs/autopilot-loop.md`
- `docs/README.md`
- `docs/architecture.md`
- `docs/project-verification.md`
- `docs/explain-todo.md`
- `docs/migration-board.md`
- `docs/wait-policy.md`
- `docs/typescript-target.md`
