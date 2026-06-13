# Migrator — Selenium C# to Playwright .NET

A migration toolkit that turns Selenium WebDriver (C# / NUnit) UI tests into Playwright .NET code through a controlled workflow: **analyze → configure → migrate → verify → propose → iterate**.

This tool does not promise fully automatic migration. It replaces line-by-line rewrites with generated scaffolding that you review, fix, and improve iteratively.

## What it does

- Parses Selenium C# test files using Roslyn AST
- Recognizes UI actions: clicks, input, waits, assertions, page navigation
- Adapts source expressions to Playwright locators using a JSON profile config
- Generates Playwright .NET C# code with TODO comments for items needing manual review
- Validates generated code quality with compile-smoke and verify checks
- Proposes profile improvements ranked by impact score
- Scans target Playwright projects to discover infrastructure facts
- Orchestrates the full pipeline as a non-destructive dry-run

## Who is it for

- Teams migrating Selenium C# / NUnit tests to Playwright .NET
- Developers who want generated scaffolding instead of writing every test from scratch
- AI agents that safely improve migration profiles without inventing selectors

## Architecture at a glance

```
input file (.cs)
    │
    ▼  [parse]      Roslyn parser: AST → intermediate representation (IR)
    ▼  [recognize]  Recognizers: click, input, assertion, wait, unsupported
    ▼  [adapt]      Adapter: source → Playwright locator (via JSON config)
    ▼  [render]     Renderer: IR → generated C# code (Playwright .NET)
    ▼  [report]     ReportBuilder: conversion statistics
    │
output file (.cs) + report
```

## Modes

| Mode | Description |
|---|---|
| `analyze` | Parse and analyze tests, produce reports and draft config |
| `migrate` | Generate Playwright C# files |
| `verify` | Validate generated code quality with quality gates |
| `propose` | Generate ranked mapping improvement proposals |
| `discover-target` | Scan target Playwright project for infrastructure facts |
| `orchestrate` | Run full pipeline: analyze → migrate → verify → propose (dry-run) |

## Quick command example

```bash
dotnet run --project Migrator.Cli -- --mode orchestrate --input ./SeleniumTests --config ./adapter-config.json --out ./orchestration --format both
```

## Recommended workflow

```
1. Start with 1-5 test files (pilot)
2. Run analyze, review what the tool understands
3. Add source-truth mappings to adapter config
4. Generate code, verify quality
5. Compile smoke test, then runtime proof
6. Use propose to find next mappings
7. Iterate until quality gates pass
```

## Documentation

- [**Quick Start**](docs/user-guide/quick-start.md) — try the tool in 10-15 minutes
- [**Migration Workflow**](docs/user-guide/migration-workflow.md) — full process from pilot to production
- [**Profile Cookbook**](docs/user-guide/project-profile-cookbook.md) — configure UiTargets, Methods, Scopes, etc.
- [**Common Recipes**](docs/user-guide/common-recipes.md) — practical solutions for frequent migration patterns
- [**Reports & Quality Gates**](docs/user-guide/reports-and-quality-gates.md) — reading reports and configuring gates
- [**Limitations**](docs/user-guide/limitations.md) — honest boundaries of what the tool can and cannot do
- [**Agent Playbooks**](docs/agent-playbooks/README.md) — procedural guides for AI agents

## Existing reference docs

- [Architecture](docs/architecture.md) — project structure and responsibilities
- [Locator Matching](docs/profile/locator-matching.md) — TargetKind and Match strategy details
- [Method Mappings](docs/profile/method-mappings.md) — exact and template method mappings
- [Parameterized Methods](docs/profile/parameterized-method-mappings.md) — pattern-based mappings with placeholder substitution
- [Profile Scoping](docs/profile/profile-scoping.md) — per-file config overrides via Scopes
- [Runtime Host](docs/profile/runtime-host.md) — TestHost config for class wrapper generation
- [Target Discovery](docs/profile/target-discovery.md) — discover-target mode reference
- [Mapping Proposals](docs/profile/mapping-proposals.md) — propose mode reference
- [Orchestrator Dry-Run](docs/profile/orchestrator-dry-run.md) — orchestrate mode reference

## Limitations

- Not 100% automatic — project-specific semantics require profile configuration
- Runtime pass requires environment, auth, and test data
- Discovery output requires human review before using as config
- Complex table/pagination flows may need manual migration
- Some generated tests need body-level edits
- Playwright TypeScript target is not supported

## Important

**Never invent selectors.** All locators must come from source-truth (PageObject code, HTML, or discovery). The tool uses `<SOURCE_TRUTH_REQUIRED>` placeholders when a mapping needs a verified selector.

## Installation

```bash
git clone <repo-url>
cd Migrator
dotnet restore
```

## Tests

```bash
dotnet test
```

Runs 188 tests: snapshot checks, parser tests, compile-smoke checks, orchestrator integration tests, and more.

## Publish

```bash
dotnet publish Migrator.Cli -c Release -o ./publish
```

Produces a self-contained executable.

## Why this approach?

The tool does not replace migration expertise. It turns migration from rewriting every test by hand into a controlled workflow: generate, verify, classify, improve profile, repeat.

Even partial migration saves significant time because developers review and fix generated tests instead of writing every test from scratch.
