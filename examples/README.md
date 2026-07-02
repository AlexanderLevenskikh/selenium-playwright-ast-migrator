# Examples

Sample inputs, configs, and outputs demonstrating Migrator usage.

## Simple end-to-end example

`simple/` is the minimal example:

- `input/SimpleSeleniumTest.cs` — sample Selenium C# / NUnit source test.
- `adapter-config.json` — basic source-truth UiTarget mappings.
- `expected/SimplePlaywright.generated.cs` — expected Playwright .NET output.
- `report.example.txt` — example analyze report.

Walkthrough: [`docs/examples/end-to-end-simple.md`](../docs/examples/end-to-end-simple.md).

Run from the repository root:

```bash
selenium-pw-migrator --mode migrate \
  --input examples/simple/input \
  --config examples/simple/adapter-config.json \
  --out examples-simple-generated \
  --format both
```

The output is written to `migration/examples-simple-generated` by default.

## Public demo and guided tutorial

`public-demo/` is the current product demo:

- `selenium-csharp-nunit/` — Selenium C# / NUnit input.
- `selenium-csharp-xunit/` — Selenium C# / xUnit input.
- `configs/` — reviewed adapter configs for NUnit and xUnit.
- `playwright-dotnet-nunit/` — expected generated Playwright .NET / NUnit output.
- `playwright-dotnet-xunit/` — expected generated Playwright .NET / xUnit output.
- `dashboard/` — static `report serve` sample and "what good looks like" checklist.

Walkthrough: [`docs/public-demo-tutorial.md`](../docs/public-demo-tutorial.md).

Run the one-command onboarding demo from the repository root:

```bash
selenium-pw-migrator init --wizard \
  --source examples/public-demo/selenium-csharp-xunit \
  --target dotnet \
  --target-test-framework xunit \
  --workspace migration/public-demo-xunit \
  --test-id-attribute data-testid \
  --install-kit
```

## Profile examples

Profile examples are useful when building project-specific config layers:

- `profiles/layering/` — infrastructure + project-specific layered config.
- `profiles/profile-match/` — profile reuse scoring example.
- `profiles/widget-pilot/` — simple page test pilot profile.
- `profiles/catalog-principals-pilot/` — table/list mappings and scope config.
- `profiles/registry-pilot/` — complex page with method mappings.
- `profiles/batch-migration/` — batch migration config for larger test sets.

## Other supporting examples

- `github-actions/migration-pilot.yml` — CI example.
- `tool-manifest/dotnet-tools.json` — local dotnet tool manifest example.
- `extensibility/mini-source-target/` — small extension sample.
