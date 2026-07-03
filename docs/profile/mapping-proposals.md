# Mapping Proposals (`--mode propose`)

## Purpose

After running `--mode migrate`, you have:
- Generated C# files with TODO/placeholder code
- `report.json` with migration statistics
- `unmapped-targets.json` with unmapped locators
- `unsupported-actions.json` with unmapped methods
- `verify-report.json` with quality gate results

**`--mode propose`** reads these artifacts and generates **deterministic, ranked proposals** for what adapter config is still needed to improve migration quality. It does NOT modify config — it only generates read-only reports.

## Usage

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode propose --input ./Generated/ --config ./Generated/adapter-config.draft.json --out ./mapping-proposals --format both
```

### Flags

| Flag | Description |
|---|---|
| `--mode propose` | Run proposal generation |
| `--input <dir>` | Directory with report files (`report.json`, `unmapped-targets.json`, etc.) and generated `.cs` files |
| `--config <file>` | Existing adapter config (to avoid proposing already-mapped targets) |
| `--out <dir>` | Output directory for `mapping-proposals.md` and `mapping-proposals.json` |
| `--format <fmt>` | Output format: `md`, `json`, or `both` (default: `both`) |

## Input Artifacts

| File | Required | Purpose |
|---|---|---|
| `report.json` | Optional | Migration summary: top unmapped targets, unsupported actions |
| `unmapped-targets.json` | Optional | Detailed unmapped target info (per-file, per-occurrence) |
| `unsupported-actions.json` | Optional | Unsupported method invocations (for method/parameterized proposals) |
| `verify-report.json` | Optional | Verify report: scope warnings, syntax errors, TODO counts (for scope/quality gate proposals) |
| `*.cs` | Optional | Generated files: parsed for additional evidence (syntax errors, patterns) |

If a file is missing, the corresponding proposal type is skipped with a warning.

## Proposal Kinds

| Kind | Source | Description |
|---|---|---|
| **UiTarget** | unmapped targets | New UiTarget mapping needed for a source expression |
| **MethodMapping** | unsupported actions | New method mapping needed for a receiver.Method pair |
| **ParameterizedMethodMapping** | unsupported actions | Same method called with different arguments across files |
| **TableMapping** | unmapped targets | `ElementAt(...)` pattern detected — table row config needed |
| **PaginationMapping** | unmapped targets | `Items.Count` pattern detected — pagination config needed |
| **ProfileScope** | verify report | Scope warning: file needs a narrow scope in config |
| **QualityGate** | verify report | Quality gate failed: TODO count, syntax errors, etc. |
| **ManualMigration** | verify report | High TODO count or many syntax errors — manual effort needed |

## Scoring and Priority

Each proposal gets a **score** calculated from:
- **Occurrences** (×1): total number of times the pattern appears
- **Affected files** (×5): each unique file adds 5 points (cross-file impact)
- **Compile blocker** (×10): if the pattern blocks compile (TODO in generated code), adds 10 per file

Priority is derived from score:
- **High**: score >= 20
- **Medium**: score >= 8
- **Low**: score < 8

Proposals are sorted by score (descending).

## Agent-Friendly Output

Each proposal includes:
- **Title**: What needs to be done
- **Id**: Unique identifier (e.g., `UT-1`, `MM-3`, `TM-2`)
- **Evidence**: What data supports this proposal
- **Affected files**: List of source files with occurrences
- **Suggested config snippet**: JSON template to add to `adapter-config.json`
- **Risks**: What could go wrong
- **Next action**: Specific steps for an agent to take

### Constraints

Every proposal enforces:
> **Do not invent selectors.** Use PageObject/source truth before applying. Add mapping to the narrowest scope. Run analyze/migrate/verify after applying.

All new UiTarget proposals use `<SOURCE_TRUTH_REQUIRED>` placeholders, not real selectors.

## Example Output

After running on the Example.E2ETests batch:

```
=== Proposal Summary ===
Total proposals: 1
  High:   0
  Medium: 1
  Low:    0
  Requires source truth: 1
```

The single Medium proposal is for `page.Pagination` table mapping — because the batch already has all other targets mapped (even if with TODO placeholders).

## Workflow Integration

1. **Run migrate**: `--mode migrate --input ./Tests --out ./Generated`
2. **Run verify**: `--mode verify --input ./Generated --config ./adapter-config.json`
3. **Run propose**: `--mode propose --input ./Generated --config ./adapter-config.json`
4. **Review proposals**: Open `mapping-proposals.md`, address High-priority first
5. **Apply config**: For each proposal, find source truth, add mapping to config
6. **Re-run**: `migrate` → `verify` → `propose` until no more High/Medium proposals

## Design Decisions

- **Deterministic only**: No AI/ML. Proposals are derived from report data using pattern matching and scoring.
- **No auto-apply**: Generator outputs read-only reports. Human or agent must apply snippets manually.
- **Source truth guardrail**: `RequiresSourceTruth = true` on all proposals that need real selectors.
- **Deduplication**: Existing config is checked — already-mapped targets are not re-proposed.
