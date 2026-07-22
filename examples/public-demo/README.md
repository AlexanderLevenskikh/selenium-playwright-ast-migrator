# Public demo: Selenium C# → Playwright .NET

This is a small, public, copyable demo for the Migrator growth phase. It now includes a tiny static fake web app, so the demo is not just a code-shaped example: the Playwright proof can open real HTML and exercise the same selectors used by the generated output.

Supported slices:

- Selenium C# / NUnit → Playwright .NET / NUnit
- Selenium C# / xUnit → Playwright .NET / xUnit
- Static HTML fake app → optional Playwright runtime proof

The fake app is intentionally lightweight: no backend, no Docker, no npm install, and no local server. It is a single checked-in HTML file with stable `data-testid` attributes.

## Folder map

```text
examples/public-demo/
  app/                                  # static fake web app opened by Playwright proof
  selenium-csharp-nunit/                 # source NUnit test input
  selenium-csharp-xunit/                 # source xUnit test input
  configs/                               # reviewed adapter configs with source-truth test ids
  playwright-dotnet-nunit/               # expected generated NUnit output
  playwright-dotnet-xunit/               # expected generated xUnit output
  playwright-dotnet-proof/               # optional runtime proof against app/index.html
  dashboard/                             # static report serve sample
  init-wizard/                           # expected init workspace shape
```

## What the fake app contains

`app/index.html` contains a small Demo Shop flow:

1. login form;
2. catalog search;
3. add-to-cart button;
4. cart total;
5. checkout;
6. order status.

The source Selenium tests use page/control objects that point to the same controls. The adapter configs map those source expressions to `GetByTestId(...)`, and the expected Playwright output uses the same ids.

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

## Optional: run the Playwright proof

The proof project is intentionally separate from the normal migrator unit tests, because installing browser binaries can be slow in CI. It is useful when recording a demo or proving that the checked-in fake app really supports the migrated selectors.

```bash
dotnet test examples/public-demo/playwright-dotnet-proof/PublicDemo.PlaywrightProof.csproj
```

The test opens `examples/public-demo/app/index.html` via `file://` and executes the same flow shown in the generated Playwright output.

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
- The migrator does not invent selectors: locators come from reviewed adapter config and `app/index.html` source-truth evidence.
- A single static app gives reviewers something concrete to open and run with Playwright.
- Remaining setup/navigation uncertainty is visible as TODOs instead of being silently guessed.
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
