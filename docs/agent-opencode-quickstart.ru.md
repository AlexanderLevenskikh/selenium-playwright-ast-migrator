# Простой запуск миграции через wizard и агента

Эта инструкция для случая, когда Migrator лежит отдельно, а Selenium/PW проекты лежат в другом репозитории продукта.

Пример:

```text
Migrator source:
C:\Users\levenskikh\Desktop\MyProjects\Migrator

Product root:
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb

Selenium tests:
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\MarketerWeb.UIFunctionalTests

Existing Playwright tests/infra:
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\MarketerWeb.PW.FunctionalTests

POM/source truth:
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\MarketerWeb.POM
```

Главный вход для обычной и агентской миграции - `init --wizard`.

`kit init` нужен реже: когда workspace уже создан вручную и надо только добавить/обновить prompt/state/templates. Для нового проекта начинайте с wizard.

## 1. Откройте терминал в product repo

Есть два нормальных варианта. Выберите один и дальше не смешивайте относительные пути.

Вариант A - из корня `billy`:

```powershell
cd C:\Users\levenskikh\Desktop\billy
```

Тогда пути будут такими:

```text
.\Web\MarketerWeb\MarketerWeb.UIFunctionalTests
.\Web\MarketerWeb\MarketerWeb.PW.FunctionalTests
.\Web\MarketerWeb\MarketerWeb.POM
```

Вариант B - из корня `MarketerWeb`:

```powershell
cd C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb
```

Тогда пути будут такими:

```text
.\MarketerWeb.UIFunctionalTests
.\MarketerWeb.PW.FunctionalTests
.\MarketerWeb.POM
```

Не запускайте агентскую миграцию из:

```powershell
C:\Users\levenskikh\Desktop\MyProjects\Migrator
```

Иначе агент может начать чинить код мигратора вместо миграции тестов.

## 2. Почему tool не виден в WebStorm

Local dotnet tool доступен только в папке, где лежит `.config/dotnet-tools.json`, и в ее подпапках.

Если tool был установлен в:

```text
C:\Users\levenskikh\Desktop\MyProjects\Migrator
```

то в:

```text
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb
```

он не обязан быть виден.

Для проекта `billy` создайте local tool manifest в той папке, из которой будете запускать команды: либо `C:\Users\levenskikh\Desktop\billy`, либо `C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb`.

## 3. Установите Migrator tool в MarketerWeb

Если пакет уже опубликован в NuGet:

```powershell
cd C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb

dotnet new tool-manifest
dotnet tool install SeleniumPlaywrightMigrator --version 0.6.0-preview.1
dotnet tool run selenium-pw-migrator -- --help
```

Если пакет собран локально из репозитория Migrator:

```powershell
cd C:\Users\levenskikh\Desktop\MyProjects\Migrator
.\scripts\pack-tool.ps1 -Version 0.6.0-preview.1

cd C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb
dotnet new tool-manifest
dotnet tool install SeleniumPlaywrightMigrator `
  --version 0.6.0-preview.1 `
  --add-source C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\nuget

dotnet tool run selenium-pw-migrator -- --help
```

В Rider, WebStorm и обычном терминале надежнее запускать local tool так:

```powershell
dotnet tool run selenium-pw-migrator -- --help
```

## 4. Создайте workspace через wizard

Из корня `MarketerWeb`:

```powershell
cd C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb

dotnet tool run selenium-pw-migrator -- init --wizard `
  --source-path .\MarketerWeb.UIFunctionalTests `
  --target dotnet `
  --target-test-framework nunit `
  --target-project .\MarketerWeb.PW.FunctionalTests `
  --workspace migration `
  --test-id-attribute data-testid `
  --install-kit
```

Если существующий Playwright проект использует xUnit, замените:

```powershell
--target-test-framework nunit
```

на:

```powershell
--target-test-framework xunit
```

Что делает wizard:

- создает `migration/`;
- создает стартовый `migration/profiles/adapter-config.json`;
- учитывает, что Playwright infra уже есть в `MarketerWeb.PW.FunctionalTests`;
- добавляет agent prompts/state через `--install-kit`;
- пишет следующие команды в `migration/next-commands.md`.

## 5. Проверьте workspace

```powershell
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
```

Стартовый config:

```text
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\migration\profiles\adapter-config.json
```

Если там пока мало mappings, это нормально. Профиль наполняется после анализа POM/helper/source truth.

## 6. Снимите POM и target evidence

Так как POM у вас лежит отдельно, сразу соберите POM index:

Если вы запускаете из `C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb`:

```powershell
dotnet tool run selenium-pw-migrator -- --mode index-pom `
  --input .\MarketerWeb.POM `
  --out migration\reports\pom-index `
  --format both
```

Если вы запускаете из `C:\Users\levenskikh\Desktop\billy`:

```powershell
dotnet tool run selenium-pw-migrator -- --mode index-pom `
  --input .\Web\MarketerWeb\MarketerWeb.POM `
  --out migration\reports\pom-index `
  --format both
```

`POM index = 0` не является блокером само по себе. Это означает только, что текущий indexer не нашел понятных selector facts. Такое бывает, если POM использует проектные фабрики вроде `ByTId(...)`, `CreateControlByTid(...)`, `WithDataTest...`, а не прямые `By.CssSelector(...)`, `By.XPath(...)`, `By.Id(...)`.

Так как Playwright infra уже существует, снимите target discovery:

Если вы запускаете из `C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb`:

```powershell
dotnet tool run selenium-pw-migrator -- --mode discover-target `
  --input .\MarketerWeb.PW.FunctionalTests `
  --out migration\reports\target-discovery `
  --format both
```

