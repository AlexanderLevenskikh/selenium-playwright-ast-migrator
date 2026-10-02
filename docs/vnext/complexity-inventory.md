# Complexity Inventory

Phase A, commit `734990c`. Catalogues what actually exists in the codebase, how big each
piece is, and where each piece sits relative to the deterministic core. This is input to
any later "extract the engine / slim the CLI" decision. Line counts are
`Measure-Object -Line` — indicative, not normative.

## Top of the tree

| Project / file | Size | Role |
|---|---|---|
| `Migrator.Cli/Program.cs` | ~11.3k lines | single entry point, mode router, all commands inline |
| `Migrator.Cli/Commands/CliCommandCatalog.cs` | 50+ modes | discovery/manifest of CLI modes |
| `Migrator.Roslyn/RoslynTestFileParser.cs` | ~1.7k | test-file parsing → TestFileModel; recognition drivers |
| `Migrator.SeleniumCSharp/DefaultProjectAdapter.cs` | ~3.2k | target resolution, config, renderer wiring |
| `Migrator.SeleniumCSharp/PlaywrightDotNetRenderer.cs` | ~2.5k | target code emission (with 7 sub-renderers) |
| `Migrator.Core/…` | core models, verify, reports, memory, orchestration | shared contracts |

## Deterministic core (what `run`/`verify` need to work)

| Component | Purpose | Inventory |
|---|---|---|
| SourceDiscovery + `SourceAutoDetector` | find source scope | needed |
| `CSharpSeleniumFrontend`/`TestFileParserSourceFrontend` | parse entry | needed |
| `RoslynTestFileParser` + `SemanticCompilationSupport` | parse + symbol recognition | needed |
| `ProjectSemanticIndex` | project graph for POM/ownership | needed (currently optional input) |
| `ProjectAdapterConfig` | everything that makes mappings deterministic | **needed, largest config surface** |
| `DefaultProjectAdapter` | target resolution | needed; candidates for simplification (§4) |
| Renderers + sub-renderers | emission | needed |
| `PlaywrightDotNetTestEnvironment` | harness to compile target code sans Playwright | CI-only; provenance tests |
| `VerifyRunner` + run-manifest | exact-target verification | needed |
| `RunDigest`, determinism harness | byte reproducibility | needed |
| `ReportBuilder`/`MigrationSummaryReport` | reports | needed, metrics audit §6 |

## The "long tail" outside the core path

| Component | What it is | Inventory verdict |
|---|---|---|
| `Orchestrator`, `Remediation*`, `DoctorFixPlanner` | agent-driven orchestration loop | **MOVE OUT OF CORE** / keep experimental arm |
| `MigrationMemory` | cross-run memory (do-not-repeat etc.) | experimental arm; keep out of default path |
| `MigrationPrPack`, `LearnPack` | MR packaging, learning | experimental arm |
| `AgentContract`, `MigrationBoard`, `ReportServe` | agent protocol, dashboard, serving | experimental arm |
| `RuntimeFailureClassifier` | classify runtime failures of migrated code | verification adjunct, useful |
| `SelectorEvidence`, `HelperInventory`, `TargetDiscovery`, `ProfileMarketplace` | discovery helpers | candidate **REMOVE** if unused after Phase A |
| `CodeFixCandidate`/remediation candidates | actionable error strings | hard-wired strings, high maintenance, candidate SIMPLIFY |
| `lab`/`Migrator.Lab`, `run-lab-block6.ps1` | runtime corpus | keep (key oracle) |
| `supervised-task` workflow (`.opencode/`) | the external agent workflow | keep as external orchestration |

## Single-file hotspots (maintenance risk)

1. `Program.cs` ~11.3k lines: routing + every command glued to one file. Even with
   `CliCommandCatalog`/`Commands/*`, most real logic stays here; E2E coverage of the CLI
   is thin.
2. `DefaultProjectAdapter.cs` ~3.2k: config + target resolution + inline regexes in one
   class. `ResolveTargetWithLocalVars` order defines semantics; the 8 inline
   `WebDriver.FindElement(s)(By.XPath|CssSelector|Id)` families at :836-935 are regex plus
   hand-rolled parsing — the single most fragile slice (LOC-01/02/03/06).
3. `Recognizers/` 19 files + `RecognizerArbitrator` priorities: deterministic, good;
   low risk, though new recognizers must be added in lockstep with arbitrator priorities
   and the AMBIGUOUS_RECOGNITION model.

## Guardrails / invariants already present

- Restrictive path rules from AGENTS.md (artifacts under `migration/**`, harness policy,
  no manual validation JSON).
- `verify-project` does not trust migration output; it compiles + runs real tests.
- Determinism harness `--assert-identical` fails the run on byte drift.

## What is knowingly NOT present

- A formal IR v2 used by the default path (it exists experimental, `Models/Ir/*` +
  `LegacyIrBridge`, default is Legacy).
- Cross-project async caller rewriting (ASYNC-01 D-rated).
- Product-state wait analysis beyond the name heuristic (WAIT-03 Unsafe).
