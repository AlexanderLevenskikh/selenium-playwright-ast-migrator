# План реализации испытательного полигона

Полигон развивается внутри текущего продукта как проект `Migrator.Lab` и команда
`selenium-pw-migrator lab ...`. Он не содержит второй реализации миграции: запуск,
проверка generated-кода и `verify-project` остаются в существующих командах CLI.

Главный критерий готовности корпуса: **каждый сценарий получил ровно свой ожидаемый
статус; неожиданных результатов — 0**. Процент зелёных сценариев сам по себе не является
критерием, потому что часть фикстур обязана завершаться как
`UNSUPPORTED_AS_EXPECTED` или намеренно воспроизводить инфраструктурный сбой.

## Блок 1. Контракт и реестр сценариев — проверен

Состав:

- проект `Migrator.Lab` без отдельного исполняемого файла;
- контракт `lab-scenario/v1` и JSON Schema;
- статусы сценариев и состояние реализации `PLANNED`/`READY`;
- загрузчик и семантическая проверка сценария;
- поиск дубликатов ID и небезопасных путей;
- отчёты проверки контракта в JSON и Markdown;
- команды `lab validate` и `lab list`;
- план вертикального среза из семи сценариев;
- contract-тесты.

Команды:

```powershell
selenium-pw-migrator lab validate `
  --corpus ./corpus/stable/vertical-slice `
  --out ./artifacts/lab/contracts

selenium-pw-migrator lab list `
  --corpus ./corpus/stable/vertical-slice `
  --state ready
```

Переход к следующему блоку разрешён, когда решение собирается, все тесты проходят,
а `lab validate` сообщает `7 valid, 0 invalid`.

### Что делает владелец после блока 1

1. Собирает решение и запускает тесты командами из раздела «Проверка блока 1» ниже.
2. Запускает `lab validate` из исходников.
3. Присылает полный вывод только при ошибке. При успехе достаточно написать:
   `Блок 1 прошёл, продолжай блок 2`.

## Блок 2. Семь настоящих фикстур и LabApp v0 — проверен

Цель: заменить плановые записи реальными, детерминированными проектами.

Состав:

- единый лёгкий `LabApp` с HTTP-host;
- страницы login, list, helper, wait и smoke;
- event log и чтение итогового DOM-состояния;
- исходные Selenium/NUnit-проекты для `p01`, `p04`, `p09`, `p15`;
- проектные фикстуры `p23`, `p24a`;
- ожидаемо неподдерживаемая фикстура `p26`;
- перевод готовых сценариев из `PLANNED` в `READY`;
- проверка, что повторная генерация/подготовка не меняет зафиксированные файлы.

Критерий: семь исходных фикстур собираются; runtime-фикстуры проходят на LabApp;
`lab validate --fail-on-planned` возвращает 0.

### Что делает владелец после блока 2

На Windows из корня репозитория запускает:

```powershell
.\scripts\run-lab-block2.ps1
```

Нужны .NET SDK 10 и установленный Google Chrome. Selenium Manager автоматически
подбирает ChromeDriver. При успехе пишет:
`Фикстуры и LabApp прошли, продолжай блок 3`.

## Блок 3. Source validation и stage-aware orchestration — проверен

Цель: выполнить первые стадии настоящего `lab run` без подмены существующего CLI.

Состав:

- `lab run --suite vertical|smoke|pr` и выбор отдельных проектов через `--project`;
- отдельная копия исходного fixture для каждого запуска;
- последовательные `dotnet restore`, `dotnet build`, `dotnet test`;
- единый LabApp на случайном свободном порту на весь suite;
- TRX-проверка ожидаемого числа исходных тестов;
- запуск уже существующего `selenium-pw-migrator run` на `source.migrationFiles`;
- проверка `orchestration-report.json`, generated и verify-артефактов;
- защита от изменения заявленных исходных файлов;
- stdout/stderr и длительность каждой стадии;
- `source-validation.json`, `scenario-result.json`, `lab-summary.json` и `.md`;
- timeout и принудительное завершение дерева процессов;
- первичная классификация `SOURCE_INVALID`, `MIGRATOR_FAILURE`,
  `INFRASTRUCTURE_FAILURE`, `REGRESSION` и `UNSUPPORTED_AS_EXPECTED`;
