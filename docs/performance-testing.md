# Performance testing

Migrator treats orchestration speed and resource stability as part of the engineering contract.

## Architecture

- `SystemProcessRunner` is the single diagnostics-rich process launcher used by the validation host and CLI tests. It applies bounded timeouts, kills the full process tree, and records duration, output, exit code, and peak working set.
- `OrchestratorScenarioCache` executes each unique input/config content fingerprint once per test process. Paths alone are not a cache key.
- Every scenario assertion receives an independent materialized snapshot, so tests cannot mutate another test's output.
- Unit tests use `FakeProcessRunner`, `InMemoryFileSystem`, and `FakeClock` instead of starting `dotnet` or `pwsh`.
- `xunit.runner.json` caps parallelism at four threads. Increase it only after measuring memory pressure.

Set `MIGRATOR_DISABLE_SCENARIO_CACHE=1` to force uncached scenario runs while debugging.

The executable layer model is documented in [test layers](test-layers.md).

## Performance report and budgets

```powershell
./scripts/run-performance-tests.ps1 -Root .
```

The command runs the cached orchestrator scenario suite and the validation-host E2E smoke. It writes JSON, Markdown, TRX, process logs, and smoke evidence under `artifacts/performance`.

Budgets are defined in `Migrator.Tests/performance-budgets.json`:

- a soft threshold produces a visible warning;
- a hard threshold fails when `-Enforce` is used;
- a same-runner baseline can additionally detect relative regression.

```powershell
./scripts/run-performance-tests.ps1 `
  -Root . `
  -Baseline artifacts/baseline/orchestrator-performance.json `
  -MaxRegressionRatio 1.35 `
  -Enforce
```

Absolute timings vary by machine. The hard limits protect against runaway execution, while the ratio compares the same runner class against its own baseline. Keep the raw slowest-test list and validation-host event durations to identify where a regression started.

## Validation-host smoke diagnostics

The performance report records `validationHostSmokeStdout`, `validationHostSmokeStderr`, and a bounded `validationHostSmokeFailure` summary when the E2E smoke fails. Expected nonzero scenarios inside the smoke (for example `VALIDATION_HOST_CONFIGURATION_REQUIRED`) are captured as process results rather than PowerShell error records, including under Windows PowerShell 5.1.
