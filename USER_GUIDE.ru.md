# Руководство пользователя Migrator

Migrator - это CLI-инструмент для управляемого переноса Selenium end-to-end тестов на Playwright.

Он не пытается "магически" идеально переписать весь проект за один запуск. Вместо этого он дает понятный цикл:

```text
посмотреть старые тесты -> собрать доказательства -> сгенерировать Playwright -> проверить -> улучшить профиль -> повторить
```

Основной production-сценарий - Selenium C# в Playwright .NET. NUnit используется по умолчанию, xUnit тоже поддерживается. Playwright TypeScript, Java Selenium и Python Selenium пока считаются preview-направлениями.

## Happy path

Сначала установите CLI. Основной публичный путь — npm wrapper, который скачивает matching standalone CLI и не требует .NET SDK:

```shell
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
bash playground/commands.sh
selenium-pw-migrator playground verify --input playground --out playground-verify --format both
```

Для реального product repo начинайте с onboarding wizard и representative pilot slice:

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

Для agent-assisted migration:

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json --opencode-install auto
# non-OpenCode handoff:
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
```

После run первым открывайте dashboard:

```shell
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

`self update` печатает channel-specific update command и не мутирует global installs автоматически.

### PowerShell 7 для shell-wrapper’ов migration-kit

Сам установленный CLI не требует PowerShell, если используется npm или standalone. Но lifecycle-wrapper’ы migration-kit требуют: `migration/scripts/*.sh` на macOS/Linux/WSL запускают ту же `.ps1`-реализацию через PowerShell 7 (`pwsh`). Перед использованием этих wrapper’ов установи PowerShell 7: https://learn.microsoft.com/powershell/scripting/install/installing-powershell. Затем проверь окружение:

```bash
selenium-pw-migrator kit doctor --workspace migration
```

В отчёте будет проверка `powershell-7`; если `pwsh` не найден, doctor покажет рекомендацию по установке.

### Быстрая standalone-установка

Используйте standalone, если npm недоступен и не хочется ставить .NET SDK или .NET Runtime:

```powershell
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1" -OutFile $installer
& $installer
selenium-pw-migrator --version
selenium-pw-migrator doctor install
```

### Быстрый старт из NuGet

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

Если хочется сначала понять AST-модель миграции, откройте teaching demo и статью:

- `examples/teaching-demo/README.md`
- `docs/articles/ast-migration-explained.md`
- `docs/articles/ast-migration-explained.ru.md`

### Установка из локально собранного пакета

Если вы проверяете release candidate до публикации, используйте одну и ту же версию при упаковке и установке:

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

В примерах ниже предполагается local dotnet tool manifest и используется `dotnet tool run selenium-pw-migrator -- ...`. `selenium-pw-migrator ...` используйте только после global install. Если запускаете из исходников, заменяйте local-tool prefix на:

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

Потом запустите `/supervised-task`. После успешного FINAL/PASS checkpoint `/supervised-task` по умолчанию останавливается для review. Используйте `/supervised-task continue`, чтобы запустить post-final TODO/source-truth research без подробного prompt для supervisor. Supervised agent должен прочитать `current-ticket.md` и `state/start-dispatch.json`, создать или возобновить `migration/runs/<run-id>/` и не задавать пользователю широкие вопросы, если state понятен.

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
dotnet tool run selenium-pw-migrator -- --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --out doctor \
  --format both
```

`doctor --fix` строит безопасный план исправлений и candidate-файлы:

```bash
dotnet tool run selenium-pw-migrator -- --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --fix \
  --dry-run \
  --out doctor-fix
```

`--apply` применяйте только если согласны с безопасными изменениями внутри workspace или `.doctor.new` config candidates:

```bash
dotnet tool run selenium-pw-migrator -- --mode doctor \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --fix \
  --apply \
  --out doctor-fix
```

### Шаг 2. Собрать доказательства из проекта

Перед большим маппингом PageObject и helper-методов:

```bash
dotnet tool run selenium-pw-migrator -- --mode index-pom \
  --input ./OldTests \
  --out pom-index \
  --format both
```

Потом проверьте helper wrappers:

```bash
dotnet tool run selenium-pw-migrator -- --mode helper-inventory \
  --input ./OldTests \
  --out helper-inventory \
  --format both
```

Если уже есть Playwright .NET проект:

```bash
dotnet tool run selenium-pw-migrator -- --mode discover-target \
  --input ./PlaywrightTests \
  --out target-discovery \
  --format both
