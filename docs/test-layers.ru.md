# Слои тестирования

Migrator отделяет быстрые детерминированные проверки от тяжёлых end-to-end сценариев. Это исполняемое разделение: xUnit traits выбирают слой, а `scripts/run-test-layer.*` запускает его напрямую.

## Слои

| Слой | Назначение | Внешние процессы | Когда запускать |
|---|---|---:|---|
| `Unit` | Модели, правила путей, процессная оркестрация через fake-объекты, ключи кэша | Нет | После каждого локального изменения и в PR |
| `Contract` | CLI wiring, схемы, промпты, safety-инварианты и документация | Нет | В каждом PR |
| `Scenario` | Собранный CLI на маленьких или закэшированных workspace | Да, ограниченно | В PR или при отладке |
| `E2E` | Полный lifecycle validation host с evidence | Да | Full validation, nightly, release |

Запуск отдельного слоя:

```powershell
./scripts/run-test-layer.ps1 -Root . -Layer Unit
./scripts/run-test-layer.ps1 -Root . -Layer Contract
./scripts/run-test-layer.ps1 -Root . -Layer Scenario
./scripts/run-test-layer.ps1 -Root . -Layer E2E -NoBuild
```

Запуск всего оптимизированного контура:

```powershell
./scripts/run-test-layer.ps1 -Root . -Layer All
```

Обычный `dotnet test Migrator.sln` остаётся источником истины для полного regression suite. Слои ускоряют обратную связь, но не разрешают пропускать обязательную финальную проверку.

Runner не сообщает успех, если выбранный фильтром слой обнаружил ноль тестов. Для слоя Unit он объединяет явный trait `Layer=Unit` с соглашением по имени класса `*UnitTests`, потому что старые адаптеры xUnit/VSTest могут не включить class-level custom traits в filtered discovery при запуске устаревшей сборки с `--no-build`. В таком случае сначала пересобери проект и повтори запуск.

PowerShell-runner’ы предпочитают `pwsh`, когда он установлен, но Windows PowerShell может выполнить compatibility path. Формирование process arguments, относительные пути, определение Windows и остановка по timeout не используют PowerShell 7-only API в fallback-режиме.

## Тестовые швы

В `Migrator.Core` находятся `IProcessRunner`, `IFileSystem` и `IClock`. Unit-тесты используют `FakeProcessRunner`, `InMemoryFileSystem` и `FakeClock`, поэтому не запускают `dotnet` или `pwsh` для каждой проверки.

`CliTestRunner` делегирует запуск общему `SystemProcessRunner`. `OrchestratorScenarioCache` хэширует содержимое входов и конфигурации и выдаёт каждому тесту отдельную копию snapshot: кэш изолирован и инвалидируется при изменении fixture.
