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
    "BaseDirectory": "C:/Users/levenskikh/Desktop/billy",
    "BuildWorkingDirectory": "C:/Users/levenskikh/Desktop/billy",
    "Solution": "ArBilling.sln",
    "ProjectReferences": [
      "Web/MarketerWeb/MarketerWeb.UIFunctionalTests/MarketerWeb.UIFunctionalTests.csproj",
      "Web/ArBilling.Infrastructure/ArBilling.Infrastructure.csproj"
    ],
    "PackageReferences": [
      { "Include": "Microsoft.Playwright.NUnit", "Version": "1.52.0" },
      { "Include": "NUnit", "Version": "4.2.2" }
    ],
    "AutoDiscoverNearestProject": true,
    "AutoDiscoverProjectReferences": true,
    "AutoDiscoverBuildFiles": true,
    "AutoDiscoverPackageReferences": false,
    "NoRestore": false,
    "Configuration": "Debug"
  }
}
```

Поля:

- `TargetFramework` — target framework временного проекта. Если не задан, берётся из первого найденного `.csproj`; fallback — `net8.0`.
- `BaseDirectory` — база для относительных путей в `ProjectReferences`/`AssemblyReferences`/`Solution`.
- `BuildWorkingDirectory` — рабочая директория для `dotnet build`; полезно указывать корень repo, чтобы подхватился `NuGet.config` внутреннего фида.
- `Solution` — опциональный `.sln` для discovery/report context. Source solution не меняется.
- `ProjectReferences` — реальные `.csproj`, которые нужны generated code.
- `PackageReferences` — дополнительные NuGet-пакеты. По умолчанию добавляются framework-specific пакеты по `TestHost.TargetTestFramework`: NUnit (`Microsoft.Playwright.NUnit`, `NUnit`, `NUnit3TestAdapter`) или xUnit (`Microsoft.Playwright.Xunit`, `xunit`, `xunit.runner.visualstudio`), если `DisableDefaultPackageReferences` не `true`.
- `AssemblyReferences` — fallback для прямых dll references, использовать только если нельзя через project/package references.
- `AutoDiscoverNearestProject` — найти ближайший `.csproj` вверх от `--input` и подключить как `ProjectReference`. По умолчанию `true`.
- `AutoDiscoverProjectReferences` — рекурсивно подключить `ProjectReference` из найденных/заданных `.csproj`. По умолчанию `true`.
- `AutoDiscoverBuildFiles` — импортировать найденные `Directory.Build.props`, `Directory.Packages.props`, `Directory.Build.targets` во временный проект. По умолчанию `true`.
- `AutoDiscoverPackageReferences` — зеркалировать `PackageReference` из project references во временный проект. По умолчанию `false`, потому что `ProjectReference` обычно уже тянет пакеты.
- `NoRestore` — добавить `--no-restore` к `dotnet build`.
- `Configuration` — конфигурация сборки, по умолчанию `Debug`.

## Правила для агента

1. Не редактировать исходный проект ради `verify-project`.
2. Не копировать generated files в реальный проект.
3. Не добавлять dummy declarations ради зелёной сборки.
4. Если build падает из-за missing reference, добавлять reference в `Verification.ProjectReferences` или `Verification.PackageReferences`.
5. Если build падает из-за unmapped/source-only code, возвращаться к `adapter-config` mappings или оставлять TODO.
6. Если требуется новый generic механизм мигратора — остановиться и написать escalation report.


## Workspace

Относительный `--out` автоматически создаётся внутри `migration/`.

```powershell
dotnet run --project .\Migrator.Cli -- --mode verify-project --input "C:\path\to\Tests" --config "adapter-config.json" --out "verify-project-1" --format both
```

Результат будет в:

```text
migration/verify-project-1/
  generated/
  project-verify/
  project-verify-report.json
  project-verify-report.md
```

## Зрелый project-aware режим

`verify-project` теперь делает больше, чем просто создаёт временный `.csproj`:

- если `TargetFramework` не задан, пытается взять его из найденного `.csproj`;
- если `AutoDiscoverNearestProject=true`, подключает ближайший `.csproj` вверх от `--input`;
- если `AutoDiscoverProjectReferences=true`, рекурсивно подключает `ProjectReference` из найденных/заданных проектов;
- если `AutoDiscoverBuildFiles=true`, импортирует найденные `Directory.Build.props`, `Directory.Packages.props`, `Directory.Build.targets` во временный verification project;
- `BuildWorkingDirectory` позволяет запускать `dotnet build` из корня исходного repo, чтобы подхватился корпоративный `NuGet.config`;
- `project-verify-report.md/json` теперь содержит discovery summary и классификацию diagnostics.

## Классификация diagnostics

Отчёт разделяет ошибки на понятные категории:

| Категория | Обычно значит | Что делать |
|---|---|---|
| `unknown-identifier` | generated code ссылается на неизвестный symbol | проверить source-only leak, `TargetKnownIdentifiers`, target locals |
| `missing-type-or-namespace` | не найден type/namespace | добавить `ProjectReference`/`PackageReference` или проверить using |
| `missing-namespace-member` | namespace найден частично | проверить references и реальный namespace |
| `missing-member` | тип есть, но метода/свойства нет | проверить mapping и target helper API |
| `signature-mismatch` | метод найден, но параметры не совпали | проверить `ParameterizedMethodMapping` placeholders |
| `nuget-restore` | restore/package source issue | указать `BuildWorkingDirectory` на repo root с `NuGet.config` |
| `msbuild-project` | проблема MSBuild/props/targets/project | проверить imports, `TargetFramework`, references |

Агент должен сначала смотреть `Diagnostic categories` и `Classified diagnostics`, а не только raw `StdOut`.

## Внутренний NuGet и Central Package Management

Если в репозитории есть корпоративный `NuGet.config`, укажи:

```json
{
  "Verification": {
    "BuildWorkingDirectory": "C:/path/to/source/repo"
  }
}
```

Так `dotnet build` временного verification project будет запускаться из корня repo и сможет увидеть внутренние package sources.

Если найден `Directory.Packages.props`, temporary project импортирует его. Для пакетов, у которых версия уже задана через `PackageVersion`, `verify-project` не пишет `Version` в `PackageReference`, чтобы не конфликтовать с Central Package Management.
