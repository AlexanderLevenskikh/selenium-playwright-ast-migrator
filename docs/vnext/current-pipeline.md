# Current Migration Pipeline — Reconstructed From Code

> Phase A baseline. This document reflects the **actual implementation** at commit
> `734990c` (`master`), not the intended architecture. It was produced by reading the
> source, not by trusting `README.md` / `docs/architecture.md`.

## 0. Scope of this document

The legacy single-project source scope is the only full-project execution path used by the
standard `run` command. The pipeline is orchestrated from `Migrator.Cli/Program.cs`
(11,338 lines) which doubles as command router and legacy command host.

## 1. Control/data flow (as implemented)

```
source discovery                      Program.cs mode dispatch; SourceAutoDetector (Core/SourceFrontends)
        ↓
Roslyn loading/parsing                RoslynTestFileParser.Parse/ParseDirectory (Migrator.Roslyn)
        ↓
semantic analysis                     SemanticCompilationSupport.CreateCompilation (+ synthetic anchors);
                                      ProjectSemanticIndex (project graph via CSharpSeleniumFrontend)
        ↓
Selenium recognition                  ~~~~ 19 recognizers in Migrator.Roslyn/Recognizers ~~~~
                                      + inline extraction in RoslynTestFileParser (wait/assert-multiple/
                                        assert-binary/collection-foreach/webdriver-wait)
        ↓
IR / intermediate models              Migrator.Core/Models (legacy TestAction hierarchy, TestFileModel)
                                      experimental: Models/Ir (MigrationDocument, Intents, LocatorRef, ValueExpr)
        ↓
adapter/config resolution             DefaultProjectAdapter.Adapt (Migrator.SeleniumCSharp) — optional config
                                      target resolution: config UiTargets/Methods/Tables/Pagination →
                                        local variable mapping → inline FindElement regexes → Unresolved
        ↓
transformation / rendering            PlaywrightDotNetRenderer (+ sub-renderers)  OR  ITargetBackend
                                      (PlaywrightTypeScript / IrV2 path via LegacyIrBridge)
        ↓
reports                              ReportBuilder / ReportWriter; analyze|generated|verify directories
        ↓
verification                         VerifyRunner / Program.cs verify-* (SyntaxChecker, dotnet build harness)
        ↓
optional orchestration/remediation   Orchestrator, Remediation*, DoctorFixPlanner, memory, PR packs,
                                      agent-contract, supervised-task workflow — all OUTSIDE the default run
```

## 2. Concrete entry points and types per stage

### 2.1 Source discovery

- `SourceAutoDetector` (`Migrator.Core/SourceFrontends/SourceAutoDetector.cs`) selects
  `csharp-selenium | java-selenium | python-selenium`.
- `SourceFrontendRegistry`/`CSharpSeleniumFrontend` (`Migrator.Roslyn/CSharpSeleniumFrontend.cs`)
  are the C# entry frontends. `TestFileParserSourceFrontend` wraps `ITestFileParser`.
- `ScopeResolver` resolves the input path to the file/dir scope.

### 2.2 Roslyn loading/parsing

- `RoslynTestFileParser` (`Migrator.Roslyn/RoslynTestFileParser.cs`, 1983 lines) implements
  `ITestFileParser`. `Parse(file)` → `TestFileModel`. It:
  - finds the `[TestFixture]` class (throws if missing, or records a per-file
    `SourceFileParseException` for non-test files — infra files are skipped with a warning
    "skipped non-test file");
  - parses class members → `PageObjectFieldAction` for UI/POM fields;
  - parses method bodies statement-by-statement (`ParseBlockStatements`) into `TestAction`;
  - parses `[TestCase]` data (`ParseCaseData`), parameters, preserved attributes.

### 2.3 Semantic analysis

Two complementary mechanisms:

1. **Per-file lightweight compilation** — `SemanticCompilationSupport.CreateCompilation`
   (`Migrator.Roslyn/SemanticCompilationSupport.cs`). Builds a `CSharpCompilation` named
   `MigratorTemp` from the input trees + TPA runtime references, then **injects synthetic
   compile-only anchors** for `OpenQA.Selenium.By/IWebElement/IWebDriver/Keys` and
   `NUnit.Framework.TestFixture/Test/TestCase/SetUp/Assert/Is` **when those types are absent**
   from the real references. Consequence: symbol identity for Selenium/NUnit is *approximated*
   by these anchors, so `recognition` is only as strong as the anchor surface (e.g.
   `IJavaScriptExecutor` is NOT anchored → casts to it resolve as unresolved casts).
