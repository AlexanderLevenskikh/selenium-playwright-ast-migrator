# Установка мигратора как dotnet tool

Если .NET SDK на машине ставить не хочется, используйте standalone-дистрибутив: [standalone-installation.ru.md](standalone-installation.ru.md).

Быстрая standalone-установка на Windows без .NET:

```powershell
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1" -OutFile $installer
& $installer
selenium-pw-migrator --version
```

Если на машине есть несколько установок, проверь приоритет командой:

```powershell
Get-Command selenium-pw-migrator -All
```


## Самый быстрый старт из NuGet

Для обычного использования установите последнюю публичную preview-версию глобально. Репозиторий клонировать не нужно:

```powershell
dotnet tool install --global SeleniumPlaywrightMigrator `
  --source https://api.nuget.org/v3/index.json `
  --prerelease

selenium-pw-migrator --help
```

Дальше можно запустить disposable playground:

```powershell
selenium-pw-migrator playground `
  --out playground `
  --target-test-framework xunit `
  --generation-policy conservative
```

## Local tool для проекта

Для командного репозитория можно закрепить фактически установленную preview-версию в local tool manifest:

```powershell
dotnet new tool-manifest

dotnet tool install SeleniumPlaywrightMigrator `
  --source https://api.nuget.org/v3/index.json `
  --prerelease
```

Запуск:

```powershell
dotnet tool run selenium-pw-migrator -- --help
```

Команда миграции:

```powershell
dotnet tool run selenium-pw-migrator -- `
  --mode migrate `
  --input "Tests/DiscountsTests" `
  --config "profiles/infrastructure-base.adapter.json" `
  --config "profiles/projects/discounts.adapter.json" `
  --out "discounts-migrate" `
  --format both
```

## Установка локально собранного `.nupkg`

Если пакет ещё не опубликован и лежит в `artifacts/nuget`, установите его через explicit source:

```powershell
./scripts/pack-tool.ps1 -Version 0.0.0-preview.1

dotnet new tool-manifest --force

dotnet tool install SeleniumPlaywrightMigrator `
  --version 0.0.0-preview.1 `
  --add-source ./artifacts/nuget

dotnet tool run selenium-pw-migrator -- --help
```

## Global tool

Подходит для личной машины разработчика:

```powershell
dotnet tool install --global SeleniumPlaywrightMigrator `
  --source https://api.nuget.org/v3/index.json `
  --prerelease
```

Запуск:

```powershell
selenium-pw-migrator --help
```

## Обновление версии

Local tool:

```powershell
dotnet tool update SeleniumPlaywrightMigrator `
  --source https://api.nuget.org/v3/index.json `
  --prerelease
```

Global tool:

```powershell
dotnet tool update --global SeleniumPlaywrightMigrator `
  --source https://api.nuget.org/v3/index.json `
  --prerelease
```

## В CI

```powershell
dotnet tool restore

dotnet tool run selenium-pw-migrator -- `
  --mode config-validate `
  --config "profiles/infrastructure-base.adapter.json" `
  --config "profiles/projects/discounts.adapter.json"
```

Если используется приватный preview-feed, положите `NuGet.config` в корень репозитория или укажите source явно в CI.

## Проверка локального пакета перед публикацией

Для релизной проверки используйте smoke из временного local tool manifest:

```powershell
./scripts/smoke-local-tool-package.ps1 -Version 0.0.0-preview.1
```

Linux/macOS:

```bash
scripts/smoke-local-tool-package.sh 0.0.0-preview.1
```

Это проверяет не source-run, а именно установленный `.nupkg`: `--help`, `--mode doctor` и запись `doctor-report.md`.
