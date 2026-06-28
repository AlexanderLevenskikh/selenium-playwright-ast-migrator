# PROD-13 — TypeScript test host, imports, and locator declarations

## Goal

Make Playwright TypeScript output less hardcoded and more project-ready without changing the default generated shape.

The production default remains:

```ts
import { test, expect } from '@playwright/test';

test('Name', async ({ page }) => {
});
```

But the target backend now has a dedicated host layer that can emit project-specific imports, fixture parameters, and optional `test.describe` wrappers.

## Implementation

Added:

- `PlaywrightTypeScriptRenderOptions`
- `PlaywrightTypeScriptTestHostRenderer`

Both legacy and IR V2 TypeScript renderers use the same host renderer, so host behavior stays consistent between:

- `TestFileModel -> PlaywrightTypeScriptRenderer`
- `MigrationDocument -> PlaywrightTypeScriptIrV2Renderer`

## Supported options

- `ImportLines`
- `TestFunctionName`
- `FixtureParameter`
- `UseDescribe`
- `DescribeName`
- `SourceLabel`

Example:

```csharp
var backend = new PlaywrightTypeScriptBackend(new PlaywrightTypeScriptRenderOptions
{
    ImportLines = new[]
    {
        "import { authTest as test, expect } from '../fixtures/auth';",
        "import { LoginPage } from '../pages/LoginPage';"
    },
    FixtureParameter = "{ page, loginPage }",
    UseDescribe = true,
    DescribeName = "Generated auth suite"
});
```

## Safety

- Default output is preserved.
- Duplicate import lines are removed while preserving order.
- Locator declarations keep using `page.locator(...)` for raw CSS/XPath literals.
- PROD-13 tests assert the same host behavior in legacy and IR V2 paths.

## Not done yet

This is target-backend infrastructure only. Config/profile v2 is not yet wired to automatically populate these options. That should happen in a later config/profile hardening task once TS project profiles are stricter.
