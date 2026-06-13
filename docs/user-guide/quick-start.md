# Quick Start

Try the Migrator on a small set of Selenium tests in 10-15 minutes.

## Prerequisites

- .NET 8 SDK
- A small directory of Selenium C# / NUnit test files
- (Optional) `adapter-config.json` with project-specific mappings

## 1. Prepare input

Select 1-5 test files from your Selenium project. Start small — a simple page test is ideal.

```bash
mkdir -p my-selenium-tests
# copy your .cs files here
```

## 2. Run analyze

Analyze tells you what the migrator understands about your tests:

```bash
dotnet run --project Migrator.Cli -- --mode analyze --input "./my-selenium-tests" --out "./analysis" --format both
```

**What to look for:**
- `analysis/report.json` — per-file statistics
- `analysis/report.txt` — human-readable summary
- `analysis/unmapped-targets.json` — elements that need config mappings
- `analysis/unsupported-actions.json` — actions the tool cannot convert

## 3. Create or review adapter config

Create `adapter-config.json` with mappings for the unmapped targets. Start with the most frequently used elements:

```json
{
  "SourceProjectName": "Example.E2ETests",
  "UiTargets": [
    {
      "SourceExpression": "page.Name",
      "TargetExpression": "Наименование",
      "TargetKind": "Text"
    },
    {
      "SourceExpression": "page.SubmitButton",
      "TargetExpression": "t_submit",
      "TargetKind": "TestId"
    }
  ],
  "PageObjects": [],
  "Methods": []
}
```

If you have an existing Playwright project, run `discover-target` first to auto-generate a draft config:

```bash
dotnet run --project Migrator.Cli -- --mode discover-target --input "./playwright-projects" --out "./discovery"
```

Review `discovery/adapter-config.draft.json` before using it.

## 4. Run migrate

Migrate generates Playwright C# code:

```bash
dotnet run --project Migrator.Cli -- --mode migrate --input "./my-selenium-tests" --config "./adapter-config.json" --out "./generated" --format both
```

**What you get:**
- `generated/` — Playwright C# files (e.g., `WidgetPlaywright.cs`)
- `generated/report.json` — conversion statistics with GeneratedFiles count

## 5. Run verify

Verify checks the quality of generated code:

```bash
dotnet run --project Migrator.Cli -- --mode verify --input "./generated" --config "./adapter-config.json" --out "./verify" --format both
```

**What to look for:**
- `verify/verify-report.json` — syntax errors, TODO comments, quality gate status
- `verify/verify-report.txt` — human-readable summary with per-file issues

## 6. Run propose

Propose suggests profile improvements based on migration artifacts:

```bash
dotnet run --project Migrator.Cli -- --mode propose --input "./generated" --config "./adapter-config.json" --out "./proposals" --format both
```

**What you get:**
- `proposals/mapping-proposals.md` — ranked proposals with suggested config
- `proposals/mapping-proposals.json` — structured proposals

## 7. Read the output

Key metrics from each stage:

| Stage | Key output | What it tells you |
|---|---|---|
| Analyze | `unmapped-targets.json` | Which elements need config |
| Migrate | `generated/report.json` | How many files were generated |
| Verify | `verify-report.json` | Code quality: syntax, TODOs, gates |
| Propose | `mapping-proposals.md` | What config to add next |

## Alternative: One-command orchestration

Instead of running each stage separately, use `orchestrate` mode:

```bash
dotnet run --project Migrator.Cli -- --mode orchestrate --input "./my-selenium-tests" --config "./adapter-config.json" --out "./orchestration" --format both
```

This runs all four stages in sequence and produces a unified report:
- `orchestration/orchestration-report.json` — combined metrics
- `orchestration/orchestration-report.md` — human-readable summary
- `orchestration/analyze/`, `generated/`, `verify/`, `propose/` — stage artifacts

## Next steps

- [Migration Workflow](migration-workflow.md) — full process guide
- [Profile Cookbook](project-profile-cookbook.md) — detailed config reference
- [Common Recipes](common-recipes.md) — practical solutions for frequent patterns
