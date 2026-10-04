# Standard Migration Run Report

**Status:** failed
**Input:** p34-cross-project-async-caller
**Output:** _candidate

## Stages

| Stage | Status | Exit Code | Message |
|---|---|---:|---|
| analyze | passed | 0 | 1 files, 1 tests |
| migrate | passed | 0 | 1 files generated |
| verify | failed | 1 | failed |
| propose | passed | 0 | 0 proposals generated |

## Metrics

| Metric | Value |
|---|---:|
| Files processed | 1 |
| Tests found | 1 |
| Generated files | 1 |
| Syntax errors | 0 |
| TODO comments | 3 |
| Page.TODO_* | 0 |
| Proposals | 0 |

## Recommended Next Actions

1. All stages passed. Attempt compile smoke test and manual runtime proof on 3-5 tests.
2. Re-run `selenium-pw-migrator run` after applying changes to verify improvement.

