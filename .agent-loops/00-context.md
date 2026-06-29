# Migrator Context

You are working on a Selenium C# → Playwright .NET migrator.

## Project purpose

The migrator converts Selenium C# / NUnit tests into Playwright .NET tests.

The intended output should be:

- compile-safe whenever possible;
- semantically close to the original Selenium test;
- explicit about unsupported cases through TODO diagnostics;
- stable under regression/snapshot tests;
- incrementally improved by categories.

## Known project structure

Expected projects may include:

- `Migrator.Core`
- `Migrator.Roslyn`
- `Migrator.Cli`
- `Migrator.Tests`

Do not assume exact paths blindly. Inspect the repository.

## Typical pipeline

The migrator usually follows this conceptual pipeline:

```text
Roslyn semantic-first analysis
→ syntax fallback when needed
→ adapter/config mapping
→ target expression resolution
→ action recognition
→ Playwright .NET renderer
→ report/diagnostics
→ snapshot/compile-smoke tests
```

## Important concepts

Common concepts in this codebase may include:

- `UnsupportedAction`
- `TargetExpression`
- `PlaywrightLocator`
- `PageObjectProperty`
- `Unresolved`
- adapter config / adapter draft
- compile-smoke
- snapshot tests
- TODO diagnostics
- unmapped targets
- unsupported actions
- `analyze`
- `migrate`
- `verify`

Names may differ in the actual repository. Use actual code as source of truth.

## Loop modes

There are two different workflows. Do not mix them.

### Default: migration-artifact loop

This is the normal loop for project migrations.

The agent may work with:

- migration reports and boards;
- generated output directories;
- adapter config/profile files;
- POM/helper inventory artifacts;
- source Selenium tests and target Playwright examples inside allowed paths;
- verify/project-verify/smoke-plan artifacts.

The agent must not edit migrator repository source code in this mode.

If the migrator engine appears to need a code fix, create a bounded finding or
ticket candidate and continue with migration-safe work if possible.

### Explicit: migrator-code loop

Use this mode only when the prompt explicitly says repository source edits are
allowed.

In this mode the agent may edit migrator source code and must add/update focused
regression tests.

## Core engineering principle

Prefer deterministic migration logic.

The migrator itself must not depend on an LLM, external agent, or
non-deterministic runtime behavior.

Agent loops drive migration work and, when explicitly allowed, deterministic
migrator-code improvements. They are not a license to ignore path boundaries or
switch modes silently.
