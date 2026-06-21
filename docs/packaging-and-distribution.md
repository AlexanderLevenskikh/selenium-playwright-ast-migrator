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


## Standalone CLI bundle для агента

Для AI-agent migration workflow лучше не давать агенту repository source code мигратора.

Вместо этого соберите отдельную папку с:

- single-file `migrator.exe`;
- JSON schema;
- agent-facing docs;
- first prompt template;
- wrapper template.

Команда:

```powershell
.\scripts\package-agent-cli-bundle.ps1 `
  -Runtime win-x64 `
  -Output artifacts/agent-cli-bundle
```

Результат:

```text
artifacts/agent-cli-bundle/tool/
  migrator.exe
  README_AGENT_TOOL.md
  .agent-loops/kickoff-prompt.txt
  run-migrator-template.ps1
  schemas/
    adapter-config.schema.json
  .agent-loops/
    kickoff-prompt.txt
    01-autopilot-loop.md
    03-stop-policy.md
  docs/
    agent-tool-boundary.md
    autopilot-loop.md
    ...
```

Эту папку можно скопировать в целевой Playwright-проект:

```text
<target-playwright-project>/tools/migrator/
```

И дать агенту путь именно до неё:

```text
<TARGET_PLAYWRIGHT_PROJECT_PATH>\tools\migrator
```

Агент должен запускать:

```powershell
.\tools\migrator\migrator.exe --mode migrate ...
```

а не:

```powershell
dotnet run --project Migrator.Cli ...
```

Так агент работает с мигратором как с black-box CLI и не может случайно начать править renderer/parser/recognizers вместо создания тикета.

Подробнее:

- `docs/agent-tool-boundary.md`
- `docs/autopilot-loop.md`

## Windows PowerShell execution policy

Если PowerShell отказывается запускать скрипт упаковки и показывает ошибку вроде:

```text
Файл ...\scripts\package-agent-cli-bundle.ps1 не имеет цифровой подписи.
Невозможно выполнить сценарий в указанной системе.
```

Запустите скрипт с временным обходом политики выполнения только для текущего процесса:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.\scripts\package-agent-cli-bundle.ps1 `
  -Runtime win-x64 `
  -Output artifacts\agent-cli-bundle
```

Это изменение действует только в текущем окне PowerShell. После закрытия терминала системная политика выполнения останется прежней.

Если файл был скачан из интернета или распакован из архива, Windows могла пометить его как небезопасный. В таком случае сначала разблокируйте файл:

```powershell
Unblock-File .\scripts\package-agent-cli-bundle.ps1
```

Или разблокируйте все файлы в распакованной папке проекта:

```powershell
Get-ChildItem -Recurse | Unblock-File
```

Также можно запустить скрипт одной командой без предварительного изменения политики в текущей сессии:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\package-agent-cli-bundle.ps1 `
  -Runtime win-x64 `
  -Output artifacts\agent-cli-bundle
```

Для PowerShell 7 используйте:

```powershell
pwsh -ExecutionPolicy Bypass -File .\scripts\package-agent-cli-bundle.ps1 `
  -Runtime win-x64 `
  -Output artifacts\agent-cli-bundle
```

Не рекомендуется глобально включать `Unrestricted`, если вы точно не понимаете, зачем это нужно. Для запуска этого скрипта достаточно временного `Bypass` на уровне текущего процесса.
