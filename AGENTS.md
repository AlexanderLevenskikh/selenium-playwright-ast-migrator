# Migrator — Autopilot AGENTS.md

## Purpose

Migrator is a .NET 8 CLI tool for converting Selenium C# / NUnit tests to Playwright tests.

Pipeline: **parse → recognize → IR → adapt/configure → render → report → verify**.

This repository is configured for **Autopilot Loop** work: an agent should continue through small verified iterations until the selected migration block is done or a real stop condition is reached.

Important mode boundary:

- Default project-migration work is `migration-artifact` mode. In that mode, do not edit migrator repository source code; work only with allowed migration inputs, config/profile files, generated outputs, reports, boards, and verification artifacts.
- Migrator source-code edits are allowed only when the prompt explicitly says `Mode: migrator-code` and the repository is listed as an allowed write path.
- Read `.agent-loops/13-loop-contract.md` before starting any loop.

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
- “Project verify is green, so I am done.”
- “The next work is migration-quality improvement, so I should stop.”
- “There are TODO reduction trade-offs, so the user should decide.”

Choose the safest option and continue.

## Strict ticket / workspace boundaries

When the user provides explicit paths, a DLL/artifact folder, a ticket folder,
or a restricted workspace, those boundaries are the task contract. Read
`.agent-loops/11-strict-ticket-boundaries.md` before doing any work.

Hard rules:

- Do not traverse parent directories looking for source code, solutions, configs, or artifacts.
- Do not search the user's Desktop/home/work folders unless that exact path was allowed.
- If the task points to DLLs or artifacts, treat them as the source of truth; do not locate or edit matching source code.
- Do not edit repository source files unless the current ticket explicitly allows source edits and the repository path is listed as an allowed write path.
- Do not broaden the current ticket into general cleanup or unrelated fixes.
- Before writing a file, verify that the path is inside an allowed write root.
- If source changes appear necessary but source editing is not allowed, report `requires source-change ticket` instead of making the change.

Restricted path rules override generic discovery advice in this file.


## Checkpoint vs completion

A green build, green `verify-project`, or zero compile errors is a checkpoint, not the end of the migration, unless the user explicitly requested only compile/build work.

If the latest migration board still has actionable categories, use:

```text
Completed batch: READY_FOR_ACCEPTANCE
Overall migration loop: CONTINUE_AUTONOMOUSLY
```

Do not stop only because generated code compiles or only warnings remain.

Migration-quality trade-offs are not a stop reason by themselves. Choose the smallest safe reversible batch and continue unless the trade-off requires product/business semantics or unavailable source truth.

Read `.agent-loops/08-continuation-rule.md` after any milestone batch.

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

Use actual repository paths only when they are inside the allowed workspace for the current task. Do not discover paths by walking outside allowed roots; ask/report `BLOCKED_BY_MISSING_INPUT` instead.

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
- Before large POM/config work, run or inspect `index-pom`.
- Before mapping/suppressing POM or project helper wrappers, run or inspect `helper-inventory`.
- Missing target Playwright POM coverage is not automatically `TICKET_NEEDED`: generate POM scaffolds in migration output or use raw locators when Selenium POM selectors are proven.
- Prefer locator strategy: existing target POM member → generated POM in migration output → raw Playwright locator from proven selector → explicit TODO.
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
| `helper-inventory` | Inspect Selenium helper/POM method bodies before MethodSemantics, mappings, or suppressions. |
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
- `.agent-loops/12-pom-helper-recovery-policy.md`
