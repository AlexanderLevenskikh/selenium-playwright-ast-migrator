# Процесс миграции

Полный гайд по миграции Selenium C# тестов на Playwright .NET через Migrator.

## Обзор

Миграция — итеративный процесс:

```
analyze → configure profile → migrate → verify → propose → iterate
```

Цель — не один идеальный проход, а контролируемый цикл, который сходится к рабочим Playwright-тестам.

## Выбор пути

Прежде чем начать, определите, какой путь подходит вашей команде:

| Путь | Ситуация | Стартовый режим |
|---|---|---|
| **Путь A: Есть Playwright-инфра** | Есть Playwright .NET проект с тестами, base-классами и auth-флоу | `discover-target` → проверить draft конфиг → `orchestrate` |
| **Путь B: Нет Playwright-инфры** | Есть Selenium-тесты, но Playwright .NET проекта нет | `scaffold` → реализовать auth/routes → проверить draft конфиг → `migrate`/`verify` |

Подробнее о Пути B: [Scaffold без инфраструктуры](no-infra-scaffold.ru.md).

## Шаг 1. Начните с маленького пилота

Не начинайте с самого большого или сложного набора тестов.

**Рекомендуемый выбор пилота:**
- 1 файл с простым тестом на странице
- Тесты, использующие straightforward локаторы (кнопки, поля, ссылки)
- Тесты без сложной table/list логики
- Избегайте Registry-heavy, table-heavy или pagination тестов в пилоте

**Стратегия итерации:**
1. Начните с 1 файла
2. Расширьте до 5-10 тестов
3. Масштабируйте до 20-50 тестов
4. Сложные паттерны — в последнюю очередь

## Шаг 2. Запустите analyze

```bash
dotnet run --project Migrator.Cli -- --mode analyze --input "./SeleniumTests" --out "./analysis" --format both
```

Проверьте:
- `analysis/unmapped-targets.json` — какие элементы нуждаются в маппингах профиля
- `analysis/unsupported-actions.json` — какие действия требуют ручной миграции или method mappings
- `analysis/report.txt` — общую картину покрытия
- `analysis/migration-quality-dashboard.md` — root causes, guardrails и следующие безопасные quality tickets

Определите самые частые unmapped-targets и первый P0/P1 пункт в `migration-quality-tickets.md`. Они дают наибольшую отдачу от усилий по конфигу или recognizer-логике.

## Шаг 3. Добавьте mappings из source truth

Откройте `adapter-config.json` и добавьте UiTarget маппинги для топ unmapped-targets.

**Критично: ищите селекторы только в source truth.**
Source truth — это:
- C# исходный код PageObject (методы `WithDataTestId`, `WithDataTest`, `WithDataTid`)
- Реальные атрибуты HTML (если можете проверить)
- Существующие Playwright-тесты целевого проекта (`target-inventory.json` из `discover-target`)

**Не делайте:**
- Не выдумывайте селекторы
- Не догадывайтесь значения атрибутов
- Не используйте значения из discovery draft без проверки

Пример:

```json
{
  "SourceExpression": "page.SearchButton",
  "TargetExpression": "t_search",
  "TargetKind": "TestId"
}
```

## Шаг 4. Сгенерируйте код

```bash
dotnet run --project Migrator.Cli -- --mode migrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./generated" --format both
```

Проверьте `generated/report.json`:
- `GeneratedFiles` — количество сгенерированных файлов
- `Mapped` / `Unmapped` — сколько таргетов было резолвлено
- `Unsupported` — действия, требующие ручного внимания

## Шаг 5. Проверьте сгенерированный вывод

```bash
dotnet run --project Migrator.Cli -- --mode verify --input "./generated" --config "./adapter-config.json" --out "./verify" --format both
```

Проверьте `verify/verify-report.json`:
- `summary.status` — `passed` или `failed`
- `summary.syntaxErrors` — синтаксические ошибки C#
- `summary.todoComments` — TODO комментарии
- `summary.placeholderLeftovers` — нерезолвленные `{placeholder}` токены
- `files[]` — issue по файлам

