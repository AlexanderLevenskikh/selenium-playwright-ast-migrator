# Selenium → Playwright AST Migrator

> **Execution model:** one standard full-project run is supported. `pilot` is optional calibration; partition-specific planning and acceptance state are not used.

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

## Public preview story: evidence before scale

`public-preview-flow/v1` — рекомендуемый публичный preview-маршрут: установить инструмент, запустить `doctor install`, начать с playground или product `start`, мигрировать через необязательный pilot и обычные полные запуски, останавливаться на gates, извлекать mapping research из шумных обычных запусков и отправлять безопасный `feedback-bundle/v1` вместо приватного репозитория.

Safe-by-default правило простое: generated output — черновик, пока standard run report, реальный `verify-project`, final gate и artifact hygiene не согласованы. Если run красный, исправь одну самую выгодную первопричину и повтори полный запуск на настроенном source scope. Короткий end-to-end маршрут: [Public preview flow](docs/public-preview-flow.ru.md).


## Три входа

Обычный запуск создаётся самой командой `selenium-pw-migrator run`; validation evidence вручную не реконструируется.


### Product-repo onboarding wizard

Если ты уже внутри реального продуктового репозитория и не хочешь руками выбирать весь маршрут, начинай отсюда:

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
```

`start` определяет source, создаёт `migration/profiles/adapter-config.start.json`, пишет `migration/next-commands.md`, `migration/current-ticket.md` и `migration/state/start-dispatch.json`, затем печатает цепочку: install diagnostics, agent bootstrap, `pilot`, `doctor`, ручной migrate или `/supervised-task`, затем dashboard после появления run artifacts. Для другого агента используй `--agent codex`, `--agent generic` или `--agent manual`.

### 1. Попробовать без агента

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
```

Открой `playground/try-this-first.md` и выполни сгенерированные команды. Это самый безопасный одноразовый маршрут.

### 2. Миграция с OpenCode

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

`bootstrap-opencode` также копирует project command pack в корень репозитория: `opencode.jsonc`, `.opencode/agents`, `.opencode/commands` и, если отсутствует, `AGENTS.md`. Затем открой корень репозитория в OpenCode и запусти:

```text
/supervised-task
```

Команда использует настроенный source как жёсткую границу и выполняет тот же обычный pipeline, что и CLI:

1. читает source scope, adapter config и project-local memory;
2. запускает диагностику установки и необязательный representative `pilot`;
3. прогоняет весь source через `selenium-pw-migrator run`;
4. запускает свежий соответствующий `verify-project`, когда доступен target project;
5. исправляет не более одной повторяющейся первопричины с максимальным эффектом и полностью перезапускает pipeline.

Обычный `/supervised-task` запускает или возобновляет этот процесс. `/supervised-task continue` выполняет один ограниченный цикл улучшения последнего обычного запуска. Профилей выполнения, автоматического перехода между частями, acceptance receipts и синтетических validation-записей больше нет. CLI crash, отсутствующий SDK или недоступный target project честно фиксируются как blocker.

Подробности: [обычный migration flow](docs/standard-migration-flow.ru.md) и краткий [справочник `/supervised-task`](docs/supervised-task-modes.ru.md).

### 3. Миграция с другим агентом

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests
# или:
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests
```

Перед масштабированием реальной миграции дай CLI выбрать маленький репрезентативный pilot-срез:

```bash
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

`pilot` пишет `pilot-selection.md/json`, `selected-tests.txt`, `next-commands.md` и копирует выбранные файлы в `selected-input/`. Сгенерированные analyze/migrate команды работают по `selected-input/`, а не по всему suite. Он старается покрыть простые smoke tests, PageObjects, table/filter patterns, waits, assertions, custom helpers, XPath и data-driven tests.

После реального run сначала открывай dashboard:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

Открывай `migration/dashboard/latest/report-dashboard.html` до ручного чтения JSON/TXT артефактов. Если остались TODO, `explain-todo` дополнительно пишет `suggested-config-patch.md/json` с grouped root causes, “fix this profile mapping first”, confidence/evidence badges и черновиками UiTarget/Method/Table mappings для ревью.

