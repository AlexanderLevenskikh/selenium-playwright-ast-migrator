# Руководство пользователя Migrator

Migrator - это CLI-инструмент для управляемого переноса Selenium end-to-end тестов на Playwright.

Он не пытается "магически" идеально переписать весь проект за один запуск. Вместо этого он дает понятный цикл:

```text
посмотреть старые тесты -> собрать доказательства -> сгенерировать Playwright -> проверить -> улучшить профиль -> повторить
```

Основной production-сценарий - Selenium C# в Playwright .NET. NUnit используется по умолчанию, xUnit тоже поддерживается. Playwright TypeScript, Java Selenium и Python Selenium пока считаются preview-направлениями.

## Happy path

Lifecycle harness-запуска принадлежит `new-harness-run.ps1`: агенты используют установленные скрипты Harness Kit, а не придумывают папки `migration/runs` вручную.

Сначала установите CLI. Рекомендуемый публичный путь — npm wrapper: он скачивает matching standalone CLI и не требует .NET SDK:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
bash playground/commands.sh
selenium-pw-migrator playground verify --input playground --out playground-verify --format both
```

Для реального product repo начните с onboarding wizard и репрезентативного pilot-среза:

```bash
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

Для настоящей миграции с агентом:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
# устаревший shortcut для Windows OpenCode Desktop:
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --project-desktop
# handoff не для OpenCode:
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests
```

### PowerShell 7 для shell-wrapper’ов migration-kit

Установленный через npm или standalone CLI не требует PowerShell. Но shell-wrapper’ы lifecycle в migration-kit требуют его: entrypoint’ы `migration/scripts/*.sh` на macOS/Linux/WSL вызывают ту же `.ps1`-реализацию через PowerShell 7 (`pwsh`). Установите PowerShell 7 перед их запуском: https://learn.microsoft.com/powershell/scripting/install/installing-powershell. Затем выполните:

```bash
selenium-pw-migrator kit doctor --workspace migration
```

Отчёт doctor содержит проверку `powershell-7` и ссылается на инструкции по установке, если `pwsh` отсутствует.

После run первым открывайте dashboard:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

Откройте `migration/dashboard/latest/report-dashboard.html` до чтения raw artifacts. Если остались TODO, запустите `explain-todo`: он пишет `suggested-config-patch.md/json` с grouped root causes, confidence/evidence badges и черновиками profile mappings для review.

Стабильный production-путь — Selenium C# -> Playwright .NET. Java, Python и Playwright TypeScript считаются experimental preview до тех пор, пока reports и target-project checks не докажут готовность.

## 1. Главная идея

Migrator лучше воспринимать как помощника миграции, а не как слепой text replace.

Инструмент читает Selenium-тесты и проектные соглашения, строит промежуточную модель действий, применяет JSON-профиль, генерирует Playwright-тесты и пишет отчеты: что получилось, что сомнительно, что нужно доказать, что стоит чинить следующим.

Самое важное понятие - source truth, то есть доказательство из исходного или целевого проекта. Хорошие доказательства:

- Selenium PageObject selectors.
- Уже существующие Playwright PageObjects или test base classes.
- Проектные helper-методы с понятным поведением.
- Реальные атрибуты `data-testid`, `data-tid`, CSS, XPath или resolved selector constants.
- Явный adapter config, который кто-то проверил.

Если доказательств мало, Migrator должен оставить TODO или отчет, а не генерировать опасную догадку.

## 2. Установка и запуск

### Быстрая npm-установка

Используйте npm, если нужен самый простой public install/update path:

```shell
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
npm update -g selenium-pw-migrator
selenium-pw-migrator self update
```

`selenium-pw-migrator self update` печатает channel-specific update command и не мутирует global installs автоматически. Совместимая mode-форма диагностики: `--mode install-doctor`.

### Быстрая standalone-установка

Используйте standalone, если npm недоступен и не хочется ставить .NET SDK или .NET Runtime:

```powershell
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1" -OutFile $installer
& $installer
selenium-pw-migrator --version
selenium-pw-migrator doctor install
```

### Альтернатива: установка через NuGet/dotnet tool

Используйте dotnet tool, если нужна global/local .NET tool установка или закрепление версии через `.config/dotnet-tools.json`. Для использования мигратора репозиторий клонировать не нужно:

```bash
dotnet tool install --global SeleniumPlaywrightMigrator --source https://api.nuget.org/v3/index.json --prerelease
selenium-pw-migrator --help
```

Для командного репозитория используйте project-local dotnet tool manifest:

```bash
dotnet new tool-manifest
dotnet tool install SeleniumPlaywrightMigrator --source https://api.nuget.org/v3/index.json --prerelease
dotnet tool run selenium-pw-migrator -- --help
```

Затем создайте одноразовый demo playground:

```bash
dotnet tool run selenium-pw-migrator -- playground \
  --out playground \
  --target-test-framework xunit \
  --generation-policy conservative
```

Откройте `playground/try-this-first.md` и прогоните готовую цепочку команд перед настоящим проектом.

Относительный путь `--out playground` вычисляется от текущей директории. Сгенерированные command-скрипты сохраняют run artifacts внутри выбранной папки playground, поэтому нестандартный `--out` остаётся самодостаточным.

Если хочется сначала понять AST-модель миграции, откройте teaching demo и статью:

- `examples/teaching-demo/README.md`
- `docs/articles/ast-migration-explained.md`
- `docs/articles/ast-migration-explained.ru.md`

### Сборка и установка локального standalone `win-x64`

Это рекомендуемый путь для проверки release candidate: устанавливается тот же self-contained layout, который затем публикуется в GitHub Releases. На машине сборки нужен .NET SDK, но после установки CLI не требует ни .NET SDK, ни .NET Runtime.

Запустите из корня репозитория в Windows PowerShell:

```powershell
$version = "0.0.0-preview.20"

Unblock-File .\scripts\*.ps1
.\scripts\package-standalone.ps1 `
  -Version $version `
  -Runtimes win-x64

$archive = ".\artifacts\release\selenium-pw-migrator-$version-win-x64.zip"
.\scripts\install-standalone.ps1 `
  -Version $version `
  -Runtime win-x64 `
  -ArchivePath $archive `
  -ChecksumsPath ".\artifacts\release\checksums.sha256"

selenium-pw-migrator --version
selenium-pw-migrator --help
Get-Command selenium-pw-migrator -All
```

Архив создаётся в `artifacts/release/`. Установщик по умолчанию заменяет файлы в `%USERPROFILE%\.selenium-pw-migrator\bin`, ставит эту папку первой в user `PATH` и обновляет `PATH` текущей PowerShell-сессии. Если старая global dotnet tool всё ещё мешает разрешению команды, добавьте к вызову установщика `-RemoveDotnetTool`. Для изолированной проверки используйте `-InstallDir "$env:LOCALAPPDATA\selenium-pw-migrator-dev"`.

### Альтернатива: локально собранный dotnet tool package

Этот вариант проверяет NuGet/dotnet-tool упаковку, а не standalone. Используйте одну и ту же версию при упаковке и установке:

```bash
version="0.0.0-preview.20"
./scripts/pack-tool.sh "$version"
dotnet new tool-manifest --force
dotnet tool install SeleniumPlaywrightMigrator \
  --version "$version" \
  --add-source ./artifacts/nuget
dotnet tool run selenium-pw-migrator -- --help
```

На Windows используйте `./scripts/pack-tool.ps1 -Version $version` и `./scripts/install-local-tool.ps1 -Version $version`. Windows helper также умеет ставить самый новый локальный пакет, если не передавать `-Version`.

### Запуск из исходников

Из исходников:

```bash
dotnet restore
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --help
```

Во всех сценариях ниже используется установленная команда `selenium-pw-migrator ...`, как после npm или standalone. Если вы выбрали local dotnet tool manifest, добавляйте префикс `dotnet tool run selenium-pw-migrator --`. Если запускаете из исходников, заменяйте установленную команду на:

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- 
```

Например:

```bash
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --mode analyze --input ./OldTests --out analysis
```

## 3. Первый правильный запуск

Начинайте с playground или маленького representative pilot slice. Не запускайте первый прогон на весь suite.

```shell
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
bash playground/commands.sh
selenium-pw-migrator playground verify --input playground --out playground-verify --format both
```

Для настоящего product repo предпочитайте `start`, а не старый ручной wizard:

```shell
selenium-pw-migrator start --input ./OldTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./OldTests --max-tests 10 --out migration/pilot
```

`start` создаёт onboarding-state:

- `migration/current-ticket.md` - активный bounded migration scope.
- `migration/next-commands.md` - точные следующие команды под выбранный route.
- `migration/profiles/adapter-config.start.json` - starter profile skeleton.
- `migration/state/start-dispatch.json` - no-menu dispatch state для `/supervised-task`.

`pilot` создаёт честный bounded input:

- `migration/pilot/pilot-selection.md/json` - почему выбраны эти файлы.
- `migration/pilot/selected-tests.txt` - выбранные source files.
- `migration/pilot/selected-input/` - копия pilot input.
- `migration/pilot/next-commands.md` - analyze/migrate команды по `selected-input`, а не по всему suite.

Для OpenCode после `start` поставьте agent team:

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json --opencode-install auto
```

Для существующего workspace команда сначала обновляет управляемый pack `migration/opencode-team/**`, а затем заново синхронизирует `.opencode/agents` и `.opencode/commands` в корне репозитория. Новые режимы `/supervised-task` должны появляться без `--force`; если старая команда остаётся `unchanged`, используется более ранняя версия с этим дефектом.

Потом запустите `/supervised-task`. После успешного FINAL/PASS checkpoint `/supervised-task` по умолчанию останавливается для review. Используйте `/supervised-task continue`, чтобы запустить post-final TODO/source-truth research без подробного prompt для supervisor. Supervised agent должен прочитать `current-ticket.md` и `state/start-dispatch.json`, создать или возобновить `migration/runs/<run-id>/` и не задавать пользователю широкие вопросы, если state понятен.

### Режимы запуска OpenCode `/supervised-task`

| Запуск | Назначение |
|---|---|
| `/supervised-task` | Возобновить следующую безопасную bounded-задачу из сохранённого state. |
| `/supervised-task <bounded request>` | Выполнить конкретную ограниченную задачу без обхода state и gates. |
| `/supervised-task waves` | Запустить/возобновить affinity-aware wavefront migration. Алиасы: `wave`, `wavefront`, `start waves`. |
| `/supervised-task waves fresh` | Архивировать текущий pilot, сохранить memory/scope и перепланировать. Алиасы: `fresh waves`, `restart waves`. |
| `/supervised-task continue` | Возобновить post-final research → review → slicing → bounded execution loop. |
| `/supervised-task continue <topic or task>` | Продолжить с указанной темой исследования или bounded-запросом. |
| `/supervised-task sentinel` | Выполнить одну forensic process inspection. Алиасы: `inspect`, `qa`. |

Глубина Harness выбирается через `--execution-profile`:

| Профиль | Значение | Пример |
|---|---|---|
| `fast` | Облегчённый режим и значение по умолчанию; дорогие роли включаются по риску. | `/supervised-task waves --execution-profile fast` |
| `standard` | Executor плюс reviewer; watchdog/sentinel остаются условными. | `/supervised-task waves --execution-profile standard` |
| `audit` | Полный Harness; обязательны executor, reviewer, watchdog и sentinel. | `/supervised-task waves --execution-profile audit` |

Модификатор работает также с обычным resume, `continue`, bounded-запросом и `continuous`. Уже созданная wave сохраняет неизменяемый `execution-policy.json`; для смены профиля используй fresh run.

Добавь `continuous` или `--continuation auto` к обычному resume, bounded-запросу, `continue`, `waves` или `waves fresh`, если текущий invocation должен сам проходить безопасные checkpoints:

```text
/supervised-task continuous
/supervised-task --continuation auto
/supervised-task continue continuous
/supervised-task continue --continuation auto
/supervised-task waves continuous
/supervised-task waves --continuation auto
/supervised-task waves fresh continuous
```

Continuous-режим записывает каждый checkpoint, но не делает паузу только ради следующего `continue`. Он останавливается на DONE, limitations, blocker, human decision, critical risk, scope violation, malformed evidence, no-progress, missing input, исчерпании budgets или явной остановке пользователя. `sentinel`, `inspect` и `qa` остаются одноразовыми. Полный контракт: [`docs/supervised-task-modes.ru.md`](docs/supervised-task-modes.ru.md).

Для Codex, CI или другого агента используйте явный handoff:

```shell
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json
```

`bootstrap-opencode --opencode-install ci` остаётся legacy compatibility mode, но новые non-OpenCode setup должны использовать `bootstrap-agent`.

Если вы работаете без агента и нужен только старый starter config/scaffold, `init --wizard` всё ещё доступен как ручной scaffold path:

```shell
selenium-pw-migrator init --wizard \
  --source-path ./OldTests \
  --target dotnet \
  --target-test-framework nunit \
  --workspace migration
```

## 4. Нормальный цикл миграции

Обычно удобно идти так.

### Шаг 1. Проверить вход и конфиг

```bash
selenium-pw-migrator --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --out doctor \
  --format both
```

`doctor --fix` строит безопасный план исправлений и candidate-файлы:

```bash
selenium-pw-migrator --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --fix \
  --dry-run \
  --out doctor-fix
```

`--apply` применяйте только если согласны с безопасными изменениями внутри workspace или `.doctor.new` config candidates:

```bash
selenium-pw-migrator --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --fix \
  --apply \
  --out doctor-fix
```

### Шаг 2. Собрать доказательства из проекта

Перед большим маппингом PageObject и helper-методов:

```bash
selenium-pw-migrator --mode index-pom \
  --input ./OldTests \
  --out pom-index \
  --format both
```

Потом проверьте helper wrappers:

```bash
selenium-pw-migrator --mode helper-inventory \
  --input ./OldTests \
  --out helper-inventory \
  --format both
```

Если уже есть Playwright .NET проект:

```bash
selenium-pw-migrator --mode discover-target \
  --input ./PlaywrightTests \
  --out target-discovery \
  --format both
```

### Шаг 3. Анализ без генерации кода

```bash
selenium-pw-migrator --mode analyze \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --out run-001-analysis \
  --format both
```

Отчет покажет, что Migrator понял, что не смог сопоставить и какие unsupported patterns встречаются чаще всего.

### Шаг 4. Сгенерировать Playwright-тесты

Для NUnit:

```bash
selenium-pw-migrator --mode migrate \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target dotnet \
  --target-test-framework nunit \
  --out run-001-generated \
  --format both
```

Для xUnit:

```bash
selenium-pw-migrator --mode migrate \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target dotnet \
  --target-test-framework xunit \
  --out run-001-generated \
  --format both
```

### Шаг 5. Проверить результат

Сначала легкая проверка:

```bash
selenium-pw-migrator --mode verify \
  --input migration/run-001-generated \
  --config migration/profiles/adapter-config.json \
  --out run-001-verify \
  --format both
```

Для Playwright .NET запускайте project-aware проверку:

```bash
selenium-pw-migrator --mode verify-project \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target-test-framework nunit \
  --out run-001-verify-project \
  --format both
```

Для Playwright TypeScript:

```bash
selenium-pw-migrator --mode verify-ts-project \
  --input migration/run-001-generated \
  --ts-project ./PlaywrightTsProject \
  --out run-001-verify-ts \
  --format both
```

### Шаг 6. Понять, что осталось

```bash
selenium-pw-migrator --mode explain-todo \
  --input migration/run-001-verify-project \
  --out run-001-explain \
  --format both
```

После этого чините не случайные TODO, а самые частые и важные категории.

### Шаг 7. Сравнить до и после

```bash
selenium-pw-migrator --mode guard \
  --before migration/baseline \
  --after migration/run-001-verify-project \
  --out run-001-guard \
  --format both
```

`guard` полезен в CI и агентских циклах: он ловит регрессии по TODO, unsupported actions, syntax errors и другим метрикам.

## 5. Один большой запуск

Когда базовая настройка готова, `orchestrate` запускает типовой dry-run workflow:

```bash
selenium-pw-migrator --mode orchestrate \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target dotnet \
  --target-test-framework nunit \
  --out run-002 \
  --format both
```

Он складывает этапы в подпапки: analysis, generated output, verification, proposals и orchestration report.

`orchestrate` удобен для повторных прогонов. Отдельные режимы удобнее, когда надо изолировать конкретную проблему.

## 6. NUnit и xUnit

Для C# Selenium input Migrator умеет распознавать распространенные NUnit и xUnit тесты.

Для Playwright .NET output поддерживаются:

- `--target-test-framework nunit`
- `--target-test-framework xunit`

NUnit используется по умолчанию.

Если целевой проект на xUnit:

```bash
selenium-pw-migrator --mode scaffold \
  --target-test-framework xunit \
  --out generated-scaffold
```

```bash
selenium-pw-migrator --mode migrate \
  --input ./OldTests \
  --config ./adapter-config.json \
  --target dotnet \
  --target-test-framework xunit \
  --out generated-xunit
```

xUnit output использует xUnit attributes и Playwright xUnit packages. NUnit output использует NUnit attributes и Playwright NUnit packages.

Для Java, Python и TypeScript-направлений поддержка test frameworks пока preview-level. Проверяйте результат через reports и target project checks.

## 7. Все CLI-режимы простыми словами

### Настройка и discovery

`playground`

Создает одноразовый пяти минутный demo workspace с готовыми командами, expected outputs, dashboard sample, PR pack sample и manifest.

```bash
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
```

`playground-verify`

Проверяет, что сгенерированный playground по-прежнему содержит публичный demo contract: manifest, готовую цепочку команд, пример Selenium input, adapter config, ожидаемый Playwright output, dashboard sample, PR pack sample и формулировки selector safety.

```bash
selenium-pw-migrator playground verify --input playground --out playground-verify
```

`memory`

Создаёт и проверяет project-scoped migration memory в `migration/state/memory/**`. Используйте её во время supervised runs, чтобы следующие bounded actions переиспользовали решения, предупреждения, final-gate lessons и selector evidence без зависимости от памяти чата.

```bash
selenium-pw-migrator memory init --workspace migration
selenium-pw-migrator memory add --kind decision "Keep POM unresolved until target mapping exists"
selenium-pw-migrator memory explain --workspace migration
selenium-pw-migrator memory doctor --workspace migration
```

`config-merge`

Объединяет проверенные wave-local файлы `config-delta.json` в candidate config и валидирует результат до promotion. Это безопасный мост между divide-and-conquer waves и основным `adapter-config.json`.

```bash
selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge
selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge
```

Команда создаёт `adapter-config.merged.json`, `merge-report.md/json`, `validate-merge-report.md/json` и `conflicts.jsonl`. Candidate не продвигается автоматически: Reviewer, Watchdog и Final Gate должны принять merge, а `conflicts.jsonl` должен быть пустым.

`release-doctor`

Проверяет готовность NuGet preview из корня репозитория: package metadata, согласованность version/changelog, release scripts, документацию упаковки README_TOOL, поддержку dry-run в publish workflow, ссылки на NuGet secrets и repository hygiene.

```bash
selenium-pw-migrator doctor release --out release-doctor
```

`runbook`

Генерирует практический план миграции: pilot scope, command chain, risk map, artifacts и acceptance checklist.

```bash
selenium-pw-migrator runbook --input ./OldTests --target dotnet --target-test-framework xunit --generation-policy conservative --out runbook
```

`start`

Product-repo onboarding wizard: создает profile skeleton, `current-ticket.md`, `next-commands.md` и `state/start-dispatch.json` для no-menu `/supervised-task`.

```shell
selenium-pw-migrator start --input ./OldTests --agent opencode --workspace migration
```

`pilot`

Выбирает representative slice, копирует его в `selected-input/` и пишет next commands по bounded input.

```shell
selenium-pw-migrator pilot --input ./OldTests --max-tests 10 --out migration/pilot
```

`init`

Legacy/manual scaffold wizard. Используйте его, когда нужен старый starter config/scaffold без product `start` state.

```shell
selenium-pw-migrator init --wizard --source-path ./OldTests --target dotnet --target-test-framework xunit
```

`doctor`

Проверяет, готовы ли input, config, environment и workspace. С `--fix` пишет безопасный план или candidate-файлы.

```bash
selenium-pw-migrator --mode doctor --input ./OldTests --config ./adapter-config.json --fix --dry-run --out doctor
```

`capabilities`

Показывает доступные source frontends и target backends.

```bash
selenium-pw-migrator --mode capabilities --out capabilities --format both
```

`framework matrix`

Пишет source framework detection и target framework readiness reports.

```bash
selenium-pw-migrator framework matrix --input ./OldTests --target dotnet --target-test-framework xunit --out framework-matrix --format both
```

`discover-target`

Сканирует существующий Playwright .NET проект: namespaces, base classes, packages, reusable infrastructure.

```bash
selenium-pw-migrator --mode discover-target --input ./PlaywrightTests --out target-discovery
```

`scaffold`

Создает минимальный Playwright .NET test project skeleton.

```bash
selenium-pw-migrator --mode scaffold --target-test-framework nunit --out scaffold
```

`bootstrap-project`

Создает migration profile skeletons для нового проекта.

```bash
selenium-pw-migrator --mode bootstrap-project --input ./OldTests --out bootstrap-oldtests
```

### Анализ и генерация

`analyze`

Читает Selenium-тесты и пишет отчет о том, что можно мигрировать.

```bash
selenium-pw-migrator --mode analyze --input ./OldTests --config ./adapter-config.json --out analysis
```

`migrate`

Генерирует Playwright output.

```bash
selenium-pw-migrator --mode migrate --input ./OldTests --config ./adapter-config.json --target dotnet --generation-policy balanced --out generated
```

`--generation-policy conservative|balanced|aggressive` управляет риском helper generation. Conservative чаще оставляет review/TODO, balanced - нормальный дефолт, aggressive генерирует больше active helper code с risk annotations.

`dump-ir`

Maintainer/debug режим, который выгружает внутреннюю модель.

```bash
selenium-pw-migrator --mode dump-ir --input ./OldTests --config ./adapter-config.json --out ir --ir-version both
```

`orchestrate`

Запускает analyze, migrate, verify и proposal generation одним dry-run процессом.

```bash
selenium-pw-migrator --mode orchestrate --input ./OldTests --config ./adapter-config.json --out run-001
```

### Проверки и quality gates

`verify`

Проверяет generated code и пишет syntax/TODO/config issues.

```bash
selenium-pw-migrator --mode verify --input migration/generated --config ./adapter-config.json --out verify
```

`verify-project`

Собирает generated Playwright .NET output во временном project-aware harness.

```bash
selenium-pw-migrator --mode verify-project --input ./OldTests --config ./adapter-config.json --out verify-project
```

`verify-ts-project`

Type-check generated Playwright TypeScript specs внутри существующего TS проекта.

```bash
selenium-pw-migrator --mode verify-ts-project --input migration/generated-ts --ts-project ./PlaywrightTs --out verify-ts
```

`guard`

Сравнивает два запуска и падает на регрессиях.

```bash
selenium-pw-migrator --mode guard --before migration/baseline --after migration/current --out guard
```

### Config и profiles

`config-schema`

Пишет JSON Schema для adapter config.

```bash
selenium-pw-migrator --mode config-schema --out schema
```

`config-validate`

Проверяет структуру adapter config и safety rules.

```bash
selenium-pw-migrator --mode config-validate --config ./adapter-config.json --validation-mode strict --out config-check
```

`config-normalize`

Maintainer режим для перевода старого config shape в новый profile shape.

```bash
selenium-pw-migrator --mode config-normalize --config ./adapter-config.json --out normalized
```

`config-diff`

Сравнивает два config и подсвечивает risky changes.

```bash
selenium-pw-migrator --mode config-diff --before adapter.old.json --after adapter-config.json --out config-diff
```

`profile list`

Показывает встроенные offline profiles.

```bash
selenium-pw-migrator profile list
```

`profile search`

Ищет profiles по framework, backend или capability.

```bash
selenium-pw-migrator profile search xunit
```

`profile recommend`

Оценивает built-in profiles для source project и рекомендует порядок установки.

```bash
selenium-pw-migrator profile recommend --input ./OldTests --target-test-framework xunit --out profile-recommendations
```

`profile inspect`

Объясняет встроенный profile перед установкой.

```bash
selenium-pw-migrator profile inspect basic-csharp-xunit
```

`profile install`

Устанавливает встроенный profile как config layer.

```bash
selenium-pw-migrator profile install basic-csharp-nunit --out profiles
```

`profile diff`

Сравнивает config с другим config или встроенным profile.

```bash
selenium-pw-migrator profile diff --before adapter-config.json --after basic-csharp-xunit --out profile-diff
```

`profile-match`

Оценивает, подходит ли существующий profile/config для исходного проекта.

```bash
selenium-pw-migrator --mode profile-match --input ./OldTests --config ./profiles/base.adapter.json --out profile-match
```

`config author`

Пишет evidence-driven config proposals и reviewable patch на основе selector evidence, POM index, helper inventory, target discovery и TODO reports. Patch не применяется автоматически.

```bash
selenium-pw-migrator config author --input migration/run-001 --config ./adapter-config.json --out config-proposals --format both
```

### Помощники для source truth

`index-pom`

Ищет Selenium PageObject selectors и source-truth candidates.

```bash
selenium-pw-migrator --mode index-pom --input ./OldTests --out pom-index
```

`helper-inventory`

Смотрит helper/POM methods перед тем, как маппить или suppress helper wrappers.

```bash
selenium-pw-migrator --mode helper-inventory --input ./OldTests --out helper-inventory
```

`selector evidence`

Объясняет происхождение локаторов: Selenium selector → config mapping → generated Playwright locator.

```bash
selenium-pw-migrator selector evidence --input migration/run-001 --config ./adapter-config.json --out selector-evidence
```

`propose`

Создает mapping proposals из migration artifacts, не меняя config.

```bash
selenium-pw-migrator --mode propose --input migration/generated --config ./adapter-config.json --out proposals
```

### Reports, runtime triage и sharing

`explain-todo`

Объясняет оставшиеся TODO и вероятные root causes.

```bash
selenium-pw-migrator --mode explain-todo --input migration/verify-project --out todo-explanation
```

`smoke-plan`

Ранжирует generated tests по runtime readiness.

```bash
selenium-pw-migrator --mode smoke-plan --input migration/verify-project --out smoke-plan
```

`runtime-classify`

Классифицирует Playwright runtime failures из logs, traces, screenshots и videos.

```bash
selenium-pw-migrator --mode runtime-classify --input migration/runtime-logs --out runtime-classify
```

`learn pack`

Извлекает reusable migration knowledge из завершенного run-а в reviewable profile layer и learning changelog.

```bash
selenium-pw-migrator learn pack --input migration/run-001 --config ./adapter-config.json --out learn-pack --format both
```

`migration-board`

Строит HTML dashboard из migration artifacts.

```bash
selenium-pw-migrator --mode migration-board --input migration/run-001 --out board --format both
```

`report serve`

Строит и при необходимости запускает локальный dashboard.

```bash
selenium-pw-migrator report serve --input migration/runs/latest --port 5077 --out migration/dashboard/latest
```

Для CI используйте static-only:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest
```

`evidence pack`

Создает redacted zip для PR, issue или внешнего review.

```bash
selenium-pw-migrator evidence pack --input migration/run-001 --out evidence/run-001.zip
```

`--include-source` используйте только после явной проверки:

```bash
selenium-pw-migrator evidence pack --input migration/run-001 --out evidence/run-001.zip --include-source
```

`pr pack`

Создает PR/review bundle: summary, generated files list, before/after metrics, risk summary, reviewer checklist и suggested PR description.

```bash
selenium-pw-migrator pr pack --input migration/run-001 --out pr-pack --format both
```

`agent contract`

Генерирует agent instructions под конкретный ticket: allowed paths, stop policy, exact commands, report template и prompts для coordinator/migrator/verifier.

```bash
selenium-pw-migrator agent contract --input migration/current-ticket.md --out agent-contract --format both
```

## 8. Частые сценарии

### Есть только Selenium-тесты, Playwright проекта нет

1. Запустите `start --input ./OldTests --agent manual --workspace migration`.
2. Запустите `pilot --input ./OldTests --max-tests 10 --out migration/pilot`.
3. Используйте `init --wizard` только если нужен legacy `scaffold/` generator.
4. Заполните auth, routes, base URL и target namespace.
5. Запустите `index-pom`, `helper-inventory` и `selector evidence`.
6. Сначала запускайте `migrate` по pilot `selected-input/`.
7. Запустите `verify-project`.

### Playwright .NET проект уже есть

1. Запустите `discover-target`.
2. Перенесите полезные target facts в adapter config.
3. Запустите `doctor`.
4. Запустите `orchestrate`.
5. Между итерациями используйте `explain-todo` и `guard`.

### Нужен быстрый полезный pilot

1. Один раз пройдите `playground`, чтобы понять flow.
2. Запустите `start --input ./OldTests --agent manual --workspace migration`.
3. Запустите `pilot --input ./OldTests --max-tests 10 --out migration/pilot`.
4. Выполните команды из `migration/pilot/next-commands.md`.
5. Откройте dashboard через `report serve` после появления run artifacts.
6. Запустите `explain-todo` и проверьте `suggested-config-patch.md/json`.
7. Чините самую частую unsupported/TODO категорию, а не случайные единичные TODO.

### Хочу безопасно использовать агентов

Передайте агенту:

- Путь к исходным тестам.
- Разрешённый output workspace.
- Текущий config/profile.
- Текущие migration artifacts.
- Правило: не менять source tests и не придумывать selectors.

Используйте `start`, `pilot`, а затем одну из bootstrap-команд:

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json --opencode-install auto
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json
```

Для guarded-запусков OpenCode Desktop используйте `docs/guarded-opencode-desktop-runbook.ru.md` вместе с установленным `migration/AGENT_CONTRACT.md`.

## 9. Как получать лучший результат

Маленький первый input лучше огромного. Берите срез с типичными patterns.

Profiles лучше ручных правок generated code. Если один TODO повторяется много раз, чините mapping или recognizer один раз.

Доказательства лучше догадок. Перед broad config changes запускайте `index-pom`, `helper-inventory`, `selector evidence` и `discover-target`.

Project-aware verification лучше простой syntax check. Для Playwright .NET используйте `verify-project`.

Reports - часть продукта. Храните `orchestration-report`, `explain-todo`, `guard` и `evidence-pack` вместе с migration ticket.

## 10. Troubleshooting

`Input not found`

Проверьте, что `--input` указывает на source tests для source-processing modes или на artifact folder для report modes.

`Config not found`

Используйте absolute path или проверьте, что config path считается относительно текущей директории терминала.

`Generated code compiles, но TODO много`

Это checkpoint, а не конец. Запустите `explain-todo`, затем чините самые частые source-truth gaps.

`verify-project падает из-за packages или references`

Запустите `doctor`. Потом добавьте реальные project/package references в секцию `Verification` config.

`Output получился NUnit, а нужен xUnit`

Передайте `--target-test-framework xunit` в `init`, `scaffold`, `migrate` и `verify-project`, или задайте `TestHost.TargetTestFramework` в adapter config.

`report serve не должен стартовать server в CI`

Используйте `--static-only` или `--port 0`.

## 11. Куда пишутся результаты

Относительные `--out` обычно пишутся внутрь `migration/`.

```bash
selenium-pw-migrator --mode analyze --out analysis
```

Обычно получится:

```text
migration/analysis/
```

Абсолютные пути сохраняются как есть:

```bash
selenium-pw-migrator --mode analyze --out C:/temp/migrator-analysis
```

Для передачи результата наружу лучше использовать `evidence pack`.

## 12. Что Migrator не должен делать

Migrator не должен молча придумывать selectors.

Migrator не должен прятать сомнительное поведение в generated code.

Migrator не должен требовать ручной правки каждого generated file.

Migrator не должен менять исходные Selenium tests.

Migrator не должен обещать runtime success, пока не настроены target environment, auth, data и routes.

Лучшая миграция - не та, где меньше всего TODO любой ценой. Лучшая миграция - та, где каждая сгенерированная строка либо корректна, либо проверена, либо честно помечена для review.


## Developer smoke для bootstrap

Чтобы проверить, что `kit bootstrap-opencode` случайно не берёт `templates/migration-kit` из product repo, запустите:

```powershell
pwsh .\scripts\run-kitroot-shadow-smoke.ps1 -Clean
```

Smoke создаёт временный product repo с теневой папкой `templates/migration-kit` и падает, если она используется как kit root.

Когда final gate проходит, `check-final-gate.ps1` обновляет `migration/state/harness-run.json` до `FINAL_STOPPED_FOR_REVIEW`, если этот файл существует. В default mode нужно объяснить, почему SUCCESS checkpoint поставил процесс на паузу, и рекомендовать `/supervised-task continue`. В режиме `continuous` / `--continuation auto` тот же checkpoint сохраняется, но агент сразу перечитывает state и продолжает работу до настоящего terminal condition.

### Snapshot wavefront / memory / config merge

При использовании project-scoped memory и divide-and-conquer waves всё равно начинайте review с dashboard:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

Отчёт содержит **Wavefront / memory / config-merge snapshot**: краткое состояние project-scoped memory, прогресс wavefront, кандидатов следующей волны, candidate config и открытых элементов `conflicts.jsonl`. Это read-only представление: оно не продвигает memory, не объединяет config с активным adapter config и не помечает волну завершённой.

### Продолжение после обрыва агентской роли

Запустите `selenium-pw-migrator migration plan-agent-recovery --out <run-dir>`. При `WAIT_FOR_ROLE` дождитесь активную роль; `recover-agent-runtime` запускайте только для `SAFE_REPAIR_AVAILABLE`; при `BLOCKED` требуется человек. Длительная роль должна продлевать lease через `heartbeat-agent-role`. Свежесть считается от последнего heartbeat, один lease ограничен двумя часами, а повреждённые или противоречивые evidence владения автоматически не ремонтируются.
