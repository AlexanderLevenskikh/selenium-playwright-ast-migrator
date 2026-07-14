# Проверка производительности

Migrator считает скорость и ресурсную устойчивость orchestration частью инженерного контракта.

## Архитектура

- `SystemProcessRunner` — единая точка запуска процессов для validation host и CLI-тестов. Он применяет ограниченный timeout, завершает всё дерево процессов и пишет duration, output, exit code и peak working set.
- `OrchestratorScenarioCache` выполняет один запуск на уникальный fingerprint **содержимого** input/config. Одни только пути больше не образуют ключ кэша.
- Каждый scenario-тест получает отдельную материализованную копию snapshot и не может изменить output другого теста.
- Unit-тесты используют `FakeProcessRunner`, `InMemoryFileSystem` и `FakeClock`, не запуская `dotnet` или `pwsh`.
- `xunit.runner.json` ограничивает параллелизм четырьмя потоками. Увеличивать лимит стоит только после измерения памяти.

Для отладки без кэша установите `MIGRATOR_DISABLE_SCENARIO_CACHE=1`.

Исполняемая модель уровней описана в документе [слои тестирования](test-layers.ru.md).

## Performance report и бюджеты

```powershell
./scripts/run-performance-tests.ps1 -Root .
```

Команда запускает закэшированные orchestrator scenarios и E2E smoke единого validation host. JSON, Markdown, TRX, process logs и smoke evidence сохраняются в `artifacts/performance`.

Бюджеты находятся в `Migrator.Tests/performance-budgets.json`:

- soft threshold создаёт заметное предупреждение;
- hard threshold завершает проверку ошибкой при `-Enforce`;
- baseline того же runner дополнительно обнаруживает относительную регрессию.

```powershell
./scripts/run-performance-tests.ps1 `
  -Root . `
  -Baseline artifacts/baseline/orchestrator-performance.json `
  -MaxRegressionRatio 1.35 `
  -Enforce
```

Абсолютное время зависит от машины. Hard limits защищают от runaway-исполнения, а ratio сравнивает прогон с baseline того же runner-класса. Список самых медленных тестов и длительности событий validation host помогают найти источник регрессии.

## Диагностика validation-host smoke

Performance report записывает `validationHostSmokeStdout`, `validationHostSmokeStderr` и ограниченный summary `validationHostSmokeFailure`, если E2E smoke завершился ошибкой. Ожидаемые ненулевые сценарии внутри smoke, например `VALIDATION_HOST_CONFIGURATION_REQUIRED`, сохраняются как process results, а не как PowerShell error records, в том числе под Windows PowerShell 5.1.
