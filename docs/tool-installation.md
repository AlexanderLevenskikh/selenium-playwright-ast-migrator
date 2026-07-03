# Установка мигратора как dotnet tool

## Самый быстрый старт из NuGet

Если пакет уже опубликован в NuGet, лучше закрепить версию в local tool manifest проекта:

```powershell
dotnet new tool-manifest

dotnet tool install SeleniumPlaywrightMigrator `
  --version 0.0.0

dotnet tool run selenium-pw-migrator -- --help
```

Дальше можно запустить disposable playground:

```powershell
dotnet tool run selenium-pw-migrator -- playground `
  --out playground `
  --target-test-framework xunit `
  --generation-policy conservative
```

## Local tool для проекта

Рекомендуемый способ:

```powershell
dotnet new tool-manifest

dotnet tool install SeleniumPlaywrightMigrator `
  --version 0.0.0
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
./scripts/pack-tool.ps1 -Version 0.0.0

dotnet new tool-manifest --force

dotnet tool install SeleniumPlaywrightMigrator `
  --version 0.0.0 `
  --add-source ./artifacts/nuget

dotnet tool run selenium-pw-migrator -- --help
```

## Global tool

Подходит для личной машины разработчика:

```powershell
dotnet tool install --global SeleniumPlaywrightMigrator `
  --version 0.0.0
```

Запуск:

```powershell
selenium-pw-migrator --help
```

## Обновление версии

Local tool:

```powershell
dotnet tool update SeleniumPlaywrightMigrator `
  --version 0.0.0
```

Global tool:

```powershell
dotnet tool update --global SeleniumPlaywrightMigrator `
  --version 0.0.0
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
./scripts/smoke-local-tool-package.ps1 -Version 0.0.0
```

Linux/macOS:

```bash
scripts/smoke-local-tool-package.sh 0.0.0
```

Это проверяет не source-run, а именно установленный `.nupkg`: `--help`, `--mode doctor` и запись `doctor-report.md`.