## Шаг 6. Compile smoke

Скопируйте сгенерированные файлы в Playwright .NET проект и запустите:

```bash
dotnet build
```

Исправьте ошибки компиляции. Частые проблемы:
- Отсутствующие `using` директивы
- Неправильные `SetUpStatements` для вашего test host
- Нерезолвленные вызовы методов, которым нужен `MethodMapping`

## Шаг 7. Runtime proof

Запустите сгенерированные тесты в реальном окружении:

```bash
dotnet test --filter "FullyQualifiedName~YourTestClass"
```

Если тесты падают, классифицируйте каждый фейл (см. [Классификация фэйлов](#классификация-фэйлов)).

## Шаг 8. Используйте propose для следующих mappings

```bash
dotnet run --project Migrator.Cli -- --mode propose --input "./generated" --config "./adapter-config.json" --out "./proposals" --format both
```

Проверьте `proposals/mapping-proposals.md`. Начните с самых приоритетных:
- `UiTarget` предложения, которые уменьшают unmapped count
- `MethodMapping` для частых хелперов
- `ParameterizedMethodMapping` для хелперов с varying аргументами

Применяйте по одной небольшой группе маппингов, перезпускайте verify, подтверждайте улучшение.

## Шаг 9. Итерация

Повторяйте шаги 3-8 пока:
- Все целевые файлы генерируют чистый код
- Compile smoke проходит
- Runtime тесты проходят
- Quality gates satisfied

## Классификация фэйлов

Когда тесты падают при runtime, классифицируйте каждый фейл:

| Категория | Причина | Кто исправляет |
|---|---|---|
| **Баг генерации** | Мигратор выдал некорректный C# | Фикс инструмента или правка вручную |
| **Проблема профиля** | Отсутствующий или неверный маппинг | Добавить/исправить adapter-config.json |
| **Неверный локатор** | Target expression не совпадает с реальной страницей | Проверить source truth, исправить маппинг |
| **Семантика хелпера** | Поведение хелпера не захвачено маппингом | Добавить MethodMapping или ParameterizedMethodMapping |
| **Стратегия table/list** | Паттерн доступа к строке или ассерт отличается | Настроить table/list mappings или правка вручную |
| **Тестовые данные** | Необходимые данные отсутствуют в окружении | Настройка данных (вне scope Migrator) |
| **Окружение/бэкенд** | Auth, сеть или сервис | Инфраструктура (вне scope Migrator) |
| **Требуется ручная миграция** | Сложная логика, которую нельзя auto-маппить | Разработчик пишет вручную |

## Quality gates для продакшена

Когда пилот стабилен, зажмите quality gates в `adapter-config.json`:

```json
{
  "QualityGates": {
    "MaxTodoComments": 0,
    "MaxUnsupportedActions": 0,
    "MaxUnmappedTargets": 0,
    "MaxRawExpressions": 0,
    "FailOnInvalidGeneratedSyntax": true,
    "FailOnPlaceholderLeftovers": true
  }
}
```

Подробнее: [Отчёты и Quality Gates](reports-and-quality-gates.md).

## Shortcut: режим orchestrate

Для итеративной разработки используйте режим orchestrate:

```bash
dotnet run --project Migrator.Cli -- --mode orchestrate --input "./SeleniumTests" --config "./adapter-config.json" --out "./orchestration" --format both
```

Проверяйте `orchestration/orchestration-report.md` после каждого прогона. Применяйте одно high-priority предложение, затем реран.

## Масштабирование на большие батчи

Когда пилот проверен:
1. Выберите следующий батч из 20-50 тестов
2. Скопируйте проверенный adapter-config.json как базу
3. Добавьте scope-specific override через `Scopes` в конфиге
4. Запустите orchestrate на новом батче
5. Обработайте новые unmapped targets и unsupported actions
6. Зажмите quality gates по необходимости
