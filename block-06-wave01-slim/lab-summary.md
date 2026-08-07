# Migrator Lab run

- **Suite:** `nightly`
- **Started:** 2026-07-31T08:51:35.0165757+00:00
- **Completed:** 2026-07-31T09:04:24.5258911+00:00
- **Corpus:** `C:\Users\levenskikh\Desktop\MyProjects\Migrator\corpus\stable\vertical-slice`
- **LabApp:** `http://127.0.0.1:57058/`

## Summary

| Status | Count |
|---|---:|
| PASS | 9 |
| PASS_WITH_WARNINGS | 0 |
| UNSUPPORTED_AS_EXPECTED | 2 |
| REGRESSION | 19 |
| MIGRATOR_FAILURE | 0 |
| SOURCE_INVALID | 0 |
| INFRASTRUCTURE_FAILURE | 0 |
| NON_DETERMINISTIC | 0 |

## Scenarios

| Scenario | Expected | Actual | Source | verify-project | Target | Quality | Oracle | Duration |
|---|---|---|---:|---|---:|---|---|---:|
| p01-basic-id-login | PASS | PASS | 1/1 | passed | 1/1 | PASS | PASS | 60501 ms |
| p02-css-clear-input | PASS | REGRESSION | 1/1 | passed | 1/1 | FAIL | PASS | 72715 ms |
| p03-xpath-text-assert | PASS | PASS | 1/1 | passed | 1/1 | PASS | PASS | 24147 ms |
| p04-findelements-count-text | PASS | PASS | 1/1 | passed | 1/1 | PASS | PASS | 20226 ms |
| p05-table-row-target | PASS | PASS | 1/1 | passed | 1/1 | PASS | PASS | 24134 ms |
| p06-checkbox-radio-selected | PASS | REGRESSION | 1/1 | passed | 1/1 | FAIL | PASS | 19128 ms |
| p07-locator-in-variable | PASS | REGRESSION | 1/1 | passed | 0/1 | FAIL | FAIL | 23239 ms |
| p08-conditional-locator | PASS | REGRESSION | 1/1 | passed | 0/1 | FAIL | FAIL | 22741 ms |
| p09-helper-extension-mapping | PASS | PASS | 1/1 | passed | 1/1 | PASS | PASS | 17902 ms |
| p10-unresolved-pageobject-chain | PASS | REGRESSION | 1/1 | passed | 1/1 | FAIL | FAIL | 19317 ms |
| p11-pageobject-separate-project | PASS | REGRESSION | 1/1 | passed | 0/1 | FAIL | FAIL | 26422 ms |
| p12-pageobject-inheritance-composition | PASS | REGRESSION | 1/1 | passed | 0/1 | FAIL | FAIL | 24422 ms |
| p13-async-lift-simple | PASS | REGRESSION | 1/1 | passed | 1/1 | FAIL | FAIL | 19671 ms |
| p14-async-lift-setup-base | PASS | PASS | 1/1 | passed | 1/1 | PASS | PASS | 18928 ms |
| p15-webdriverwait-visible | PASS | PASS | 1/1 | passed | 1/1 | PASS | PASS | 20677 ms |
| p16-wait-disappear-negative | PASS | REGRESSION | 1/1 | passed | 1/1 | FAIL | PASS | 21046 ms |
| p17-custom-wait-state | PASS | REGRESSION | 1/1 | passed | 1/1 | FAIL | PASS | 20363 ms |
| p18-assert-multiple-fluent | PASS | REGRESSION | 1/1 | passed | 1/1 | FAIL | PASS | 22166 ms |
| p19-control-flow-loops | PASS | REGRESSION | 1/1 | passed | 0/1 | FAIL | FAIL | 24442 ms |
| p20-nunit-testcasesource-valuesource | PASS | REGRESSION | 4/4 | passed | 0/4 | PASS | FAIL | 26730 ms |
| p21-nunit-parallelizable-retry-order | PASS | REGRESSION | 2/2 | passed | 0/2 | FAIL | FAIL | 38109 ms |
| p22-file-scoped-globalusings-nullable | PASS | REGRESSION | 1/1 | passed | 0/1 | FAIL | FAIL | 25279 ms |
| p23-cpm-isolation | PASS | PASS | 1/1 | passed | 1/1 | PASS | PASS | 19772 ms |
| p24a-transitive-warning-isolated | PASS | PASS | 1/1 | passed | 1/1 | PASS | PASS | 23639 ms |
| p24b-transitive-warning-sabotage | INFRASTRUCTURE_FAILURE | REGRESSION | 1/1 | failed | 0/1 | PASS | FAIL | 13800 ms |
| p25-multitarget-conditional-itemgroup | PASS | REGRESSION | 1/1 | failed | 0/1 | PASS | FAIL | 18434 ms |
| p26-jsexecutor-unsupported | UNSUPPORTED_AS_EXPECTED | UNSUPPORTED_AS_EXPECTED | 1/1 | passed | 1/1 | PASS | PASS | 34608 ms |
| p27-actions-api-unsupported | UNSUPPORTED_AS_EXPECTED | UNSUPPORTED_AS_EXPECTED | 1/1 | passed | 1/1 | PASS | PASS | 21441 ms |
| p28-frames-popup-upload-download | UNSUPPORTED_AS_EXPECTED | REGRESSION | 1/1 | passed | 0/1 | FAIL | FAIL | 24947 ms |
| p29-raw-statement-dynamic | UNSUPPORTED_AS_EXPECTED | REGRESSION | 1/1 | passed | 1/1 | FAIL | PASS | 18792 ms |