Если вы запускаете из `C:\Users\levenskikh\Desktop\billy`:

```powershell
dotnet tool run selenium-pw-migrator -- --mode discover-target `
  --input .\Web\MarketerWeb\MarketerWeb.PW.FunctionalTests `
  --out migration\reports\target-discovery `
  --format both
```

Если POM index пустой, следующий шаг - не останавливаться, а собрать helper inventory и посмотреть реальные POM/helper patterns:

```powershell
dotnet tool run selenium-pw-migrator -- --mode helper-inventory `
  --input .\Web\MarketerWeb\MarketerWeb.POM `
  --out migration\reports\helper-inventory-pom `
  --format both
```

## 7. Первый ручной migration run

```powershell
dotnet tool run selenium-pw-migrator -- --mode migrate `
  --input .\MarketerWeb.UIFunctionalTests `
  --config .\migration\profiles\adapter-config.json `
  --out migration\runs\run-001 `
  --format both
```

Если нужен полный цикл analyze -> migrate -> verify/report, можно вместо этого использовать:

```powershell
dotnet tool run selenium-pw-migrator -- --mode orchestrate `
  --input .\MarketerWeb.UIFunctionalTests `
  --config .\migration\profiles\adapter-config.json `
  --out migration\runs\run-001 `
  --format both
```

## 8. Что открыть в OpenCode

Откройте в OpenCode:

```text
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb
```

Не открывайте:

```text
C:\Users\levenskikh\Desktop\MyProjects\Migrator
```

## 9. Какой prompt дать агенту

Дайте агенту файл:

```text
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\migration\prompts\kickoff-prompt.txt
```

Можно добавить перед prompt короткую рамку:

```text
Работай строго по migration\prompts\kickoff-prompt.txt.
Пиши только в migration/.
MarketerWeb.UIFunctionalTests, MarketerWeb.POM и MarketerWeb.PW.FunctionalTests только читать.
Не редактируй C:\Users\levenskikh\Desktop\MyProjects\Migrator.
```

## 10. Что агенту можно читать

```text
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\MarketerWeb.UIFunctionalTests
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\MarketerWeb.PW.FunctionalTests
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\MarketerWeb.POM
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\migration
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\.agent-loops
```

## 11. Что агенту можно писать

По умолчанию только:

```text
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\migration
```

Например:

```text
migration\profiles\adapter-config.json
migration\runs\run-001
migration\reports
migration\tickets
migration\state
```

## 12. Что агенту нельзя

Без отдельного явного разрешения агенту нельзя править:

```text
C:\Users\levenskikh\Desktop\MyProjects\Migrator
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\MarketerWeb.UIFunctionalTests
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\MarketerWeb.PW.FunctionalTests
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\MarketerWeb.POM
```

Также нельзя:

- искать и чинить `Migrator.Cli`, `Migrator.Core`, `Migrator.Tests`;
- вручную чинить generated files как финальное решение;
- добавлять broad suppressions, чтобы просто уменьшить TODO;
- придумывать selectors по названию свойства;
- останавливать цикл только потому, что compile стал green, если migration board еще показывает actionable work.

## 13. Следующий маленький тикет для агента

После первого run:

```powershell
dotnet tool run selenium-pw-migrator -- kit next-ticket `
  --workspace migration `
  --input migration\runs\run-001
```

Дайте агенту:

```text
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\migration\prompts\generated-next-ticket-prompt.txt
```

## 14. Строгий agent contract

Если хотите дать агенту не общий autopilot, а один строго ограниченный contract:

```powershell
dotnet tool run selenium-pw-migrator -- agent contract `
  --input migration\current-ticket.md `
  --config migration\profiles\adapter-config.json `
  --out migration\agent-contract `
  --format both
```

Дайте агенту:

```text
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb\migration\agent-contract\agent-contract.md
```

## 15. Самый короткий путь

```powershell
cd C:\Users\levenskikh\Desktop\MyProjects\Migrator
.\scripts\pack-tool.ps1 -Version 0.6.0-preview.1

cd C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb
dotnet new tool-manifest
dotnet tool install SeleniumPlaywrightMigrator --version 0.6.0-preview.1 --add-source C:\Users\levenskikh\Desktop\MyProjects\Migrator\artifacts\nuget

dotnet tool run selenium-pw-migrator -- init --wizard --source-path .\MarketerWeb.UIFunctionalTests --target dotnet --target-test-framework nunit --target-project .\MarketerWeb.PW.FunctionalTests --workspace migration --test-id-attribute data-testid --install-kit

dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
dotnet tool run selenium-pw-migrator -- --mode index-pom --input .\MarketerWeb.POM --out migration\reports\pom-index --format both
dotnet tool run selenium-pw-migrator -- --mode discover-target --input .\MarketerWeb.PW.FunctionalTests --out migration\reports\target-discovery --format both
```

Потом:

```text
OpenCode opens:
C:\Users\levenskikh\Desktop\billy\Web\MarketerWeb

Agent prompt:
migration\prompts\kickoff-prompt.txt
```
