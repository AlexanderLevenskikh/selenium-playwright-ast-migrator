# Установка мигратора как dotnet tool

## Local tool для проекта

Рекомендуемый способ:

```powershell
dotnet new tool-manifest

dotnet tool install SeleniumPlaywrightAstMigrator `
  --version 0.6.0-preview.1 `

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

## Global tool

Подходит для личной машины разработчика:

```powershell
dotnet tool install --global SeleniumPlaywrightAstMigrator `
  --version 0.6.0-preview.1 `

```

Запуск:

```powershell
selenium-pw-migrator --help
```

## Обновление версии

Local tool:

```powershell
dotnet tool update SeleniumPlaywrightAstMigrator `
  --version 0.6.0-preview.2 `

```

Global tool:

```powershell
dotnet tool update --global SeleniumPlaywrightAstMigrator `
  --version 0.6.0-preview.2 `

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
./scripts/smoke-local-tool-package.ps1 -Version 0.6.0-preview.1
```

Linux/macOS:

```bash
scripts/smoke-local-tool-package.sh 0.6.0-preview.1
```

Это проверяет не source-run, а именно установленный `.nupkg`: `--help`, `--mode doctor` и запись `doctor-report.md`.
