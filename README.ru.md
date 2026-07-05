# Selenium → Playwright AST Migrator

[![npm preview](https://img.shields.io/npm/v/selenium-pw-migrator/preview?label=npm%20preview)](https://www.npmjs.com/package/selenium-pw-migrator)
[![NuGet preview](https://img.shields.io/nuget/vpre/SeleniumPlaywrightMigrator?label=NuGet)](https://www.nuget.org/packages/SeleniumPlaywrightMigrator)

**.NET 10 CLI-инструмент для измеримой и проверяемой миграции Selenium-тестов в Playwright.**

Migrator парсит Selenium-тесты, строит промежуточную модель действий, применяет project-specific profile/config mappings и генерирует Playwright-тесты вместе с отчётами. Он полезен, когда нужно переносить большой E2E-набор не вручную по одному тесту, а через контролируемый цикл: source truth → config/profile → generated code → verification → следующий паттерн.

Основной production-путь: **Selenium C# → Playwright .NET** с NUnit по умолчанию и поддерживаемым xUnit target framework. Остальные source/target варианты помечены как preview/experimental.

## Что делает инструмент

- Анализирует Selenium-тесты и показывает unmapped targets, unsupported actions и повторяющиеся migration-паттерны.
- Маппит PageObjects, helper methods, table/list patterns, waits и project conventions через reviewable JSON profiles.
- Генерирует Playwright .NET тесты; экспериментально может генерировать Playwright TypeScript specs через `--target ts`.
- Проверяет generated output: syntax checks, project-aware compile checks, TypeScript type checks, quality gates, migration dashboards и quality-backlog с root cause / next-action tickets.
- Помогает человеку или агенту безопасно улучшать миграцию маленькими итерациями.

Идея не в “магической конвертации”, а в том, чтобы вся неопределённость была видимой и проверяемой.


## Три входа

### Product-repo onboarding wizard

Если ты уже внутри реального продуктового репозитория и не хочешь руками выбирать весь маршрут, начинай отсюда:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
```

`start` определяет source, создаёт `migration/profiles/adapter-config.start.json`, пишет `migration/next-commands.md` и печатает цепочку: `pilot`, `doctor`, agent bootstrap или ручной режим, затем dashboard. Для другого агента используй `--agent codex`, `--agent generic` или `--agent manual`.

### 1. Попробовать без агента

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
```

### 2. Миграция с OpenCode

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

### 3. Миграция с другим агентом

```bash
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests
```

Перед масштабированием реальной миграции дай CLI выбрать маленький репрезентативный pilot-срез:

```bash
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

`pilot` пишет `pilot-selection.md/json`, `selected-tests.txt` и `next-commands.md`. Он старается покрыть простые smoke tests, PageObjects, table/filter patterns, waits, assertions, custom helpers, XPath и data-driven tests.

После реального run сначала открывай dashboard:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

Открывай `migration/dashboard/latest/report-dashboard.html` до ручного чтения JSON/TXT артефактов. Если остались TODO, `explain-todo` дополнительно пишет `suggested-config-patch.md/json` с grouped root causes, “fix this profile mapping first”, confidence/evidence badges и черновиками UiTarget/Method/Table mappings для ревью.

## Поддерживаемые source и target

| Source frontend | Target backend | Статус | Примечание |
|---|---|---|---|
| Selenium C# / NUnit или xUnit | Playwright .NET / NUnit или xUnit | Stable public path | Полный workflow analyze/migrate/verify на Roslyn; NUnit остаётся default target framework. |
| Selenium C# / NUnit | Playwright TypeScript | Experimental preview | Используй `--target ts`; project-aware verify требует `--ts-project`. |
| Selenium Java | Playwright .NET / TypeScript | Experimental MVP | Для простых Java Selenium fixtures; без Java semantic model. |
| Selenium Python | Playwright .NET / TypeScript | Experimental spike | Для простых pytest/unittest Selenium diagnostics; не production-ready. |

## Установка

### Вариант для frontend-команд: npm wrapper

Для внешних пользователей самый простой путь — npm wrapper. Он скачивает подходящий standalone CLI и не требует установленного .NET SDK:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
```

Обновление:

```bash
npm update -g selenium-pw-migrator
# или только напечатать команду обновления для текущего канала:
selenium-pw-migrator self update
```

`doctor install` / `--mode install-doctor` показывает, что реально запускается из shell: executable, version, channel, runtime, PATH candidates и рекомендуемую команду install/update.

### Рекомендуемый вариант: standalone CLI

Для закрытых окружений или release smoke tests standalone остаётся самым прямым install path. npm wrapper выше остаётся самым удобным frontend-friendly вариантом, но standalone-дистрибутив не требует установленного .NET SDK или .NET Runtime на машине пользователя. Используй его, если npm недоступен или нужен прямой GitHub Release install.

Windows PowerShell:

```powershell
$installer = Join-Path $env:TEMP "install-standalone.ps1"
Invoke-WebRequest "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.ps1" -OutFile $installer
& $installer
selenium-pw-migrator --version
```

Linux/macOS/WSL:

```bash
curl -fsSL https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download/install-standalone.sh -o /tmp/install-standalone.sh
bash /tmp/install-standalone.sh
export PATH="$HOME/.selenium-pw-migrator/bin:$PATH"
selenium-pw-migrator --version
```

Windows-установщик по умолчанию добавляет standalone-папку в начало user `PATH`, даже если она уже была ниже. Если нужно понять, какая версия запускается, используй `Get-Command selenium-pw-migrator -All` в PowerShell или `which -a selenium-pw-migrator` в Unix-like shell. Чтобы сразу удалить старую dotnet global tool установку, передай `-RemoveDotnetTool`.

Чтобы удалить standalone-установку на Windows, запусти тот же installer с `-Uninstall`. На Linux/macOS запусти `install-standalone.sh --uninstall` и убери PATH-строку из shell profile.

### Вариант для frontend-команд: npm wrapper

Перед сравнением каналов установки проверь, что реально запускает shell: `./scripts/diagnose-install.ps1` или `Get-Command selenium-pw-migrator -All`; одного `dotnet tool list` недостаточно.


Npm-пакет — тонкая обёртка над теми же standalone release-архивами. Он удобен для frontend/test-automation команд, где Node.js уже есть, а .NET ставить не хочется.

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator --version
```


Для закреплённого preview можно поставить конкретную npm-версию или matching GitHub Release asset:

```bash
npm install -g selenium-pw-migrator@0.0.0-preview.8
npm install -g https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/download/v0.0.0-preview.8/selenium-pw-migrator-0.0.0-preview.8.tgz
```

Во время `postinstall` npm wrapper скачивает подходящий standalone-архив для `win-x64`, `linux-x64`, `osx-x64` или `osx-arm64`, проверяет `checksums.sha256`, если он доступен, и сохраняет exit code нативного CLI. Для корпоративной установки можно использовать Nexus npm proxy и `--selenium-pw-migrator-base-url` на внутреннее зеркало standalone-архивов. Для проверки npmjs/Nexus установки есть изолированные registry smoke-скрипты. Подробнее: [npm wrapper](docs/npm-wrapper.md). Инструкция по публикации: [npm publishing](docs/npm-publishing.md).

### Для .NET-разработчиков: dotnet tool

Используй dotnet tool, если нужна global/local .NET tool установка или закрепление версии через `.config/dotnet-tools.json`. Этот вариант требует .NET SDK.

```bash
dotnet tool install --global SeleniumPlaywrightMigrator --source https://api.nuget.org/v3/index.json --prerelease
selenium-pw-migrator --help
```

Репозиторий нужен только для разработки самого инструмента или сборки из исходников.

## Сборка или локальный запуск из исходников

Из исходников:

```bash
dotnet restore
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --help
```

Как локально собранный dotnet tool package — команды разделены по shell.

Windows PowerShell:

```powershell
.\scripts\pack-tool.ps1 -Version 0.0.0-preview.1
.\scripts\install-local-tool.ps1 -Version 0.0.0-preview.1
dotnet tool run selenium-pw-migrator -- --help
```

macOS/Linux/WSL:

```bash
scripts/pack-tool.sh 0.0.0-preview.1
dotnet new tool-manifest --force
dotnet tool install SeleniumPlaywrightMigrator --version 0.0.0-preview.1 --add-source ./artifacts/nuget
dotnet tool run selenium-pw-migrator -- --help
```

`selenium-pw-migrator --help` используйте только после global install. Для local tool manifest используйте `dotnet tool run selenium-pw-migrator -- ...`.

Подробнее: [Tool installation](docs/tool-installation.md), [Standalone installation](docs/standalone-installation.ru.md), [npm wrapper](docs/npm-wrapper.md) и [Packaging and distribution](docs/packaging-and-distribution.md).

## Happy path

Для стабильного production-сценария держим путь максимально коротким:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
bash playground/commands.sh
selenium-pw-migrator playground verify --input playground --out playground-verify --format both
```

Для реального проекта один раз bootstrap’им guarded workspace, дальше run lifecycle ведёт агент:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

Потом запускаем `/supervised-task` в OpenCode или передаём другому агенту `migration/AGENT_CONTRACT.md` и `migration/prompts/kickoff-prompt.txt`. `migration/runs/<run-id>` руками не создаём — это делает harness.

Java, Python и Playwright TypeScript — experimental-направления. В release/demo/marketing основной production promise остаётся Selenium C# -> Playwright .NET.

## Быстрый старт

Начинай с маленького pilot-набора, а не со всей тестовой базы:

```bash
dotnet tool run selenium-pw-migrator -- --mode doctor \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out doctor

dotnet tool run selenium-pw-migrator -- --mode orchestrate \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out run-001 \
  --format both
```

По умолчанию относительный `--out` пишется внутрь workspace `migration/`, например `migration/run-001`.

Типичные outputs:

```text
migration/run-001/
  analyze/
  generated/
  verify/
  propose/
  orchestration-report.md
  orchestration-report.json
```

Быстрый пяти минутный playground:

```bash
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
cat playground/try-this-first.md
```

Пошагово:

- [Quick start](docs/quick-start.md)
- [Migration runbook](docs/migration-runbook.md)
- [End-to-end simple example](docs/examples/end-to-end-simple.md)
- [Public demo and guided tutorial](docs/public-demo-tutorial.md)
- [Public Demo / Playground](docs/public-playground.md)
- [Teaching demo: AST migration explained](examples/teaching-demo/README.md)
- [AST migration explained](docs/articles/ast-migration-explained.md) / [RU](docs/articles/ast-migration-explained.ru.md)
- [Public demo files](examples/public-demo/README.md)
- [Migration workflow](docs/user-guide/migration-workflow.md)
- [Guarded OpenCode Desktop migration runbook](docs/guarded-opencode-desktop-runbook.ru.md)
- [Extensibility and public API](docs/extensibility.md)

## Безопасный агентский старт

Для agent-assisted миграции не создавай `migration/` и `migration/runs/<run-id>/` вручную. Теперь основной portable bootstrap можно сделать одной командой из корня product repo:

```powershell
dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install auto
```

`auto` выбирает подходящий режим: Windows OpenCode Desktop => `project-desktop`, macOS/Linux/WSL OpenCode CLI => `project-local`. Для Codex/CI/manual agents используй `--opencode-install ci`, чтобы поставить workspace без OpenCode config. После bootstrap запусти `/supervised-task` в OpenCode или передай non-OpenCode агенту `migration/prompts/kickoff-prompt.txt`. Orchestrator должен сам создать или возобновить `migration/runs/<run-id>/` через `new-harness-run.ps1`, читать `Prompt.md` / `Plan.md` / `Implement.md` / `Documentation.md`, писать events, запускать `check-harness-policy.ps1` и завершаться только после final gate.

Ручной fallback остаётся доступен:

```bash
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --backup --with-team
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
```

```powershell
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop
```

См. [Migrator Agent Harness Kit](docs/migrator-agent-harness-kit.md), [Agent environments](docs/agent-environments.ru.md), [Harness dashboard](docs/migrator-agent-harness-dashboard.md) и канонический [Guarded OpenCode Desktop runbook](docs/guarded-opencode-desktop-runbook.ru.md).

Developer smoke для проверки resolver-а template root в `bootstrap-opencode`:

```powershell
pwsh .\scripts\run-kitroot-shadow-smoke.ps1 -Clean
```

Он создаёт fake product repo с собственной папкой `templates/migration-kit` и проверяет, что `bootstrap-opencode` всё равно использует bundled шаблоны Migrator.

## Основные CLI modes

| Mode | Статус | Назначение |
|---|---|---|
| `playground` | Stable | Создать пяти минутный публичный demo workspace с готовыми командами, ожидаемыми outputs, dashboard sample и PR pack sample. |
| `runbook` | Stable | Практический план миграции: pilot scope, command chain, risk map, artifacts и acceptance checklist. |
| `doctor` | Stable | Preflight checks и безопасные `--fix` repair plans для input, config layers, project files и workspace hygiene. |
| `analyze` | Stable | Парсинг Selenium-файлов и отчёты без генерации target files. |
| `migrate` | Stable | Генерация Playwright target files. |
| `verify` | Stable | Лёгкая проверка generated code. |
| `verify-project` | Stable | Компиляция generated Playwright .NET тестов в project-aware harness. |
| `config-validate` | Stable | Проверка profile structure и safety rules. |
| `config-diff` | Stable | Сравнение profile changes и risky edits. |
| `guard` | Stable | Сравнение before/after migration metrics. |
| `index-pom` | Stable | Поиск selector evidence в Selenium PageObjects. |
| `helper-inventory` | Stable | Анализ helper/POM method bodies и MethodSemantics candidates. |
| `discover-target` | Stable | Сканирование существующего Playwright .NET проекта и target inventory. |
| `scaffold` | Stable | Минимальный compile-ready Playwright .NET scaffold для NUnit/xUnit. |
| `capabilities` | Stable | Показывает capability reports для source frontends и target backends. |
| `verify-ts-project` | Experimental | Type-check generated Playwright TS specs внутри существующего TS проекта. |
| `orchestrate` | Experimental | Dry-run analyze → migrate → verify → propose. |
| `explain-todo` / `smoke-plan` / `runtime-classify` / `selector-evidence` / `migration-board` / `report-serve` | Experimental | Приоритизация fixes по artifacts/logs, runtime root causes, readiness score, dashboard по run artifacts и экспорт triage decisions. |
| `learn-pack` | Experimental | Извлечение reusable migration knowledge из завершённых runs в reviewable profile layer и learning changelog. |
| `config-author` | Experimental | Evidence-driven config proposals и reviewable patch без автоматического применения. |
| `agent-contract` | Experimental | Ticket-specific agent instructions: allowed paths, stop policy, exact commands и multi-agent prompts. |
| `pr-pack` | Experimental | PR/review bundle: summary, generated files list, before/after metrics, risks, checklist и suggested PR description. |
| `evidence pack` | Stable | Redacted zip для issue/PR: reports, generated artifacts, manifest и checksums. |
| `profile list/search/inspect/install/diff` | Experimental | Offline built-in profiles как reviewable config layers. |

Command-specific help:

```bash
selenium-pw-migrator --mode migrate --help
```

## Safety rules

- Не придумывать selectors.
- Source truth важнее догадок: Selenium PageObject code, verified HTML attributes, existing target POM/tests или project-owned helper semantics.
- TODO в generated code — это reviewable evidence, а не мусор, который надо спрятать.
- Не чинить generated files как финальное решение; лучше исправить profile/source-truth mapping или поведение migrator.
- Перед suppress/manual rewrite повторяющихся PageObject/helper patterns используй `index-pom` и `helper-inventory`.

Если Selenium POM содержит доказанные selectors (`ByTId("value")`, `CreateControlByTid(...)`, явный `data-tid`, CSS, XPath, resolved constants), порядок такой: existing target POM member → generated POM scaffold → raw Playwright locator из доказанного selector → explicit TODO.

## Карта документации

- [Полное руководство пользователя](USER_GUIDE.ru.md)
- [Complete user guide](USER_GUIDE.md)
- [Documentation index](docs/README.md)
- [Quick start](docs/quick-start.md)
- [User guide](docs/user-guide/README.md)
- [Config and profile guide](docs/config-profile-guide.md)
- [Limitations](docs/user-guide/limitations.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Migration quality program](docs/migration-quality-program.md)
- [Report serve dashboard](docs/report-serve-dashboard.md)
- [Migration runbook](docs/migration-runbook.md)
- [Teaching demo: AST migration explained](examples/teaching-demo/README.md)
- [AST migration explained](docs/articles/ast-migration-explained.md) / [RU](docs/articles/ast-migration-explained.ru.md)
- [Framework matrix](docs/framework-matrix.md)
- [Migration PR pack](docs/migration-pr-pack.md)
- [Migration learning pack](docs/migration-learning-pack.md)
- [Config Authoring Assistant](docs/config-authoring-assistant.md)
- [Generation Policy](docs/generation-policy.md)
- [Public roadmap](docs/public-roadmap.md)
- [Release process](docs/release-process.md)

## Разработка

```bash
dotnet restore
dotnet test --no-restore
```

Тесты покрывают parser behavior, adapter mappings, snapshots, compile-smoke checks, orchestration, TypeScript target basics, safety guards, packaging guardrails и regression cases для частых migration blockers.

## Public release status

Проект готовится как public preview. Stable commands рассчитаны на внешних пользователей; experimental commands могут меняться между preview-релизами. См. [CHANGELOG.md](CHANGELOG.md), [SECURITY.md](SECURITY.md), [CONTRIBUTING.md](CONTRIBUTING.md).


Windows OpenCode Desktop shortcut: `--project-desktop` остаётся alias для `--opencode-install project-desktop`.
