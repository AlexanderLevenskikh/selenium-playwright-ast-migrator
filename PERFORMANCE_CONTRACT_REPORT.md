# Migrator performance contract — iteration 1

## Problem confirmed

The orchestration integration suite repeatedly executed the same Roslyn-heavy CLI scenario for assertions over different artifacts. Under machines that report many CPUs but have limited memory, xUnit could also schedule several heavy collections concurrently. The result was long test runs, timeout cascades, and misleading secondary failures such as missing reports.

## Implemented

### Scenario snapshot cache

`Migrator.Tests/TestInfrastructure/OrchestratorScenarioCache.cs`

- unique `(input, config)` scenarios execute once per test process;
- every test receives an independent copy of the produced workspace;
- mutable test outputs remain isolated;
- `MIGRATOR_DISABLE_SCENARIO_CACHE=1` forces uncached debugging;
- each canonical scenario writes `.scenario-receipt.json` with duration, exit code, peak memory, and command line.

### Process diagnostics

`CliTestRunner` now records:

- wall-clock duration;
- peak working set;
- command line;
- timeout status;
- stdout/stderr.

Orchestrator tests fail immediately and diagnostically on timeout instead of later reporting a missing artifact.

### Controlled parallelism

`Migrator.Tests/xunit.runner.json` caps test parallelism at four threads. This prevents CPU-count-based oversubscription on memory-constrained runners.

### Performance runner

`scripts/run-performance-tests.ps1/.sh`

- runs the orchestrator test class;
- emits TRX, JSON, and Markdown reports;
- lists the ten slowest tests;
- compares wall-clock duration to a previous baseline;
- optionally fails when the regression ratio exceeds the configured limit.

Example:

```powershell
./scripts/run-performance-tests.ps1 -Root .
./scripts/run-performance-tests.ps1 -Root . `
  -Baseline artifacts/baseline/orchestrator-performance.json `
  -MaxRegressionRatio 1.35 -Enforce
```

### Documentation

- `docs/performance-testing.md`
- `docs/performance-testing.ru.md`
- README links in both languages.

### Regression coverage

`Orchestrator_ReusesCachedScenarioButMaterializesIndependentOutputs` verifies that a repeated baseline scenario does not execute again and that materialized output directories stay isolated.

## Validation performed

- `validate-scripts.ps1 -Root . -RequireShell`: PASS;
- 60 PowerShell scripts parsed;
- 61 shell scripts passed syntax validation;
- .NET SDK 10.0.300 and PowerShell 7.6.3 used from persistent `/mnt/data` directories.

A full test build could not be completed in this final clean workspace because NuGet.org DNS was unavailable and the restored package cache from the earlier run was no longer present. The source changes should therefore receive one native `dotnet test` pass in the user's environment before merge.
