# Упаковка и распространение мигратора

Milestone 6 добавляет официальный способ распространять CLI как внутренний `dotnet tool`.

## Зачем это нужно

Пока мигратор запускается так:

```powershell
dotnet run --project .\Migrator.Cli -- --mode migrate ...
```

это удобно для разработки, но неудобно для команд: нужно клонировать репозиторий, собирать проект и помнить путь до CLI.

После упаковки команда получает обычную команду:

```powershell
selenium-pw-migrator --mode migrate --input "..." --config "..." --out "..."
```

или через local tool:

```powershell
dotnet tool run selenium-pw-migrator -- --mode migrate --input "..." --config "..." --out "..."
```

## Что упаковывается

Упаковывается проект:

```text
Migrator.Cli/Migrator.Cli.csproj
```

В `csproj` включены свойства:

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>selenium-pw-migrator</ToolCommandName>
<PackageId>SeleniumPlaywrightAstMigrator</PackageId>
```

`ToolCommandName` — имя команды после установки.

`PackageId` можно переопределить при pack, если внутренний NuGet требует корпоративный namespace:

```powershell
/p:PackageId=Company.SeleniumPlaywrightMigrator
```

## Локальная упаковка

```powershell
.\scripts\pack-tool.ps1 -Version 0.6.0-preview.1
```

Или вручную:

```powershell
dotnet pack .\Migrator.Cli\Migrator.Cli.csproj `
  -c Release `
  -o .\artifacts\nuget `
  /p:Version=0.6.0-preview.1
```

Результат:

```text
artifacts/nuget/SeleniumPlaywrightAstMigrator.0.6.0-preview.1.nupkg
```

## Проверка пакета без публикации

Через временный локальный source:

```powershell
dotnet new tool-manifest --force

dotnet tool install SeleniumPlaywrightAstMigrator `
  --version 0.6.0-preview.1 `
  --add-source .\artifacts\nuget

dotnet tool run selenium-pw-migrator -- --help
```

Или скриптом:

```powershell
.\scripts\install-local-tool.ps1 -Version 0.6.0-preview.1
```

## Публикация во внутренний NuGet

Если source уже есть в `NuGet.config`:

```powershell
.\scripts\push-tool.ps1 `
  -Version 0.6.0-preview.1 `
  -Source company-nuget `
  -ApiKey $env:NUGET_API_KEY
```

Или вручную:

```powershell
dotnet nuget push .\artifacts\nuget\SeleniumPlaywrightAstMigrator.0.6.0-preview.1.nupkg `
  --source company-nuget `
  --api-key $env:NUGET_API_KEY
```

В некоторых корпоративных NuGet API key может быть фиктивным, а авторизация идёт через credential provider. Тогда используйте правила вашего внутреннего feed.

## Установка из внутреннего NuGet

Глобально:

```powershell
dotnet tool install --global SeleniumPlaywrightAstMigrator `
  --version 0.6.0-preview.1 `
  --add-source company-nuget
```

В репозитории проекта лучше использовать local tool manifest:

```powershell
dotnet new tool-manifest

dotnet tool install SeleniumPlaywrightAstMigrator `
  --version 0.6.0-preview.1 `
  --add-source company-nuget
```

После этого в репозитории появится:

```text
.config/dotnet-tools.json
```

Версия инструмента будет закреплена для проекта.

## Рекомендуемый способ для команд

Для миграции проектов лучше local tool, а не global install:

```powershell
dotnet tool restore

dotnet tool run selenium-pw-migrator -- `
  --mode verify-project `
  --input "Tests/DiscountsTests" `
  --config "profiles/infrastructure-base.adapter.json" `
  --config "profiles/projects/discounts.adapter.json" `
  --out "discounts-verify-project" `
  --format both
```

Плюсы local tool:

- версия фиксируется в репозитории;
- разные проекты могут использовать разные версии мигратора;
- CI воспроизводимее;
- не нужно просить пользователей ставить tool глобально.

## Что не решает упаковка

NuGet/tool решает доставку CLI, но не решает project-aware compilation сам по себе.

Для реальной проверки всё равно нужен `Verification` в `adapter-config`:

```json
{
  "Verification": {
    "BaseDirectory": "C:/repo/project",
    "BuildWorkingDirectory": "C:/repo/project",
    "ProjectReferences": [
      "Web/MarketerWeb/MarketerWeb.UIFunctionalTests/MarketerWeb.UIFunctionalTests.csproj"
    ],
    "AutoDiscoverNearestProject": true,
    "AutoDiscoverProjectReferences": true,
    "AutoDiscoverBuildFiles": true
  }
}
```

## Правила безопасности публикации

- Не коммитьте токены/API keys.
- Не храните реальные credentials в `NuGet.config.template`.
- Публикуйте preview-версии с суффиксом `-preview.N`, пока формат CLI/config меняется.
- Перед публикацией запускайте `dotnet test --no-restore`.
- Перед публикацией проверяйте локальную установку из `artifacts/nuget`.
- Не публикуйте пакет, если `config-validate` / `guard` показывают регрессии на пилотном проекте.
