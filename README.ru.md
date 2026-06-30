# Selenium → Playwright AST Migrator

**.NET 8 CLI-инструмент для измеримой и проверяемой миграции Selenium-тестов в Playwright.**

Migrator парсит Selenium-тесты, строит промежуточную модель действий, применяет project-specific profile/config mappings и генерирует Playwright-тесты вместе с отчётами. Он полезен, когда нужно переносить большой E2E-набор не вручную по одному тесту, а через контролируемый цикл: source truth → config/profile → generated code → verification → следующий паттерн.

Основной production-путь: **Selenium C# → Playwright .NET** с NUnit по умолчанию и поддерживаемым xUnit target framework. Остальные source/target варианты помечены как preview/experimental.

## Что делает инструмент

- Анализирует Selenium-тесты и показывает unmapped targets, unsupported actions и повторяющиеся migration-паттерны.
- Маппит PageObjects, helper methods, table/list patterns, waits и project conventions через reviewable JSON profiles.
- Генерирует Playwright .NET тесты; экспериментально может генерировать Playwright TypeScript specs через `--target ts`.
- Проверяет generated output: syntax checks, project-aware compile checks, TypeScript type checks, quality gates, migration dashboards и quality-backlog с root cause / next-action tickets.
- Помогает человеку или агенту безопасно улучшать миграцию маленькими итерациями.

Идея не в “магической конвертации”, а в том, чтобы вся неопределённость была видимой и проверяемой.

## Поддерживаемые source и target

| Source frontend | Target backend | Статус | Примечание |
|---|---|---|---|
| Selenium C# / NUnit или xUnit | Playwright .NET / NUnit или xUnit | Stable public path | Полный workflow analyze/migrate/verify на Roslyn; NUnit остаётся default target framework. |
| Selenium C# / NUnit | Playwright TypeScript | Experimental preview | Используй `--target ts`; project-aware verify требует `--ts-project`. |
| Selenium Java | Playwright .NET / TypeScript | Experimental MVP | Для простых Java Selenium fixtures; без Java semantic model. |
| Selenium Python | Playwright .NET / TypeScript | Experimental spike | Для простых pytest/unittest Selenium diagnostics; не production-ready. |

## Установка или локальный запуск

Из исходников:

```bash
dotnet restore
dotnet run --project Migrator.Cli -- --help
```

Как локальный dotnet tool package:

```bash
./scripts/pack-tool.sh
./scripts/install-local-tool.ps1 -PackageSource ./artifacts/nuget
selenium-pw-migrator --help
```

Подробнее: [Tool installation](docs/tool-installation.md) и [Packaging and distribution](docs/packaging-and-distribution.md).

## Быстрый старт

Начинай с маленького pilot-набора, а не со всей тестовой базы:

```bash
selenium-pw-migrator --mode doctor \
  --input ./SeleniumTests \
  --config ./adapter-config.json \
  --out doctor

selenium-pw-migrator --mode orchestrate \
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

Пошагово:

- [Quick start](docs/quick-start.md)
- [End-to-end simple example](docs/examples/end-to-end-simple.md)
- [Public demo and guided tutorial](docs/public-demo-tutorial.md)
- [Public demo files](examples/public-demo/README.md)
- [Public launch demo](examples/public-launch-demo/README.md)
- [Screenshot walkthrough](docs/public-launch/walkthrough.md)
- [Migration workflow](docs/user-guide/migration-workflow.md)
- [Extensibility and public API](docs/extensibility.md)

## Основные CLI modes

| Mode | Статус | Назначение |
|---|---|---|
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
| `explain-todo` / `smoke-plan` / `runtime-classify` / `migration-board` / `report-serve` | Experimental | Приоритизация следующих migration fixes по artifacts/logs и dashboard по run artifacts. |
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

- [Documentation index](docs/README.md)
- [Quick start](docs/quick-start.md)
- [User guide](docs/user-guide/README.md)
- [Config and profile guide](docs/config-profile-guide.md)
- [Agent/autopilot guide](docs/agent-autopilot-guide.md)
- [Agent loop hardening](docs/agent-loop-hardening.md)
- [Limitations](docs/user-guide/limitations.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Migration quality program](docs/migration-quality-program.md)
- [Report serve dashboard](docs/report-serve-dashboard.md)
- [Public launch pack](docs/public-launch/README.md)
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