- стабильные suite exit codes 0/10–15;
- unit/scenario-тесты классификатора и оркестратора с поддельным process runner.

Критерий: семь fixture проходят source validation; существующий migration run создаёт
обязательные артефакты; нет ложного смешивания проблем исходника, мигратора и среды.

### Что делает владелец после блока 3

На Windows из корня репозитория запускает:

```powershell
.\scripts\run-lab-block3.ps1
```

Скрипт собирает решение, запускает полный тестовый набор Migrator и выполняет полный вертикальный suite.
При успехе последняя строка:

```text
Block 3 passed: 7 source projects validated, existing migration run executed, suite statuses classified.
```

После этого пишет: `Классификация стадий верна, продолжай блок 4`.

## Блок 4. Verify-project, Playwright runtime и поведенческий oracle — проверен

Цель: доказать не только синтаксическую корректность generated-кода, но и сохранение
исполняемого поведения.

Состав:

- повторное использование существующего `verify` внутри `run` и отдельный вызов
  существующего `verify-project`;
- чтение `project-verify-report.json`, classified diagnostics и `HarnessEvidence`;
- isolated Playwright .NET/NUnit runtime-проект из точных generated-файлов текущего run;
- runtime navigation на страницу сценария через общий LabApp;
- сравнение числа target test cases с контрактом;
- серверный event log и snapshots конечного DOM после каждого business event;
- ordered-event, DOM, test-count, generated-assertion и timeout oracles;
- отдельный oracle для `UNSUPPORTED_AS_EXPECTED`: ожидаемая диагностика плюс сохранность
  соседнего поддерживаемого действия;
- бюджеты TODO, unmapped, unsupported и warning-bearing files;
- trace ZIP и screenshot только при падении target-теста;
- `runtime-validation.json`, `semantic-diff.json`, `quality-evaluation.json` и расширенный
  suite report.

Критерий: каждый сценарий получает ровно ожидаемый статус. Первый запуск может честно
обнаружить регрессии самого мигратора; это результат работы полигона, а не повод менять
expected status или ослаблять oracle.

### Что делает владелец после блока 4

На Windows из корня репозитория запускает:

```powershell
.\scripts\run-lab-block4.ps1
```

Если suite обнаружит регрессии, присылает `artifacts/lab/block-04/lab-summary.md` и архив
папки `artifacts/lab/block-04`. После устранения найденных дефектов и полного совпадения
статусов пишет: `Oracle понятен, продолжай блок 5`.

Текущий vertical slice полностью принят:

- `p01-basic-id-login` — PASS;
- `p04-findelements-count-text` — PASS;
- `p09-helper-extension-mapping` — PASS;
- `p15-webdriverwait-visible` — PASS;
- `p23-cpm-isolation` — PASS;
- `p24a-transitive-warning-isolated` — PASS;
- `p26-jsexecutor-unsupported` — UNSUPPORTED_AS_EXPECTED.

## Блок 5. Suite report, replay и baseline diff — принят

Состав:

- `lab-summary.json`, `.md`, `.html`;
- единая модель exit codes `0`, `10–15`;
- `lab replay --project <id>` через тот же полный runtime pipeline;
- `lab baseline` с нормализованным machine-readable snapshot;
- `lab diff` со сравнением статуса, diagnostics, quality budgets, semantic outcome,
  target test counts, generated fingerprint и времени;
- нормализация путей, temp-каталогов, GUID, timestamps и generated trivia;
- JSON/Markdown/HTML diff report;
- regression exit code `10`, improvement/changed без ложного CI failure.

Критерий: одна команда воспроизводит отдельный сценарий, clean diff не даёт изменений,
а synthetic regression детектируется и возвращает exit code `10`.

### Что делает владелец после блока 5

Из корня репозитория запускает:

```powershell
.\scripts\run-lab-block5.ps1
```

Playwright повторно не скачивается. После успешной финальной строки пишет:
`Replay и diff работают, продолжай блок 6`.

