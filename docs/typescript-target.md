# Playwright TypeScript target

Milestone 13 adds an experimental `Selenium C# -> Playwright TypeScript` target.

The migrator can generate `.spec.ts` files without a TypeScript project.
A real Playwright TS project is required only for project-aware verification/runtime
checks, because imports, fixtures, helpers, `tsconfig`, package dependencies and
Playwright runtime conventions must come from that project.

## Migrate to TypeScript

```powershell
selenium-pw-migrator `
  --mode migrate `
  --target ts `
  --input "C:\path\to\selenium-csharp-tests" `
  --config "profiles\infrastructure-base.adapter.json" `
  --config "profiles\projects\discounts-ts.adapter.json" `
  --out "discounts-ts-migrate" `
  --format both
```

`migrate --target ts` can generate files without a TS project. Use `verify-ts-project --ts-project <path>` when you want project-aware TypeScript verification.

## Verify generated TS inside the real TS project

```powershell
selenium-pw-migrator `
  --mode verify-ts-project `
  --input "migration\discounts-ts-migrate" `
  --ts-project "C:\path\to\playwright-ts-project" `
  --out "discounts-ts-verify" `
  --format both
```

The command creates a temporary `tsconfig.migrator.json`, copies generated `.spec.ts`
files into the migration workspace and runs:

```powershell
npx tsc -p <generated tsconfig> --noEmit
```

It does not modify the TS project.

## Config/profile guidance

For TS migrations, prefer a TS-specific project profile layer. Do not reuse .NET
`TargetStatements` blindly if they contain C# Playwright code.

Good target-specific mapped statement:

```json
{
  "SourceMethod": "WaitVisible",
  "TargetStatements": [
    "await Assertions.Expect({TARGET}).ToBeVisibleAsync();"
  ],
  "Targets": {
    "playwright-typescript": {
      "TargetStatements": [
        "await expect({TARGET}).toBeVisible();"
      ]
    }
  }
}
```

`TargetStatements` remains the legacy/default fallback, usually for `playwright-dotnet`.
When `Targets.playwright-typescript.TargetStatements` is present, the TS renderer uses it directly instead of trying to translate C# Playwright statements.

If a legacy/default statement is not TS-safe and no TS override exists, the renderer emits:

```ts
// TODO: ... [MIGRATOR:TS_MAPPING_REQUIRED]
```

The agent should then add a TS profile override or leave the statement for manual migration.
