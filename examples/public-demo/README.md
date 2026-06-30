# Public demo: Selenium C# → Playwright .NET

This is the smallest public, copyable demo for the Migrator growth wave. It shows both supported C# target test frameworks:

- Selenium C# / NUnit → Playwright .NET / NUnit
- Selenium C# / xUnit → Playwright .NET / xUnit

The demo deliberately keeps the application fake and the test slice tiny. The point is to show the migration workflow, review artifacts, and safety posture without depending on a private product repository.

## Folder map

```text
examples/public-demo/
  selenium-csharp-nunit/                 # source NUnit test input
  selenium-csharp-xunit/                 # source xUnit test input
  configs/                               # reviewed adapter configs
  playwright-dotnet-nunit/               # expected generated NUnit output
  playwright-dotnet-xunit/               # expected generated xUnit output
  dashboard/                             # static report serve sample
  init-wizard/                           # expected init workspace shape
```

## One-command onboarding demo

From the repository root, generate a starter xUnit migration workspace:

```bash
selenium-pw-migrator init --wizard \
  --source examples/public-demo/selenium-csharp-xunit \
  --target dotnet \
  --target-test-framework xunit \
  --workspace migration/public-demo-xunit \
  --test-id-attribute data-testid \
  --install-kit
```

Then open `migration/public-demo-xunit/README.md` and `migration/public-demo-xunit/next-commands.md`.

The expected workspace shape is documented in [`init-wizard/expected-workspace-tree.md`](init-wizard/expected-workspace-tree.md).

## Run the NUnit demo

```bash
selenium-pw-migrator --mode config-validate \
  --config examples/public-demo/configs/adapter-config.nunit.json \
  --validation-mode production \
  --out public-demo-nunit-config

selenium-pw-migrator --mode migrate \
  --input examples/public-demo/selenium-csharp-nunit \
  --config examples/public-demo/configs/adapter-config.nunit.json \
  --target dotnet \
  --target-test-framework nunit \
  --out public-demo-nunit \
  --format both
```

Review the result against [`playwright-dotnet-nunit/LoginSmokeTestPlaywright.generated.cs`](playwright-dotnet-nunit/LoginSmokeTestPlaywright.generated.cs).

## Run the xUnit demo

```bash
selenium-pw-migrator --mode config-validate \
  --config examples/public-demo/configs/adapter-config.xunit.json \
  --validation-mode production \
  --out public-demo-xunit-config

selenium-pw-migrator --mode migrate \
  --input examples/public-demo/selenium-csharp-xunit \
  --config examples/public-demo/configs/adapter-config.xunit.json \
  --target dotnet \
  --target-test-framework xunit \
  --out public-demo-xunit \
  --format both
```

Review the result against [`playwright-dotnet-xunit/LoginSmokeFactsPlaywright.generated.cs`](playwright-dotnet-xunit/LoginSmokeFactsPlaywright.generated.cs).

## View the static dashboard example

The checked-in static dashboard is intentionally tiny and safe to open locally:

- [`dashboard/report-dashboard.html`](dashboard/report-dashboard.html)
- [`dashboard/report-dashboard.md`](dashboard/report-dashboard.md)
- [`dashboard/what-good-looks-like.md`](dashboard/what-good-looks-like.md)

To generate a fresh dashboard from a real run:

```bash
selenium-pw-migrator report serve \
  --input migration/public-demo-nunit \
  --static-only \
  --out migration/public-demo-dashboard
```

## What this demo proves

- NUnit remains the default supported C# target path.
- xUnit is first-class for config, CLI selection, scaffold, and generated output.
- The migrator does not invent selectors: locators come from reviewed adapter config.
- Remaining setup/navigation uncertainty is visible as TODOs.
- `report serve` gives reviewers a single dashboard view.
- `evidence pack` can package the run for an issue or PR.

## Suggested reviewer script

```bash
selenium-pw-migrator --mode migrate \
  --input examples/public-demo/selenium-csharp-nunit \
  --config examples/public-demo/configs/adapter-config.nunit.json \
  --target dotnet \
  --target-test-framework nunit \
  --out public-demo-nunit \
  --format both

selenium-pw-migrator report serve \
  --input migration/public-demo-nunit \
  --static-only \
  --out migration/public-demo-dashboard

selenium-pw-migrator evidence pack \
  --input migration/public-demo-nunit \
  --out migration/public-demo-evidence.zip
```
