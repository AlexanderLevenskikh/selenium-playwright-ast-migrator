# Результат блока 4: verify-project, Playwright runtime и semantic oracle

Статус: **реализован в исходниках; требуется проверка на Windows с .NET SDK 10,
Chrome/Selenium и установленным Chromium для Playwright**.

## Что добавлено

### Проверка проекта

- `lab run` после существующего migration `run` вызывает существующий `verify-project`;
- для каждого сценария сохраняются:
  - `project-verify-report.json` и Markdown;
  - snapshot временного harness-проекта;
  - classified build diagnostics;
  - `HarnessEvidence` по CPM и импортам MSBuild;
- `p23` проверяет, что CPM обнаружен и изолирован;
- `p24a` проверяет обнаружение entry- и transitive `ProjectReference` без ложной
  классификации как дефекта generated-кода.

### Исполняемый target

- exact generated `.cs` текущего migration run копируются в отдельный NUnit-проект;
- target-проект изолирован от родительских `Directory.Build.props/targets` и CPM;
- namespace-local `PageTest` наследует официальный Playwright NUnit `PageTest`;
- перед каждым тестом открывается маршрут сценария в общем LabApp;
- target test count читается из TRX и сравнивается с `oracle.target.mustPassTests`;
- при падении сохраняются screenshot и Playwright trace ZIP; при успехе trace отбрасывается.

### Поведенческий oracle

- LabApp принимает business events через `POST /__lab/events`;
- вместе с событием сохраняется DOM snapshot элементов с `id`;
- проверяются:
  - упорядоченная подпоследовательность ожидаемых событий;
  - конечные text/visible состояния DOM;
  - semantic time budget для wait-сценария;
  - наличие активных count/text assertions, а не только TODO-комментариев;
  - обязательные и запрещённые diagnostics;
  - сохранность соседнего поддерживаемого действия у unsupported-сценария.

### Качество и отчёты

- бюджеты `todoMax`, `unmappedMax`, `unsupportedMax`, `warningsMax` стали исполняемыми;
- отчёт suite получил target, verify-project, quality и oracle колонки;
- для каждого проекта пишутся:
  - `target/runtime-validation.json`;
  - `target/runtime-observations.json`;
  - `target/semantic-diff.json`;
  - `target/quality-evaluation.json`;
  - target TRX и process logs;
  - failure-only runtime artifacts.

## Важное ожидание первого запуска

Блок 3 уже показал остаточные TODO и unmapped-конструкции в нескольких сценариях.
Блок 4 намеренно превращает compile-safe, но семантически пустой generated-код в
`REGRESSION`. Поэтому первый прогон может завершиться ненулевым exit code и дать новый
список реальных дефектов мигратора. Нельзя автоматически ослаблять бюджеты или менять
expected status: сначала нужно разобрать evidence и исправить мигратор либо доказать, что
контракт сценария неверен.

## Проверка одной командой

```powershell
.\scripts\run-lab-block4.ps1
```

Если Chromium уже установлен для Playwright:

```powershell
.\scripts\run-lab-block4.ps1 -SkipBrowserInstall
```

При полном успехе последняя строка:

```text
Block 4 passed: verify-project, target Playwright runtime, semantic oracle, and quality budgets matched all 7 scenario contracts.
```

При регрессии основной triage-файл:

```text
artifacts/lab/block-04/lab-summary.md
```

Для отправки на разбор удобно упаковать всю папку:

```powershell
Compress-Archive -Path .\artifacts\lab\block-04\* -DestinationPath .\block-04.zip -Force
```

После полного совпадения ожидаемых и фактических статусов следующий шаг владельца:

```text
Oracle понятен, продолжай блок 5
```