## Failure details

### p02-css-clear-input: REGRESSION

- Migration: `passed`; TODO `2`; unmapped `0`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `1/1`.
- Quality budget exceeded: TODO comments = 2, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p02-css-clear-input\target\runtime-artifacts`.

### p06-checkbox-radio-selected: REGRESSION

- Migration: `passed`; TODO `3`; unmapped `0`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `1/1`.
- Quality budget exceeded: TODO comments = 3, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p06-checkbox-radio-selected\target\runtime-artifacts`.

### p07-locator-in-variable: REGRESSION

- Migration: `passed`; TODO `3`; unmapped `1`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `0/1`.
- Quality budget exceeded: TODO comments = 3, maximum = 0.
- Quality budget exceeded: unmapped targets = 1, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- Semantic oracle failed (event-sequence): expected locator:primary; actual .
- Semantic oracle failed (dom-element): expected #locator-status; actual missing from final observation.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p07-locator-in-variable\target\runtime-artifacts`.

### p08-conditional-locator: REGRESSION

- Migration: `passed`; TODO `3`; unmapped `1`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: `unknown`.
- Target tests: `0/1`.
- Quality budget exceeded: TODO comments = 3, maximum = 0.
- Quality budget exceeded: unmapped targets = 1, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- Semantic oracle failed (event-sequence): expected locator:primary; actual .
- Semantic oracle failed (dom-element): expected #locator-status; actual missing from final observation.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p08-conditional-locator\target\runtime-artifacts`.

### p10-unresolved-pageobject-chain: REGRESSION

- Migration: `passed`; TODO `3`; unmapped `1`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `1/1`.
- Quality budget exceeded: TODO comments = 3, maximum = 0.
- Quality budget exceeded: unmapped targets = 1, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Semantic oracle failed (event-sequence): expected pom:login -> pom:dashboard; actual .
- Semantic oracle failed (dom-element): expected #dashboard-status; actual missing from final observation.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p10-unresolved-pageobject-chain\target\runtime-artifacts`.

### p11-pageobject-separate-project: REGRESSION

- Migration: `passed`; TODO `2`; unmapped `0`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `0/1`.
- Quality budget exceeded: TODO comments = 2, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- Semantic oracle failed (event-sequence): expected pom:login -> pom:dashboard; actual .
- Semantic oracle failed (dom-element): expected #dashboard-status; actual missing from final observation.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p11-pageobject-separate-project\target\runtime-artifacts`.

### p12-pageobject-inheritance-composition: REGRESSION

- Migration: `passed`; TODO `2`; unmapped `0`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `0/1`.
- Quality budget exceeded: TODO comments = 2, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- Semantic oracle failed (event-sequence): expected modal:open -> modal:save; actual .
- Semantic oracle failed (dom-element): expected #modal-status; actual missing from final observation.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p12-pageobject-inheritance-composition\target\runtime-artifacts`.

### p13-async-lift-simple: REGRESSION

- Migration: `passed`; TODO `3`; unmapped `0`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `1/1`.
- Quality budget exceeded: TODO comments = 3, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Semantic oracle failed (event-sequence): expected async:click; actual .
- Semantic oracle failed (dom-element): expected #async-status; actual missing from final observation.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p13-async-lift-simple\target\runtime-artifacts`.

### p16-wait-disappear-negative: REGRESSION

- Migration: `passed`; TODO `3`; unmapped `0`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `1/1`.
- Quality budget exceeded: TODO comments = 3, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p16-wait-disappear-negative\target\runtime-artifacts`.

### p17-custom-wait-state: REGRESSION

- Migration: `passed`; TODO `1`; unmapped `1`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `1/1`.
- Quality budget exceeded: TODO comments = 1, maximum = 0.
- Quality budget exceeded: unmapped targets = 1, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p17-custom-wait-state\target\runtime-artifacts`.

### p18-assert-multiple-fluent: REGRESSION

