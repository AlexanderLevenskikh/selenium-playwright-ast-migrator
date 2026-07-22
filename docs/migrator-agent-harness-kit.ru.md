# Standard Migration Agent Kit

Имя kit сохранено ради совместимости пакетов. По поведению это тонкий safety-слой вокруг обычного CLI Migrator без отдельного scheduler и lifecycle state machine.

## Установка

```bash
selenium-pw-migrator kit bootstrap-opencode \
  --workspace migration \
  --source ./LegacyTests \
  --opencode-install auto
```

## Запуск

```bash
selenium-pw-migrator run \
  --input ./LegacyTests \
  --config migration/profiles/adapter-config.json \
  --out migration/runs/run-001 \
  --format both

selenium-pw-migrator verify-project \
  --input ./LegacyTests \
  --config migration/profiles/adapter-config.json \
  --out migration/runs/run-001/verify-project \
  --format both
```

Затем выполняются установленные scope-, artifact- и final-gate-проверки. Необязательная команда `/supervised-task` запускает ту же последовательность и может применить максимум одно ограниченное исправление с наибольшей отдачей, после чего повторяет полный source scope.

## Что осталось

- project-local adapter config и память;
- source-scope contract;
- реальный `verify-project`;
- artifact hygiene и final gate;
- четыре роли: orchestrator, executor, reviewer, watchdog.

Нельзя создавать validation evidence вручную или считать pilot доказательством покрытия всего проекта.
