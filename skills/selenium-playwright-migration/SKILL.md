---
name: selenium-playwright-migration
description: Agent workflow for safe migration of Selenium C# NUnit tests to Playwright .NET using the Migrator MCP tools.
---

# Selenium → Playwright migration skill

Use for C# NUnit test suites. Do not claim xUnit source, Python, Java or full behavioral parity in this alpha.

## Workflow

1. Call `analyze_migration` with the narrowest source scope and optional project adapter config. Read `discoveryStatus`, `files`, `findings`, `declaredTests` and `parsedTests`.
2. Stop or narrow scope if discovery is `partial`. In particular, `MULTIPLE_FIXTURES`, `UNSUPPORTED_SOURCE_FRAMEWORK`, and `DECLARATION_COUNT_MISMATCH` must be resolved or reported; never treat the omitted tests as migrated.
3. For supported inputs, call `preview_migration` with `expectedAnalysisId`. If stale, start fresh with analyze. Preview is read-only.
4. Inspect returned generated source and TODOs; do not invent selectors or infer project-specific helper semantics from names.
5. Make changes yourself only in an explicitly authorized target workspace. Preserve assertions, data cases and lifecycle behavior. MCP alpha never applies patches.
6. Run a real project-aware compilation and, if available, selected test/runtime checks with the normal development tools. Record commands, exit codes and artifacts.
7. Do not represent successful generation as successful compilation, nor compilation as behavior preservation. If no actual check was run, report `not_run`.
8. Summarize transformed tests, unresolved cases and independently observed verification results.

## Evidence labels

`syntax` means an AST match, `recognized` means a parser/adapter finding, and `diagnostic` means an error observed by tooling. None is itself a behavioral proof.

Never suppress unsupported actions just to improve completion metrics. Do not turn TODO-only generated files into successes. Do not fabricate JSON PASS results or reuse evidence after any source/config/target change.
