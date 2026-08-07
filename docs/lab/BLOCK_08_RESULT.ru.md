# Блок 8: минимизация, кластеризация и bounded task packs

Статус: **реализован, ожидает локальной проверки**.

Это финальный блок первой версии Migrator Lab.

## Что добавлено

### `lab reduce`

Консервативный feature-aware reducer. Он не делает свободный AST shrinking и не рискует изменить смысл теста. Из candidate/scenario он оставляет только:

- `scenario.json`;
- файлы из `project.files`;
- `source.migrationFiles`;
- adapter config;
- feature list в reduction report.

`bin`, `obj`, логи, заметки и любой не входящий в контракт шум не попадают в минимальный repro.

### `lab triage`

Команда читает существующий `lab-summary.json`, связывает результаты с scenario contract и группирует реальные findings по:

- stage;
- diagnostic categories;
- semantic diff kinds;
- нормализованным feature families.

Для каждого кластера выдаются severity, вероятные компоненты мигратора, рекомендуемый regression level и `AUTO_FIX_ELIGIBLE`/`MANUAL_REVIEW`.

### Bounded task pack

Для каждого кластера создаётся отдельная папка:

```text
task-packs/<cluster>/
  TASK.md
  task-pack.json
  cluster.json
  evidence.json
  reduction.json
  reduction.md
  repro/
  migrator-code/       # максимум 3 релевантных файла
```

Пакет содержит evidence, минимальный project repro, команду воспроизведения, релевантный код и тесты, ограничения, quality baseline и definition of done.

### `lab promote`

Создаёт reviewed promotion artifact для одного из уровней:

- `unit-test` — сохраняет минимальный repro и требует focused unit assertion;
- `project-fixture` — готовит проектный regression fixture;
- `saved-seed` — готовит принятый seed.

Команда намеренно не генерирует бессмысленный `Fact` по шаблону: содержание unit-test должно доказывать конкретную причину дефекта.

### `lab release-gate`

Редкий pre-release gate объединяет два доказательства:

1. stable lab run без неожиданных outcomes;
2. свежий `PASS` evidence от настоящего проекта.

По умолчанию evidence старше 14 дней отклоняется. Gate не предназначен для каждого PR.

## Политика автоматизации

См. `docs/lab/AUTOMATION_POLICY.ru.md`.

Автоисправление разрешается только для воспроизводимого, source-valid, bounded PASS→REGRESSION-кластера без изменения oracle/budget/expected status. Infrastructure, invalid fixtures, nondeterminism и unsupported boundaries требуют ручного решения.

## Проверка

```powershell
.\scripts\run-lab-block8.ps1 -SkipBrowserInstall
```

Скрипт проверяет reducer, clustering, task pack, promotion и release gate на настоящем `p01` pipeline и синтетически внесённой в копию отчёта регрессии. Stable corpus при этом не изменяется.
