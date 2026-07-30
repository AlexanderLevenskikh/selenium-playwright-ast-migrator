# Результат блока 3: source validation и stage-aware orchestration

Статус: **реализован в исходниках, требуется проверка на Windows с Chrome и .NET SDK 10**.

## Что добавлено

- новая команда `selenium-pw-migrator lab run`;
- suites `vertical`, `smoke`, `pr`;
- фильтры `--project`, `--tag`;
- отдельный рабочий каталог для каждого fixture;
- один LabApp на свободном порту на весь suite;
- `dotnet restore → build → test` исходного проекта;
- чтение TRX и проверка `oracle.source.mustPassTests`;
- запуск существующей команды мигратора `run`, а не отдельной реализации pipeline;
- использование только файлов из `source.migrationFiles` как migration input;
- проверка обязательных артефактов текущего run:
  - `orchestration-report.json`;
  - стадии `analyze`, `migrate`, `verify`;
  - каталог `generated`;
  - `verify/verify-report.json`;
- контроль SHA-256 исходного fixture до и после запуска;
- stdout/stderr, exit code, команда, рабочий каталог и длительность каждой стадии;
- timeout на каждый внешний процесс и завершение process tree;
- классификация:
  - `SOURCE_INVALID` — исходник не восстанавливается, не собирается или не проходит тесты;
  - `INFRASTRUCTURE_FAILURE` — отсутствует SDK/CLI, недоступен NuGet, не стартует Chrome/ChromeDriver или истёк timeout;
  - `MIGRATOR_FAILURE` — CLI повредил pipeline, потерял обязательные артефакты или дал failed stage;
  - `UNSUPPORTED_AS_EXPECTED` — ожидаемо неподдерживаемый fixture оставил видимую диагностику;
  - `REGRESSION` — ожидаемо неподдерживаемая операция исчезла без диагностики;
- suite exit codes:
  - `0` — только `PASS`, `PASS_WITH_WARNINGS`, `UNSUPPORTED_AS_EXPECTED`;
  - `10` — regression;
  - `11` — migrator failure;
  - `12` — source invalid;
  - `13` — infrastructure failure;
  - `14` — non-deterministic;
  - `15` — ошибка самого lab/контракта;
- отчёты:
  - `lab-summary.json`;
  - `lab-summary.md`;
  - `projects/<id>/scenario-result.json`;
  - `projects/<id>/source/source-validation.json`;
  - отдельные stdout/stderr логи;
- unit-тесты классификации и artifact reader;
- scenario-тест полного coordinator через поддельный process runner;
- PowerShell- и Bash-скрипты проверки блока.

## Проверка одной командой

Из корня репозитория на Windows:

```powershell
.\scripts\run-lab-block3.ps1
```

Ожидаемая итоговая строка:

```text
Block 3 passed: 7 source projects validated, existing migration run executed, suite statuses classified.
```

Основной отчёт после прогона:

```text
artifacts/lab/block-03/lab-summary.md
```

## Ручной запуск

```powershell
dotnet run --project Migrator.Cli -c Release --no-build -- lab run `
  --suite vertical `
  --corpus ./corpus/stable/vertical-slice `
  --out ./artifacts/lab/block-03 `
  --timeout-seconds 600
```

Сохранить временные копии проектов для диагностики:

```powershell
dotnet run --project Migrator.Cli -c Release --no-build -- lab run `
  --project p09-helper-extension-mapping `
  --out ./artifacts/lab/repro-p09 `
  --keep-workspaces
```

После успешного прогона следующий шаг владельца:

```text
Классификация стадий верна, продолжай блок 4
```
