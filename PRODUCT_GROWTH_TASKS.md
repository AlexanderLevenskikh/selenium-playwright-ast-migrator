# Product Growth Tasks

Temporary planning file for the next product wave. Keep tasks large and product-shaped; split into implementation issues only when a task is ready for a sprint.

## Current Framework Support Audit

### C# Source And Playwright .NET Target

Current state:
- The stable public path is documented as Selenium C# / NUnit -> Playwright .NET.
- The Playwright .NET renderer already supports NUnit and xUnit output through `TestHost.TargetTestFramework`.
- Supported config values are `nunit`, `NUnit`, `xunit`, and `xUnit`.
- xUnit rendering emits `Microsoft.Playwright.Extensions.Xunit`, `Xunit`, `[Fact]`, `[Theory]`, `[InlineData]`, `[Trait]`, and `IAsyncLifetime` for generated setup.
- NUnit remains the default and best-covered path.

Gaps:
- `scaffold` is still NUnit-only.
- `verify-project` default package references are still NUnit-only.
- Public docs still mostly speak as if NUnit is the main supported C# framework.
- There is no simple CLI option such as `--target-test-framework xunit`; users must know how to write `TestHost.TargetTestFramework` in config.
- xUnit has regression coverage, but it needs a fuller compile/project-verify fixture.

Desired public stance:
- C# target should officially support both NUnit and xUnit.
- NUnit can remain the default for backward compatibility.
- xUnit should be treated as supported once scaffold, verify-project defaults, docs, and fixtures are aligned.
- MSTest should be called out as not supported yet unless implemented.

### Other Languages

Current state:
- Java Selenium and Python Selenium are experimental source frontends.
- Playwright TypeScript is an experimental target backend.
- Framework-specific target choices for Java/Python are not yet a first-class product concept.

Desired direction:
- Java source framework detection should distinguish JUnit 4, JUnit 5, and TestNG when possible.
- Python source framework detection should distinguish pytest and unittest when possible.
- TypeScript target should default to `@playwright/test`.
- Future Java target should choose Playwright Java + JUnit 5/TestNG explicitly.
- Future Python target should choose Playwright Python + pytest/unittest explicitly.

## 1. Init Wizard And Project Onboarding

Goal: make `migrator init --wizard` the easiest way to start a migration correctly.

Why this matters:
- New users should not need to understand every config field before their first useful run.
- The wizard should turn discovery facts into a safe starter workspace, profile, commands, and next steps.
- This is the most important public-usability feature.

Proposed command:

```text
selenium-pw-migrator init --wizard
```

Optional non-interactive form:

```text
selenium-pw-migrator init --wizard --source ./OldTests --target dotnet --target-test-framework xunit --workspace migration
```

Wizard flow:
- Ask for source path.
- Auto-detect source language and source test framework.
- Ask/confirm target backend: Playwright .NET or Playwright TypeScript.
- For Playwright .NET, ask/confirm target test framework: NUnit or xUnit.
- Ask whether a target Playwright project already exists.
- If target project exists, run or suggest `discover-target`.
- If no target project exists, generate scaffold using the selected test framework.
- Ask for target namespace/base class only when discovery cannot infer them.
- Ask for default test id attribute: `data-testid`, `data-test-id`, `data-test`, `data-tid`, or custom.
- Ask whether to install migration-kit/agent loop files.
- Generate profile/config, run ledger, safety checklist, and first command file.

Generated outputs:
- `migration/profiles/adapter-config.json`
- `migration/current-ticket.md`
- `migration/state/run-ledger.md`
- `migration/README.md`
- optional scaffold project
- optional `.agent-loops` or kit prompts
- `next-commands.md` with exact commands to run

Acceptance criteria:
- A user can run one command and get a usable migration workspace.
- Wizard supports both NUnit and xUnit for Playwright .NET.
- Wizard never silently overwrites an existing migration workspace.
- Non-interactive flags produce the same output as interactive answers.
- Generated config passes `config-validate`.
- Generated scaffold builds for NUnit and xUnit.

## 2. Framework Matrix For Source And Target

Goal: make test-framework support explicit, selectable, and verifiable.

Scope:
- C# source framework detection: NUnit, xUnit, MSTest as detected/unsupported if not implemented.
- C# target framework selection: NUnit and xUnit.
- TypeScript target framework: `@playwright/test`.
- Python source framework detection: pytest, unittest.
- Java source framework detection: JUnit 4, JUnit 5, TestNG.

