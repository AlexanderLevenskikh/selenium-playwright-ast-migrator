# Playbook: Use Propose Report

## Input artifacts

- `mapping-proposals.md` — from propose output
- `mapping-proposals.json` — structured proposals
- `adapter-config.json` — current configuration
- `verify-report.json` — current verify status

## Goal

Improve the adapter config by applying the highest-impact proposals safely.

## Steps

1. **Read `mapping-proposals.md`** — review proposals sorted by priority.
2. **Start with High priority proposals** — score >= 20.
3. **For each proposal:**
   a. Read the `suggestedConfig` snippet
   b. Verify source truth for any selectors (proposals use `<SOURCE_TRUTH_REQUIRED>` placeholders)
   c. Replace placeholders with verified selectors
   d. Apply the config change to `adapter-config.json`
   e. Re-run orchestrate:
      ```bash
      dotnet run --project Migrator.Cli -- --mode orchestrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./orchestration" --format both
      ```
   f. Compare metrics before and after
4. **Move to Medium priority** — score 8-19 — after all High proposals are addressed.
5. **Skip Low priority** — score < 8 — unless specifically requested.

## What NOT to do

- Do NOT apply all proposals blindly without verification
- Do NOT apply proposals without checking source truth for selectors
- Do NOT apply multiple proposals in one pass without verifying each
- Do NOT apply proposals that have `RequiresReview: true` without manual inspection
- Do NOT use `<SOURCE_TRUTH_REQUIRED>` as an actual selector value

## Acceptance criteria

- Each applied proposal reduces the relevant metric (unmapped count, unsupported count, etc.)
- No new issues are introduced in `verify-report.json`
- All placeholders are replaced with verified selectors
- Metrics improvement is documented per proposal

## What to report back

```
Proposal applied: [High] Map UiTarget for page.SearchButton (score: 25)
Action: Added UiTarget mapping with verified selector from WidgetPage.cs
Before: Unmapped: 17, TODO: 65
After: Unmapped: 16, TODO: 62
Skipped proposals: 2 (require manual source truth verification)
Status: Ready for next proposal
```
