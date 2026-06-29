# Reports and Quality Gates

How to read Migrator reports and configure quality gates.

## Report files

### Analyze reports

| File | Format | Description |
|---|---|---|
| `report.json` | JSON | Per-file statistics: tests, actions, mapped/unmapped targets, TODO count |
| `report.txt` | Text | Human-readable summary of all analyzed files |
| `unmapped-targets.json` | JSON | Source expressions that have no config mapping, grouped by frequency |
| `unsupported-actions.json` | JSON | Actions the tool cannot convert, grouped by type |
| `migration-quality-dashboard.json` | JSON | Quality metrics, TODO categories, guardrails, and recommended tickets |
| `migration-quality-dashboard.md` | Markdown | Human-readable migration-quality dashboard |
| `migration-quality-tickets.md` | Markdown | Focused tickets for the next quality-improvement batch |

### Migrate reports

| File | Format | Description |
|---|---|---|
| `report.json` | JSON | Same as analyze report, with `GeneratedFiles` count |
| `report.txt` | Text | Human-readable summary |
| `migration-quality-dashboard.json` | JSON | Quality metrics, TODO categories, guardrails, and recommended tickets |
| `migration-quality-dashboard.md` | Markdown | Human-readable migration-quality dashboard |
| `migration-quality-tickets.md` | Markdown | Focused tickets for the next quality-improvement batch |

### Verify report

| File | Format | Description |
|---|---|---|
| `verify-report.json` | JSON | Structured quality report with `summary`, `files`, and `issues` |
| `verify-report.txt` | Text | Human-readable per-file issue listing |

**verify-report.json structure:**
```json
{
  "summary": {
    "status": "passed",
    "filesChecked": 5,
    "todoComments": 65,
    "syntaxErrors": 0,
    "placeholderLeftovers": 0
  },
  "files": [
    {
      "sourceFile": "Widget.cs",
      "generatedFile": "WidgetPlaywright.cs",
      "status": "passed",
      "issues": [
        {
          "category": "Todo",
          "severity": "Warning",
          "message": "TODO comment found: map source expression to Playwright locator: page.SubmitButton"
        }
      ]
    }
  ],
  "issues": [
    {
      "category": "Todo",
      "severity": "Warning",
      "message": "TODO comment found: ..."
    }
  ]
}
```

### Migration quality dashboard

`migration-quality-dashboard.*` is the bridge between raw reports and implementation work. It groups TODO comments by `[MIGRATOR:<CODE>]`, explains the likely root cause, lists the next safe action, and generates ticket-sized follow-up work. The companion `migration-quality-tickets.md` file is designed to be copied into an issue tracker or handed to an agent.

See [Migration Quality Program](../migration-quality-program.md).

### Propose reports

| File | Format | Description |
|---|---|---|
| `mapping-proposals.md` | Markdown | Ranked proposals with config snippets |
| `mapping-proposals.json` | JSON | Structured proposals with scores, evidence, and priorities |

**Proposal structure:**
- `title` — description of the proposed change
- `id` — unique identifier
- `kind` — type of proposal (UiTarget, MethodMapping, etc.)
- `score` — impact score (Higher = more important)
- `priority` — High (>=20), Medium (8-19), Low (<8)
- `suggestedConfig` — JSON snippet to add to adapter-config.json
- `risks` — what to watch out for
- `affectedFiles` — files that will be affected

### Orchestration report

| File | Format | Description |
|---|---|---|
| `orchestration-report.json` | JSON | Unified report combining all stages |
| `orchestration-report.md` | Markdown | Human-readable summary with stages, metrics, and recommendations |

**orchestration-report.json structure:**
```json
{
  "status": "passed_with_warnings",
  "inputPath": "...",
  "configPath": "...",
  "stages": [
    { "name": "analyze", "status": "passed", "exitCode": 0 },
    { "name": "migrate", "status": "passed", "exitCode": 0 },
    { "name": "verify", "status": "passed_with_warnings", "exitCode": 1 },
    { "name": "propose", "status": "passed", "exitCode": 0 }
  ],
  "metrics": {
    "filesProcessed": 5,
    "testsFound": 12,
    "generatedFiles": 5,
    "syntaxErrors": 0,
    "todoComments": 65,
    "proposals": 8
  },
  "issues": [],
  "topProposals": [
    "[High] Map UiTarget for page.SearchButton (score: 25)"
  ],
  "recommendedNextActions": [
    "Add source-truth UiTarget mappings for unmapped targets."
  ],
  "warnings": []
}
```

## Quality Gates

Quality gates are configured in `adapter-config.json`:

```json
{
  "QualityGates": {
    "MaxTodoComments": 0,
    "MaxUnsupportedActions": 0,
    "MaxUnmappedTargets": 0,
    "MaxRawExpressions": 0,
    "FailOnPageTodo": true,
    "FailOnInvalidGeneratedSyntax": true,
    "FailOnPlaceholderLeftovers": true,
    "FailOnMultipleMatchingScopes": true
  }
}
```

### Gate fields

| Field | Type | Description | Default |
|---|---|---|---|
| `MaxTodoComments` | int | Max TODO comments across all generated files | `int.MaxValue` (warn only) |
| `MaxUnsupportedActions` | int | Max unsupported actions | `int.MaxValue` (warn only) |
| `MaxUnmappedTargets` | int | Max unmapped targets | `int.MaxValue` (warn only) |
| `MaxRawExpressions` | int | Max raw expressions (unprocessed) | `int.MaxValue` (warn only) |
| `FailOnPageTodo` | bool | Fail if `Page.TODO_*` calls remain | `true` |
| `FailOnInvalidGeneratedSyntax` | bool | Fail if generated code has C# syntax errors | `true` |
| `FailOnPlaceholderLeftovers` | bool | Fail if unresolved `{placeholder}` tokens remain | `true` |
| `FailOnMultipleMatchingScopes` | bool | Fail if multiple scopes match one file | `true` |

### Soft vs Strict mode

**Soft mode (defaults):** All count-based gates are warnings only. The verify stage reports issues but does not fail.

**Strict mode:** Set count-based gates to `0` (or a threshold). Violations cause the verify stage to fail.

### Verify exit codes

| Exit code | Meaning |
|---|---|
| 0 | All quality gates passed |
| 1 | Quality gate failed (e.g., too many TODOs) |
| 2 | Config error (e.g., `Match: "Nth"` without `Index`) |
| 3 | Syntax errors in generated code |

### Orchestration exit codes

When running `orchestrate` mode, exit codes reflect the most severe stage result:

| Exit code | Meaning |
|---|---|
| 0 | All stages passed, all quality gates passed |
| 1 | Verify quality gates failed |
| 2 | Invalid input or verify config error |
| 3 | Analyze or migrate stage failed |
| 4 | Generated syntax errors detected |

### Recommended progression

1. **Initial run**: use defaults (soft mode), review reports
2. **After pilot config**: set `MaxUnmappedTargets` to current unmapped count, then decrease
3. **Before batch migration**: set strict gates for the target batch
4. **CI/CD integration**: use `--fail-on-unsupported` and `--fail-on-todo` flags for pipeline gates
