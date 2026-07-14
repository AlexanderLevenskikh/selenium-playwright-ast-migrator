# Усиление производительности и кэша

Этот checkpoint превращает fast-path метрики и кэш в нормальный эксплуатационный контракт.

## Единый performance report

Запуск:

```powershell
selenium-pw-migrator migration perf-report --out migration/runs/wave-001
```

Команда объединяет доступные данные из:

- `performance-trace.json` — материализация wave;
- `validation-host-result.json` — длительность validation host и cache hit;
- `agent-lifecycle-performance.json` — роли и wall clock agent lifecycle;
- `agent-risk-assessment.json` — уровень и score риска.

Создаются `performance-report.json` и `performance-report.md` с единым correlation id, breakdown по фазам и крупнейшим измеренным компонентом. Сумма измеренных компонентов является диагностикой, а не выдаётся за точный parallel critical path.

## Совместимость кэша

Переиспользуемые validation entries привязаны к `migration-cache-compatibility/v1`. Fingerprint включает:

- конкретную сборку CLI и её module version id;
- сборку Roslyn recognizers;
- сборку renderer;
- Selenium source adapter;
- версии контрактов run-context, validation result и validation host.

Поэтому изменение recognizer или renderer гарантированно приводит к cache miss, даже если версия пакета случайно не была повышена.

## Обслуживание кэша

```powershell
selenium-pw-migrator migration cache-stats --workspace migration
selenium-pw-migrator migration cache-verify --workspace migration
selenium-pw-migrator migration cache-prune --workspace migration --cache-max-age-days 30 --cache-max-size-mb 2048 --cache-apply false
selenium-pw-migrator migration cache-prune --workspace migration --cache-max-age-days 30 --cache-max-size-mb 2048 --cache-apply true
```

`cache-prune` по умолчанию работает как dry run. Entries, на которые ссылаются активные validation plans, защищены от удаления. Некорректные entries можно удалить, а структурно корректные, но несовместимые entries сохраняются как непереиспользуемая история, пока prune policy не выберет их для удаления.

## Scope audit ролей

```powershell
selenium-pw-migrator migration record-role-scope-access --out migration/runs/wave-001 --role reviewer --role-phase final --scope-operation read --scope-path migration/runs/wave-001/review/review-bundle.json
selenium-pw-migrator migration scope-audit --out migration/runs/wave-001
```

Фактически объявленный доступ вне manifest roots всегда является ошибкой. Отсутствие декларации допускается только как warning в `fast`; в `standard` и `audit` это ошибка. При провале scope audit финальный handoff блокируется.

Audit проверяет декларации, evidence paths, review bundle и runtime roots. Он честно не утверждает, что способен восстановить необъявленные чтения файлов на уровне ОС.
