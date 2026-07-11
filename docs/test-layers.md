# Test layers

Migrator separates fast deterministic checks from process-heavy end-to-end scenarios. The separation is executable rather than documentary: xUnit traits select each layer and `scripts/run-test-layer.*` runs it directly.

## Layers

| Layer | Purpose | External processes | Typical trigger |
|---|---|---:|---|
| `Unit` | Models, path rules, process orchestration with fakes, cache-key behavior | No | Every local edit and pull request |
| `Contract` | CLI wiring, schemas, prompts, safety invariants, documentation contracts | No | Every pull request |
| `Scenario` | Built CLI against minimal or cached workspaces | Yes, bounded | Pull request or targeted debugging |
| `E2E` | Full validation-host lifecycle and artifact evidence | Yes | Full validation, nightly, release |

Run one layer:

```powershell
./scripts/run-test-layer.ps1 -Root . -Layer Unit
./scripts/run-test-layer.ps1 -Root . -Layer Contract
./scripts/run-test-layer.ps1 -Root . -Layer Scenario
./scripts/run-test-layer.ps1 -Root . -Layer E2E -NoBuild
```

Run the optimized stack:

```powershell
./scripts/run-test-layer.ps1 -Root . -Layer All
```

The ordinary `dotnet test Migrator.sln` remains the source of truth for the complete regression suite. Layer filters are an acceleration mechanism, not a permission to skip required final validation.

The runner refuses to report success when a filtered layer discovers zero tests. For the Unit layer it combines the explicit `Layer=Unit` trait with the `*UnitTests` class-name convention because older xUnit/VSTest adapters can omit class-level custom traits from filtered discovery on a stale `--no-build` assembly. When that happens, rebuild before retrying.

The PowerShell runners prefer `pwsh` when it is installed, but Windows PowerShell can execute the compatibility path. Process argument construction, relative paths, Windows detection, and timeout termination avoid PowerShell 7-only APIs in that fallback mode.

## Test seams

`Migrator.Core` owns `IProcessRunner`, `IFileSystem`, and `IClock`. Unit tests use `FakeProcessRunner`, `InMemoryFileSystem`, and `FakeClock`; process launch behavior is tested once instead of spawning `dotnet` or `pwsh` for every assertion.

`CliTestRunner` delegates to the shared `SystemProcessRunner`. `OrchestratorScenarioCache` hashes input and configuration contents and gives every test a private materialized snapshot, so caching remains isolated and invalidates after fixture changes.
