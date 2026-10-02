# Standard Migration Run Report

**Status:** failed
**Input:** p01-basic-id-login
**Output:** _candidate

## Stages

| Stage | Status | Exit Code | Message |
|---|---|---:|---|
| analyze | passed | 0 | 1 files, 1 tests |
| migrate | passed | 0 | 1 files generated |
| verify | failed | 1 | failed |
| propose | passed | 0 | 3 proposals generated |

## Metrics

| Metric | Value |
|---|---:|
| Files processed | 1 |
| Tests found | 1 |
| Generated files | 1 |
| Syntax errors | 0 |
| TODO comments | 6 |
| Page.TODO_* | 0 |
| Proposals | 3 |

## Top Proposals

1. [Low] Add UiTarget mapping for `WebDriver.FindElement(By.Id("login"))` (score: 7)
2. [Low] Add UiTarget mapping for `WebDriver.FindElement(By.Id("password"))` (score: 7)
3. [Low] Add UiTarget mapping for `WebDriver.FindElement(By.Id("username"))` (score: 7)

## Recommended Next Actions

1. Add source-truth UiTarget mappings for 3 unmapped target(s). Review analyze/unmapped-targets.json and source truth before adding mappings.
2. Review mapping-proposals.md for suggested config improvements.
3. Re-run `selenium-pw-migrator run` after applying changes to verify improvement.