```

### Шаг 3. Анализ без генерации кода

```bash
dotnet tool run selenium-pw-migrator -- --mode analyze \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --out run-001-analysis \
  --format both
```

Отчет покажет, что Migrator понял, что не смог сопоставить и какие unsupported patterns встречаются чаще всего.

### Шаг 4. Сгенерировать Playwright-тесты

Для NUnit:

```bash
dotnet tool run selenium-pw-migrator -- --mode migrate \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target dotnet \
  --target-test-framework nunit \
  --out run-001-generated \
  --format both
```

Для xUnit:

```bash
dotnet tool run selenium-pw-migrator -- --mode migrate \
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
dotnet tool run selenium-pw-migrator -- --mode verify \
  --input migration/run-001-generated \
  --config migration/profiles/adapter-config.json \
  --out run-001-verify \
  --format both
```

Для Playwright .NET запускайте project-aware проверку:

```bash
dotnet tool run selenium-pw-migrator -- --mode verify-project \
  --input ./OldTests \
  --config migration/profiles/adapter-config.json \
  --target-test-framework nunit \
  --out run-001-verify-project \
  --format both
```

Для Playwright TypeScript:

```bash
dotnet tool run selenium-pw-migrator -- --mode verify-ts-project \
  --input migration/run-001-generated \
  --ts-project ./PlaywrightTsProject \
  --out run-001-verify-ts \
  --format both
```

### Шаг 6. Понять, что осталось

```bash
dotnet tool run selenium-pw-migrator -- --mode explain-todo \
  --input migration/run-001-verify-project \
  --out run-001-explain \
  --format both
```

После этого чините не случайные TODO, а самые частые и важные категории.

### Шаг 7. Сравнить до и после

```bash
dotnet tool run selenium-pw-migrator -- --mode guard \
  --before migration/baseline \
  --after migration/run-001-verify-project \
  --out run-001-guard \
  --format both
```

`guard` полезен в CI и агентских циклах: он ловит регрессии по TODO, unsupported actions, syntax errors и другим метрикам.

## 5. Один большой запуск

Когда базовая настройка готова, `orchestrate` запускает типовой dry-run workflow:

```bash
dotnet tool run selenium-pw-migrator -- --mode orchestrate \
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
dotnet tool run selenium-pw-migrator -- --mode scaffold \
  --target-test-framework xunit \
  --out generated-scaffold
