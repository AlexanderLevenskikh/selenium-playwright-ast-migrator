# Safety Rules

Hard rules for working with the Migrator. Read before starting any task.

## Never invent selectors

All locators must come from verified source truth:
- PageObject C# source code (methods like `WithDataTestId`, `WithDataTest`, `WithDataTid`)
- Actual HTML attributes verified by inspection
- Target project's existing Playwright tests (from `discover-target` output, after review)

If you cannot find a selector in source truth, use `<SOURCE_TRUTH_REQUIRED>` as a placeholder. Do not guess.

## Never delete assertions

Removing assertions to make tests pass corrupts test coverage. If an assertion fails:
- Classify the failure
- Fix the root cause
- Re-run

## Never commit local or private profiles

Do not commit:
- `adapter-config.local.json` files
- Configs with real URLs, credentials, or internal hostnames
- Configs with project-specific paths that expose internal structure

Use placeholders: `<test-login>`, `<base-url>`, `<SOURCE_TRUTH_REQUIRED>`.

## Never add project-specific logic to the tool

Project-specific mappings belong in `adapter-config.json`, not in:
- `Migrator.Core`
- `Migrator.Roslyn`
- `Migrator.PlaywrightDotNet`

The tool must remain generic. All project knowledge goes into the config.

## Never claim runtime proof without browser execution

A test is only "proven" if it ran in a real browser environment. Compile-smoke and verify reports are not proof of runtime correctness.

## Never treat discovery draft as final config

`adapter-config.draft.json` from `discover-target` mode contains:
- `<REVIEW_REQUIRED>` — values needing manual verification
- `<SOURCE_TRUTH_REQUIRED>` — selectors needing source verification
- `<redacted-host>` — redacted URLs

Review and fill in all placeholders before using as production config.

## Never auto-apply proposals

`propose` mode generates suggestions. Each proposal must be:
1. Reviewed against source truth
2. Applied one at a time
3. Verified with a re-run of the pipeline
4. Metrics checked before proceeding to the next proposal

## Never modify the tool to work around a specific project

If the generated code doesn't work, the fix should be:
1. Update the profile config (preferred)
2. Manual edit of generated code
3. Report a tool bug (only if the code is objectively wrong)

Not: adding project-specific logic to the migrator's core.

## One change at a time

Apply one config change, re-run, verify improvement. This ensures:
- Each change's impact is measurable
- Regressions are easy to identify
- The config remains maintainable

## Report metrics

For every change, report:
- Before metrics: unmapped count, TODO count, unsupported count
- After metrics: same
- What changed: specific config entry added or modified
- Verification: confirm the change produced the expected result
