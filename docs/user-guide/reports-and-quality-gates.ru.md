# Отчёты и Quality Gates

Как читать отчёты Migrator и настраивать quality gates.

## Файлы отчётов

### Отчёты analyze

| Файл | Формат | Описание |
|---|---|---|
| `report.json` | JSON | Статистика по файлам: тесты, действия, mapped/unmapped targets, TODO |
| `report.txt` | Text | Человекочитаемое резюме всех проанализированных файлов |
| `unmapped-targets.json` | JSON | Source-выражения без маппинга, сгруппированы по частоте |
| `unsupported-actions.json` | JSON | Действия, которые инструмент не может конвертировать |
| `migration-quality-dashboard.json` | JSON | Метрики качества, категории TODO, guardrails и рекомендуемые tickets |
| `migration-quality-dashboard.md` | Markdown | Человекочитаемый migration-quality dashboard |
| `migration-quality-tickets.md` | Markdown | Сфокусированные tickets для следующего quality-improvement batch |

### Отчёты migrate

| Файл | Формат | Описание |
|---|---|---|
| `report.json` | JSON | Аналогичен analyze, с `GeneratedFiles` count |
| `report.txt` | Text | Человекочитаемое резюме |
| `migration-quality-dashboard.json` | JSON | Метрики качества, категории TODO, guardrails и рекомендуемые tickets |
| `migration-quality-dashboard.md` | Markdown | Человекочитаемый migration-quality dashboard |
| `migration-quality-tickets.md` | Markdown | Сфокусированные tickets для следующего quality-improvement batch |

### Отчёт verify

| Файл | Формат | Описание |
|---|---|---|
| `verify-report.json` | JSON | Структурированный отчёт качества с `summary`, `files`, `issues` |
| `verify-report.txt` | Text | Человекочитаемый listing по файлам |

**Структура verify-report.json:**
```json
{
  "summary": {
    "status": "passed",
    "filesChecked": 5,
    "todoComments": 65,
    "syntaxErrors": 0,
    "placeholderLeftovers": 0
  },
  "files": [
    {
      "sourceFile": "Widget.cs",
      "generatedFile": "WidgetPlaywright.cs",
      "status": "passed",
      "issues": [
        {
          "category": "Todo",
          "severity": "Warning",
          "message": "TODO comment found: map source expression to Playwright locator: page.SubmitButton"
        }
      ]
    }
  ],
  "issues": [
    {
      "category": "Todo",
      "severity": "Warning",
      "message": "TODO comment found: ..."
    }
  ]
}
```

### Migration quality dashboard

`migration-quality-dashboard.*` связывает raw reports с implementation work: группирует TODO по `[MIGRATOR:<CODE>]`, объясняет root cause, показывает next safe action и генерирует ticket-sized follow-up work. `migration-quality-tickets.md` можно копировать в issue tracker или отдавать агенту.

Подробнее: [Migration Quality Program](../migration-quality-program.md).

### Отчёты propose

| Файл | Формат | Описание |
|---|---|---|
| `mapping-proposals.md` | Markdown | Ранжированные предложения с config snippets |
| `mapping-proposals.json` | JSON | Структурированные предложения с scores, evidence, priorities |

**Структура предложения:**
- `title` — описание предложенного изменения
- `id` — уникальный идентификатор
- `kind` — тип (UiTarget, MethodMapping и т.д.)
- `score` — impact score (высокий = важнее)
- `priority` — High (>=20), Medium (8-19), Low (<8)
- `suggestedConfig` — JSON snippet для добавления в adapter-config.json
- `risks` — на что обратить внимание
- `affectedFiles` — файлы, которые затронет изменение

### Отчёт orchestration

| Файл | Формат | Описание |
|---|---|---|
| `orchestration-report.json` | JSON | Единый отчёт, объединяющий все этапы |
| `orchestration-report.md` | Markdown | Человекочитаемое резюме со stage, метриками, рекомендациями |