2. **Project semantic index** — `ProjectSemanticIndex` / `ProjectSemanticIndexBuilder`
   (`Migrator.Roslyn/ProjectSemanticIndex.cs`): when an SDK-style project root is supplied,
   it reconstructs a project compilation graph (source + `ProjectReference` `CompilationReference`
   edges) without MSBuild; index records: types, methods, calls, resolution status, diagnostics.
   `ProjectSemanticIndexBuilder` represents project references as `CompilationReference`
   instances so `IsResolvedProjectMethod` can trust source ownership.

### 2.4 Selenium recognition

**Semantic path (`TryRecognizeSemantic`)** — only four rules use symbol identity:
- `.Click()` when the receiver's `ContainingType` is/derives from an `OpenQA.Selenium`
  type implementing `IWebElement` (`RoslynTestFileParser.cs:1157`);
- `.SendKeys(...)`/`.InputText(...)` on a Selenium type (`:1160`); `Keys.*` → `PressAction`;
- `Assert.That/AreEqual` **only** when the containing type is `NUnit.Framework.Assert`
  (`IsNUnitAssertType`, `:1196`);
- resolved project-owned calls are preserved as `MethodInvocationAction` (authoritative
  negative for builtins); resolved builtin/System calls are dropped.

Everything else runs the **syntax-fallback recognizer pipeline** via
`RecognizerArbitrator.Recognize` — an explicit priority table + deterministic tie-breaking
(ambiguous → `UnsupportedAction` reason `AMBIGUOUS_RECOGNITION`). Priorities:
`WebDriverFindElement 2000, Table 1900, ProjectAssertionHelper 1850, FluentText 1800,
Visibility 1750, WaitPresence 1700, UrlAssertion 1650, FluentAssertions 1600,
Assert 1550, PlaywrightAssertion 1500, SelectValue 1450, Navigation 1400, Wait 1350,
AsyncPlaywright 1300, SendKeys 1200, Click 1100, PageObjectMethod -1000`.

Plus **inline extraction** (not recognizers) for: WebDriverWait declaration,
`wait.Until(driver => driver.FindElement(By...).Displayed)`, `Assert.Multiple(...)`,
`Assert.That(x.Text == ...)` / `.Count.Get()` binary assertions, `foreach`/`.ForEach(lambda)`
collections.

### 2.5 IR / intermediate models

- **Legacy IR (production-executable)**: `TestFileModel` → `TestModel` → `TestAction`
  hierarchy (~30 concrete types), `TargetExpression`/`TargetKind`
  (`Migrator.Core/Models/`). `TargetKind` = `PlaywrightLocator, PageObjectProperty,
  RawExpression, Unresolved, Text, CssSelector, TestIdBeginning, ClassNameBeginning`.
- **IR v2 (experimental)**: `MigrationDocument`/`TestSuiteIr`/`TestStatementIr`,
  `LocatorRef` (`ByTestId/ByCss/ByXpath/ByText/ByRole/ByClassNamePrefix/PageObjectLocator/
  PlaywrightLocatorRef/RawLocatorExpression/UnresolvedLocator`), `ValueExpr`,
  `AssertionIntent/WaitIntent/NavigationIntent`, `RawStatementSafety`.
  Bridged from legacy via `LegacyIrBridge` (`Migrator.Core/Models/Ir/LegacyIrBridge.cs`).
  Production default remains **Legacy** (`MigrationPipelineRenderMode.Legacy`).

### 2.6 Adapter / config resolution

- `DefaultProjectAdapter` (`Migrator.SeleniumCSharp/DefaultProjectAdapter.cs`, 3621 lines):
  `Adapt(TestFileModel)` applies per-file `ResolvedFileConfig` (config merged from profile
  scopes + file config). Target resolution order (`ResolveTargetWithLocalVars`):
  1. local-variable mapping (declared `FindElement`/`By`-alias locals) →
  2. `FindElement(localLocator)` wrappers → 3. `local.ElementAt(i)` / `local[i]` →
  4. config `UiTargets` map → 5. inline `WebDriver.FindElement(s)(By.XPath/CssSelector/Id(...))`
     literal regexes (`:836-935`) → 6. `UnresolvedTarget`.
