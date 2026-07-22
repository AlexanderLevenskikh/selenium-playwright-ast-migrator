# Migration workspace

Все артефакты миграции должны жить в одной рабочей папке, по умолчанию `migration/`.

Это нужно, чтобы рядом с кодом мигратора не появлялись десятки папок вида `orchestration-7`, `verify-project-4`, `pom-index`, `todo-explanation`.

## Как работает CLI

Если `--out` указан относительным путём, CLI автоматически кладёт результат внутрь workspace:

```powershell
--out orchestration-7
```

фактически пишет в:

```text
migration/orchestration-7
```

Если `--out` уже начинается с `migration/`, путь не меняется:

```powershell
--out migration/custom-run
```

Если нужен вывод вне workspace, укажи абсолютный путь:

```powershell
--out C:\temp\migration-run
```

## Настройка workspace

По умолчанию:

```text
migration/
```

Можно переопределить:

```powershell
dotnet run --project .\Migrator.Cli -- run --input "<tests>" --out run-1 --workspace ".migration" --format both
```

Тогда результат будет в:

```text
.migration/run-1
```

## Рекомендуемая структура

```text
migration/
  agent-state.md
  pre-stop-checklist.md
  blocked-report.md
  migrator-tickets.md

  baseline/
  pom-index/
  orchestration-1/
  orchestration-2/
  verify-project-1/
  explain-todo-1/
```

## Правила для агента

1. Не создавай рабочие папки рядом с кодом мигратора.
2. Все артефакты `analyze`, `migrate`, `verify`, `verify-project`, `index-pom`, `explain-todo`, `propose`, `orchestrate` складывай в `migration/`.
3. Для нового прогона используй понятное имя:

```text
migration/orchestration-8
migration/verify-project-5
migration/pom-index-2
```

4. Не смешивай артефакты разных прогонов в одной папке, если сравниваешь метрики.
5. Не редактируй generated `.cs` вручную внутри workspace.
6. Если нужно очистить старые артефакты, сначала спроси пользователя или сделай отдельный список кандидатов на удаление.

## Что коммитить

Обычно `migration/` — локальная рабочая папка и не коммитится.

Коммитить можно только осознанно выбранные файлы, например:

- итоговый `adapter-config.json` / profile;
- документацию;
- маленькие проверочные fixtures;
- финальный отчёт, если команда договорилась хранить его в репозитории.
