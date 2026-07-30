# План реализации испытательного полигона

Полигон развивается внутри текущего продукта как проект `Migrator.Lab` и команда
`selenium-pw-migrator lab ...`. Он не содержит второй реализации миграции: запуск,
проверка generated-кода и `verify-project` остаются в существующих командах CLI.

Главный критерий готовности корпуса: **каждый сценарий получил ровно свой ожидаемый
статус; неожиданных результатов — 0**. Процент зелёных сценариев сам по себе не является
критерием, потому что часть фикстур обязана завершаться как
`UNSUPPORTED_AS_EXPECTED` или намеренно воспроизводить инфраструктурный сбой.

## Блок 1. Контракт и реестр сценариев — реализован, ожидает проверки

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
  --corpus ./corpus/planning/vertical-slice `
  --out ./artifacts/lab/contracts

selenium-pw-migrator lab list `
  --corpus ./corpus/planning/vertical-slice `
  --state planned
```

Переход к следующему блоку разрешён, когда решение собирается, все тесты проходят,
а `lab validate` сообщает `7 valid, 0 invalid`.

### Что делает владелец после блока 1

1. Собирает решение и запускает тесты командами из раздела «Проверка блока 1» ниже.
2. Запускает `lab validate` из исходников.
3. Присылает полный вывод только при ошибке. При успехе достаточно написать:
   `Блок 1 прошёл, продолжай блок 2`.

## Блок 2. Семь настоящих фикстур и LabApp v0

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

Запускает проверку исходных фикстур на своей машине с установленным браузером. При
успехе пишет: `Фикстуры и LabApp прошли, продолжай блок 3`.

## Блок 3. Source validation и stage-aware orchestration

Цель: выполнить первые стадии настоящего lab-run без подмены существующего CLI.

Состав:

- `lab run --suite smoke|vertical`;
- отдельные временные директории;
- restore/build/test исходного проекта;
- запуск существующего `run`;
- проверка обязательных stage-артефактов;
- `source-validation.json` и журнал процессов;
- timeout, отмена и завершение дерева процессов;
- первичная классификация `SOURCE_INVALID`, `MIGRATOR_FAILURE`,
  `INFRASTRUCTURE_FAILURE`.

Критерий: runner правильно различает сломанный исходник, падение CLI и проблему среды.

### Что делает владелец после блока 3

Запускает `lab run --suite vertical` и проверяет, что причины искусственно внесённых
сбоев классифицируются разными статусами. Затем пишет:
`Классификация стадий верна, продолжай блок 4`.

## Блок 4. Verify, verify-project и поведенческий oracle

Цель: доказать не только компиляцию, но и сохранение поведения.

Состав:

- вызов существующих `verify` и `verify-project`;
- runtime Playwright .NET;
- сравнение числа test cases;
- сравнение event log и конечного DOM/app state;
- особый oracle для `UNSUPPORTED_AS_EXPECTED`;
- traces/screenshots только при runtime-падении;
- бюджеты TODO, unmapped, unsupported и warnings.

Критерий: все семь сценариев получают строго ожидаемые статусы, неожиданных — 0.

### Что делает владелец после блока 4

Просматривает один успешный и один неуспешный отчёт, убеждается, что из него понятно,
что именно сломалось. Затем пишет: `Oracle понятен, продолжай блок 5`.

## Блок 5. Suite report, replay и baseline diff

Состав:

- `lab-summary.json`, `.md`, `.html`;
- единая модель exit codes;
- `lab replay --project <id>`;
- baseline текущей основной ветки;
- сравнение статуса, diagnostics, quality budgets, semantic outcome и времени;
- нормализация путей, временных файлов и generated trivia.

Критерий: одна команда воспроизводит отдельный сценарий, а diff явно показывает
регрессии и улучшения.

### Что делает владелец после блока 5

Сохраняет baseline основной ветки и запускает тестовый PR-diff. После проверки пишет:
`Replay и diff работают, продолжай блок 6`.

## Блок 6. Полный стабильный корпус из 30 проектов

Состав:

- расширение корпуса по утверждённому каталогу;
- отдельные сценарии для положительного и намеренно саботажного варианта `p24`;
- smoke/PR/nightly-разметка;
- матрица покрытия;
- запуск затронутых scenarios по feature tags;
- CI: smoke на PR, полный stable по расписанию.

Критерий: 30 сценариев детерминированы, каждый получает ожидаемый статус, PR-бюджет
остаётся приемлемым.

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

## Проверка блока 1

Из корня репозитория:

```powershell
dotnet restore
dotnet build -c Release --no-restore
dotnet test Migrator.Tests\Migrator.Tests.csproj -c Release --no-build

dotnet run --project Migrator.Cli -- lab validate `
  --corpus ./corpus/planning/vertical-slice `
  --out ./artifacts/lab/contracts

dotnet run --project Migrator.Cli -- lab list `
  --corpus ./corpus/planning/vertical-slice `
  --state planned
```

Bash:

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet test Migrator.Tests/Migrator.Tests.csproj -c Release --no-build

dotnet run --project Migrator.Cli -- lab validate \
  --corpus ./corpus/planning/vertical-slice \
  --out ./artifacts/lab/contracts

dotnet run --project Migrator.Cli -- lab list \
  --corpus ./corpus/planning/vertical-slice \
  --state planned
```