- Mapping surface (config-driven, class-B rules): `UiTargets`, `PageObjects`, `Methods`,
  `ParameterizedMethods`, `Tables`, `Pagination`, `NavigationUrls`, `WaitPolicies`,
  `TargetStatements`, `RequiresReview`, `TargetKnownTypes/Identifiers`,
  `SourceOnlyIdentifiers`, `SuppressedMethods/Patterns`, `ScaffoldMethods/Patterns`,
  `RecognizerAliases` (`ProjectAdapterConfig.cs`).

### 2.7 Transformation / rendering

- `PlaywrightDotNetRenderer` (2489 lines) + sub-renderers:
  `DotNetActionDispatchRenderer` (dispatch), `DotNetAssertionAndWaitRenderer`,
  `DotNetCollectionForEachRenderer`, `DotNetControlStateAssertionRenderer`,
  `DotNetLocatorRenderer`, `DotNetMappedTemplateRenderer`,
  `DotNetTestFileScaffoldRenderer`. Scoped symbol-table safety (`RenderTargetSafeDeclaration`):
  unresolved roots → `[MIGRATOR:*]` smart TODO comments.
- Alternate backends: `ITargetBackend` (`PlaywrightDotNetBackend`, TypeScript backends),
  experimental `IrV2` path selected with `--render-ir v2`.

### 2.8 Reports

- `ReportBuilder.Build` (`Migrator.Core/ReportBuilder.cs`, 61 lines) → `MigrationReport`
  per file: `SuccessfulConvertedTests`, `SemanticActions`, `SyntaxFallbackActions`,
  `UnsupportedCount`, `MappedTargets`, `UnmappedTargets`, `TodoComments`.
- `MigrationSummaryReport` aggregate; `unmapped-targets.json/csv`, `unsupported-actions`,
  `mapping-proposals`, `migration-quality-dashboard`, `QualityGates` (max todos / unmapped /
  raw expressions etc.) from `QualityGatesConfig`.

### 2.9 Verification

- `VerifyRunner` (`Migrator.Core/VerifyRunner.cs`, 1275 lines) + `Program.cs` verify modes:
  - `--mode verify`: legacy diagnostics, regenerates source before build;
  - `--mode verify-project --run-manifest`: **authoritative exact-target verification** —
    compiles the generated tree from the recorded `run-manifest.json` (provenance-bound);
  - `SyntaxChecker` (`CreateGeneratedCodeChecker`) for L3 static compile of generated code;
  - `ProjectVerifyHarnessEvidence`, identity capture (`target-tree.sha256`).

### 2.10 Orchestration (outside default run)

`Orchestrator`, `RemediationStateEvaluator`, `RemediationRebaselineEvaluator`,
`RemediationCycleGuard`, `DoctorFixPlanner`, memory (`MigrationMemory*`),
`MigrationPrPack`, `LearnPack`, `AgentContract`, `MigrationBoard`, `ReportServe`,
`RuntimeFailureClassifier`, `SelectorEvidence`, `HelperInventory`, `TargetDiscovery`,
`ProfileMarketplace`, lab (`Migrator.Lab`), supervised-task workflow — none are required
for a deterministic `run`. See `docs/vnext/complexity-inventory.md`.

## 3. Mutable state / filesystem / config / previous-run dependencies

| Stage | Mutable state | Filesystem deps | Config deps | Previous-run deps |
|---|---|---|---|---|
| Parse (per-file) | `localVariableMappings` inside adapter only | source tree paths | `RecognizerAliases` | none |
| Semantic index | cached index per project root | SDK project refs, source files | — | none (deterministic rebuild) |
| Adapt | `_resolvedConfigs` cache keyed by path | config JSON files | full `ProjectAdapterConfig` | none |
| Render | `_targetLocals` method-scoped symbol table | — | `TargetKnown*`, `SourceOnly*` | none |
| Verify | — | dotnet SDK, csproj harness | `VerificationConfig` | `run-manifest.json` (verify-project) |
| Orchestration | `migration/state/**` ledgers, memory | workspace dirs | profiles + memory | run history, memory, stop-policy |

## 4. What the "default deterministic path" is

`selenium-pw-migrator run --input <src> [--config <adapter-config.json>] --out <dir>`:
analyze → migrate → verify → propose. A run-manifest + target-tree hash + run-digest are
written. This is the path measured in `determinism-baseline.md` and `phase-a-findings.md`.
