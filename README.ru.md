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

## Public preview story: evidence before scale

`public-preview-flow/v1` — рекомендуемый публичный preview-маршрут: установить инструмент, запустить `doctor install`, начать с playground или product `start`, мигрировать через pilot/waves, останавливаться на gates, извлекать mapping research из шумных волн и отправлять безопасный `feedback-bundle/v1` вместо приватного репозитория.

Safe-by-default правило простое: generated output — черновик, пока `verify-project`, final gate, artifact hygiene, sentinel lifecycle и wave quality evidence не согласованы. Если run красный, сначала выполняй `migration/current-ticket.md` и [операторский runbook для wave mode](docs/wave-mode-operator-runbook.ru.md), а не стартуй новую wave. Короткий end-to-end маршрут: [Public preview flow](docs/public-preview-flow.ru.md).


## Три входа

Жизненным циклом harness run управляет `new-harness-run.ps1`; агенты используют установленные скрипты Harness Kit, а не создают migration/runs вручную.


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
/supervised-task waves
```

Режим `waves` — рекомендуемый divide-and-conquer старт. Он использует source из `kit bootstrap-opencode --source ...` как жёсткий scope, если тот настроен; автоматически определяет только отсутствующие source/target/framework details; спрашивает только обязательные недостающие данные; запускает kit doctor; создаёт wavefront plan; материализует первую wave и выполняет только wave-local migration. До появления wave workspace он не должен запускать full-source migration или искать соседние проекты функциональных тестов. Все артефакты `migration/**` являются repository-root state; вложенные workspace вроде `Web/**/migration/**` считаются дефектом процесса.

Для существующего workspace обычный `/supervised-task` возобновляет следующую bounded-задачу. После успешного FINAL/PASS checkpoint supervised agent по умолчанию один раз останавливается для review. Чтобы продолжить post-final research без длинного prompt, запусти `/supervised-task continue` или обычный `/supervised-task` после `FINAL_STOPPED_FOR_REVIEW`. Для безопасного архивного перезапуска разросшегося pilot используй `/supervised-task waves fresh`; для явной форензик-проверки — `/supervised-task sentinel` (`inspect` и `qa` — алиасы).

При необходимости профиль Harness можно выбрать явно. Для новых run по умолчанию используется облегчённый `fast`:

```text
/supervised-task waves --execution-profile fast      # облегчённый/по умолчанию
/supervised-task waves --execution-profile standard  # сбалансированный
/supervised-task waves --execution-profile audit     # полный Harness
```

Тот же модификатор работает с обычным `/supervised-task`, `continue` и `continuous`. Уже созданная wave сохраняет неизменяемый профиль из `execution-policy.json`.

Чтобы тот же запуск автоматически проходил checkpoint-ы, после которых обычно нужно вводить `continue`, добавь `continuous` или `--continuation auto`:

```text
/supervised-task continuous
/supervised-task continue continuous
/supervised-task waves continuous
# эквивалентная flag-форма:
/supervised-task waves --continuation auto
```

Модификатор работает с обычным resume, `continue`, `waves`, `waves fresh` и bounded-запросами. Он всё равно останавливается на DONE, limitations, blocker, human decision, critical risk, scope violation, no-progress и исчерпании budget; `sentinel/inspect/qa` остаются одноразовыми.

Перед созданием рабочего wave-плана профиль `auto` выполняет детерминированный эксперимент **без агентов**: выводит диапазоны параметров из размера и квантилей сложности текущего inventory, перебирает варианты размера волн, учитывает повторное использование контекста одного файла/POM и выбирает конфигурацию с наименьшей оценочной полной стоимостью. Поэтому один режим адаптируется к маленьким, средним и большим наборам тестов. Результат сохраняется в `migration/plan/wave-tuning.md/json`. Ручной запуск эксперимента: `selenium-pw-migrator migration tune-wave-plan --input ./SeleniumTests --workspace migration --out migration/plan-tuning`. Подробнее: [подбор параметров wave-плана](docs/wave-plan-tuning.ru.md).

Материализованная wave использует быстрый и инкрементальный контракт: `wave-manifest.json` фиксирует файлы/tests, `execution-policy.json` выбирает профиль `fast|standard|audit`, а `run-context.json` связывает неизменяемые входы, baseline `generated/`, config и cache root. `migration validate-wave` проверяет отсутствие drift; единый `migration validate` вычисляет влияние, выполняет минимально достаточные проверки, пишет process evidence и использует кэш только при совпадении exact inputs и validation contract; `checkpoint-wave`/`resume-wave` позволяют продолжать без повторной материализации; `build-review-bundle` готовит компактный вход для reviewer; `check-progress` останавливает повторяющийся цикл, а `perf-report` показывает длительность фаз. Final review, sentinel и final gate не заменяются. Дополнительно `scope-audit` проверяет границы role evidence, а `cache-stats`/`cache-verify`/`cache-prune` обслуживают versioned validation cache. Подробнее: [быстрый путь миграции](docs/migration-fast-path.ru.md), [инкрементальный конвейер](docs/migration-incremental-pipeline.ru.md), [единый validation host](docs/migration-validation-host.ru.md) и [усиление производительности и кэша](docs/performance-cache-hardening.ru.md).

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

Полный справочник режимов и алиасов находится в [документации `/supervised-task`](docs/supervised-task-modes.ru.md): zero-argument resume, bounded requests, `waves`, `waves fresh`, `continue`, continuous-модификаторы, `sentinel | inspect | qa` и все условия остановки. Для ежедневной эксплуатации уже созданного wave workspace см. [операторский runbook для wave mode](docs/wave-mode-operator-runbook.ru.md): там описаны `BLOCKED_BY_GATE`, `current-ticket.md`, lifecycle sentinel findings, wave quality budget, mapping research memory и безопасная отправка feedback bundle автору мигратора.

### Безопасный feedback bundle для улучшения мигратора

Если мигратор оставил много TODO, syntax fallback, unresolved symbols или упал `verify-project`, можно помочь улучшить инструмент без отправки приватного репозитория. Из корня проекта запустите:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration
```

или на macOS/Linux/WSL:

```bash
migration/scripts/create-feedback-bundle.sh -Workspace migration
```

Скрипт создаёт `feedback-bundle/v1` zip в `migration/state/feedback-bundles/`. По умолчанию туда попадают только отчёты/evidence: mapping research memory, wave quality budget, sentinel findings, `project-verify-report.*`, `project-verify-harness.csproj`, `migration-board.*`, `explain-todo.md`. Исходники проекта и generated `.cs` samples не включаются по умолчанию. Перед отправкой проверьте `manifest.json`.

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

После этого запусти `/supervised-task` в OpenCode или передай другому агенту `migration/AGENT_HANDOFF.md`, `migration/AGENT_CONTRACT.md` и `migration/prompts/kickoff-prompt.txt`. Не создавай `migration/runs/<run-id>` вручную — это делает harness.

Java, Python и Playwright TypeScript остаются experimental preview-направлениями. Release demo и production promises должны быть сфокусированы на Selenium C# -> Playwright .NET.

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

См. [Migrator Agent Harness Kit](docs/migrator-agent-harness-kit.md), [Agent environments](docs/agent-environments.ru.md), [Harness dashboard](docs/migrator-agent-harness-dashboard.md) и канонический [Guarded OpenCode Desktop runbook](docs/guarded-opencode-desktop-runbook.ru.md).

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
| `migration` | Stable | Строить divide-and-conquer wave plans (`inventory/cluster/plan/plan show`) и готовить bounded wave workspaces (`run-wave`) с project-scoped memory deltas. |
| `config merge-deltas` / `config validate-merge` | Stable | Объединять wave-local `config-delta.json` в reviewable candidate config и проверять conflicts до promotion. |
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

Когда final gate проходит, `check-final-gate.ps1` обновляет `migration/state/harness-run.json` до `FINAL_STOPPED_FOR_REVIEW`, если файл существует. В default mode нужно объяснить, почему SUCCESS checkpoint поставлен на паузу, и рекомендовать `/supervised-task continue`. В `continuous` / `--continuation auto` тот же checkpoint сохраняется, после чего state немедленно перечитывается и выполнение продолжается до реального terminal condition.



## Планирование волн по принципу divide-and-conquer

Для больших проектов предпочитай one-command wavefront start в OpenCode. Один раз установи или обнови guarded project command pack, затем позволь `/supervised-task waves` выполнить всю setup-цепочку:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

Открой репозиторий в OpenCode и запусти:

```text
/supervised-task waves
```

Команда должна по возможности автоматически определить source/target/framework, запустить doctor, создать план, материализовать первую wave и выполнить только wave-local migration. Ручные команды остаются для отладки и CI:

```bash
selenium-pw-migrator migration tune-wave-plan --input ./SeleniumTests --workspace migration --out migration/plan-tuning
selenium-pw-migrator migration plan --input ./SeleniumTests --strategy wavefront --workspace migration --out migration/plan --wave-profile auto --smoke-wave-size 1
selenium-pw-migrator migration plan show --plan migration/plan
```

Auto planner сначала проводит детерминированный planning-only эксперимент и пишет `wave-tuning.json` / `wave-tuning.md`, а затем создаёт `inventory.json`, `clusters.json`, `waves.json`, `plan.md`, `selected-tests.txt`, `memory-recall.md` и `next-commands.md`. Диапазоны кандидатов выводятся из размера inventory и квантилей действий/сложности, поэтому один режим адаптируется к маленьким, средним и большим наборам тестов. Во время tuning/planning агенты не вызываются и файлы не мигрируются. Подробнее: [подбор параметров wave-плана без агентов](docs/wave-plan-tuning.ru.md).

Если `kit bootstrap-opencode --source ...` задал source, он является жёсткой границей wavefront: соседние functional-test проекты нельзя обнаруживать, планировать, копировать или рекомендовать. Первая wave — однотестовая low-risk smoke-проверка. Следующие waves группируются по affinity исходного файла и переиспользуемому POM-контексту. Первый тест файла оплачивает полную оценочную сложность; следующие тесты того же файла — только калиброванную marginal cost. Мягкие action/complexity targets направляют упаковку, а широкие hard ceilings защищают от настоящего runaway scope. Это позволяет среднему проекту укладываться в единицы или низкие десятки волн, а не создавать по одной wave на тест.

`run-wave` передаёт pipeline параметр `--selected-tests selected-tests.txt`, поэтому выполняются выбранные тесты, а не все тесты каждого скопированного файла. Перед превращением wave в bounded task агенты должны запускать `memory explain`, `memory doctor` и `memory recall --file` для каждого scoped-файла. Recall создаёт проверяемые receipts в `state/memory/recall-index.json` и `recall-ledger.jsonl`; final gate отклоняет active memory без актуального recall evidence текущей wave.

Ручная подготовка bounded wave workspace нужна только при отладке agent setup или запуске CI:

```bash
selenium-pw-migrator migration run-wave --plan migration/plan --wave wave-001 --workspace migration --out migration/runs/wave-001 --execution-profile fast
selenium-pw-migrator migration validate-wave --out migration/runs/wave-001
# запусти migration/runs/wave-001/run-migrate.ps1 или run-migrate.sh
selenium-pw-migrator migration validate --out migration/runs/wave-001 --validation-project ./Target.Tests/Target.Tests.csproj
# validation-plan + record-validation остаются только для recovery/import внешнего evidence
selenium-pw-migrator migration build-review-bundle --out migration/runs/wave-001
selenium-pw-migrator migration resume-wave --out migration/runs/wave-001
selenium-pw-migrator migration check-progress --out migration/runs/wave-001 --max-identical-snapshots 3
selenium-pw-migrator migration perf-report --out migration/runs/wave-001
selenium-pw-migrator migration scope-audit --out migration/runs/wave-001
selenium-pw-migrator migration cache-stats --workspace migration
selenium-pw-migrator migration cache-verify --workspace migration
selenium-pw-migrator migration cache-prune --workspace migration --cache-max-age-days 30 --cache-max-size-mb 2048 --cache-apply false
```

`migration run-wave` материализует неизменяемые `wave-manifest.json`, `execution-policy.json`, `run-context.json`, `source-scope/`, `generated/`, `input-scope.json`, `preflight-budget.json`, `config-delta.json`, `memory-delta.jsonl`, `wave-validation.json`, `performance-trace.json`, `run-summary.md`, `wave-status.json` и migrate-wrapper’ы. Команда работает только в рамках проекта: она не продвигает memory, не объединяет config и не публикует cross-project/org knowledge packs. Существующие run directories проверяются и переиспользуются, а не материализуются заново.

`validate-wave` отклоняет drift manifest/source/tests/policy/context, а `check-progress` останавливает повторяющиеся одинаковые fix-циклы и требует watchdog или смену стратегии. Единый host `migration validate` вычисляет влияние изменившегося output, выполняет минимальный безопасный набор проверок, сохраняет process evidence, переиспользует только PASS с точным совпадением inputs и validation contract и создаёт восстанавливаемый checkpoint. Остальные incremental-команды выбирают следующее действие и формируют компактный reviewer bundle. Подробнее: [быстрый путь миграции](docs/migration-fast-path.ru.md), [инкрементальный конвейер](docs/migration-incremental-pipeline.ru.md), [единый validation host](docs/migration-validation-host.ru.md) и [усиление производительности и кэша](docs/performance-cache-hardening.ru.md).

Оба generated migrate-wrapper’а обновляют `wave-status.json` и пишут `validation-plan.json`, поэтому заполненная папка `generated/` не может остаться ошибочно помеченной как `prepared`, а неизменившаяся validation не запускается повторно.

Объединяй проверенные wave-local config deltas в candidate config только после появления evidence:

```bash
selenium-pw-migrator config merge-deltas --base migration/adapter-config.json --deltas migration/state/memory/config-deltas --out migration/config-merge
selenium-pw-migrator config validate-merge --base migration/adapter-config.json --candidate migration/config-merge/adapter-config.merged.json --out migration/config-merge
```

`config merge-deltas` создаёт `adapter-config.merged.json`, `merge-report.md/json` и `conflicts.jsonl`. `config validate-merge` создаёт `validate-merge-report.md/json`. Ни одна команда не продвигает candidate автоматически: merge должны принять Reviewer, Watchdog и Final Gate, а `conflicts.jsonl` должен быть пустым.

### Snapshot wavefront / memory / config merge

### Новый bounded restart

Если pilot wave накопила слишком много remediation tickets, используй `/supervised-task waves fresh`. Команда запускает `migration/scripts/start-fresh-wavefront-run.ps1` или `.sh`, архивирует текущие plan/runs/volatile state в `migration/archive/**`, сохраняет project memory и configured source scope, затем перепланирует работу с однотестовой smoke-wave. Автоматический post-final remediation прекращается после четырёх завершённых tickets или двух последовательных no-progress tickets со статусом `FINAL_WITH_LIMITATIONS`; удаление текста TODO без восстановления исполняемого кода не считается прогрессом.

После использования project-scoped memory, wavefront planning, `migration run-wave` или `config merge-deltas` открой обычный dashboard:

```bash
selenium-pw-migrator report serve --input migration/runs/latest --static-only --out migration/dashboard/latest --format both
```

Dashboard содержит **snapshot wavefront / memory / config merge**: количество project-scoped memory entries, прогресс waves, кандидатов следующей wave, состояние config merge и рекомендуемые next commands. Сгенерированный `report-dashboard-evidence.zip` также включает соседние artifacts `state/memory`, `plan` и `config-merge` как review evidence.

### Ограничители agent orchestration

Во время kit bootstrap/init Migrator записывает `migration/state/scope-contract.json`, чтобы supervised waves знали разрешённый source root, workspace root, forbidden roots и classes команд. Final gate читает этот контракт и отклоняет изменения вне scope. Файловые claim-скрипты в `migration/scripts/*claim*` предоставляют лёгкий MVP lease/heartbeat для параллельных wave agents. Подробнее: `docs/agent-orchestration.md`.

## Performance

- [Проверка производительности](docs/performance-testing.ru.md)
- [Слои тестирования](docs/test-layers.ru.md)


### Адаптивная маршрутизация риска агента

`migration assess-agent-risk` создаёт объяснимый `agent-risk-assessment.json` и связывает разрешение на роль с `riskAssessmentFingerprint`. Для низкого риска fast-run получает компактный потолок в четыре вызова без watchdog; детерминированные no-progress/protected/scope-сигналы включают watchdog; критические evidence останавливают автоматическое продолжение. Подробнее: [`docs/migration-agent-risk-routing.ru.md`](docs/migration-agent-risk-routing.ru.md).

`migration plan-agent-recovery` классифицирует прерванное состояние роли до нового dispatch. Активная роль владеет ограниченным `agent-role-lease.json` и продлевает его через `heartbeat-agent-role`; свежесть считается от последнего корректного heartbeat, а изменения lease и журнала сериализуются эксклюзивной локальной блокировкой. Протухший lease маршрутизируется в единственную детерминированную команду `recover-agent-runtime`. Безопасный ремонт добавляет FAILED-событие для зависшей роли, восстанавливает только derived ledger head, архивирует orphan lease или переносит незавершённые atomic temp-файлы в quarantine. Невозможная временная шкала lease, противоречивые активные роли и повреждённый hash-chained журнал fail closed и автоматически не переписываются. Подробнее: [`docs/migration-agent-recovery.ru.md`](docs/migration-agent-recovery.ru.md).
