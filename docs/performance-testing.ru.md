# Проверка производительности

Migrator считает скорость и ресурсную устойчивость orchestration частью инженерного контракта.

## Зачем

Интеграционные orchestration-тесты запускают тяжёлые Roslyn-процессы и PowerShell-сценарии. Повтор одного и того же запуска ради каждой отдельной проверки расходует минуты и при ограниченной памяти вызывает каскадные таймауты.

## Архитектура тестов

- `OrchestratorScenarioCache` выполняет каждый уникальный сценарий `(input, config)` один раз на процесс тестов.
- Каждый тест получает независимую копию snapshot, поэтому тесты не делят изменяемый каталог.
- `CliTestRunner` измеряет время и peak working set, а при таймауте завершает всё дерево процессов.
- `xunit.runner.json` ограничивает параллелизм четырьмя потоками.

Для отладки без кэша установите `MIGRATOR_DISABLE_SCENARIO_CACHE=1`.

## Baseline и проверка регрессии

```powershell
./scripts/run-performance-tests.ps1 -Root .
```

Отчёты JSON, Markdown и TRX появятся в `artifacts/performance`.
Сравнение с предыдущим baseline:

```powershell
./scripts/run-performance-tests.ps1 `
  -Root . `
  -Baseline artifacts/baseline/orchestrator-performance.json `
  -MaxRegressionRatio 1.35 `
  -Enforce
```

Абсолютное время зависит от компьютера, поэтому основной gate сравнивает прогон с baseline того же runner-класса. Список самых медленных тестов помогает быстро найти источник регрессии.
