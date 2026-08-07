# Блок 6 — полный стабильный корпус

## Результат

Полигон расширен с семи проектов вертикального среза до **30 READY-сценариев**.
Стабильный corpus остаётся декларативным и воспроизводимым: каждый проект имеет
`scenario.json`, source/target oracle, budget, точный список файлов и SHA-256 content hash.

## Состав

- 25 сценариев ожидают `PASS`;
- 4 сценария ожидают `UNSUPPORTED_AS_EXPECTED`;
- `p24b-transitive-warning-sabotage` намеренно ожидает `INFRASTRUCTURE_FAILURE`;
- smoke: 7 проектов;
- PR: 18 проектов;
- nightly: все 30 проектов.

Полная матрица: `docs/lab/STABLE_CORPUS_MATRIX.ru.md` и
`corpus/stable/vertical-slice/coverage-matrix.json`.

## Новые возможности runner

- `--suite nightly`;
- `--feature <name[,name]>`, повторяемый и совместимый с project selection;
- suite exit code зависит от **неожиданных** результатов: ожидаемый негативный контракт
  не делает CI красным;
- semantic oracle проверяет активные generated tokens и DOM-свойства `value`, `enabled`,
  `checked`, `visible`.

## LabApp v1

Добавлены детерминированные страницы и endpoint-ы для:

- edit/forms/table/conditional locator;
- Page Object и composition;
- async/setup;
- visible/disappear/custom waits;
- control flow и parameterized tests;
- frame, popup, upload/download;
- Actions API и dynamic/raw negative contracts.

## CI

`.github/workflows/migrator-lab.yml` запускает smoke на pull request и полный stable
corpus по расписанию. Артефакты отчёта загружаются даже при падении.

## Локальная проверка

Из корня репозитория:

```powershell
.\scripts\run-lab-block6.ps1
```

Скрипт не устанавливает Playwright повторно. Он выполняет build, полный набор тестов,
валидацию contracts, smoke, PR, nightly и отдельную проверку `--feature`.

При первой приёмке новые scenarios могут честно обнаружить пробелы мигратора. В таком
случае бюджеты и expected statuses не ослабляются: `artifacts/lab/block-06` используется
для последовательной ремедиации, как в Блоке 4.
