# Быстрый путь миграции

Быстрый путь уменьшает оркестрационные накладные расходы, не ослабляя scope, evidence и требования final gate.

## Профили выполнения

`migration run-wave` принимает:

```text
--execution-profile fast      по умолчанию; сначала executor, остальные роли по риску/событиям
--execution-profile standard  executor + reviewer, watchdog/sentinel по событиям
--execution-profile audit     executor + reviewer + watchdog + sentinel
```

Выбранный профиль записывается в `execution-policy.json`. Политика управляет маршрутизацией ролей, но её safety-инварианты детерминированы: final gate обязателен, scope нельзя расширять, подавление assertions запрещено, runtime-state нельзя править вручную.

## Неизменяемый контракт wave

В run workspace создаётся `wave-manifest.json`, который фиксирует:

- идентичность и SHA-256 плана;
- id/index/phase/cluster wave;
- выбранные source files и их SHA-256;
- выбранные tests;
- пути source/generated;
- execution profile.

После материализации run directory неизменяема. Повторный `run-wave` только валидирует и переиспользует workspace, но не копирует source заново. Для запуска уже созданной wave используй `run-migrate.ps1` или `run-migrate.sh`.

Перед реализацией и review запусти:

```bash
selenium-pw-migrator migration validate-wave --out migration/runs/wave-001
```

`validate-wave` падает при изменении copied files, selected tests, fingerprint manifest или safety-инвариантов execution policy.

## Детектор отсутствия прогресса

После каждого bounded fix/review цикла записывай progress snapshot:

```bash
selenium-pw-migrator migration check-progress \
  --out migration/runs/wave-001 \
  --max-identical-snapshots 3
```

Детектор учитывает generated output, evidence/review artifacts, TODO, unmapped и validation failures. JSON timestamps, event hashes, durations и типовой elapsed-time шум в логах нормализуются, поэтому простая перегенерация timing evidence не считается прогрессом. Повтор одного состояния до порога создаёт `NO_PROGRESS_DETECTED`, требует watchdog/смену стратегии и запрещает очередной слепой retry.

Артефакты:

```text
progress-history.jsonl
no-progress-result.json
```

## Performance trace

`run-wave` пишет `performance-trace.json` с длительностью фаз. Отчёт:

```bash
selenium-pw-migrator migration perf-report --out migration/runs/wave-001
```

Trace измеряет материализацию и CLI execution. Время agent roles должен дописывать вызывающий Harness/runtime.

## Lifecycle быстрого пути

```text
materialize wave
  -> validate-wave
  -> bounded migration
  -> check-progress после каждого fix cycle
  -> watchdog/reviewer/sentinel по событиям
  -> deterministic scope/policy/memory/project gates
  -> sentinel перед final handoff
  -> final gate
```

Быстрый путь убирает повторную работу ролей, но не превращает checkpoint в `DONE` и не обходит существующий final gate.
