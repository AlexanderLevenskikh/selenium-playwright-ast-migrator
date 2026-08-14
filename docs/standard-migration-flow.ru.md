# Стандартный процесс миграции

Поддерживаемый процесс использует один настроенный source scope и одну последовательность обычных run. Исходники не разбиваются на части. Автономность реализована ограниченными циклами исправлений с полным повтором source scope.

## Первый запуск

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
selenium-pw-migrator run --input ./SeleniumTests --config migration/profiles/adapter-config.start.json --out migration/runs/run-001 --format both
selenium-pw-migrator verify-project --input ./SeleniumTests --config migration/profiles/adapter-config.start.json --out migration/runs/run-001/verify-project --format both
```

Pilot — необязательная калибровка, которая не заменяет полный запуск.

## Проверка

Все проверки запускаются для одного и того же реального run:

```shell
./migration/scripts/check-harness-policy.sh -Workspace migration -RepoRoot .
./migration/scripts/check-scope.sh -RepoRoot . -AllowedRoots migration
./migration/scripts/validate-run-artifacts.sh -RunPath migration/runs/run-001
./migration/scripts/check-final-gate.sh -Workspace migration -Run migration/runs/run-001 -RepoRoot .
```

На Windows используются `.ps1`. Отсутствующую или неуспешную project verification нельзя заменять искусственным результатом.

## Автономные циклы исправлений

Обычный вызов, `continue` или `continuous` получает до пяти циклов исправлений. Исходный baseline-run в этот лимит не входит.

Каждый цикл:

1. выбирает одну неисчерпанную первопричину, подтверждённую исходниками;
2. сохраняет принятый baseline-run и стабильную человекочитаемую метку кандидата;
3. запускает `selenium-pw-migrator remediation guard --accepted-run <before> --input <source> --config <adapter-config> --autonomy-state migration/state/autonomy-state.json --out migration/state/remediation-cycle-guard.json` и открывает транзакцию через `update-autonomy-state -Action StartCycle -GuardPath ...`;
4. делает одно ограниченное изменение в adapter config, generated helper/POM или другом разрешённом месте;
5. проверяет изменение и полностью повторяет source scope в новый immutable run;
6. запускает `selenium-pw-migrator remediation evaluate --before-run <before> --after-run <after> --candidate "<label>" --autonomy-state migration/state/autonomy-state.json --out migration/state/remediation-evaluation.json`;
7. передаёт в `update-autonomy-state -Action RecordCycle` только решение deterministic Core.

Только Core определяет прогресс. `ACCEPT` принимает новое состояние. `REJECT_NO_PROGRESS` и `REJECT_REGRESSION` оставляют `rollbackRequired=true`: bounded change нужно полностью откатить, а Core должен вернуть `ROLLBACK_CONFIRMED` до следующего `StartCycle`. После `REJECT_CYCLE` откат тоже подтверждается до handoff, затем процесс останавливается с `REMEDIATION_CYCLE_DETECTED`. Хэши состояний и rollback-state сохраняются между свежими invocation-budget, поэтому `continue` не скрывает возврат A→B→A и не сбрасывает отвергнутую транзакцию.

`/supervised-task continue` открывает новый бюджет из пяти циклов, сохраняя реальные evidence и список исчерпанных кандидатов. `/supervised-task continuous` явно включает автоматический переход между циклами.

## Независимые виды проверки

Синтаксис generated-кода, метрики миграции, restore/build проекта и runtime/smoke проверяются отдельно. Известный дефект verify-project, CPM или транзитивной сборки может блокировать только project verification, но не обязан запрещать подтверждённые mappings и helper-исправления, которые измеряются по diff, TODO/unmapped и синтаксису.

Ноль синтаксических ошибок C# не означает успешную сборку проекта.

## Handoff

`state/handoff.md` переписывается целиком, содержит ровно один статус и stop reason и совпадает с `state/autonomy-state.json`. Перед остановкой запускается `scripts/validate-handoff.ps1` или `.sh`. При исчерпании бюджета указывается `AUTONOMOUS_CYCLE_BUDGET_REACHED`, ранжируются оставшиеся безопасные кандидаты и не заявляется завершение миграции.

## Обновление старого workspace

Запустите kit update с backup. Новый kit добавит `state/autonomy-state.json` и валидатор handoff. Настоящие run-артефакты сохраняются; validation evidence нельзя реконструировать или копировать.

> В режиме `continuous` лимит в пять циклов является контрольной точкой, а не остановкой: агент автоматически начинает следующую пятёрку без повторного вызова `continue` и работает до настоящего условия остановки.
