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
2. сохраняет стабильный fingerprint и исходные метрики;
3. делает одно ограниченное изменение в adapter config, generated helper/POM или другом разрешённом месте;
4. проверяет изменение;
5. полностью повторяет настроенный source scope и доступные проверки;
6. записывает результат `PROGRESS`, `NO_PROGRESS` или `BLOCKED`.

После прогресса счётчик no-progress сбрасывается, и агент автоматически начинает следующий безопасный цикл. После первого `NO_PROGRESS` текущий кандидат помечается исчерпанным и выбирается другой независимый кандидат. Остановка происходит после двух подряд разных безрезультатных циклов, реального блокера, решения человека или пяти завершённых циклов.

`/supervised-task continue` открывает новый бюджет из пяти циклов, сохраняя реальные evidence и список исчерпанных кандидатов. `/supervised-task continuous` явно включает автоматический переход между циклами.

## Независимые виды проверки

Синтаксис generated-кода, метрики миграции, restore/build проекта и runtime/smoke проверяются отдельно. Известный дефект verify-project, CPM или транзитивной сборки может блокировать только project verification, но не обязан запрещать подтверждённые mappings и helper-исправления, которые измеряются по diff, TODO/unmapped и синтаксису.

Ноль синтаксических ошибок C# не означает успешную сборку проекта.

## Handoff

`state/handoff.md` переписывается целиком, содержит ровно один статус и stop reason и совпадает с `state/autonomy-state.json`. Перед остановкой запускается `scripts/validate-handoff.ps1` или `.sh`. При исчерпании бюджета указывается `AUTONOMOUS_CYCLE_BUDGET_REACHED`, ранжируются оставшиеся безопасные кандидаты и не заявляется завершение миграции.

## Обновление старого workspace

Запустите kit update с backup. Новый kit добавит `state/autonomy-state.json` и валидатор handoff. Настоящие run-артефакты сохраняются; validation evidence нельзя реконструировать или копировать.

> В режиме `continuous` лимит в пять циклов является контрольной точкой, а не остановкой: агент автоматически начинает следующую пятёрку без повторного вызова `continue` и работает до настоящего условия остановки.