**Структура orchestration-report.json:**
```json
{
  "status": "passed_with_warnings",
  "inputPath": "...",
  "configPath": "...",
  "stages": [
    { "name": "analyze", "status": "passed", "exitCode": 0 },
    { "name": "migrate", "status": "passed", "exitCode": 0 },
    { "name": "verify", "status": "passed_with_warnings", "exitCode": 1 },
    { "name": "propose", "status": "passed", "exitCode": 0 }
  ],
  "metrics": {
    "filesProcessed": 5,
    "testsFound": 12,
    "generatedFiles": 5,
    "syntaxErrors": 0,
    "todoComments": 65,
    "proposals": 8
  },
  "issues": [],
  "topProposals": [
    "[High] Map UiTarget for page.SearchButton (score: 25)"
  ],
  "recommendedNextActions": [
    "Add source-truth UiTarget mappings for unmapped targets."
  ],
  "warnings": []
}
```

## Quality Gates

Quality gates настраиваются в `adapter-config.json`:

```json
{
  "QualityGates": {
    "MaxTodoComments": 0,
    "MaxUnsupportedActions": 0,
    "MaxUnmappedTargets": 0,
    "MaxRawExpressions": 0,
    "FailOnPageTodo": true,
    "FailOnInvalidGeneratedSyntax": true,
    "FailOnPlaceholderLeftovers": true,
    "FailOnMultipleMatchingScopes": true
  }
}
```

### Поля gate

| Поле | Тип | Описание | Дефолт |
|---|---|---|---|
| `MaxTodoComments` | int | Макс TODO комментариев во всех сгенерированных файлах | `int.MaxValue` (только warning) |
| `MaxUnsupportedActions` | int | Макс unsupported действий | `int.MaxValue` (только warning) |
| `MaxUnmappedTargets` | int | Макс unmapped таргетов | `int.MaxValue` (только warning) |
| `MaxRawExpressions` | int | Макс raw expressions (необработанных) | `int.MaxValue` (только warning) |
| `FailOnPageTodo` | bool | Fail если остались `Page.TODO_*` вызовы | `true` |
| `FailOnInvalidGeneratedSyntax` | bool | Fail если сгенерированный код имеет C# синтаксические ошибки | `true` |
| `FailOnPlaceholderLeftovers` | bool | Fail если остались нерезолвленные `{placeholder}` токены | `true` |
| `FailOnMultipleMatchingScopes` | bool | Fail если несколько scopes совпали для одного файла | `true` |

### Soft vs Strict mode

**Soft mode (дефолты):** Все count-based gate — только warning. Verify stage отчитывает issue но не падает.

**Strict mode:** Установите count-based gate в `0` (или порог). Нарушения — fail verify stage.

### Exit коды verify

| Exit code | Значение |
|---|---|
| 0 | Все quality gates пройдены |
| 1 | Quality gate провалился (например, слишком много TODO) |
| 2 | Config error (например, `Match: "Nth"` без `Index`) |
| 3 | Синтаксические ошибки в сгенерированном коде |

### Exit коды orchestration

При запуске режима `orchestrate`, exit коды отражают самый серьёзный результат этапа:

| Exit code | Значение |
|---|---|
| 0 | Все этапы прошли, все quality gates пройдены |
| 1 | Quality gates verify провалились |
| 2 | Невалидный input или config error verify |
| 3 | Этап analyze или migrate провалился |
| 4 | Обнаружены синтаксические ошибки генерации |

### Рекомендуемая прогрессия

1. **Первый запуск:** используйте дефолты (soft mode), проверьте отчёты
2. **После пилотного конфига:** установите `MaxUnmappedTargets` на текущий unmapped count, затем уменьшайте
3. **Перед batch миграцией:** строгие gate для целевого батча
4. **Интеграция в CI/CD:** используйте флаги `--fail-on-unsupported` и `--fail-on-todo`
