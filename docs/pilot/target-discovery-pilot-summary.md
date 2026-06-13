# Target Discovery — Pilot Implementation Summary

## Overview

Implemented `--mode discover-target` to scan existing Playwright .NET test projects and produce a factual infrastructure inventory.

## Architecture

| File | Responsibility |
|---|---|
| `Migrator.Core/TargetInventory.cs` | Inventory model (TargetInventory, DetectedFramework, DetectedTestHost, etc.) |
| `Migrator.Core/TargetDiscovery.cs` | Text-based scanner: framework, base class, setup, locator, navigation, auth, helpers |
| `Migrator.Core/DiscoveryWriter.cs` | Output generators: JSON, Markdown, draft config, warnings |
| `Migrator.Cli/Program.cs` | CLI integration for `discover-target` mode |
| `Migrator.Tests/DiscoveryTests.cs` | 15 unit tests with fixture projects |
| `Migrator.Tests/TestFixtures/TargetProjects/` | 3 fixture projects (NUnit, PageTest, Mixed) |

## Key decisions

- **Text-based scanning**: No Roslyn dependency — uses regex/line scanning for speed and simplicity.
- **Relative paths only**: All output paths are relative to target project root.
- **URL redaction**: Hostnames replaced with `<redacted-host>` to prevent leaking internal addresses.
- **Secret redaction**: Long hex strings and base64 tokens are redacted.
- **Review markers**: Draft config always has `"RequiresReview": true` and `<REVIEW_REQUIRED>` placeholders.
- **No auto-apply**: Discovery is read-only. Never modifies any config or source files.

## Real project results

Tested on arbilling-e2e-tests (33 .cs files, 2 .csproj):

- Framework: NUnit (High)
- Base classes: TestBase (4), PageBase (2), ControlBase (1)
- Locator attributes: data-test (46), data-test-id (14), data-tid (5)
- Navigation patterns: 2
- Auth patterns: 4
- Helper methods: 4
- Redactions: 2
- Warnings: 1 (multiple base classes detected)

## Test results

15 discovery tests, all passing. Full suite: 158 passed, 1 pre-existing failure.

## Known limitations

- Text-based scanning may miss complex patterns (e.g., multi-line attribute arguments).
- Does not handle Playwright TypeScript projects.
- Attribute collection uses backward scan — may miss attributes separated by blank lines.
- Method detection is based on naming conventions, not semantic analysis.

## Next steps

- Use discovery output to seed adapter config for new target projects.
- Integrate with propose mode to cross-reference discovery facts with mapping proposals.
- Consider Roslyn-based scanning for more accurate detection in future iterations.
