# Надёжное восстановление агента

Итерация 6 добавляет детерминированное восстановление после обрыва процесса, потери сессии, зависшей активной роли, незавершённой атомарной записи JSON или пропавшего derived ledger head. Восстановление не считает checkpoint состоянием `DONE` и не заменяет validation, финальный reviewer, sentinel, scope checks и final gate.

## Артефакты runtime

- `agent-role-lease.json` — lease текущей активной роли;
- `agent-recovery-plan.json` — классификация состояния без изменения истории;
- `agent-recovery-result.json` — точный список применённых безопасных ремонтов;
- `recovery/leases/` — архив завершённых, осиротевших и восстановленных lease;
- `recovery/quarantine/` — незавершённые временные файлы атомарных записей.

Роль со статусом `STARTED` получает ограниченный lease до фиксации события в append-only журнале. По умолчанию lease действует 30 минут, жёсткий максимум — 2 часа. Протухание считается от последнего корректного heartbeat, а не от первоначального старта роли, поэтому долгий, но живой процесс не будет закрыт во время регулярного продления. Изменения lease и журнала ролей сериализуются локальной эксклюзивной блокировкой. Длительная роль продлевает lease командой:

```powershell
selenium-pw-migrator migration heartbeat-agent-role `
  --out migration/runs/wave-001 `
  --role executor `
  --role-phase execution
```

## Процесс восстановления

```powershell
selenium-pw-migrator migration plan-agent-recovery `
  --out migration/runs/wave-001

selenium-pw-migrator migration recover-agent-runtime `
  --out migration/runs/wave-001
```

Статусы плана:

- `CLEAN` — ремонт не требуется;
- `WAIT_FOR_ROLE` — активная роль всё ещё владеет действующим lease;
- `SAFE_REPAIR_AVAILABLE` — разрешён узкий детерминированный ремонт;
- `BLOCKED` — evidence противоречивы и не должны переписываться автоматически.

Автоматически разрешены только четыре операции:

1. восстановить derived ledger head из корректного hash-chained журнала;
2. закрыть протухшую активную роль новым append-only событием `FAILED`;
3. архивировать осиротевший lease;
4. переместить незавершённые `agent-*.json.tmp-*` в quarantine.

Повреждённый JSONL, сломанная цепочка event hash, несколько противоречивых активных `STARTED`, невозможная временная шкала lease или lease, не совпадающий с активным событием, блокируют автоматическое продолжение. Lease длиннее двух часов отклоняется, override порога восстановления ограничен 24 часами, а runtime никогда не удаляет и не переписывает повреждённую append-only историю.

## Маршрутизация

`next-agent-action` сначала строит recovery plan. Действующий lease даёт `WAIT_FOR_ROLE`, безопасный ремонт — единственное действие `RUN_COMMAND recover-agent-runtime`, а небезопасное состояние — `BLOCKED`. Новая роль назначается только после состояния `CLEAN`.