- Migration: `passed`; TODO `1`; unmapped `0`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `1/1`.
- Quality budget exceeded: TODO comments = 1, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p18-assert-multiple-fluent\target\runtime-artifacts`.

### p19-control-flow-loops: REGRESSION

- Migration: `passed`; TODO `2`; unmapped `0`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `0/1`.
- Quality budget exceeded: TODO comments = 2, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- Semantic oracle failed (dom-text): expected #control-status=beta; actual gamma.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p19-control-flow-loops\target\runtime-artifacts`.

### p20-nunit-testcasesource-valuesource: REGRESSION

- Migration: `passed`; TODO `0`; unmapped `0`; unsupported `0`; warning files `0`.
- verify-project: `passed`; categories: ``.
- Target tests: `0/4`.
- Semantic oracle failed (target-test-count): expected 4; actual 0/1.
- Semantic oracle failed (generated-contains): expected TestCaseSource; actual missing-or-comment-only.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p20-nunit-testcasesource-valuesource\target\runtime-artifacts`.

### p21-nunit-parallelizable-retry-order: REGRESSION

- Migration: `passed`; TODO `1`; unmapped `1`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `0/2`.
- Quality budget exceeded: TODO comments = 1, maximum = 0.
- Quality budget exceeded: unmapped targets = 1, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Semantic oracle failed (target-test-count): expected 2; actual 0/2.
- Semantic oracle failed (generated-contains): expected Parallelizable; actual missing-or-comment-only.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p21-nunit-parallelizable-retry-order\target\runtime-artifacts`.

### p22-file-scoped-globalusings-nullable: REGRESSION

- Migration: `passed`; TODO `2`; unmapped `1`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `0/1`.
- Quality budget exceeded: TODO comments = 2, maximum = 0.
- Quality budget exceeded: unmapped targets = 1, maximum = 0.
- Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- Semantic oracle failed (event-sequence): expected smoke:click; actual .
- Semantic oracle failed (dom-element): expected #smoke-status; actual missing from final observation.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p22-file-scoped-globalusings-nullable\target\runtime-artifacts`.

### p24b-transitive-warning-sabotage: REGRESSION

- Migration: `passed`; TODO `0`; unmapped `0`; unsupported `0`; warning files `0`.
- verify-project: `failed`; categories: `nuget-restore`.
- Target tests: `0/1`.

### p25-multitarget-conditional-itemgroup: REGRESSION

- Migration: `passed`; TODO `0`; unmapped `0`; unsupported `0`; warning files `0`.
- verify-project: `failed`; categories: `unknown`.
- Target tests: `0/1`.

### p28-frames-popup-upload-download: REGRESSION

- Migration: `passed`; TODO `15`; unmapped `0`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `0/1`.
- Quality budget exceeded: TODO comments = 15, maximum = 12.
- Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- Semantic oracle failed (event-sequence): expected complex:neighbour-click; actual .
- Semantic oracle failed (dom-element): expected #complex-status; actual missing from final observation.
- Semantic oracle failed (unsupported-neighbour-preserved): expected expected neighbour business event; actual not observed.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p28-frames-popup-upload-download\target\runtime-artifacts`.

### p29-raw-statement-dynamic: REGRESSION

- Migration: `passed`; TODO `3`; unmapped `1`; unsupported `0`; warning files `1`.
- verify-project: `passed`; categories: ``.
- Target tests: `1/1`.
- Quality budget exceeded: unmapped targets = 1, maximum = 0.
- Runtime failure artifacts: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\lab\block-06\nightly\projects\p29-raw-statement-dynamic\target\runtime-artifacts`.

## All issues

