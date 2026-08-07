# Migrator Lab stable corpus

Каталог содержит **30 детерминированных Selenium/NUnit-сценариев** первой стабильной
версии полигона.

- `scenario.json` задаёт ожидаемый статус, semantic oracle, бюджеты качества и точные файлы;
- `source.migrationFiles` отделяет мигрируемый код от browser bootstrap и source-only evidence;
- `implementation.contentHash` защищает READY-фикстуру от незаметного изменения;
- все браузерные тесты используют один детерминированный `LabApp`;
- `lab run` проверяет source restore/build/test, текущий migration pipeline,
  `verify-project`, Playwright runtime, semantic oracle и quality budgets;
- `coverage-matrix.json` является машинно-читаемой матрицей корпуса.

Наборы:

- `smoke` — 7 основных сценариев;
- `pr` — 18 сценариев;
- `nightly` — все 30 сценариев;
- `--feature` запускает сценарии по значениям `source.features`.

Положительный и намеренно негативный варианты verification harness разделены:

- `p24a-transitive-warning-isolated` → `PASS`;
- `p24b-transitive-warning-sabotage` → `INFRASTRUCTURE_FAILURE`.

Проверка Блока 6 на Windows из корня репозитория:

```powershell
.\scripts\run-lab-block6.ps1
```

Playwright Chromium должен быть уже установлен. Скрипт не скачивает браузеры.
