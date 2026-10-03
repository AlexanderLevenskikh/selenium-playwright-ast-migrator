# Migration Quality Dashboard

This report turns raw migration counters into the next safest quality-improvement work queue.

## Summary

| Metric | Value |
|---|---:|
| Quality level | `clean` |
| Files processed | 1 |
| Tests found | 1 |
| Actions found | 7 |
| Target mapping coverage | 100% |
| Mapped targets | 6 |
| Unmapped targets | 0 |
| Unsupported actions | 0 |
| TODO comments | 0 |
| TODO/test | 0 |
| Unsupported/test | 0 |

## Guardrails

| Status | Id | Guardrail | Next action |
|---|---|---|---|
| `pass` | `unsafe-suppression` | Suppression must never turn a test into an apparently green empty test. | No empty-test-after-suppression TODOs were found in this run. |
| `pass` | `pom-helper-recovery` | POM/helper recovery should use source POMs and helper bodies before raw locators or suppression. | No obvious POM/helper backlog was found in the top categories. |
| `pass` | `selector-evidence` | Selector mappings require evidence from Selenium POMs, helper bodies, target HTML, or existing Playwright components. | All target expressions are mapped in this run. |

