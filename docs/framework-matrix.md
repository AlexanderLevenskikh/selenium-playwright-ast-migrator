# Framework Matrix

This matrix makes source and target framework support explicit. Language support and test-framework support are tracked separately: a language frontend can exist while a specific test framework is still preview or unsupported.

## C# source and Playwright .NET target

| Area | Framework | Status | Notes |
|---|---|---|---|
| Selenium C# source | NUnit | Stable | Best-covered production path with Roslyn semantic analysis and syntax fallback. |
| Selenium C# source | xUnit | Supported | xUnit test shapes are recognized and can be rendered to xUnit target output. |
| Selenium C# source | MSTest | Not supported yet | Treat as detected/unsupported until recognizers, fixtures, and renderer behavior are implemented. |
| Playwright .NET target | NUnit | Stable default | Default for backward compatibility. |
| Playwright .NET target | xUnit | Supported | Select with `--target-test-framework xunit` or `TestHost.TargetTestFramework`. |
| Playwright .NET target | MSTest | Not supported yet | No scaffold, verify-project defaults, or renderer contract yet. |

## Other source frontends and targets

| Area | Framework | Status | Notes |
|---|---|---|---|
| Selenium Java source | JUnit 4 | Experimental | Heuristic parsing without Java semantic type resolution. |
| Selenium Java source | JUnit 5 | Experimental | Heuristic parsing without Java semantic type resolution. |
| Selenium Java source | TestNG | Experimental | Heuristic parsing without Java semantic type resolution. |
| Selenium Python source | pytest | Experimental | Conservative Python AST/text recognition; validate every TODO. |
| Selenium Python source | unittest | Experimental | Conservative setup/test recognition; validate every TODO. |
| Playwright TypeScript target | `@playwright/test` | Experimental | Use `--target ts`; verify with `verify-ts-project`. |

Future Java and Python target backends should make their target framework explicit too: Playwright Java + JUnit 5/TestNG, and Playwright Python + pytest/unittest.

## Selecting a Playwright .NET target framework

NUnit remains the default:

```bash
selenium-pw-migrator --mode migrate \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --target dotnet \
  --target-test-framework nunit \
  --out migration/run-001
```

Generate xUnit output with:

```bash
selenium-pw-migrator --mode migrate \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --target dotnet \
  --target-test-framework xunit \
  --out migration/run-001
```

The same choice applies to scaffold and project verification defaults:

```bash
selenium-pw-migrator --mode scaffold \
  --target-test-framework xunit \
  --out generated-scaffold

selenium-pw-migrator --mode verify-project \
  --input migration/run-001/generated \
  --config ./adapter-config.json \
  --target-test-framework xunit \
  --out migration/run-001/project-verify
```

## Persisting the choice in config

The CLI flag writes through to the same logical setting as config:

```json
{
  "TestHost": {
    "TargetTestFramework": "xunit"
  }
}
```

Supported values are `nunit` and `xunit`. Case-insensitive aliases such as `NUnit`, `xUnit`, `n-unit`, and `x-unit` are normalized by the CLI/scaffold path. `config-validate` rejects unsupported values so framework typos do not silently produce the wrong package set.

## Verify-project package defaults

When `Verification.DisableDefaultPackageReferences` is not `true`, `verify-project` adds framework-specific default packages:

| Target framework | Default package family |
|---|---|
| NUnit | `Microsoft.NET.Test.Sdk`, `Microsoft.Playwright.NUnit`, `NUnit`, `NUnit3TestAdapter` |
| xUnit | `Microsoft.NET.Test.Sdk`, `Microsoft.Playwright.Xunit`, `xunit`, `xunit.runner.visualstudio` |

Custom `Verification.PackageReferences` are still appended after defaults. Set `DisableDefaultPackageReferences` to `true` only when the target repository or verification harness supplies its own complete test framework package set.
