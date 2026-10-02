# Semantic Accounting (whole corpus, unconfigured default path)

Source report: `C:\Users\levenskikh\Desktop\MyProjects\Migrator\migration\artifacts\baseline\perf\whole-corpus\report.json`

| Files | Tests | Actions | Semantic | SyntaxFallback | Unsupported | Mapped | Unmapped | TODO | Fully converted files |
|---|---|---|---|---|---|---|---|---|---|
| 31 | 32 | 120 | 35 | 92 | 0 | 2 | 31 | 117 | 0 |

> Note: Semantic+SyntaxFallback (127) > ActionsFound (120) is the ReportBuilder double-counting artefact (block containers counted both as nodes and via Flatten). `SuccessfullyConvertedTests` is 1 for every story even when all assertions/locators became TODOs; the run-level quality gates (`AssertionLoss`, `SemanticNoOp`, `MISSING_MAPPING`) are the honest signal.

## Per-file

| File | Tests | Sem | Syn | Unsup | Mapped | Unmapped | TODO | Fully converted |
|---|---|---|---|---|---|---|---|---|
| corpus\stable\vertical-slice\p01-basic-id-login\Tests\LoginTests.cs | 1 | 3 | 4 | 0 | 0 | 3 | 6 | no |
| corpus\stable\vertical-slice\p02-css-clear-input\Tests\EditTests.cs | 1 | 2 | 4 | 0 | 0 | 2 | 5 | no |
| corpus\stable\vertical-slice\p03-xpath-text-assert\Tests\XPathTests.cs | 1 | 0 | 2 | 0 | 0 | 0 | 1 | no |
| corpus\stable\vertical-slice\p04-findelements-count-text\Tests\ListTests.cs | 1 | 0 | 5 | 0 | 0 | 0 | 4 | no |
| corpus\stable\vertical-slice\p05-table-row-target\Tests\TableTests.cs | 1 | 0 | 3 | 0 | 0 | 0 | 2 | no |
| corpus\stable\vertical-slice\p06-checkbox-radio-selected\Tests\FormStateTests.cs | 1 | 0 | 8 | 0 | 0 | 0 | 5 | no |
| corpus\stable\vertical-slice\p07-locator-in-variable\Tests\VariableLocatorTests.cs | 1 | 1 | 2 | 0 | 0 | 1 | 2 | no |
| corpus\stable\vertical-slice\p08-conditional-locator\Tests\ConditionalLocatorTests.cs | 1 | 1 | 3 | 0 | 0 | 1 | 2 | no |
| corpus\stable\vertical-slice\p09-helper-extension-mapping\Tests\HelperTests.cs | 1 | 1 | 2 | 0 | 0 | 0 | 4 | no |
| corpus\stable\vertical-slice\p10-unresolved-pageobject-chain\Tests\PageObjectChainTests.cs | 1 | 0 | 2 | 0 | 0 | 0 | 3 | no |
| corpus\stable\vertical-slice\p11-pageobject-separate-project\Tests\SeparateProjectTests.cs | 1 | 1 | 1 | 0 | 0 | 0 | 3 | no |
| corpus\stable\vertical-slice\p12-pageobject-inheritance-composition\Tests\PomInheritanceTests.cs | 1 | 1 | 1 | 0 | 0 | 0 | 3 | no |
| corpus\stable\vertical-slice\p13-async-lift-simple\Tests\AsyncLiftTests.cs | 1 | 1 | 1 | 0 | 0 | 0 | 3 | no |
| corpus\stable\vertical-slice\p14-async-lift-setup-base\Tests\SetupLiftTests.cs | 1 | 2 | 2 | 0 | 0 | 2 | 4 | no |
| corpus\stable\vertical-slice\p15-webdriverwait-visible\Tests\WaitVisibleTests.cs | 1 | 1 | 4 | 0 | 1 | 1 | 3 | no |
| corpus\stable\vertical-slice\p16-wait-disappear-negative\Tests\NegativeWaitTests.cs | 1 | 1 | 5 | 0 | 1 | 1 | 3 | no |
| corpus\stable\vertical-slice\p17-custom-wait-state\Tests\CustomWaitTests.cs | 1 | 2 | 1 | 0 | 0 | 1 | 4 | no |
| corpus\stable\vertical-slice\p17b-custom-wait-closing-state\Tests\DialogCloseTests.cs | 1 | 3 | 1 | 0 | 0 | 2 | 5 | no |
| corpus\stable\vertical-slice\p18-assert-multiple-fluent\Tests\AssertionTests.cs | 1 | 1 | 5 | 0 | 0 | 1 | 3 | no |
| corpus\stable\vertical-slice\p19-control-flow-loops\Tests\ControlFlowTests.cs | 1 | 0 | 7 | 0 | 0 | 2 | 2 | no |
| corpus\stable\vertical-slice\p20-nunit-testcasesource-valuesource\Tests\ParameterizedTests.cs | 2 | 2 | 0 | 0 | 0 | 0 | 4 | no |
| corpus\stable\vertical-slice\p21-nunit-parallelizable-retry-order\Tests\ParallelRetryTests.cs | 1 | 1 | 1 | 0 | 0 | 1 | 2 | no |
| corpus\stable\vertical-slice\p22-file-scoped-globalusings-nullable\Tests\ModernSyntaxTests.cs | 1 | 1 | 3 | 0 | 0 | 1 | 3 | no |
| corpus\stable\vertical-slice\p23-cpm-isolation\Tests\SmokeTests.cs | 1 | 1 | 1 | 0 | 0 | 1 | 2 | no |
| corpus\stable\vertical-slice\p24a-transitive-warning-isolated\Tests\SmokeTests.cs | 1 | 1 | 1 | 0 | 0 | 1 | 2 | no |
| corpus\stable\vertical-slice\p24b-transitive-warning-sabotage\Tests\SabotageSmokeTests.cs | 1 | 1 | 1 | 0 | 0 | 1 | 2 | no |
| corpus\stable\vertical-slice\p25-multitarget-conditional-itemgroup\Tests\MultiTargetTests.cs | 1 | 1 | 1 | 0 | 0 | 1 | 2 | no |
| corpus\stable\vertical-slice\p26-jsexecutor-unsupported\Tests\JsExecutorTests.cs | 1 | 1 | 3 | 0 | 0 | 1 | 5 | no |
| corpus\stable\vertical-slice\p27-actions-api-unsupported\Tests\ActionsApiTests.cs | 1 | 1 | 3 | 0 | 0 | 1 | 4 | no |
| corpus\stable\vertical-slice\p28-frames-popup-upload-download\Tests\ComplexWindowTests.cs | 1 | 3 | 12 | 0 | 0 | 4 | 19 | no |
| corpus\stable\vertical-slice\p29-raw-statement-dynamic\Tests\DynamicTests.cs | 1 | 1 | 3 | 0 | 0 | 2 | 5 | no |