Tasks:
- Add a public framework matrix document.
- Add `--target-test-framework <nunit|xunit>` for relevant commands.
- Persist framework choice into `TestHost.TargetTestFramework`.
- Make `scaffold` generate NUnit or xUnit project files.
- Make `verify-project` default package references depend on target framework.
- Add compile/project-verify fixtures for xUnit.
- Make `capabilities` report framework coverage, not only language/backend coverage.
- Make docs stop implying C# support means NUnit only.

Acceptance criteria:
- `migrate --target dotnet --target-test-framework nunit` renders NUnit output.
- `migrate --target dotnet --target-test-framework xunit` renders xUnit output.
- `scaffold --target-test-framework nunit` builds.
- `scaffold --target-test-framework xunit` builds.
- `verify-project` uses correct default packages for NUnit and xUnit.
- Capability report clearly says what is stable, preview, unsupported, or planned.

## 3. Doctor Fix Mode

Goal: make `doctor --fix` a safe repair assistant for common setup/config problems.

Why this matters:
- `doctor` already detects many problems. The next product step is to turn safe findings into reversible fixes.
- This should feel like a helpful setup assistant, not an unsafe auto-refactor.

Proposed commands:

```text
selenium-pw-migrator --mode doctor --input ./OldTests --config ./adapter-config.json --out doctor
selenium-pw-migrator --mode doctor --input ./OldTests --config ./adapter-config.json --fix --out doctor-fix
selenium-pw-migrator --mode doctor --input ./OldTests --fix --dry-run
```

Fix categories:
- Create missing migration workspace folders.
- Create starter adapter config when none exists.
- Add missing `Verification` defaults.
- Add missing schema reference hints.
- Normalize config shape from older versions.
- Add safe target framework/package defaults after confirmation or wizard selection.
- Create `.gitignore` for generated migration artifacts.
- Suggest, but not apply, risky selector/POM mappings.

Safety model:
- Default to dry-run preview unless `--apply` is explicit.
- Never edit source tests.
- Never invent selectors.
- Always create backups or `.new` files when touching existing config.
- Separate safe automatic fixes from manual recommendations.

Artifacts:
- `doctor-fix-plan.md`
- `doctor-fix-plan.json`
- `doctor-fix.patch` or generated `.new` files
- `doctor-fix-report.md`

Acceptance criteria:
- `doctor --fix --dry-run` never writes project files.
- `doctor --fix --apply` only writes inside allowed workspace/config paths.
- Every applied fix is listed with before/after explanation.
- Risky fixes are recommendations only.
- Tests cover missing config, missing verification defaults, old config shape, and no-overwrite behavior.

## 4. Report Serve Dashboard

Goal: add `report serve` as a local interactive dashboard for migration artifacts.

Why this matters:
- The tool already emits many reports. Users need a single navigable view for progress, TODOs, failures, and next tickets.
- This is a high-value public demo feature.

Proposed command:

```text
selenium-pw-migrator report serve --input migration/runs/latest --port 5077
```

Alternative mode-compatible command:

```text
selenium-pw-migrator --mode report-serve --input migration/runs/latest --port 5077
```

Dashboard sections:
- Overview: files, tests, generated files, syntax errors, TODOs, unsupported actions, unmapped targets.
- Quality trend: compare multiple runs.
- TODO explorer: grouped by `MIGRATOR:*` code.
- Unsupported actions: frequency, examples, recommended ticket.
- Unmapped targets: source expression, suggested evidence, mapping status.
- Verify/project-verify diagnostics.
- Runtime failures from `runtime-classify`.
- Proposed next tickets.
- Downloadable evidence pack.

Implementation approach:
- Start with static HTML generated from artifacts.
- Then add optional local server with no external network dependency.
- Avoid sending project data anywhere.

Acceptance criteria:
- `report serve` opens a local dashboard from an artifact directory.
- Static fallback works without a long-running server.
- Dashboard can compare at least two runs.
- It handles missing artifact files gracefully.
- It can export an evidence zip.

## 5. Profile Marketplace

Goal: make reusable migration profiles discoverable, installable, and versioned.

Why this matters:
- Migration quality depends heavily on project/profile knowledge.
- A public tool needs reusable examples and a safe way to borrow known mappings.

Proposed commands:

```text
selenium-pw-migrator profile list
selenium-pw-migrator profile search selenium-nunit
selenium-pw-migrator profile install basic-csharp-nunit
selenium-pw-migrator profile inspect basic-csharp-xunit
selenium-pw-migrator profile diff --before adapter-config.json --after profile.json
```

Profile types:
- Built-in profiles bundled with the tool.
- Local profiles under `profiles/`.
- Remote index later, only after trust/signing model exists.

Profile metadata:
- id
- version
- source language/framework
- target backend/framework
- supported patterns
- required evidence
- safety level
- known limitations
- sample input/output

Safety rules:
- Profiles must be validated before install.
- Profiles cannot silently suppress assertions.
- Profiles cannot add broad source-only identifiers without explanation.
- Profiles must include a changelog and compatibility range.

Acceptance criteria:
- Built-in profile list works offline.
- Installing a profile writes a reviewed config layer, not hidden behavior.
- `profile inspect` explains exactly what mappings will do.
- Marketplace profiles pass `config-validate --validation-mode production`.
- Profile compatibility is shown in `profile-match`.

## 6. Runtime Trace Classify

Goal: upgrade `runtime-classify` from log grouping into a trace-aware diagnostic feature.

Why this matters:
- Compile success is only a checkpoint. Runtime failures are where migration quality gets real.
- Playwright traces/screenshots/videos can reveal whether a failure is selector, data, auth, navigation, wait, or assertion semantics.

Inputs:
- Playwright trace zip.
- NUnit/xUnit/pytest/JUnit logs.
- Screenshots/videos.
- Console/network logs when available.
- Generated test file and migration report.

Output categories:
- locator-not-found
- strict-mode-violation
- timeout-wait-state
- assertion-mismatch
- navigation-route-missing
- auth/session-not-ready
- test-data-missing
- modal/dialog-state
- frame/shadow-dom
- environment/flaky-infra

Artifacts:
- `runtime-classification.md`
- `runtime-classification.json`
- `runtime-next-tickets.md`
- optional annotated screenshot references

Acceptance criteria:
- Runtime classifier accepts trace zip directories without modifying them.
- Classifier links each runtime failure to generated test file and source migration context when possible.
- Classifier proposes next action and likely owner: config/profile, source truth, target infra, test data, or product semantics.
- Classifier works for NUnit and xUnit logs in C#.
- Trace parsing gracefully degrades when trace files are absent.

## 7. Evidence Pack And Sharing Workflow

Goal: make every migration run easy to review, share, and debug.

Proposed command:

```text
selenium-pw-migrator evidence pack --input migration/runs/run-042 --out evidence/run-042.zip
```

Contents:
- reports
- generated files
- config layers
- migration quality dashboard
- verify/project-verify summaries
- runtime classification
- selected logs
- manifest with redaction metadata

Safety:
- Redact absolute local paths where possible.
- Do not include source repository files unless explicitly requested.
- Mark potentially sensitive values.

Acceptance criteria:
- Evidence pack can be attached to an issue or PR.
- Pack includes a manifest and checksum.
- Redaction behavior is documented and tested.

## 8. Public Demo And Guided Tutorial

Goal: make the public repo self-explanatory in 10 minutes.

Tasks:
- Create a demo project with Selenium C# NUnit input.
- Add a demo project with Selenium C# xUnit input.
- Add generated Playwright .NET NUnit output.
- Add generated Playwright .NET xUnit output.
- Add a short walkthrough using `init --wizard`.
- Add screenshots or static dashboard output from `report serve`.
- Add "what good looks like" quality dashboard examples.

Acceptance criteria:
- A new user can clone, run one command, and see migration output.
- Demo covers NUnit and xUnit.
- Demo has no private/internal references.
- Public README links directly to the tutorial.

## Recommended Order

1. Framework Matrix For Source And Target.
2. Init Wizard And Project Onboarding.
3. Doctor Fix Mode.
4. Report Serve Dashboard.
5. Runtime Trace Classify.
6. Profile Marketplace.
7. Evidence Pack And Sharing Workflow.
8. Public Demo And Guided Tutorial.

Rationale:
- Framework selection must be explicit before the wizard can generate correct configs/scaffolds.
- Wizard and doctor are the onboarding layer.
- Report serve and trace classify make migration progress visible.
- Marketplace and evidence packs become much more useful once the core workflows are stable.