## Блок 6. Полный стабильный корпус из 30 проектов — реализован, ожидает локальной приёмки

Состав:

- расширение корпуса по утверждённому каталогу;
- отдельные сценарии для положительного и намеренно саботажного варианта `p24`;
- smoke/PR/nightly-разметка;
- матрица покрытия;
- запуск затронутых scenarios по feature tags;
- CI: smoke на PR, полный stable по расписанию.

Критерий: 30 сценариев детерминированы, каждый получает ожидаемый статус, PR-бюджет
остаётся приемлемым.

Реализовано:

- 30 READY-сценариев: 25 `PASS`, 4 `UNSUPPORTED_AS_EXPECTED`, 1 ожидаемый `INFRASTRUCTURE_FAILURE`;
- 7 smoke, 18 PR и 30 nightly-сценариев;
- положительный `p24a` и намеренно саботажный `p24b` разделены;
- `--feature` выбирает сценарии по `source.features`;
- LabApp v1 покрывает формы, таблицы, Page Object, waits, popup/frame/upload/download и негативные сценарии;
- машинная и человекочитаемая матрицы покрытия;
- GitHub Actions: smoke на pull request, полный stable по расписанию.

### Что делает владелец после блока 6

Проверяет длительность PR и nightly на реальном runner. Если PR слишком долгий,
утверждает новый состав обязательного PR-набора, после чего пишет:
`Stable corpus принят, продолжай блок 7`.

## Блок 7. Seedable generation и metamorphic testing

Состав:

- параметризованные шаблоны вместо свободного C#-AST fuzzing;
- сохранение seed и полного окружения;
- pairwise-комбинации;
- переименование, `var`/явный тип, namespace shape, перенос файла и alias using;
- проверка детерминированности генератора;
- автоматическое сохранение полезного seed как regression candidate.

Критерий: повтор seed создаёт тот же проект и outcome; invalid fixtures не доминируют
над полезными находками.

### Что делает владелец после блока 7

Просматривает первые найденные seed-кейсы и решает, какие перевести в постоянные
регрессии. Затем пишет: `Seeds проверены, продолжай блок 8`.

## Блок 8. Минимизация, кластеризация и задания агенту

Состав:

- feature-aware reducer;
- кластеризация по stage + diagnostic code + semantic diff + feature tags;
- bounded task pack с evidence, кодом мигратора, repro и definition of done;
- перевод исправленного дефекта в unit-test/project fixture/saved seed;
- редкий real-project release gate.

Критерий: найденный сбой превращается в компактное воспроизводимое задание, а после
исправления автоматически закрепляется на нужном уровне регрессии.

### Что делает владелец после блока 8

Утверждает policy: какие кластеры агент исправляет автоматически, а какие требуют
ручного решения. После этого полигон считается введённым в постоянную эксплуатацию.

## Текущая проверка после блока 5

Из корня репозитория на Windows:

```powershell
.\scripts\run-lab-block5.ps1
```

Скрипт использует уже установленный Playwright Chromium и не скачивает браузеры повторно.
Он проверяет полный vertical run, HTML report, baseline, replay, clean diff и synthetic
regression с exit code `10`.

Ручное сохранение baseline:

```powershell
dotnet run --project .\Migrator.Cli -c Release --no-build -- `
  lab baseline `
  --input .\artifacts\lab\main `
  --out .\artifacts\lab\baselines\main `
  --label main
```

Ручной PR-diff:

```powershell
dotnet run --project .\Migrator.Cli -c Release --no-build -- `
  lab diff `
  --baseline .\artifacts\lab\baselines\main `
  --current .\artifacts\lab\pr `
  --out .\artifacts\lab\diff
```

Воспроизведение одного сценария:

```powershell
dotnet run --project .\Migrator.Cli -c Release --no-build -- `
  lab replay `
  --project p15-webdriverwait-visible `
  --corpus .\corpus\stable\vertical-slice `
  --out .\artifacts\lab\replay-p15 `
  --timeout-seconds 600 `
  --configuration Release
```

Bash:

```bash
./scripts/run-lab-block5.sh
```
