# Результат блока 2: семь готовых фикстур и LabApp v0

Статус: **реализован в исходниках, требуется проверка на Windows с Chrome и .NET SDK 10**.

## Что добавлено

- `LabApp` без нового исполняемого продукта: сервер запускается через существующий CLI;
- маршруты `/login`, `/list`, `/helper`, `/wait`, `/smoke`, `/unsupported`, `/health`;
- клиентский журнал событий `#lab-event-log` на каждой странице;
- детерминированная задержка 250 мс для сценария ожидания;
- параллельная обработка браузерных HTTP-соединений, чтобы speculative connections Chrome не блокировали сервер;
- семь изолированных Selenium/NUnit-фикстур;
- CPM-фикстура с `Directory.Packages.props`;
- transitive `ProjectReference`-фикстура с `Directory.Build.props` и `TreatWarningsAsErrors`;
- unsupported-фикстура `IJavaScriptExecutor.ExecuteScript` с соседним поддерживаемым действием;
- явный список `source.migrationFiles`, чтобы будущий runner не мигрировал browser bootstrap;
- явный `project.entryProject`, используемый smoke-скриптом;
- SHA-256 контракт содержимого каждого готового fixture;
- все семь сценариев переведены в `READY` и перенесены в `corpus/stable/vertical-slice`;
- PowerShell- и Bash-smoke-скрипты блока 2;
- contract- и scenario-тесты LabApp/fixture hash.

## Почему browser bootstrap отделён от migration input

Каждый runtime-проект содержит partial-класс с `ChromeDriver`, навигацией и cleanup, но
`source.migrationFiles` указывает только на мигрируемый Selenium-тест и, где требуется,
helper. Так полигон проверяет реальные исходные тесты, не превращая создание браузера в
случайный шум AST-миграции. Формальный runner начнёт использовать этот контракт в блоке 3.

## Проверка одной командой

Из корня репозитория на Windows:

```powershell
.\scripts\run-lab-block2.ps1
```

Скрипт:

1. собирает основное решение;
2. запускает `lab app serve` на свободном локальном порту;
3. проверяет `/health`;
4. последовательно запускает семь исходных Selenium/NUnit-проектов;
5. выполняет `lab validate --fail-on-planned`.

Ожидаемая итоговая строка:

```text
Block 2 passed: 7 ready fixtures, LabApp health OK, all source tests passed.
```

## Если Selenium Manager недоступен

Можно явно указать Chrome и ChromeDriver:

```powershell
$env:MIGRATOR_LAB_CHROME_BINARY = "C:\Program Files\Google\Chrome\Application\chrome.exe"
$env:MIGRATOR_LAB_CHROMEDRIVER_DIRECTORY = "C:\tools\chromedriver"
.\scripts\run-lab-block2.ps1
```

После успешного прогона следующий шаг владельца:

```text
Фикстуры и LabApp прошли, продолжай блок 3
```
