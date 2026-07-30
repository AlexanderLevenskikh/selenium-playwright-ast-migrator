# Результат блока 1: контракт и реестр сценариев

Статус: **реализован в исходниках, требуется сборка и запуск тестов в среде .NET SDK 10**.

## Что добавлено

- библиотека `Migrator.Lab`, подключённая к существующему CLI;
- `lab-scenario/v1` и `schemas/lab-scenario.schema.json`;
- строгая проверка обязательных и неизвестных полей;
- проверка ID, тегов, бюджетов, относительных путей и файлов готового сценария;
- статусы результата и состояние `PLANNED`/`READY`;
- каталог сценариев с обнаружением дубликатов ID;
- JSON/Markdown-отчёты;
- команды `lab validate` и `lab list`;
- семь сценариев первого вертикального среза в состоянии `PLANNED`;
- contract-тесты на каталог, схему, безопасные пути, отчёты и CLI surface.

## Что намеренно не сделано в этом блоке

- Selenium-проекты ещё не созданы;
- LabApp и браузерный runtime ещё не созданы;
- `lab run`, `replay`, `diff` и semantic oracle ещё отсутствуют;
- сценарии нельзя считать готовыми: `--fail-on-planned` должен завершаться ошибкой до блока 2.

## Проверка

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

Ожидаемый итог `lab validate`:

```text
Migrator Lab contract validation: 7 valid, 0 invalid, 0 ready, 7 planned.
```

`lab validate --fail-on-planned` на этом этапе **обязан** вернуть exit code 15. Это защита
от случайного запуска незавершённого корпуса как полноценного испытательного набора.
