# Project-aware verification (`verify-project`)

`verify` быстро проверяет generated C# изолированно через Roslyn. Это полезный smoke, но он не видит сборки исходного проекта: `ArBilling.Infrastructure`, enum/static helpers, shared fixtures, DTO и другие project references.

`verify-project` решает эту проблему безопасно: он создаёт временный verification project под `--out`, подключает generated files и references из `adapter-config.json`, затем запускает `dotnet build`. Исходный проект не меняется.

## Команда

```powershell
dotnet run --project .\Migrator.Cli -- `
  --mode verify-project `
  --input "C:\path\to\SeleniumTests\DiscountsTests" `
  --config "adapter-config.json" `
  --out "migrate_discounts_project_verify" `
  --format both
```

Результат:

```text
migrate_discounts_project_verify/
  generated/                         # generated Playwright files
  project-verify/
    Generated.Playwright.Verify.csproj # temporary harness project
  project-verify-report.json
  project-verify-report.md
```

## `adapter-config.json`

Минимальный вариант — вообще не задавать `Verification`: режим попробует найти ближайший `.csproj` вверх от `--input` и подключить его как `ProjectReference`.

Для стабильного сценария лучше явно настроить:

```json
{
  "Verification": {
    "TargetFramework": "net8.0",
    "BaseDirectory": "C:/Users/levenskikh/Desktop/billy",
    "ProjectReferences": [
      "Web/MarketerWeb/MarketerWeb.UIFunctionalTests/MarketerWeb.UIFunctionalTests.csproj",
      "Web/ArBilling.Infrastructure/ArBilling.Infrastructure.csproj"
    ],
    "PackageReferences": [
      { "Include": "Microsoft.Playwright.NUnit", "Version": "1.52.0" },
      { "Include": "NUnit", "Version": "3.14.0" }
    ],
    "AutoDiscoverNearestProject": true,
    "NoRestore": false,
    "Configuration": "Debug"
  }
}
```

Поля:

- `TargetFramework` — target framework временного проекта, по умолчанию `net8.0`.
- `BaseDirectory` — база для относительных путей в `ProjectReferences`/`AssemblyReferences`.
- `ProjectReferences` — реальные `.csproj`, которые нужны generated code.
- `PackageReferences` — дополнительные NuGet-пакеты. По умолчанию добавляются `Microsoft.Playwright.NUnit` и `NUnit`, если `DisableDefaultPackageReferences` не `true`.
- `AssemblyReferences` — fallback для прямых dll references, использовать только если нельзя через project/package references.
- `AutoDiscoverNearestProject` — если `ProjectReferences` пустой, найти ближайший `.csproj` вверх от `--input`. По умолчанию `true`.
- `NoRestore` — добавить `--no-restore` к `dotnet build`.
- `Configuration` — конфигурация сборки, по умолчанию `Debug`.

## Правила для агента

1. Не редактировать исходный проект ради `verify-project`.
2. Не копировать generated files в реальный проект.
3. Не добавлять dummy declarations ради зелёной сборки.
4. Если build падает из-за missing reference, добавлять reference в `Verification.ProjectReferences` или `Verification.PackageReferences`.
5. Если build падает из-за unmapped/source-only code, возвращаться к `adapter-config` mappings или оставлять TODO.
6. Если требуется новый generic механизм мигратора — остановиться и написать escalation report.
