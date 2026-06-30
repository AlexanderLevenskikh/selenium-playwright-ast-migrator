# Runtime Host Integration (TestHost Config)

## What is TestHost

`TestHost` is an optional section in `adapter-config.json` that controls how the generated
Playwright test class wraps into a real test host project. `TestHost.TargetTestFramework` selects
NUnit or xUnit rendering; when it is absent, NUnit is used for backward compatibility. With
`TestHost`, the renderer generates a project-ready class with configured base class, attributes,
usings, namespace, and setup.

## Config Shape

```json
{
  "TestHost": {
    "TargetTestFramework": "nunit",
    "Namespace": "Example.E2ETests.Tests",
    "BaseClass": "TestBase",
    "ClassName": "CatalogPrincipalsFilterPlaywrightTests",
    "ClassAttributes": [
      "TestFixture",
      "Parallelizable(ParallelScope.Self)"
    ],
    "Usings": [
      "NUnit.Framework",
      "Example.E2ETests.Infrastructure"
    ],
    "SetUpStatements": [
      "await Page.GotoAsync(\"<test-login>\");",
      "await Page.GotoAsync(\"/catalogs?activeTab=principals\");"
    ]
  }
}
```

### Fields

| Field | Type | Required | Description |
|---|---|---|---|
| `TargetTestFramework` | string? | No | `nunit` or `xunit`. Default: `nunit`. |
| `Namespace` | string? | No | Target namespace. Overrides source namespace. When absent, uses source namespace + `.Playwright`. |
| `BaseClass` | string? | No | Base class for test class. Default: `PageTest`. |
| `ClassName` | string? | No | Full class name. Default: `{SourceClassName}Playwright`. |
| `ClassAttributes` | string[]? | No | C# attributes above the class declaration. |
| `Usings` | string[]? | No | Additional using directives to prepend. Framework defaults are selected from `TargetTestFramework`. |
| `SetUpStatements` | string[]? | No | C# statements inside `[SetUp]` method. Replaces original mapped setup actions (preserved as comments). |

## Why TestHost belongs in the profile, not Core/Roslyn/Renderer

- `TestHost` carries **project-specific** values: `TestBase`, `TestSettings.LoginRoute`, `Example.E2ETests` namespace.
- Hardcoding these in the renderer would make Migrator non-generic and tied to one project.
- By keeping `TestHost` in the adapter config, Migrator stays project-agnostic. Each target project
  maintains its own profile with the correct host settings.
- The config model (`TestHostConfig`) lives in Core, but only defines the contract — no values are hardcoded.

## Public vs Local profiles

### Public (sanitized) profile

Location: `examples/profiles/<profile-name>/adapter-config.json`

Contains neutral, sanitized values suitable for public sharing:
```json
"SetUpStatements": [
  "await Page.GotoAsync(\"<test-login>\");",
  "await Page.GotoAsync(\"/catalogs?activeTab=principals\");"
]
```

### Local (private) profile

Location: `profiles/<profile-name>/adapter-config.local.json`

Contains real project values:
```json
"SetUpStatements": [
  "await Page.GotoAsync(TestSettings.LoginRoute);",
  "await Page.GotoAsync(\"/catalogs?activeTab=principals\");"
]
```

The `.local.json` pattern is gitignored (`*.local.json` in `.gitignore`). Never commit real
URLs, auth credentials, or internal project names.

## Generated Output Example

With `TestHost` configured, a generated file looks like:

```csharp
using NUnit.Framework;
using Example.E2ETests.Infrastructure;

namespace Example.E2ETests.Tests;

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class CatalogPrincipalsFilterPlaywrightTests : TestBase
{
    [SetUp]
    public async Task SetUp()
    {
        await Page.GotoAsync(TestSettings.LoginRoute);
        await Page.GotoAsync("/catalogs?activeTab=principals");

        // Original Selenium setup (mapped):
        //   var pagef = Navigation.OpenCatalogPrincipalPage()
        //   pagef.Loader.ValidateLoading()
    }

    [Test]
    public async Task CheckActivityToPrincipals()
    {
        // Generated test body...
    }
}
```

## Setup Priority Rule

When `SetUpStatements` is provided:
- The configured statements become the `[SetUp]` body.
- Original parsed setup actions are **not silently dropped** — they are preserved as
  commented lines after the configured statements, under the `// Original Selenium setup (mapped):` header.
- This avoids double navigation while keeping the original source visible for reference.

## Known Limitations

1. **Test body still needs manual review**: `TestHost` only controls the class wrapper.
   Generated test bodies still carry `// TODO: mapped method requires manual review` for
   methods that couldn't be mapped cleanly. The adapter config's `TargetStatements` control
   how methods are mapped, but real DOM interactions may need runtime-specific adjustments
   (e.g., `GetByText("Наименование")` instead of `[data-test-id='t_principals_principal']`).

2. **Generated files are project-specific when TestHost is enabled**: The output is tied to
   the configured host's namespaces, usings, and base class. This is intentional — the goal
   is to produce a file that is close to ready for the target project.

3. **No auto-discovery of host settings**: The user must manually configure `TestHost` in the
   adapter profile. Migrator does not inspect the target project to infer these values.