Краткий справочник двух поддерживаемых вариантов запуска находится в [документации `/supervised-task`](docs/supervised-task-modes.ru.md). Для ежедневной эксплуатации см. [обычный migration flow](docs/standard-migration-flow.ru.md): полный `run`, настоящий `verify-project`, project-local memory и безопасная отправка feedback bundle.

### Безопасный feedback bundle для улучшения мигратора

Если мигратор оставил много TODO, syntax fallback, unresolved symbols или упал `verify-project`, можно помочь улучшить инструмент без отправки приватного репозитория. Из корня проекта запустите:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration
```

или на macOS/Linux/WSL:

```bash
migration/scripts/create-feedback-bundle.sh -Workspace migration
```

Скрипт создаёт `feedback-bundle/v1` zip в `migration/state/feedback-bundles/`. По умолчанию туда попадают только отчёты/evidence: mapping research memory, отчёты обычного запуска, `project-verify-report.*`, `project-verify-harness.csproj`, `migration-board.*`, `explain-todo.md`. Исходники проекта и generated `.cs` samples не включаются по умолчанию. Перед отправкой проверьте `manifest.json`.

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

### Кроссплатформенные lifecycle-скрипты

Сам CLI при установке через npm или standalone не требует PowerShell. Но migration-kit lifecycle-скрипты устроены иначе: у каждого `.ps1` есть одноимённый `.sh`, а тонкие Unix-wrapper’ы делегируют выполнение в PowerShell 7 (`pwsh`), чтобы Windows и Unix запускали одну и ту же реализацию. На macOS/Linux/WSL установи PowerShell 7 перед использованием `migration/scripts/*.sh` или release/package shell entrypoints: https://learn.microsoft.com/powershell/scripting/install/installing-powershell. `selenium-pw-migrator kit doctor` показывает это проверкой `powershell-7`.

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

### npm details

Перед сравнением каналов установки проверь, что реально запускает shell: `./scripts/diagnose-install.ps1` или `Get-Command selenium-pw-migrator -All`; одного `dotnet tool list` недостаточно.

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

Есть три разных локальных сценария. Лучше выбрать один и не смешивать команды.

### 1. Запуск напрямую из исходников

Подходит, когда ты правишь репозиторий и хочешь просто запустить CLI без установки:

```bash
dotnet restore
dotnet run --project ./Migrator.Cli/Migrator.Cli.csproj -- --help
```

### 2. Установка локально собранного .NET tool package

Подходит, когда нужно проверить NuGet/dotnet-tool package, собранный из текущей репы. Нужен .NET SDK. Это local tool manifest, поэтому запуск через `dotnet tool run`.

Windows PowerShell:

```powershell
$version = "0.0.0-preview.20"
Unblock-File .\scripts\*.ps1
.\scripts\pack-tool.ps1 -Version $version
.\scripts\install-local-tool.ps1 -Version $version
dotnet tool run selenium-pw-migrator -- --help
```

Скрипт установки сначала проверяет `artifacts/nuget`, а уже потом вызывает `dotnet tool install`. Значение `-Version` должно совпадать с существующим локальным `.nupkg`. Чтобы установить самый новый локально собранный пакет и не помнить версию, можно не передавать `-Version`:

```powershell
.\scripts\pack-tool.ps1 -Version $version
.\scripts\install-local-tool.ps1
```

macOS/Linux/WSL:

```bash
version="0.0.0-preview.20"
scripts/pack-tool.sh "$version"
dotnet new tool-manifest --force
dotnet tool install SeleniumPlaywrightMigrator --version "$version" --add-source ./artifacts/nuget
dotnet tool run selenium-pw-migrator -- --help
```

Если local manifest уже есть, `install-local-tool.ps1` переиспользует его. `selenium-pw-migrator --help` используй только после global install; для local tool manifest используй `dotnet tool run selenium-pw-migrator -- ...`.

### 3. Сборка и локальная установка standalone `win-x64`

Подходит, когда нужно проверить тот же self-contained standalone layout, который публикуется в GitHub Releases. Это локальный аналог установки release-архива.

Windows PowerShell:

```powershell
$version = "0.0.0-preview.20"
Unblock-File .\scripts\*.ps1
.\scripts\package-standalone.ps1 -Version $version -Runtimes win-x64
.\scripts\install-standalone.ps1 `
  -Version $version `
  -Runtime win-x64 `
  -ArchivePath ".\artifacts\release\selenium-pw-migrator-$version-win-x64.zip" `
  -ChecksumsPath ".\artifacts\release\checksums.sha256" `
  -InstallDir "$env:LOCALAPPDATA\selenium-pw-migrator-dev"

& "$env:LOCALAPPDATA\selenium-pw-migrator-dev\bin\selenium-pw-migrator.exe" --help
Get-Command selenium-pw-migrator -All
```

`install-standalone.ps1` по умолчанию обновляет user PATH. Открой новый терминал, если bare-команда `selenium-pw-migrator` видна не сразу. Добавь `-SkipUserPathUpdate`, если хочешь проверить executable только по полному пути.

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

Для реального проекта начни с onboarding и репрезентативного pilot-среза:

```bash
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

Затем один раз bootstrap’ни guarded workspace и передай агенту управление lifecycle запуска:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

Для Codex или другого агента используй явный non-OpenCode handoff:

```bash
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests
```

После этого запусти `/supervised-task` в OpenCode или передай другому агенту `migration/AGENT_HANDOFF.md`, `migration/AGENT_CONTRACT.md` и `migration/prompts/kickoff-prompt.txt`. Не создавай `migration/runs/<run-id>` вручную — это делает стандартный CLI/agent flow.

Java, Python и Playwright TypeScript остаются experimental preview-направлениями. Release demo и production promises должны быть сфокусированы на Selenium C# -> Playwright .NET.

## Быстрый старт

Начинай с маленького pilot-набора, а не со всей тестовой базы:

```bash
dotnet tool run selenium-pw-migrator -- --mode doctor \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out doctor

dotnet tool run selenium-pw-migrator -- run \
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
dotnet tool run selenium-pw-migrator -- playground --out playground --target-test-framework xunit --generation-policy conservative
dotnet tool run selenium-pw-migrator -- playground verify --input playground --out playground-verify
cat playground/try-this-first.md
```

Пошагово:

- [Quick start](docs/quick-start.md)
- [Init wizard](docs/init-wizard.md)
- [Migration runbook](docs/migration-runbook.md)
- [Guarded OpenCode Desktop migration runbook](docs/guarded-opencode-desktop-runbook.ru.md)
- [End-to-end simple example](docs/examples/end-to-end-simple.md)
- [Public demo and guided tutorial](docs/public-demo-tutorial.md)
- [Public Demo / Playground](docs/public-playground.md)
- [Teaching demo: AST migration explained](examples/teaching-demo/README.md)
- [AST migration explained](docs/articles/ast-migration-explained.md) / [RU](docs/articles/ast-migration-explained.ru.md)
- [Public demo files](examples/public-demo/README.md)
- [Migration workflow](docs/user-guide/migration-workflow.ru.md)
- [Extensibility and public API](docs/extensibility.md)

## Безопасный агентский старт

Для agent-assisted миграции не создавай `migration/` и `migration/runs/<run-id>/` вручную. Сначала создай product onboarding-state и representative pilot, потом выбери agent handoff.

```shell
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
```

OpenCode path:

```shell
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json --opencode-install auto
```

Codex/generic/CI path:

```shell
selenium-pw-migrator kit bootstrap-agent --agent codex --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
selenium-pw-migrator kit bootstrap-agent --agent generic --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.start.json
```

OpenCode install modes:

```text
--project-desktop / --opencode-install project-desktop  Windows OpenCode Desktop
--opencode-install project-local                        macOS/Linux/WSL OpenCode CLI
--opencode-install ci                                   Legacy compatibility; для non-OpenCode агентов предпочитай bootstrap-agent
```

После bootstrap запусти `/supervised-task` в OpenCode или передай non-OpenCode агенту `migration/AGENT_HANDOFF.md` и `migration/AGENT_CONTRACT.md`. Orchestrator должен читать `migration/current-ticket.md`, `migration/state/start-dispatch.json` и `migration/pilot/next-commands.md`; если state понятен, он не должен снова спрашивать широкое меню.


Developer smoke для проверки resolver-а template root в `bootstrap-opencode`:

```powershell
pwsh .\scripts\run-kitroot-shadow-smoke.ps1 -Clean
```

Он создаёт fake product repo с собственной папкой `templates/migration-kit` и проверяет, что `bootstrap-opencode` всё равно использует bundled шаблоны Migrator.

## Основные CLI modes

| Mode | Статус | Назначение |
|---|---|---|
| `runbook` | Stable | Создать практический migration plan с pilot scope, цепочкой команд, картой рисков, artifacts и acceptance checklist. |
| `playground` | Stable | Создать пятиминутный public demo workspace с готовыми командами, ожидаемыми outputs, dashboard sample и PR pack sample. |
| `playground-verify` | Stable | Проверить, что сгенерированный playground по-прежнему содержит manifest, command chain, demo input, expected output и корректные safety-формулировки. |
| `memory` | Stable | Управлять project-scoped migration memory (`init/add/explain/doctor/summarize/recall`) в `migration/state/memory/**` для supervised runs. |
| `run` | Stable | Обработать весь настроенный source через обычный pipeline analyze → generate → verify → proposals в одной run-директории. |
| `config merge-deltas` / `config validate-merge` | Stable | Объединять run-local `config-delta.json` в reviewable candidate config и проверять conflicts до promotion. |
| `doctor` | Stable | Preflight checks и безопасные `--fix` repair plans для inputs, config layers, project files и workspace hygiene. |
| `release-doctor` | Stable | Проверить готовность NuGet preview: package metadata, docs, scripts, workflow dry-run, secret references и release hygiene. |
| `analyze` | Stable | Разобрать Selenium-файлы и создать отчёты без генерации target files. |
| `migrate` | Stable | Сгенерировать Playwright target files. |
| `verify` | Stable | Выполнить лёгкую проверку generated code. |
| `verify-project` | Stable | Скомпилировать generated Playwright .NET tests в project-aware harness. |
| `config-validate` | Stable | Проверить structure профиля и safety rules. |
| `config-diff` | Stable | Сравнить изменения профиля и подсветить risky edits. |
| `guard` | Stable | Сравнить migration metrics до/после и обнаружить регрессии. |
| `index-pom` | Stable | Извлечь selector evidence из Selenium PageObjects и target-side Playwright/Kontur POM. |
| `selector-evidence` | Experimental | Объяснить provenance Selenium selector → config mapping → generated locator с confidence и unsafe/inferred flags. |
| `agent-contract` | Experimental | Создать ticket-specific agent contract pack с allowed paths, stop policy, точными командами и prompts для coordinator/migrator/verifier. |
| `pr-pack` | Experimental | Создать PR/review bundle с summary, списком changed/generated files, before/after metrics, risk summary, reviewer checklist, evidence references и suggested PR description. |
| `learn-pack` | Experimental | Извлечь reusable migration knowledge из завершённых runs в reviewable profile layer и learning changelog. |
| `config-author` | Experimental | Создать evidence-driven config proposals и reviewable patch без автоматического применения. |
| `helper-inventory` | Stable | Проанализировать тела helper/POM методов и вывести кандидатов MethodSemantics. |
| `discover-target` | Stable | Просканировать существующий Playwright .NET project и создать reviewable target inventory. |
| `scaffold` | Stable | Сгенерировать минимальный compile-ready Playwright .NET project scaffold. |
| `bootstrap-project` | Stable | Создать reusable migration profile skeletons для нового source project. |
| `capabilities` | Stable | Показать built-in capability reports для source frontends и target backends. |
| `verify-ts-project` | Experimental | Выполнить type-check generated Playwright TS specs внутри существующего TS project. |
| `orchestrate` | Experimental | Выполнить analyze → migrate → verify → propose как единый dry-run workflow. |
| `explain-todo` / `smoke-plan` / `runtime-classify` / `selector-evidence` / `migration-board` / `report-serve` | Experimental | Приоритизировать follow-up work по artifacts/runtime logs, классифицировать runtime root causes, считать readiness, объяснять selector provenance и экспортировать triage decisions. |
| `evidence pack` | Stable | Создать redacted shareable zip с reports, generated artifacts, manifest и checksums. |
| `profile list/search/inspect/install/diff` | Experimental | Использовать offline built-in profiles как reviewable config layers. |

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

- [Complete user guide](USER_GUIDE.md)
- [Полное руководство пользователя](USER_GUIDE.ru.md)
- [Documentation index](docs/README.md)
- [Quick start](docs/quick-start.md)
- [Init wizard](docs/init-wizard.md)
- [Migration runbook](docs/migration-runbook.md)
- [Guarded OpenCode Desktop migration runbook](docs/guarded-opencode-desktop-runbook.ru.md)
- [Teaching demo: AST migration explained](examples/teaching-demo/README.md)
- [AST migration explained](docs/articles/ast-migration-explained.md) / [RU](docs/articles/ast-migration-explained.ru.md)
- [Framework matrix](docs/framework-matrix.md) — статическая support-таблица и generated readiness reports команды `framework matrix`
- [Doctor fix mode](docs/doctor-fix-mode.md)
- [Report serve dashboard](docs/report-serve-dashboard.md)
- [Profile marketplace](docs/profile-marketplace.md)
- [Migration PR pack](docs/migration-pr-pack.md)
- [Migration learning pack](docs/migration-learning-pack.md)
- [Config Authoring Assistant](docs/config-authoring-assistant.md)
- [Generation Policy](docs/generation-policy.md)
- [Evidence pack workflow](docs/evidence-pack.md)
- [User guide](docs/user-guide/README.md)
- [Config and profile guide](docs/config-profile-guide.md)
- [Guarded OpenCode Desktop migration runbook](docs/guarded-opencode-desktop-runbook.ru.md) — каноническая процедура guarded agent launch
- [Limitations](docs/user-guide/limitations.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Migration quality program](docs/migration-quality-program.md)
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


## Обычная полная миграция проекта

Поддерживается один настроенный source scope и одна обычная директория запуска. Для OpenCode:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

Затем:

```text
/supervised-task
```

Для ручного запуска или CI тот же процесс выглядит явно:

```bash
selenium-pw-migrator pilot --input ./SeleniumTests --max-tests 10 --out migration/pilot
selenium-pw-migrator run --input ./SeleniumTests --config migration/profiles/adapter-config.json --out migration/runs/run-001 --format both
selenium-pw-migrator verify-project --input ./SeleniumTests --config migration/profiles/adapter-config.json --out migration/runs/run-001/verify-project --format both
selenium-pw-migrator report serve --input migration/runs/run-001 --static-only --out migration/dashboard/run-001 --format both
```

`pilot` — только необязательная калибровка. `run` обрабатывает весь настроенный source линейно: analyze → generate → verify → proposals. `verify-project` должен опираться на реальный target project и toolchain; отсутствующие prerequisites остаются видимыми blockers.

Project-local memory помогает выбрать повторяющуюся первопричину, но не является validation evidence:

```bash
selenium-pw-migrator memory explain --workspace migration
selenium-pw-migrator memory doctor --workspace migration
```

Проверенные config deltas можно объединить только в candidate, не меняя активный config автоматически:

```bash
selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge
selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge
```

Обычный final gate проверяет реальные artifacts запуска (`orchestration-report.json`, `generated/report.json` и настоящий project-verification report, когда он обязателен). Подменять их вручную созданным evidence нельзя.

## Performance

- [Проверка производительности](docs/performance-testing.ru.md)
- [Слои тестирования](docs/test-layers.ru.md)
