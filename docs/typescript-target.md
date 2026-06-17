# Playwright TypeScript target

Milestone 13 adds an experimental `Selenium C# -> Playwright TypeScript` target.

This mode is intentionally allowed only when you have a real Playwright TS project.
The migrator can generate `.spec.ts` files, but imports, fixtures, helpers,
`tsconfig`, package dependencies and Playwright runtime conventions must come from
an existing TS project.

## Migrate to TypeScript

```powershell
selenium-pw-migrator `
  --mode migrate `
  --target ts `
  --ts-project "C:\path\to\playwright-ts-project" `
  --input "C:\path\to\selenium-csharp-tests" `
  --config "profiles\infrastructure-base.adapter.json" `
  --config "profiles\projects\discounts-ts.adapter.json" `
  --out "discounts-ts-migrate" `
  --format both
```

If `--target ts` is used without `--ts-project`, the command fails during preflight.

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

Good TS-specific mapped statement:

```json
{
  "SourceMethod": "WaitVisible",
  "TargetStatements": [
    "await {TARGET}.toBeVisible();"
  ]
}
```

If a statement is not TS-safe, the renderer emits:

```ts
// TODO: ... [MIGRATOR:TS_MAPPING_REQUIRED]
```

The agent should then add a TS profile override or leave the statement for manual migration.
