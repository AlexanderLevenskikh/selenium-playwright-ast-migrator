# Public demo and guided tutorial

This tutorial is the 10-minute public path for the repository. It uses only checked-in demo files and does not depend on a private application.

Demo folder: [`examples/public-demo/`](../examples/public-demo/README.md)

## What you will see

- Selenium C# NUnit input and generated Playwright .NET NUnit output.
- Selenium C# xUnit input and generated Playwright .NET xUnit output.
- `init --wizard` onboarding into a safe migration workspace.
- A static `report serve` dashboard sample.
- A short checklist for what a healthy first migration run looks like.

## Step 1: create a starter workspace with the wizard

```bash
selenium-pw-migrator init --wizard \
  --source examples/public-demo/selenium-csharp-xunit \
  --target dotnet \
  --target-test-framework xunit \
  --workspace migration/public-demo-xunit \
  --test-id-attribute data-testid \
  --install-kit
```

Open the generated files:

```text
migration/public-demo-xunit/README.md
migration/public-demo-xunit/next-commands.md
migration/public-demo-xunit/profiles/adapter-config.json
migration/public-demo-xunit/state/run-ledger.md
```

The wizard should not silently overwrite an existing non-empty workspace. Delete or rename the demo workspace before re-running the command.

## Step 2: run a NUnit migration slice

```bash
selenium-pw-migrator --mode migrate \
  --input examples/public-demo/selenium-csharp-nunit \
  --config examples/public-demo/configs/adapter-config.nunit.json \
  --target dotnet \
  --target-test-framework nunit \
  --out public-demo-nunit \
  --format both
```

Compare the generated file with:

```text
examples/public-demo/playwright-dotnet-nunit/LoginSmokeTestPlaywright.generated.cs
```

## Step 3: run a xUnit migration slice

```bash
selenium-pw-migrator --mode migrate \
  --input examples/public-demo/selenium-csharp-xunit \
  --config examples/public-demo/configs/adapter-config.xunit.json \
  --target dotnet \
  --target-test-framework xunit \
  --out public-demo-xunit \
  --format both
```

Compare the generated file with:

```text
examples/public-demo/playwright-dotnet-xunit/LoginSmokeFactsPlaywright.generated.cs
```

## Step 4: open a dashboard

The checked-in dashboard sample is here:

```text
examples/public-demo/dashboard/report-dashboard.html
```

Generate a fresh static dashboard after a real run:

```bash
selenium-pw-migrator report serve \
  --input migration/public-demo-nunit \
  --static-only \
  --out migration/public-demo-dashboard
```

## Step 5: package evidence for review

```bash
selenium-pw-migrator evidence pack \
  --input migration/public-demo-nunit \
  --out migration/public-demo-evidence.zip
```

The zip should include a manifest and checksums, and should not include source repository files unless `--include-source` is explicitly passed.

## What good looks like

Use [`examples/public-demo/dashboard/what-good-looks-like.md`](../examples/public-demo/dashboard/what-good-looks-like.md) as the review checklist.

A good first run should have:

- zero invented selectors;
- config-backed locator mappings;
- TODOs grouped by `MIGRATOR:*` code;
- setup/navigation uncertainty isolated into a follow-up ticket;
- a dashboard and evidence pack that are safe to attach to a PR or issue.

## Related docs

- [Framework matrix](framework-matrix.md)
- [Init wizard](init-wizard.md)
- [Report serve dashboard](report-serve-dashboard.md)
- [Evidence pack](evidence-pack.md)
