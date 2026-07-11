# Performance testing

Migrator treats orchestration speed and resource stability as part of the engineering contract.

## Why

CLI orchestration tests launch Roslyn-heavy child processes and PowerShell workflows. Repeating the same scenario per assertion wastes time and can cause timeout cascades when xUnit sees many CPUs but the machine has limited memory.

## Test architecture

- `OrchestratorScenarioCache` executes each unique `(input, config)` scenario once per test process.
- Every test receives an independent copy of the scenario snapshot, so assertions cannot mutate another test's output.
- `CliTestRunner` records wall-clock duration and peak working set and kills the whole process tree on timeout.
- `xunit.runner.json` caps parallelism at four threads. Override only after measuring memory pressure.

Set `MIGRATOR_DISABLE_SCENARIO_CACHE=1` to force uncached runs while debugging.

## Baseline and regression check

```powershell
./scripts/run-performance-tests.ps1 -Root .
```

The command writes JSON, Markdown, and TRX artifacts under `artifacts/performance`.
To compare against a committed or downloaded baseline:

```powershell
./scripts/run-performance-tests.ps1 `
  -Root . `
  -Baseline artifacts/baseline/orchestrator-performance.json `
  -MaxRegressionRatio 1.35 `
  -Enforce
```

Absolute timings vary by machine. The recommended gate compares the same runner class against its own baseline and fails only when the ratio crosses the configured limit. Keep the raw slowest-test list to identify where the regression started.
