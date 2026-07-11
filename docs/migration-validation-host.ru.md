# Единый validation host

Итерация 3 убирает из обычного wave-процесса ручную агентскую цепочку `validation-plan → запуск shell-команды → record-validation`.

## Команда

```powershell
selenium-pw-migrator migration validate `
  --out migration/runs/wave-001 `
  --validation-project ./Target.Tests/Target.Tests.csproj
```

Также поддерживается alias `migration validation-host`. Команды `validation-plan` и `record-validation` остаются для восстановления и импорта evidence, выполненного вне host.

## Что делает один вызов

1. проверяет неизменяемый контракт wave и `run-context`;
2. вычисляет влияние изменённых файлов и точный input fingerprint;
3. материализует PASS без повторного запуска только при совпадении exact inputs и validation contract;
4. парсит runtime JSON/JSONL;
5. проверяет синтаксис сгенерированного C# и базовую целостность TypeScript/JavaScript;
6. запускает build target-проекта или явную проектную validation-команду;
7. сохраняет stdout/stderr, длительность, timeout, командную строку и peak memory каждого процесса;
8. записывает PASS/FAIL через incremental validation contract;
9. создаёт один validation checkpoint после нового реально выполненного PASS.

Cache hit не создаёт дублирующий checkpoint.

## Project validation

Для изменений кода host работает fail-closed: PASS невозможен без исполняемого project evidence.

```powershell
# .NET solution/project или TypeScript tsconfig
selenium-pw-migrator migration validate `
  --out migration/runs/wave-001 `
  --validation-project ./Target.Tests/Target.Tests.csproj

# Существующая проектная команда build/test/gate
selenium-pw-migrator migration validate `
  --out migration/runs/wave-001 `
  --validation-command "dotnet test ./Target.Tests/Target.Tests.csproj --no-restore"
```

Для `--validation-project` используются:

- `dotnet build <project> --no-restore` для `.sln`, `.slnx`, `.csproj`;
- `npx --no-install tsc --noEmit -p <tsconfig>` для `tsconfig.json`.

Зависимости следует восстановить один раз до wave-loop, чтобы не повторять restore и сетевые обращения на каждой проверке.

## Профили и параметры

- `--validation-profile auto|fast|standard|audit` — `auto` читает immutable execution policy;
- `--validation-timeout-seconds <n>` — жёсткий timeout каждого внешнего процесса;
- `--validation-dry-run true` — строит проверки и команды, но не выполняет процессы и не записывает PASS;
- `--force-validation true` — игнорирует cache hit по exact inputs и validation contract;
- `--checkpoint-on-pass true|false` — по умолчанию `true`, но только для нового выполненного PASS.

## Артефакты

```text
validation-plan.json
validation-result.json
validation-host-result.json
validation/processes/<invocation-id>/*.stdout.log
validation/processes/<invocation-id>/*.stderr.log
validation/host-runs/<invocation-id>.json
migration/.cache/validation/<input>.<validation-contract>.json
latest-checkpoint.json           # только после нового выполненного PASS
```

`validation-host-result.json` использует схему `migration-validation-host-result/v1` и содержит internal checks, процессы, cache decision, resource metrics и safety invariants.

## Гарантии безопасности

- изменения кода нельзя отметить PASS только artifact-проверками;
- пустая или упавшая команда никогда не попадает в PASS-cache;
- синтаксическая ошибка C# останавливает внешние процессы и создаёт FAIL evidence;
- cached PASS обязан совпадать по exact inputs и validation contract: профилю, проекту, командам и timeout;
- checkpoint означает восстановимый прогресс, но не `DONE`;
- final reviewer, sentinel, project gates и final gate остаются отдельными обязательными границами.

## Уровни тестирования

- unit-тесты проверяют `ValidationProcessExecutor` через fake process/clock/filesystem adapters;
- scenario smoke исполняет пути configuration-required, PASS, cache-hit, syntax-failure и timeout safety;
- полный E2E остаётся в scheduled full-validation workflow;
- performance budgets отслеживают регрессии wall-clock orchestration и validation host.
