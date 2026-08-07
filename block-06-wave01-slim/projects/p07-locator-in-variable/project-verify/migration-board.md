# Migration Board

- **Generated**: 2026-07-31 08:55:28 +00:00
- **Source**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p07-locator-in-variable\project-verify`
- **Artifact root**: `<USER_HOME>\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p07-locator-in-variable\project-verify`
- **Artifact lookup**: `direct-only`
- **Project verify**: `passed`
- **TODO**: `3`
- **Syntax/compile errors**: `0`
- **Runtime-ready**: `0`
- **Smoke candidates**: `0`

## Quality gates
| Gate | Value | Status |
|---|---:|---|
| Project verify | `passed` | ok |
| Compile errors | 0 | ok |
| EMPTY_TEST_AFTER_SUPPRESSION | 0 | ok |
| DEPENDS_ON_SUPPRESSED_SIDE_EFFECT | 0 | ok |
| SuppressedMethodPatterns | not-run | not-run |
| Regex-looking suppressions | not-run | not-run |

## Recommended next actions
- Top normalized root cause: RAW_STATEMENT: method family `Id` (1). Group all occurrences of this helper/method family; inspect source/helper body or run --mode helper-inventory before adding MethodSemantics/ParameterizedMethods.
- Следующий лучший config-шаг: If repeated, add Method/ParameterizedMethod mapping; otherwise keep manual TODO.

## Top TODO / migration insights
| # | Category | Impact | Title | Suggested action |
|---|---|---:|---|---|
| 1 | `RAW_STATEMENT` | 1 | Review TODO [RAW_STATEMENT]: raw statement — review: var target = By.Id("locator-primary") | If repeated, add Method/ParameterizedMethod mapping; otherwise keep manual TODO. |
| 2 | `UNAVAILABLE_SYMBOLS` | 1 | Classify unavailable target symbols: references unavailable symbol(s) 'By' — verify in target | Add TargetKnownTypes/TargetKnownIdentifiers only for real target symbols; otherwise map/comment the expression. |
| 3 | `UNRESOLVED_SYMBOL` | 1 | Fix upstream unresolved symbol: depends on unresolved symbol 'target' | Find the first TODO that blocked the symbol; fix that root cause first. |

## Top normalized root causes
| # | Category | Group | Count | Suggested action |
|---|---|---|---:|---|
| 1 | `RAW_STATEMENT` | RAW_STATEMENT: method family `Id` | 1 | Group all occurrences of this helper/method family; inspect source/helper body or run --mode helper-inventory before adding MethodSemantics/ParameterizedMethods. |
| 2 | `UNAVAILABLE_SYMBOLS` | UNAVAILABLE_SYMBOLS: source-only root `unknown-root` | 1 | Map the full source expression or classify it explicitly; do not mark source-only roots as target-known unless they truly exist in target code. |
| 3 | `UNRESOLVED_SYMBOL` | UNRESOLVED_SYMBOL: source-only root `unknown-root` | 1 | Map the full source expression or classify it explicitly; do not mark source-only roots as target-known unless they truly exist in target code. |

## Table/list mapping candidates
| # | Source root | Accessor | Assertion | Count | Suggested config hint | Example |
|---|---|---|---|---:|---|---|
|  |  |  |  | 0 | No table/list candidates inferred. |  |

## Runtime candidates
| # | Level | Score | Test | TODO | Active | File |
|---|---|---:|---|---:|---:|---|