```

```bash
dotnet tool run selenium-pw-migrator -- --mode migrate \
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
dotnet tool run selenium-pw-migrator -- playground --out playground --target-test-framework xunit --generation-policy conservative
```

`runbook`

Генерирует практический план миграции: pilot scope, command chain, risk map, artifacts и acceptance checklist.

```bash
dotnet tool run selenium-pw-migrator -- runbook --input ./OldTests --target dotnet --target-test-framework xunit --generation-policy conservative --out runbook
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
dotnet tool run selenium-pw-migrator -- --mode doctor --input ./OldTests --config ./adapter-config.json --fix --dry-run --out doctor
```

`capabilities`

Показывает доступные source frontends и target backends.

```bash
dotnet tool run selenium-pw-migrator -- --mode capabilities --out capabilities --format both
```

`framework matrix`

Пишет source framework detection и target framework readiness reports.

```bash
dotnet tool run selenium-pw-migrator -- framework matrix --input ./OldTests --target dotnet --target-test-framework xunit --out framework-matrix --format both
```

`discover-target`

Сканирует существующий Playwright .NET проект: namespaces, base classes, packages, reusable infrastructure.

```bash
dotnet tool run selenium-pw-migrator -- --mode discover-target --input ./PlaywrightTests --out target-discovery
```

`scaffold`

Создает минимальный Playwright .NET test project skeleton.

```bash
dotnet tool run selenium-pw-migrator -- --mode scaffold --target-test-framework nunit --out scaffold
```

`bootstrap-project`

Создает migration profile skeletons для нового проекта.

```bash
dotnet tool run selenium-pw-migrator -- --mode bootstrap-project --input ./OldTests --out bootstrap-oldtests
```

### Анализ и генерация

`analyze`

Читает Selenium-тесты и пишет отчет о том, что можно мигрировать.

```bash
dotnet tool run selenium-pw-migrator -- --mode analyze --input ./OldTests --config ./adapter-config.json --out analysis
```

`migrate`

Генерирует Playwright output.

```bash
dotnet tool run selenium-pw-migrator -- --mode migrate --input ./OldTests --config ./adapter-config.json --target dotnet --generation-policy balanced --out generated
```

`--generation-policy conservative|balanced|aggressive` управляет риском helper generation. Conservative чаще оставляет review/TODO, balanced - нормальный дефолт, aggressive генерирует больше active helper code с risk annotations.

`dump-ir`

Maintainer/debug режим, который выгружает внутреннюю модель.

```bash
dotnet tool run selenium-pw-migrator -- --mode dump-ir --input ./OldTests --config ./adapter-config.json --out ir --ir-version both
```

`orchestrate`

Запускает analyze, migrate, verify и proposal generation одним dry-run процессом.

```bash
dotnet tool run selenium-pw-migrator -- --mode orchestrate --input ./OldTests --config ./adapter-config.json --out run-001
```

### Проверки и quality gates

`verify`

Проверяет generated code и пишет syntax/TODO/config issues.

```bash
dotnet tool run selenium-pw-migrator -- --mode verify --input migration/generated --config ./adapter-config.json --out verify
```

`verify-project`

Собирает generated Playwright .NET output во временном project-aware harness.

```bash
dotnet tool run selenium-pw-migrator -- --mode verify-project --input ./OldTests --config ./adapter-config.json --out verify-project
```

`verify-ts-project`

Type-check generated Playwright TypeScript specs внутри существующего TS проекта.

```bash
dotnet tool run selenium-pw-migrator -- --mode verify-ts-project --input migration/generated-ts --ts-project ./PlaywrightTs --out verify-ts
```

`guard`

Сравнивает два запуска и падает на регрессиях.

```bash
dotnet tool run selenium-pw-migrator -- --mode guard --before migration/baseline --after migration/current --out guard
```

### Config и profiles

`config-schema`

Пишет JSON Schema для adapter config.

```bash
dotnet tool run selenium-pw-migrator -- --mode config-schema --out schema
```

`config-validate`

Проверяет структуру adapter config и safety rules.

```bash
dotnet tool run selenium-pw-migrator -- --mode config-validate --config ./adapter-config.json --validation-mode strict --out config-check
```

`config-normalize`

Maintainer режим для перевода старого config shape в новый profile shape.

```bash
dotnet tool run selenium-pw-migrator -- --mode config-normalize --config ./adapter-config.json --out normalized
```

`config-diff`

Сравнивает два config и подсвечивает risky changes.

```bash
dotnet tool run selenium-pw-migrator -- --mode config-diff --before adapter.old.json --after adapter-config.json --out config-diff
```

`profile list`

Показывает встроенные offline profiles.

```bash
dotnet tool run selenium-pw-migrator -- profile list
```

`profile search`

Ищет profiles по framework, backend или capability.

```bash
dotnet tool run selenium-pw-migrator -- profile search xunit
```

`profile recommend`

Оценивает built-in profiles для source project и рекомендует порядок установки.

```bash
dotnet tool run selenium-pw-migrator -- profile recommend --input ./OldTests --target-test-framework xunit --out profile-recommendations
```

`profile inspect`

Объясняет встроенный profile перед установкой.

```bash
dotnet tool run selenium-pw-migrator -- profile inspect basic-csharp-xunit
```

`profile install`

Устанавливает встроенный profile как config layer.

```bash
dotnet tool run selenium-pw-migrator -- profile install basic-csharp-nunit --out profiles
```

`profile diff`

Сравнивает config с другим config или встроенным profile.

```bash
dotnet tool run selenium-pw-migrator -- profile diff --before adapter-config.json --after basic-csharp-xunit --out profile-diff
```

`profile-match`

Оценивает, подходит ли существующий profile/config для исходного проекта.

```bash
dotnet tool run selenium-pw-migrator -- --mode profile-match --input ./OldTests --config ./profiles/base.adapter.json --out profile-match
```

`config author`

Пишет evidence-driven config proposals и reviewable patch на основе selector evidence, POM index, helper inventory, target discovery и TODO reports. Patch не применяется автоматически.

```bash
dotnet tool run selenium-pw-migrator -- config author --input migration/run-001 --config ./adapter-config.json --out config-proposals --format both
```

### Помощники для source truth

`index-pom`

Ищет Selenium PageObject selectors и source-truth candidates.

```bash
dotnet tool run selenium-pw-migrator -- --mode index-pom --input ./OldTests --out pom-index
```

`helper-inventory`

Смотрит helper/POM methods перед тем, как маппить или suppress helper wrappers.

```bash
dotnet tool run selenium-pw-migrator -- --mode helper-inventory --input ./OldTests --out helper-inventory
```

`selector evidence`

Объясняет происхождение локаторов: Selenium selector → config mapping → generated Playwright locator.

```bash
dotnet tool run selenium-pw-migrator -- selector evidence --input migration/run-001 --config ./adapter-config.json --out selector-evidence
```

`propose`

Создает mapping proposals из migration artifacts, не меняя config.

```bash
dotnet tool run selenium-pw-migrator -- --mode propose --input migration/generated --config ./adapter-config.json --out proposals
```

### Reports, runtime triage и sharing

`explain-todo`

Объясняет оставшиеся TODO и вероятные root causes.

```bash
dotnet tool run selenium-pw-migrator -- --mode explain-todo --input migration/verify-project --out todo-explanation
```

`smoke-plan`

Ранжирует generated tests по runtime readiness.

```bash
dotnet tool run selenium-pw-migrator -- --mode smoke-plan --input migration/verify-project --out smoke-plan
```

`runtime-classify`

Классифицирует Playwright runtime failures из logs, traces, screenshots и videos.

```bash
dotnet tool run selenium-pw-migrator -- --mode runtime-classify --input migration/runtime-logs --out runtime-classify
```

`learn pack`

Извлекает reusable migration knowledge из завершенного run-а в reviewable profile layer и learning changelog.

```bash
dotnet tool run selenium-pw-migrator -- learn pack --input migration/run-001 --config ./adapter-config.json --out learn-pack --format both
```

`migration-board`

Строит HTML dashboard из migration artifacts.

```bash
dotnet tool run selenium-pw-migrator -- --mode migration-board --input migration/run-001 --out board --format both
```

`report serve`

Строит и при необходимости запускает локальный dashboard.

```bash
dotnet tool run selenium-pw-migrator -- report serve --input migration/runs/latest --port 5077 --out migration/dashboard/latest
```

Для CI используйте static-only:

```bash
dotnet tool run selenium-pw-migrator -- report serve --input migration/runs/latest --static-only --out migration/dashboard/latest
```

`evidence pack`

Создает redacted zip для PR, issue или внешнего review.

```bash
dotnet tool run selenium-pw-migrator -- evidence pack --input migration/run-001 --out evidence/run-001.zip
```

`--include-source` используйте только после явной проверки:

```bash
dotnet tool run selenium-pw-migrator -- evidence pack --input migration/run-001 --out evidence/run-001.zip --include-source
```

`pr pack`

Создает PR/review bundle: summary, generated files list, before/after metrics, risk summary, reviewer checklist и suggested PR description.

```bash
dotnet tool run selenium-pw-migrator -- pr pack --input migration/run-001 --out pr-pack --format both
```

`agent contract`

Генерирует agent instructions под конкретный ticket: allowed paths, stop policy, exact commands, report template и prompts для coordinator/migrator/verifier.

```bash
dotnet tool run selenium-pw-migrator -- agent contract --input migration/current-ticket.md --out agent-contract --format both
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