- p02-css-clear-input: Quality budget exceeded: TODO comments = 2, maximum = 0.
- p02-css-clear-input: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p06-checkbox-radio-selected: Quality budget exceeded: TODO comments = 3, maximum = 0.
- p06-checkbox-radio-selected: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p07-locator-in-variable: Quality budget exceeded: TODO comments = 3, maximum = 0.
- p07-locator-in-variable: Quality budget exceeded: unmapped targets = 1, maximum = 0.
- p07-locator-in-variable: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p07-locator-in-variable: Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- p07-locator-in-variable: Semantic oracle failed (event-sequence): expected locator:primary; actual .
- p07-locator-in-variable: Semantic oracle failed (dom-element): expected #locator-status; actual missing from final observation.
- p08-conditional-locator: Quality budget exceeded: TODO comments = 3, maximum = 0.
- p08-conditional-locator: Quality budget exceeded: unmapped targets = 1, maximum = 0.
- p08-conditional-locator: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p08-conditional-locator: Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- p08-conditional-locator: Semantic oracle failed (event-sequence): expected locator:primary; actual .
- p08-conditional-locator: Semantic oracle failed (dom-element): expected #locator-status; actual missing from final observation.
- p10-unresolved-pageobject-chain: Quality budget exceeded: TODO comments = 3, maximum = 0.
- p10-unresolved-pageobject-chain: Quality budget exceeded: unmapped targets = 1, maximum = 0.
- p10-unresolved-pageobject-chain: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p10-unresolved-pageobject-chain: Semantic oracle failed (event-sequence): expected pom:login -> pom:dashboard; actual .
- p10-unresolved-pageobject-chain: Semantic oracle failed (dom-element): expected #dashboard-status; actual missing from final observation.
- p11-pageobject-separate-project: Quality budget exceeded: TODO comments = 2, maximum = 0.
- p11-pageobject-separate-project: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p11-pageobject-separate-project: Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- p11-pageobject-separate-project: Semantic oracle failed (event-sequence): expected pom:login -> pom:dashboard; actual .
- p11-pageobject-separate-project: Semantic oracle failed (dom-element): expected #dashboard-status; actual missing from final observation.
- p12-pageobject-inheritance-composition: Quality budget exceeded: TODO comments = 2, maximum = 0.
- p12-pageobject-inheritance-composition: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p12-pageobject-inheritance-composition: Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- p12-pageobject-inheritance-composition: Semantic oracle failed (event-sequence): expected modal:open -> modal:save; actual .
- p12-pageobject-inheritance-composition: Semantic oracle failed (dom-element): expected #modal-status; actual missing from final observation.
- p13-async-lift-simple: Quality budget exceeded: TODO comments = 3, maximum = 0.
- p13-async-lift-simple: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p13-async-lift-simple: Semantic oracle failed (event-sequence): expected async:click; actual .
- p13-async-lift-simple: Semantic oracle failed (dom-element): expected #async-status; actual missing from final observation.
- p16-wait-disappear-negative: Quality budget exceeded: TODO comments = 3, maximum = 0.
- p16-wait-disappear-negative: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p17-custom-wait-state: Quality budget exceeded: TODO comments = 1, maximum = 0.
- p17-custom-wait-state: Quality budget exceeded: unmapped targets = 1, maximum = 0.
- p17-custom-wait-state: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p18-assert-multiple-fluent: Quality budget exceeded: TODO comments = 1, maximum = 0.
- p18-assert-multiple-fluent: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p19-control-flow-loops: Quality budget exceeded: TODO comments = 2, maximum = 0.
- p19-control-flow-loops: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p19-control-flow-loops: Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- p19-control-flow-loops: Semantic oracle failed (dom-text): expected #control-status=beta; actual gamma.
- p20-nunit-testcasesource-valuesource: Semantic oracle failed (target-test-count): expected 4; actual 0/1.
- p20-nunit-testcasesource-valuesource: Semantic oracle failed (generated-contains): expected TestCaseSource; actual missing-or-comment-only.
- p21-nunit-parallelizable-retry-order: Quality budget exceeded: TODO comments = 1, maximum = 0.
- p21-nunit-parallelizable-retry-order: Quality budget exceeded: unmapped targets = 1, maximum = 0.
- p21-nunit-parallelizable-retry-order: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p21-nunit-parallelizable-retry-order: Semantic oracle failed (target-test-count): expected 2; actual 0/2.
- p21-nunit-parallelizable-retry-order: Semantic oracle failed (generated-contains): expected Parallelizable; actual missing-or-comment-only.
- p22-file-scoped-globalusings-nullable: Quality budget exceeded: TODO comments = 2, maximum = 0.
- p22-file-scoped-globalusings-nullable: Quality budget exceeded: unmapped targets = 1, maximum = 0.
- p22-file-scoped-globalusings-nullable: Quality budget exceeded: warning-bearing files = 1, maximum = 0.
- p22-file-scoped-globalusings-nullable: Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- p22-file-scoped-globalusings-nullable: Semantic oracle failed (event-sequence): expected smoke:click; actual .
- p22-file-scoped-globalusings-nullable: Semantic oracle failed (dom-element): expected #smoke-status; actual missing from final observation.
- p28-frames-popup-upload-download: Quality budget exceeded: TODO comments = 15, maximum = 12.
- p28-frames-popup-upload-download: Semantic oracle failed (target-test-count): expected 1; actual 0/1.
- p28-frames-popup-upload-download: Semantic oracle failed (event-sequence): expected complex:neighbour-click; actual .
- p28-frames-popup-upload-download: Semantic oracle failed (dom-element): expected #complex-status; actual missing from final observation.
- p28-frames-popup-upload-download: Semantic oracle failed (unsupported-neighbour-preserved): expected expected neighbour business event; actual not observed.
- p29-raw-statement-dynamic: Quality budget exceeded: unmapped targets = 1, maximum = 0.
