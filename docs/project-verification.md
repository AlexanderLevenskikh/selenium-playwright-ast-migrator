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
    "BaseDirectory": "C:/path/to/product-repo",
    "BuildWorkingDirectory": "C:/path/to/product-repo",
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

- `TargetFramework` — target framework временного проекта. Если не задан, берётся из первого найденного `.csproj`; fallback — `net10.0`.
- `BaseDirectory` — база для относительных путей в `ProjectReferences`/`AssemblyReferences`/`Solution`.
- `BuildWorkingDirectory` — рабочая директория для `dotnet build`; полезно указывать корень repo, чтобы подхватился `NuGet.config` внутреннего фида.
- `Solution` — опциональный `.sln` для discovery/report context. Source solution не меняется.
- `ProjectReferences` — реальные `.csproj`, которые нужны generated code.
- `PackageReferences` — дополнительные NuGet-пакеты. По умолчанию добавляются framework-specific пакеты по `TestHost.TargetTestFramework`: NUnit (`Microsoft.Playwright.NUnit`, `NUnit`, `NUnit3TestAdapter`) или xUnit (`Microsoft.Playwright.Xunit`, `xunit`, `xunit.runner.visualstudio`), если `DisableDefaultPackageReferences` не `true`.
- `AssemblyReferences` — fallback для прямых dll references, использовать только если нельзя через project/package references.
- `AutoDiscoverNearestProject` — найти ближайший `.csproj` вверх от `--input` и подключить как `ProjectReference`. По умолчанию `true`.
- `AutoDiscoverProjectReferences` — рекурсивно подключить `ProjectReference` из найденных/заданных `.csproj`. По умолчанию `true`.
- `AutoDiscoverBuildFiles` — обнаруживать repo-wide `Directory.Build.props`, `Directory.Build.targets` и `Directory.Packages.props`. Без CPM props/targets импортируются явно. При обнаруженном CPM temporary harness намеренно пропускает repo-wide `Directory.Build.props/targets`, потому что они часто добавляют `PackageReference` без `Version`, чьи версии живут только в исходном `Directory.Packages.props`; такой гибрид вызывает `NU1015`. CPM при этом изолируется через локальный shim, `DirectoryPackagesPropsPath` и `ManagePackageVersionsCentrally=false`, чтобы inline `PackageReference Version` не падали с `NU1008`. По умолчанию `true`.
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
- если `AutoDiscoverBuildFiles=true`, обнаруживает repo-wide build files; без CPM импортирует `Directory.Build.props/targets`, а при CPM намеренно пропускает их и source `Directory.Packages.props`, пишет локальный `project-verify/Directory.Packages.props` shim и пинит `DirectoryPackagesPropsPath`, чтобы избежать одновременно `NU1008` и безверсионных `PackageReference`/`NU1015`;
- `BuildWorkingDirectory` позволяет запускать `dotnet build` из корня исходного repo, чтобы подхватился корпоративный `NuGet.config`;
- `project-verify-report.md/json` теперь содержит discovery summary, классификацию diagnostics и `verify-project-harness/v1` evidence.
- рядом с отчётом сохраняется `project-verify-harness.csproj` snapshot с SHA256, чтобы можно было доказуемо понять, какие props/targets/package references реально попали во временный harness.

## Классификация diagnostics

Отчёт разделяет ошибки на понятные категории:

| Категория | Обычно значит | Что делать |
|---|---|---|
| `unknown-identifier` | generated code ссылается на неизвестный symbol | проверить source-only leak, `TargetKnownIdentifiers`, target locals |
| `missing-type-or-namespace` | не найден type/namespace | добавить `ProjectReference`/`PackageReference` или проверить using |
| `missing-namespace-member` | namespace найден частично | проверить references и реальный namespace |
| `missing-member` | тип есть, но метода/свойства нет | проверить mapping и target helper API |
| `signature-mismatch` | метод найден, но параметры не совпали | проверить `ParameterizedMethodMapping` placeholders |
| `central-package-management` | `NU1008`: temporary harness унаследовал CPM при inline `PackageReference Version` | открыть `project-verify-harness.csproj` snapshot и `HarnessEvidence`, проверить local shim, `DirectoryPackagesPropsPath` и `ManagePackageVersionsCentrally=false` |
| `missing-package-version` | `NU1015`: repo-wide props/targets добавили `PackageReference` без версии после изоляции CPM | обновить Migrator и повторить `verify-project`; если пакет нужен generated-коду, добавить его с явной версией в `Verification.PackageReferences` |
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

Если найден `Directory.Packages.props`, `verify-project` считает это признаком Central Package Management и делает temporary harness изолированным: рядом с `Generated.Playwright.Verify.csproj` создаётся локальный `Directory.Packages.props` shim, harness пинит `DirectoryPackagesPropsPath` на этот shim и выставляет `ManagePackageVersionsCentrally=false`, а собственные `PackageReference` остаются с явными `Version`. Repo-wide `Directory.Build.props/targets` в этом режиме намеренно не импортируются: иначе они могут добавить `JetBrains.Annotations`, analyzers или внутренние пакеты без `Version`, ожидая версии из уже пропущенного source `Directory.Packages.props`, что приводит к `NU1015`. Это одновременно предотвращает `NU1008` и ложные restore-blockers временного harness.


## Кодировка вывода `dotnet build`

На Windows `dotnet` пишет перенаправленный вывод в UTF-8, но старый default `ProcessStartInfo` мог декодировать его через OEM code page (например, CP866). В отчёте это выглядело как `╨╜╨╡...`, хотя сам Markdown-файл уже был корректным UTF-8.

`verify-project` теперь явно задаёт `StandardOutputEncoding = UTF-8` и `StandardErrorEncoding = UTF-8`, а `project-verify-report.md/json` записывает как UTF-8 without BOM. Поэтому feedback/migration ZIP должен содержать читаемые русские diagnostics без постобработки.

## Verify harness evidence

Каждый свежий `verify-project` теперь пишет машинно-читаемый блок `HarnessEvidence` со схемой `verify-project-harness/v1` в `project-verify-report.json` и отдельный snapshot `project-verify-harness.csproj` рядом с отчётом.

Ключевые поля:

- `CentralPackageManagementDetected` — найден ли `Directory.Packages.props` около verification anchors;
- `CentralPackageManagementMode` — `isolated`, если CPM обнаружен и временный harness отключил его локально;
- `CentralPackageFiles` — найденные CPM-файлы;
- `ImportedBuildFiles` — фактически импортируемые `Directory.Build.props` / `.targets`;
- `SkippedBuildFiles` — намеренно пропущенные files: source `Directory.Packages.props`, а при CPM также repo-wide `Directory.Build.props/targets`;
- `DirectoryPackagesPropsPathPinned` — temporary harness пинит CPM path на локальный shim;
- `LocalDirectoryPackagesPropsShim` — путь к локальному shim-файлу рядом с temporary harness;
- `ManagePackageVersionsCentrallyDisabled` — есть ли в snapshot `<ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>`;
- `Nu1008Mitigation` — краткое объяснение выбранной защиты.

Это нужно для feedback bundles: пользователь может прислать `project-verify-report.json` и `project-verify-harness.csproj` без полного приватного repo, а maintainer сразу увидит, был ли `NU1008` проблемой migrator harness или внешнего package source.
