# Quick start

This path gets you from a small Selenium sample to generated Playwright output. Start with 1-5 tests before scaling to a full suite.

## Prerequisites

- .NET 8 SDK
- Selenium test files to migrate
- Optional but recommended: `adapter-config.json` with verified PageObject/helper mappings

## 1. Check the tool and your input

```bash
selenium-pw-migrator --help
selenium-pw-migrator --mode doctor --input ./SeleniumTests --config ./adapter-config.json --out doctor
```

Relative `--out` values are written under the default `migration/` workspace. The command above writes to `migration/doctor`.

## 2. Analyze Selenium tests

```bash
selenium-pw-migrator --mode analyze \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out analysis \
  --format both
```

Important outputs:

- `migration/analysis/report.md` / `report.json`
- `migration/analysis/unmapped-targets.json`
- `migration/analysis/unsupported-actions.json`
- `migration/analysis/migration-quality-dashboard.md`
- `migration/analysis/migration-quality-tickets.md`

## 3. Add or improve source-truth mappings

Use the report to fill in `adapter-config.json`. Do not guess selectors. Use Selenium PageObject code, verified HTML attributes, existing Playwright tests/POMs, or helper semantics that your project owns.

Small example:

```json
{
  "SourceProjectName": "Example.E2ETests",
  "UiTargets": [
    {
      "SourceExpression": "page.SubmitButton",
      "TargetExpression": "submit-button",
      "TargetKind": "TestId"
    }
  ],
  "PageObjects": [],
  "Methods": []
}
```

## 4. Generate Playwright output

```bash
selenium-pw-migrator --mode migrate \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out generated-tests \
  --format both
```

Generated files and migration reports are written to `migration/generated-tests`.

## 5. Verify generated output

For renderer-level checks:

```bash
selenium-pw-migrator --mode verify \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out verify \
  --format both
```

For project-aware Playwright .NET compile checks:

```bash
selenium-pw-migrator --mode verify-project \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out verify-project \
  --format both
```

For TypeScript preview output, generate with `--target ts` and type-check with `verify-ts-project --ts-project <path>`.

## 6. Run the full dry-run workflow

After the first pass works, use orchestration:

```bash
selenium-pw-migrator --mode orchestrate \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out run-001 \
  --format both
```

Typical output:

```text
migration/run-001/
  analyze/
  generated/
  verify/
  propose/
  orchestration-report.md
  orchestration-report.json
```

## Next steps

- [End-to-end simple example](examples/end-to-end-simple.md)
- [Migration workflow](user-guide/migration-workflow.md)
- [Config and profile guide](config-profile-guide.md)
- [Limitations](user-guide/limitations.md)
