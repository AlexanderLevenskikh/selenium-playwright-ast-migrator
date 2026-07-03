# Target Playwright Infrastructure Discovery

## What discovery does

`--mode discover-target` scans an existing Playwright .NET test project and produces a factual inventory of its infrastructure:

- **Test framework** (NUnit, xUnit, MSTest)
- **Base classes / TestHost candidates** (TestBase, PageTest, custom bases)
- **SetUp / TearDown methods** with statements
- **Locator conventions** (data-test-id, data-test, GetByTestId, GetByRole)
- **Navigation / auth patterns** (GotoAsync, login helpers, credential patterns)
- **Helper methods** (Login, GoTo, WaitFor, etc.)

## What discovery does NOT do

- Does NOT auto-apply config changes
- Does NOT invent selectors, routes, or credentials
- Does NOT mutate `adapter-config.json`
- Does NOT execute tests or modify source code
- Does NOT call AI or external services

## Output files

| File | Description |
|---|---|
| `target-inventory.json` | Full structured inventory with all detected facts |
| `target-style-notes.md` | Human/agent-readable summary and recommendations |
| `adapter-config.draft.json` | Draft adapter config with review markers |
| `discovery-warnings.txt` | Warnings and redaction count |

## Usage

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode discover-target --input ./team-playwright-tests --out ./target-discovery --format both
```

## Why draft config requires review

The draft config is generated from automated scanning. It:

1. May detect multiple base classes — the highest-frequency one is selected but needs confirmation.
2. May redact URLs — hostnames are replaced with `<redacted-host>` for safety.
3. Uses `<REVIEW_REQUIRED>` and `<SOURCE_TRUTH_REQUIRED>` placeholders where source truth is needed.
4. Is marked `"RequiresReview": true` in JSON.

## How to review target-inventory.json

1. Check `DetectedFrameworks` — confirm the right framework is detected with High confidence.
2. Check `DetectedTestHosts` — verify base class, namespace, attributes, and usings.
3. Check `DetectedLocatorAttributes` — confirm the dominant convention.
4. Check `DetectedNavigationPatterns` — review redacted URLs for accuracy.
5. Check `DetectedAuthPatterns` — confirm auth setup matches your project.
6. Check `Warnings` — address any warnings before proceeding.

## How to convert draft into real adapter config

1. Copy `adapter-config.draft.json` to `adapter-config.json`.
2. Replace `<REVIEW_REQUIRED>` values with actual values.
3. Replace `<redacted-url>` and `<redacted-host>` with real routes (use source truth).
4. Add `SourceProjectName`, `UiTargets`, and `Methods` as needed.
5. Run `--mode analyze` / `--mode migrate` / `--mode verify` to validate.

## Safety rules

- All paths in output are relative to the target project root.
- URLs have hostnames redacted (`https://<redacted-host>/path`).
- Secret-like strings (long hex, base64) are redacted.
- Real credentials are never included in output.
- The `RedactionCount` field tracks how many values were redacted.
