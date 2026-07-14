# Защищённый быстрый agent runtime

Итерация 4 переносит выбор следующей роли из длинного агентского prompt-а в детерминированные runtime-команды. Это сокращает повторные вызовы reviewer/watchdog/sentinel, но сохраняет обязательные финальные review, sentinel inspection, scope checks и final gate.

## Команды

```bash
selenium-pw-migrator migration next-agent-action --out migration/runs/wave-001
selenium-pw-migrator migration record-agent-role --out migration/runs/wave-001 --role executor --role-phase execution --role-status STARTED
selenium-pw-migrator migration record-agent-role --out migration/runs/wave-001 --role executor --role-phase execution --role-status COMPLETED --role-evidence generated
selenium-pw-migrator migration check-agent-budget --out migration/runs/wave-001
selenium-pw-migrator migration agent-perf-report --out migration/runs/wave-001
```

`next-agent-action` выдаёт ровно одно действие:

- `RUN_ROLE`;
- `RUN_COMMAND`;
- `WAIT_FOR_ROLE`;
- `HUMAN_REVIEW_REQUIRED`;
- `BLOCKED`;
- `FINAL_HANDOFF`.

Решение записывается в `agent-next-action.json`. Агент должен выполнить только это ограниченное действие, а затем снова запросить решение runtime.

## Квитанции ролей

`record-agent-role` дописывает события в `agent-role-events.jsonl`. События проверяются по sequence и связаны цепочкой `previousEventHash` / `eventHash`; текущая вершина журнала закрепляется в `agent-role-ledger-head.json`. Timestamp входит в hash события. Терминальное событие требует соответствующего `STARTED` для той же роли, фазы и fingerprint входа. `STARTED` принимается только для текущего решения `RUN_ROLE`. Для `COMPLETED` обязателен существующий evidence-файл или каталог внутри wave run.

Фазы:

- `pre` — предварительная проверка, обязательная для выбранного профиля;
- `execution` — один ограниченный ход executor;
- `quality` — одно привязанное к fingerprint метрик решение `migration-wave-manager`;
- `recovery` — watchdog после no-progress или подозрительных risk flags;
- `final` — обязательные финальные reviewer и sentinel.

## Профили

- `fast` пропускает необязательные предварительные роли и обычно сразу вызывает executor.
- `standard` требует предварительный bounded review.
- `audit` требует предварительные reviewer, watchdog и sentinel.

Во всех профилях обязательны детерминированный `measure-wave` и одна квитанция роли `migration-wave-manager/quality`. Финальные reviewer, sentinel и scope audit запускаются только после предложения менеджера принять волну и обязательны до выдачи receipt командой `accept-wave` и до `FINAL_HANDOFF`. Менеджер не может отменять жёсткие проверки. Существующий final gate остаётся источником истины, а материализация следующей волны дополнительно требует валидный `wave-acceptance.json`.

## Бюджет agent-turns

В `execution-policy.json` добавлен ограниченный `roleBudgets` для executor/reviewer/watchdog/sentinel и `migration-wave-manager`. Runtime запрещает повторный запуск уже активной роли и прекращает автоматическое продолжение при исчерпании общего или ролевого лимита. Risk routing может ужесточить лимиты, но не может убрать конечную quality-manager границу. Результат сохраняется в `agent-budget-result.json`; вместо слепого повтора или искусственного принятия выдаётся честное ограничение либо запрос решения человека.

## Performance evidence

`agent-lifecycle-performance.json` содержит число вызовов ролей, итоговый статус, длительность по ролям/фазам и общее время lifecycle. `agent-perf-report` выводит короткую сводку. Временные поля не участвуют в progress signature и validation cache.

## Типичный fast lifecycle

```text
next-agent-action
  -> executor STARTED / COMPLETED
  -> next-agent-action
  -> migration validate
  -> next-agent-action
  -> build-review-bundle
  -> measure-wave
  -> migration-wave-manager quality STARTED / COMPLETED
  -> одно bounded remediation / split / честная остановка
     ИЛИ менеджер предлагает принять волну
  -> final reviewer STARTED / COMPLETED
  -> final sentinel STARTED / COMPLETED
  -> scope-audit
  -> accept-wave
  -> FINAL_HANDOFF
  -> существующие scope/harness/final-gate команды
```

Нельзя вручную редактировать runtime-артефакты и выбирать следующую роль только по тексту prompt-а.
