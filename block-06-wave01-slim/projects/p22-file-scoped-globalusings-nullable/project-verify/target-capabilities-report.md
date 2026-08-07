# Target Capability Report

- Target: `playwright-dotnet`
- Language: `csharp`
- Framework: `playwright`
- Status: `stable`

Primary production target backend for Playwright .NET output with NUnit and xUnit target frameworks; NUnit remains the default.

## Capability matrix
| Area | Support | Details | Examples |
|---|---|---|---|
| `legacy-ir-rendering` | `strong` | Renders the mature legacy TestFileModel action model used by the production C# path. | ClickAsync; FillAsync; Expect(...).ToBeVisibleAsync |
| `ir-v2-rendering` | `strong` | IR V2 documents can be rendered through the compatibility bridge while the canonical renderer evolves. | MigrationDocument; legacy bridge |
| `project-verification` | `strong` | Generated C# can be compiled through verify-project with framework-specific package/project references. | temporary csproj; NUnit; xUnit; Microsoft.Playwright.NUnit; Microsoft.Playwright.Xunit |
| `config-driven-mappings` | `strong` | UiTargets, method mappings, table/list, navigation and wait mappings are supported through adapter-config/profile files. | UiTargets; ParameterizedMethods; Tables; NavigationUrls |
| `scaffold` | `strong` | Can generate minimal NUnit or xUnit Playwright .NET scaffolds for proof-of-compilation pilots. | --target-test-framework nunit; --target-test-framework xunit; GeneratedTestBase; ExampleSmokeTest |
| `target-test-frameworks` | `strong` | NUnit and xUnit are explicit target choices; MSTest target output is not supported yet. | TestHost.TargetTestFramework=nunit; TestHost.TargetTestFramework=xunit; --target-test-framework xunit |
| `runtime-readiness` | `basic` | The backend emits TODO/root-cause reports and smoke-plan artifacts, but it does not execute browser runtime tests itself. | smoke-plan; runtime-classify |

## Limitations
- Generated runtime correctness still depends on source-backed selectors and target project helper availability.
- MSTest target output is not supported yet; use NUnit or xUnit for Playwright .NET output.
- IR V2 direct rendering is intentionally conservative; parity with the legacy renderer is guarded by tests.

## Recommended validation
- Run verify and verify-project after each profile change.
- Use migration-quality-dashboard and migration-quality-tickets to reduce TODO categories before broad rollout.
- Run a small runtime smoke set in the real Playwright .NET project before scaling.
