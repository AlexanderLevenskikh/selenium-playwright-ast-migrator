# Стандартный процесс миграции

Поддерживаемый процесс использует один настроенный source scope и один обычный run-каталог. Исходники не разбиваются на части, а поверх мигратора больше нет второй state machine.

## Первый запуск

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
selenium-pw-migrator run --input ./SeleniumTests --config migration/profiles/adapter-config.start.json --out migration/runs/run-001 --format both
selenium-pw-migrator verify-project --input ./SeleniumTests --config migration/profiles/adapter-config.start.json --out migration/runs/run-001/verify-project --format both
```

Pilot — необязательная калибровка. Он помогает заранее увидеть нехватку mappings, но не является отдельной частью выполнения и не заменяет полный запуск.

## Проверка

Все проверки запускаются для одного и того же реального run:

```shell
./migration/scripts/check-harness-policy.sh -Workspace migration -RepoRoot .
./migration/scripts/check-scope.sh -RepoRoot . -AllowedRoots migration
./migration/scripts/validate-run-artifacts.sh -RunPath migration/runs/run-001
./migration/scripts/check-final-gate.sh -Workspace migration -Run migration/runs/run-001 -RepoRoot .
```

На Windows используются соответствующие `.ps1`. Final gate по умолчанию требует настоящий успешный `verify-project`. Отсутствие SDK, target project или package source считается блокером, а не поводом создать заменяющий JSON вручную.

## Оптимизированное продолжение

Оптимизация остаётся простой и проверяемой:

- перед изменением mappings читается project-scoped migration memory;
- повторяющиеся TODO/первопричины сортируются по ожидаемой пользе;
- за цикл делается не более одного bounded source-backed исправления в adapter config, generated helper или generated POM внутри product workspace; подозрение на дефект recognizer/renderer самого Migrator оформляется как минимальный reproduction, если пользователь явно не разрешил править исходники Migrator;
- после обычного анализа агент не спрашивает «продолжать ли»: одно безопасное agent-executable исправление выполняется в этом же запуске; вопрос пользователю нужен только для продуктового решения или нового разрешения на запись;
- после него полностью повторяется настроенный source scope и сравниваются отчёты;
- процесс останавливается после успеха, конкретного блокера или повторного отсутствия прогресса.

Так мы убираем coordination overhead, но сохраняем полезные страховки: необязательный pilot, project memory, reviewable config deltas, scope checks, artifact hygiene, project verification и честный final gate.

## Обновление старого workspace

Старые run/state-артефакты лучше архивировать и bootstrap-нуть workspace заново. Не нужно реконструировать старый partition state или копировать validation evidence в новый run.
