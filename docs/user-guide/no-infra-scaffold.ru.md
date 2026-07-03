# Scaffold без инфраструктуры

Генерация минимального, готового к компиляции Playwright .NET проекта, когда у вашей команды нет существующей Playwright-инфраструктуры.

## Когда использовать scaffold

Используйте `--mode scaffold`, когда:

- У вашей команды есть Selenium C# тесты, но нет Playwright .NET проекта
- Нужна отправная точка для миграции без реверс-инжиниринга чужой инфраструктуры
- Вы хотите консистентную структуру проекта, совместимую с Migrator

## Когда НЕ использовать scaffold

Не используйте `--mode scaffold`, когда:

- У вас уже есть Playwright .NET проект с тестами, base-классами и auth → используйте `discover-target`
- Нужен готовый runtime-набор тестов — scaffold только компилируется, не гарантирует прогон
- Вы хотите, чтобы инструмент настраивал auth, маршруты или тестовые данные — это вы делаете руками

## discover-target vs scaffold

| Аспект | `discover-target` | `scaffold` |
|---|---|---|
| **Требование** | Существующий Playwright .NET проект | Нет |
| **Вход** | `--input` на целевой проект | Не требуется |
| **Вывод** | `target-inventory.json`, `adapter-config.draft.json` | Полный проект: `.csproj`, base class, конфиг, smoke test |
| **Подходит для** | Команд с существующей Playwright-инфрой | Команд, начинающих с нуля |
| **Runtime-ready** | Зависит от существующей инфы | Нет — только компиляция |

## Сгенерированные файлы

`scaffold` создаёт следующие файлы в выходной директории:

| Файл | Назначение |
|---|---|
| `*.csproj` | Проект .NET 10 с пакетами Playwright + NUnit или xUnit |
| `GeneratedTestBase.cs` | Абстрактный base-класс с `LoginAsync`, `GoToAsync`, `WaitForAppReadyAsync` |
| `TestSettings.cs` | Конфиг через переменные окружения (`E2E_BASE_URL`, `E2E_LOGIN_ROUTE`, `E2E_DEFAULT_ROUTE`) |
| `ExampleSmokeTest.cs` | Пример теста, показывающий ожидаемый стиль |
| `adapter-config.draft.json` | Draft adapter config с `RequiresReview: true` |
| `README.md` | Гайд по настройке сгенерированного проекта |
| `.gitignore` | Стандартные правила игнорирования для .NET тестовых проектов |

Дополнительно генерируются файлы отчётов:
- `scaffold-report.json` — структурированный отчёт
- `scaffold-report.md` — человекочитаемый отчёт

## Как запустить

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode scaffold --out "./new-playwright-tests"
```

Опциональные флаги:
- `--target-test-framework nunit|xunit`, `--format text|json|both` — какие файлы отчётов генерировать (по умолчанию: `both`)

Выходная директория не должна существовать или должна быть пустой. Если она существует и содержит файлы, scaffold безопасно завершается с ошибкой, не изменяя ничего.

## Что нужно заполнить вручную

Scaffold — это скелет. Вы должны:

### 1. Реализовать авторизацию

Отредактируйте `GeneratedTestBase.cs` и замените заглушку `LoginAsync` на реальный auth-флоу вашего проекта.

### 2. Настроить переменные окружения

Установите переменные окружения перед запуском тестов:

```bash
$env:E2E_BASE_URL="https://your-test-env.example.com"
$env:E2E_LOGIN_ROUTE="/login"
$env:E2E_DEFAULT_ROUTE="/dashboard"
```

### 3. Заменить placeholder-маршруты

Отредактируйте `TestSettings.cs` и замените `<test-login>` и `<ROUTE_SOURCE_TRUTH_REQUIRED>` на реальные значения из вашего приложения.

### 4. Проверьте и заполните adapter-config.draft.json

Draft конфиг имеет `RequiresReview: true`. Заполните:
- `SourceProjectName` — название вашего Selenium проекта
- `UiTargets` — маппинги селекторов из source truth
- `PageObjects` — объявления page objects

### 5. Установите Playwright браузеры

```bash
dotnet build
pwsh bin/Debug/net10.0/playwright.ps1 install
```

## Почему runtime pass не гарантируется

Scaffold предоставляет только каркас инфраструктуры. Runtime-тесты требуют:

- Реального тестового окружения (`E2E_BASE_URL`)
- Работающего auth-флоу
- Валидных тестовых данных
- Конфигурации маршрутов вашего проекта
- Маппингов селекторов из source truth в `adapter-config.draft.json`

Scaffold не может и не должен предоставлять ни одно из этого.

## Использование adapter-config.draft.json после проверки

После того как вы проверили и заполнили draft конфиг:

1. Скопируйте или переименуйте в `adapter-config.json`
2. Запустите миграцию:

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode migrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./generated" --format both
```

3. Запустите проверку:

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode verify --input "./generated" --config "./adapter-config.json" --out "./verify" --format both
```

4. Скопируйте сгенерированные файлы в сгенерированный проект и запустите compile smoke:

```bash
dotnet build
```

## Два пути

```
Путь A: Есть Playwright-инфра
  discover-target → проверить конфиг → orchestrate

Путь B: Нет Playwright-инфры
  scaffold → реализовать auth/routes → проверить конфиг → migrate → verify
```

Подробнее: [Процесс миграции](migration-workflow.md).