Дайте агенту:

- Путь к исходным тестам.
- Разрешенный output workspace.
- Текущий config/profile.
- Текущие migration artifacts.
- Правило: не править source tests и не придумывать selectors.

Для guarded agent запусков сначала используйте `start` и `pilot`, а затем bootstrap из корня product repo:

```powershell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json --opencode-install auto
```

Режимы:

```text
--opencode-install auto             Windows => project-desktop; macOS/Linux/WSL => project-local
--project-desktop                   shortcut для Windows OpenCode Desktop
--opencode-install project-local    portable OpenCode CLI config в .opencode-migrator
--opencode-install ci               Legacy compatibility: только workspace; без OpenCode config
```

После bootstrap запустите `/supervised-task` в OpenCode. Для non-OpenCode agents используйте явный handoff: `selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./OldTests --config migration/profiles/adapter-config.start.json` или `--agent generic`. Агент должен сам создать или возобновить `migration/runs/<run-id>/` через `new-harness-run.ps1`/`.sh` и сначала работать по `migration/pilot/selected-input`. Подробности: `docs/agent-environments.ru.md`.

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
dotnet tool run selenium-pw-migrator -- --mode analyze --out analysis
```

Обычно получится:

```text
migration/analysis/
```

Абсолютные пути сохраняются как есть:

```bash
dotnet tool run selenium-pw-migrator -- --mode analyze --out C:/temp/migrator-analysis
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

When a final gate passes, `check-final-gate.ps1` updates `migration/state/harness-run.json` to `FINAL_STOPPED_FOR_REVIEW` when that file exists. Reports should say why work stopped: the SUCCESS checkpoint requires review, and the next action starts with `To continue, run: /supervised-task continue`, which triggers post-final research by default.

