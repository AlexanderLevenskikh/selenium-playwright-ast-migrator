# Адаптивная маршрутизация риска агента

Итерация 5 делает защищённый agent runtime чувствительным к риску, но не превращает оценку риска в ещё одно свободное мнение агента.

## Команды

```bash
selenium-pw-migrator migration assess-agent-risk --out migration/runs/wave-001
selenium-pw-migrator migration next-agent-action --out migration/runs/wave-001
selenium-pw-migrator migration check-agent-budget --out migration/runs/wave-001
selenium-pw-migrator migration agent-perf-report --out migration/runs/wave-001
```

`assess-agent-risk` создаёт `agent-risk-assessment.json`. Для текущих evidence оценка детерминирована и содержит:

- балл от 0 до 100;
- уровень `low`, `medium`, `high` или `critical`;
- причины с весами и ссылкой на источник;
- fingerprint оценки;
- рекомендуемые роли;
- адаптивный бюджет ролей;
- разрешено ли автоматическое продолжение.

## Сигналы

Маршрутизатор использует только runtime-owned или детерминированные evidence:

- риск и complexity budget из wave plan;
- состояние validation и wave contract;
- no-progress evidence;
- изменённые, удалённые и защищённые пути;
- количество TODO и unmapped;
- risk flags из review bundle;
- повторные и неуспешные вызовы ролей.

Риск не выводится из свободного текста самой роли.

## Адаптивные бюджеты

Низкорисковый `fast`-run получает максимум четыре автоматических вызова ролей: до двух executor-turn, один финальный reviewer и один финальный sentinel. Бюджет watchdog равен нулю, пока детерминированные evidence не повысят риск.

При среднем и высоком риске возвращаются watchdog и более широкий, но ограниченный бюджет. Профили `standard` и `audit` сохраняют усиленные предварительные проверки. Финальный reviewer, финальный sentinel, scope checks и final gate обязательны при любом уровне риска.

## Критический риск

Ослабление gate/assertions, манипуляция evidence, невалидный wave contract или жёсткое превышение complexity budget дают `critical` и `HUMAN_REVIEW_REQUIRED`. Runtime не переключает себя молча в более permissive-профиль.

Решение `RUN_ROLE` содержит `riskAssessmentFingerprint`. Перед записью `STARTED` runtime пересчитывает риск. Если evidence изменились, разрешение устарело и необходимо снова вызвать `next-agent-action`.

## Performance evidence

`agent-lifecycle-performance.json` теперь содержит risk score, risk level, fingerprint оценки, адаптивный lifecycle budget и `lifecycleBudgetStatus`. Wall-clock budget диагностический, потому что может включать ожидание человека; жёсткой границей автоматического продолжения остаётся бюджет вызовов ролей.
