# Блок 5 — suite report, replay и baseline diff

## Результат

Полигон получил воспроизводимый слой сравнения запусков поверх уже работающего runtime-
pipeline Блока 4.

Реализовано:

- `lab-summary.html` рядом с существующими JSON/Markdown-отчётами;
- `lab replay --project <id>` для полного воспроизведения одного сценария;
- `lab baseline --input <run> --out <dir> --label <name>`;
- нормализованный `lab-baseline.json` и человекочитаемый `lab-baseline.md`;
- `lab diff --baseline <path> --current <run> --out <dir>`;
- `lab-diff.json`, `lab-diff.md` и `lab-diff.html`;
- единые exit codes через `LabExitCodes`;
- сравнение expected/actual status, TODO, unmapped, unsupported, warning-bearing files,
  project-verify diagnostics, semantic checks, target test counts и длительности;
- нормализация абсолютных путей, temp-каталогов, GUID и timestamp;
- semantic fingerprint generated C# без generated-комментариев, line trivia и отступов;
- performance regression gate: по умолчанию рост более 20% и минимум на 1000 мс;
- PowerShell/Bash smoke-скрипты Блока 5.

## Правила diff

Регрессией считаются, в частности:

- сценарий выполнял expected contract, а текущий запуск перестал;
- выросли TODO, unmapped, unsupported или warning-bearing files;
- появились новые project-verify diagnostics;
- quality/oracle сменились с PASS на FAIL;
- уменьшилось число прошедших target-тестов;
- сценарий исчез из текущего корпуса;
- длительность превысила заданный относительный порог с абсолютной разницей не менее 1 с.

Улучшения классифицируются отдельно и не дают отрицательный exit code. Изменение
нормализованного generated-кода без ухудшения статуса, метрик или oracle помечается как
`CHANGED`, а не как автоматическая регрессия.

## Команды

```powershell
# Воспроизвести один сценарий

dotnet run --project .\Migrator.Cli -c Release --no-build -- `
  lab replay `
  --project p15-webdriverwait-visible `
  --corpus .\corpus\stable\vertical-slice `
  --out .\artifacts\lab\replay\p15 `
  --timeout-seconds 600 `
  --configuration Release

# Сохранить baseline зелёного запуска

dotnet run --project .\Migrator.Cli -c Release --no-build -- `
  lab baseline `
  --input .\artifacts\lab\main `
  --out .\artifacts\lab\baselines\main `
  --label main

# Сравнить PR-запуск с baseline

dotnet run --project .\Migrator.Cli -c Release --no-build -- `
  lab diff `
  --baseline .\artifacts\lab\baselines\main `
  --current .\artifacts\lab\pr `
  --out .\artifacts\lab\diff `
  --duration-regression-percent 20
```

Exit code `0` означает отсутствие регрессий, `10` — найден хотя бы один regression,
`15` — ошибка входных файлов или самого lab command.

## Автоматическая проверка

Из корня репозитория:

```powershell
.\scripts\run-lab-block5.ps1
```

Скрипт не устанавливает Playwright: он использует уже установленный Chromium. Он:

1. собирает решение и запускает полный набор тестов;
2. выполняет зелёный vertical run;
3. сохраняет baseline;
4. выполняет replay `p15`;
5. проверяет clean diff baseline против того же запуска;
6. создаёт synthetic PR regression и проверяет exit code `10`;
7. проверяет наличие JSON/Markdown/HTML-артефактов.

Ожидаемая финальная строка:

```text
Block 5 passed: HTML report, single-scenario replay, normalized baseline, clean diff, and regression exit code 10 are verified.
```
