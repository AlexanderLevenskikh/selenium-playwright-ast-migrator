# Руководство пользователя Migrator

Migrator - это CLI-инструмент для управляемого переноса Selenium end-to-end тестов на Playwright.

Он не пытается "магически" идеально переписать весь проект за один запуск. Вместо этого он дает понятный цикл:

```text
посмотреть старые тесты -> собрать доказательства -> сгенерировать Playwright -> проверить -> улучшить профиль -> повторить
```

Основной production-сценарий - Selenium C# в Playwright .NET. NUnit используется по умолчанию, xUnit тоже поддерживается. Playwright TypeScript, Java Selenium и Python Selenium пока считаются preview-направлениями.

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

Из исходников:

```bash
dotnet restore
dotnet run --project Migrator.Cli -- --help
```

После упаковки как local .NET tool:

```bash
selenium-pw-migrator --help
```

В примерах ниже используется `selenium-pw-migrator`. Если запускаете из исходников, заменяйте это на:

```bash
dotnet run --project Migrator.Cli -- 
```

Например:

```bash
dotnet run --project Migrator.Cli -- --mode analyze --input ./OldTests --out analysis
```

## 3. Первый правильный запуск

Начинайте с маленького набора характерных тестов, а не со всего проекта сразу.

```bash
selenium-pw-migrator init --wizard \
  --source-path ./OldTests \
  --target dotnet \
  --target-test-framework nunit \
  --workspace migration
```

Команда создаст безопасный workspace:

- `profiles/adapter-config.json` - стартовый профиль.
- `current-ticket.md` - текущий migration scope.
- `state/run-ledger.md` - журнал запусков.
- `next-commands.md` - готовые следующие команды.
- `scaffold/` - Playwright .NET scaffold, если целевого проекта еще нет.

Если нужен xUnit:

```bash
selenium-pw-migrator init --wizard \
  --source-path ./OldTests \
  --target dotnet \
  --target-test-framework xunit \
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

`init`

Создает migration workspace и стартовый config.

```bash
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
selenium-pw-migrator --mode migrate --input ./OldTests --config ./adapter-config.json --target dotnet --out generated
```

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

`migration-board`

Строит HTML dashboard из migration artifacts.

```bash
selenium-pw-migrator --mode migration-board --input migration/run-001 --out board --format both
```

`report serve`

Строит и при необходимости запускает локальный dashboard.

```bash
selenium-pw-migrator report serve --input migration/run-001 --port 5077 --out report-dashboard
```

Для CI используйте static-only:

```bash
selenium-pw-migrator report serve --input migration/run-001 --static-only --out report-dashboard
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

## 8. Частые сценарии

### Есть только Selenium-тесты, Playwright проекта нет

1. Запустите `init --wizard`.
2. Используйте созданный `scaffold/`.
3. Заполните auth, routes, base URL и target namespace.
4. Запустите `index-pom` и `helper-inventory`.
5. Запустите `migrate`.
6. Запустите `verify-project`.

### Playwright .NET проект уже есть

1. Запустите `discover-target`.
2. Перенесите полезные target facts в adapter config.
3. Запустите `doctor`.
4. Запустите `orchestrate`.
5. Между итерациями используйте `explain-todo` и `guard`.

### Нужен быстрый полезный pilot

1. Возьмите 10-30 характерных тестов.
2. Запустите `init --wizard`.
3. Запустите `orchestrate`.
4. Откройте dashboard через `report serve`.
5. Чините самую частую unsupported/TODO категорию, а не случайные единичные TODO.

### Хочу безопасно использовать агентов

Дайте агенту:

- Путь к исходным тестам.
- Разрешенный output workspace.
- Текущий config/profile.
- Текущие migration artifacts.
- Правило: не править source tests и не придумывать selectors.

Для repo-level autopilot используйте `.agent-loops/` и `AGENTS.md`.

## 9. Как получать лучший результат

Маленький первый input лучше огромного. Берите срез с типичными patterns.

Profiles лучше ручных правок generated code. Если один TODO повторяется много раз, чините mapping или recognizer один раз.

Доказательства лучше догадок. Перед broad config changes запускайте `index-pom`, `helper-inventory` и `discover-target`.

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
