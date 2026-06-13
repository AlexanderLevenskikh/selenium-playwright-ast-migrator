# Mapping Proposals — Pilot Summary

## Overview

`--mode propose` was implemented as the feedback loop between migrate and config updates. It reads migration artifacts and generates ranked, deterministic proposals for missing adapter config.

## Implementation

### Core Components

**`MappingProposal` model** (`Migrator.Core/MappingProposal.cs`):
- Properties: `Id`, `Kind`, `Title`, `Priority`, `Confidence`, `Evidence`, `Occurrences`, `Score`, `AffectedFiles`, `SuggestedConfigSnippet`, `RequiresSourceTruth`, `Reason`, `Risks`, `NextAction`
- Enums: `ProposalKind` (8 kinds), `ProposalPriority` (High/Medium/Low), `ProposalConfidence` (High/Medium/Low)

**`ProposalGenerator`** (`Migrator.Core/ProposalGenerator.cs`):
- `Generate(ProposalInput)` — main entry point, orchestrates 8 sub-generators
- Scoring: occurrences ×1, affected files ×5, compile blockers ×10
- Deduplication: checks `ExistingConfig` to avoid proposing already-mapped targets
- Sorts by score descending

**Sub-generators** (8):
1. `GenerateUiTargetProposals` — from unmapped targets, groups by source expression
2. `GenerateMethodProposals` — from unsupported actions, groups by receiver.Method
3. `GenerateParameterizedMethodProposals` — detects same method, different args
4. `GenerateTableMappingProposals` — from `ElementAt(...)` patterns in unmapped targets
5. `GeneratePaginationMappingProposals` — from `Items.Count` patterns
6. `GenerateScopeProposals` — from verify report scope warnings
7. `GenerateQualityGateProposals` — from verify report TODO/syntax/scope issue counts
8. `GenerateManualMigrationProposals` — from verify report files with high issue density

**`ProposalWriter`** (`Migrator.Core/ProposalWriter.cs`):
- `ToMarkdown` — structured report with summary table, proposals by kind, agent constraints
- `ToJson` — machine-readable JSON with proposals array and summary

**CLI integration** (`Migrator.Cli/Program.cs`):
- Added `--mode propose` branch with artifact loading
- `RunPropose` function: loads JSON reports, builds ProposalInput, calls generator, writes output
- Graceful handling: missing reports produce warnings, not crashes

### Tests (11)

All in `Migrator.Tests/ProposalTests.cs`:

| # | Test | Validates |
|---|---|---|
| 1 | `Propose_UiTarget_FromUnmappedTargets` | Groups by source expression, collects affected files |
| 2 | `Propose_MethodMapping_FromRepeatedUnsupportedInvocation` | Method mapping from unsupported actions |
| 3 | `Propose_ParameterizedMethodMapping_ForSameMethodDifferentArgs` | Detects parameterized variants |
| 4 | `Propose_TableMapping_FromElementAtUnresolvedPattern` | ElementAt → TableMapping |
| 5 | `Propose_Scope_WhenDifferentFilesNeedDifferentTestHost` | Scope warnings → ProfileScope |
| 6 | `Propose_DoesNotInventSelector` | No real selectors, only placeholders |
| 7 | `Propose_SkipsAlreadyMappedUiTarget` | Dedup against existing config |
| 8 | `Propose_RanksHighForRepeatedCompileBlocker` | Score and priority calculation |
| 9 | `Propose_WritesMarkdownAndJson` | Output format correctness |
| 10 | `Propose_HandlesMissingOptionalReports` | Graceful handling of missing input |
| 11 | `Propose_OutputContainsAgentFriendlyNextActions` | Agent constraints in output |

**Test results**: 143 passed, 1 pre-existing failure (unrelated to proposals).

## Batch Run Results

Ran on ArFilters batch (15 files, 176 lines total, 0 compile errors):

```
Total proposals: 1
  Medium: 1 — Add TableMapping for `page.Pagination`
```

Low proposal count is expected: the batch is already clean. The 4 unmapped targets in `unmapped-targets.json` are already in `adapter-config.draft.json` (with TODO placeholders), so they're correctly skipped. The only new proposal is for `page.Pagination` table pattern, which needs proper RowTarget config.

## Files Changed

| File | Lines | Purpose |
|---|---|---|
| `Migrator.Core/MappingProposal.cs` | ~200 | Model, enums, ProposalInput |
| `Migrator.Core/ProposalGenerator.cs` | ~650 | 8 sub-generators, scoring, dedup |
| `Migrator.Core/ProposalWriter.cs` | ~250 | Markdown + JSON output |
| `Migrator.Cli/Program.cs` | ~120 added | `--mode propose` branch, `RunPropose` |
| `Migrator.Tests/ProposalTests.cs` | ~330 | 11 unit tests |
| `docs/profile/mapping-proposals.md` | new | Feature documentation |

**Total**: ~1550 lines of new code + docs.

## Next Steps (Not Implemented)

These would enhance the propose mode in future iterations:

1. **Auto-discover source truth**: Scan PageObject files for `WithDataTestId` patterns and fill in selectors automatically (reducing `RequiresSourceTruth` from true to false for matched proposals).
2. **Apply mode**: `--mode apply --proposal TM-1` — auto-apply a specific proposal's config snippet.
3. **Diff proposals**: Compare proposals across migrate runs to track config drift.
4. **Interactive review**: TUI mode for browsing and filtering proposals.
