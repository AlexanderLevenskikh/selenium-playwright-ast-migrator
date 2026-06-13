# Быстрый старт

Попробуйте Migrator на небольшом наборе Selenium тестов за 10-15 минут.

## Требования

- .NET 8 SDK
- Небольшая директория с Selenium C# / NUnit тестами
- (Опционально) `adapter-config.json` с проектно-специфичными маппингами

## 1. Подготовьте входные данные

Выберите 1-5 тестовых файлов из вашего Selenium проекта. Начните с малого — простой тест на странице идеален.

```bash
mkdir -p my-selenium-tests
# скопируйте ваши .cs файлы сюда
```

## 2. Запустите analyze

Analyze показывает, что мигратор понимает в ваших тестах:

```bash
dotnet run --project Migrator.Cli -- --mode analyze --input "./my-selenium-tests" --out "./analysis" --format both
```

**Что смотреть:**
- `analysis/report.json` — статистика по файлам
- `analysis/report.txt` — человекочитаемое резюме
- `analysis/unmapped-targets.json` — элементы, которым нужны маппинги
- `analysis/unsupported-actions.json` — действия, которые инструмент не может конвертировать

## 3. Создайте или проверьте adapter config

Создайте `adapter-config.json` с маппингами для самых частых элементов:

```json
{
  "SourceProjectName": "Example.E2ETests",
  "UiTargets": [
    {
      "SourceExpression": "page.Name",
      "TargetExpression": "Наименование",
      "TargetKind": "Text"
    },
    {
      "SourceExpression": "page.SubmitButton",
      "TargetExpression": "t_submit",
      "TargetKind": "TestId"
    }
  ],
  "PageObjects": [],
  "Methods": []
}
```

Если у вас уже есть Playwright проект, запустите `discover-target` для автогенерации draft-конфига:

```bash
dotnet run --project Migrator.Cli -- --mode discover-target --input "./playwright-projects" --out "./discovery"
```

Проверьте `discovery/adapter-config.draft.json` перед использованием.

## 4. Запустите migrate

Migrate генерирует Playwright C# код:

```bash
dotnet run --project Migrator.Cli -- --mode migrate --input "./my-selenium-tests" --config "./adapter-config.json" --out "./generated" --format both
```

**Что вы получаете:**
- `generated/` — Playwright C# файлы (например, `WidgetPlaywright.cs`)
- `generated/report.json` — статистика конвертации с количеством сгенерированных файлов

## 5. Запустите verify

Verify проверяет качество сгенерированного кода:

```bash
dotnet run --project Migrator.Cli -- --mode verify --input "./generated" --config "./adapter-config.json" --out "./verify" --format both
```

**Что смотреть:**
- `verify/verify-report.json` — синтаксические ошибки, TODO, статус quality gates
- `verify/verify-report.txt` — человекочитаемое резюме с issue по файлам

## 6. Запустите propose

Propose предлагает улучшения профиля на основе артефактов миграции:

```bash
dotnet run --project Migrator.Cli -- --mode propose --input "./generated" --config "./adapter-config.json" --out "./proposals" --format both
```

**Что вы получаете:**
- `proposals/mapping-proposals.md` — ранжированные предложения с suggested config
- `proposals/mapping-proposals.json` — структурированные предложения

## 7. Результат

Ключевые метрики каждого этапа:

| Этап | Ключевой вывод | Что показывает |
|---|---|---|
| Analyze | `unmapped-targets.json` | Какие элементы нуждаются в конфигах |
| Migrate | `generated/report.json` | Сколько файлов сгенерировано |
| Verify | `verify-report.json` | Качество кода: синтаксис, TODO, gate |
| Propose | `mapping-proposals.md` | Какие конфиги добавить дальше |

## Альтернатива: один вызов orchestrate

Вместо запуска каждого этапа отдельно, используйте режим `orchestrate`:

```bash
dotnet run --project Migrator.Cli -- --mode orchestrate --input "./my-selenium-tests" --config "./adapter-config.json" --out "./orchestration" --format both
```

Запускает все четыре этапа подряд и выдает единый отчёт:
- `orchestration/orchestration-report.json` — объединенные метрики
- `orchestration/orchestration-report.md` — человекочитаемое резюме
- `orchestration/analyze/`, `generated/`, `verify/`, `propose/` — артефакты этапов

## Нет существующей Playwright-инфраструктуры?

Если у вашей команды ещё нет Playwright .NET проекта, начните с режима `scaffold`:

```bash
dotnet run --project Migrator.Cli -- --mode scaffold --out "./new-playwright-tests"
```

Создаёт минимальный, готовый к компиляции Playwright .NET проект с draft adapter config.
Подробнее: [Scaffold без инфраструктуры](no-infra-scaffold.ru.md).

## Следующие шаги

- [Процесс миграции](migration-workflow.md) — полный гайд
- [Кукбук профиля](project-profile-cookbook.md) — детальный справочник по конфигам
- [Типовые рецепты](common-recipes.md) — практические решения частых паттернов
- [Scaffold без инфраструктуры](no-infra-scaffold.ru.md) — генерация Playwright проекта с нуля
