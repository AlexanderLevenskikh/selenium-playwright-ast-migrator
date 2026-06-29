# Public launch demo

This folder is a tiny, copyable demo repository for the public preview.

It contains:

```text
examples/public-launch-demo/
  selenium-tests/LoginSmokeTest.cs
  adapter-config.json
  playwright-migrated/LoginSmokePlaywright.generated.cs
  reports/before-after-report.md
```

The demo is intentionally small. It shows the core public workflow without requiring a private application repository.

## Run the demo

From the repository root:

```bash
selenium-pw-migrator --mode doctor \
  --input examples/public-launch-demo/selenium-tests \
  --config examples/public-launch-demo/adapter-config.json \
  --out public-launch-doctor

selenium-pw-migrator --mode migrate \
  --input examples/public-launch-demo/selenium-tests \
  --config examples/public-launch-demo/adapter-config.json \
  --out public-launch-demo \
  --format both

selenium-pw-migrator --mode verify \
  --input examples/public-launch-demo/selenium-tests \
  --config examples/public-launch-demo/adapter-config.json \
  --out public-launch-verify \
  --format both
```

Expected review flow:

1. `doctor` validates the input/config shape.
2. `migrate` writes generated Playwright output and reports under `migration/public-launch-demo/`.
3. `verify` checks generated code shape and emits review artifacts.
4. The reviewer compares the result with [`playwright-migrated/LoginSmokePlaywright.generated.cs`](playwright-migrated/LoginSmokePlaywright.generated.cs) and [`reports/before-after-report.md`](reports/before-after-report.md).

## What this demo proves

- Selenium PageObject expressions can be mapped to Playwright locators through adapter config.
- Generated code remains reviewable and line-attributed.
- Remaining TODOs should be treated as source-truth questions, not hidden.
- The migration report gives reviewers a concrete before/after summary.
