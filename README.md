# Selenium Playwright Migrator — Core alpha

Extracted engine snapshot from [selenium-playwright-ast-migrator](https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator), base commit `b93cd6bc86767725e057e06e3cb7067305c24a81`.

This branch is an **export-ready root tree** for a future `selenium-playwright-migrator-core` repository. It does not alter the original `master`. The GitHub connection cannot create repositories, so this is staged in the original repository's `split/core-alpha` branch until the destination repository exists.

## Included

- Original domain models, Roslyn parser, Selenium C# adapter, and Playwright .NET renderer (unchanged from the pinned base).
- `Migrator.Application`: a small direct .NET API with `Analyze`, `Preview` and `Explain`.
- `Migrator.Application.Smoke`: regression smoke for multiple fixtures, unsupported xUnit source, normal NUnit input, and stale analysis.
- A GitHub Actions build/smoke workflow.

The alpha intentionally **fails closed** on multiple fixtures in one file instead of silently generating only the first. It counts xUnit `Fact/Theory` declarations but does not claim to migrate them. It returns incomplete status for unsupported shapes.

## Development

```sh
dotnet restore Migrator.Application.Smoke/Migrator.Application.Smoke.csproj
dotnet run --project Migrator.Application.Smoke/Migrator.Application.Smoke.csproj
```

Basic direct API:

```csharp
var engine = new Migrator.Application.MigrationEngine();
var analysis = engine.Analyze("./SeleniumTests", "./adapter-config.json");
var preview = engine.Preview("./SeleniumTests", "./adapter-config.json", analysis.AnalysisId);
var explanation = engine.Explain("./SeleniumTests", analysis.Findings[0].Id,
    "./adapter-config.json", analysis.AnalysisId);
```

Output is read-only and generated as memory-only candidates; no source/config changes are applied. Compiling/running a generated target is **not** included in this alpha, and preview is not itself verification. Full MSBuild evaluation, exact source spans, xUnit input and behavior evidence remain roadmap work.

## Package boundary

The engine consists of several C# projects with project references. A future package release should publish version-matched domain/implementation dependencies, not only a single application nupkg. Do not claim this branch is already published to NuGet.

License: MIT, as in the source repository.

## Preview NuGet packaging

The Core alpha packages **all five** version-matched modules, rather than shipping only a facade with missing dependencies. Their package versions come from `Directory.Build.props`. The `PackageConsumer.Smoke` project has no project references; GitHub Actions builds it against the generated NuGet feed.

Use `dotnet pack` on all five projects, then point a consumer to the produced `artifacts/nuget` feed. The CI workflow uploads the packages as a build artifact. These are preview test artifacts, **not published NuGet releases**.
