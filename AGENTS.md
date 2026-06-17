# Migrator — AGENTS.md

## Обзор

Migrator — .NET 8 CLI-инструмент, преобразующий Selenium C# автотесты (NUnit) в Playwright .NET.
Pipeline: **parse → recognize → IR → adapt → render → report**.

## Архитектура (кратко)

| Проект | Ответственность |
|---|---|
| `Migrator.Core` | Модели IR, интерфейсы, пайплайн, отчёт. **Ничего** от Roslyn/Selenium/Playwright. |
| `Migrator.Roslyn` | Парсер (Roslyn AST → IR), recognizer'ы |
| `Migrator.SeleniumCSharp` | Адаптер (JSON-конфиг → маппинг таргетов) |
| `Migrator.PlaywrightDotNet` | Рендерер (IR → C# Playwright .NET) |
| `Migrator.Cli` | Точка входа, командная строка |
| `Migrator.Tests` | Тесты (xUnit), compile-smoke |

Core — только модели и контракты. Не класть в Core: зависимости от фреймворков, логику распознавания, конкретные реализации.

## Ключевые файлы

- `Migrator.Roslyn/RoslynTestFileParser.cs` — парсер, регистрация recognizer'ов (`CreateDefaultRecognizers`)
- `Migrator.Roslyn/Recognizers/` — recognizer'ы (Click, SendKeys, Wait, Assert, и др.)
- `Migrator.PlaywrightDotNet/PlaywrightDotNetRenderer.cs` — генерация C#
- `Migrator.SeleniumCSharp/DefaultProjectAdapter.cs` — маппинг `TargetKind` из JSON
- `Migrator.Core/Models/` — `TestAction`, `TargetExpression`, `TargetKind`, `RecognitionConfidence`
- `Migrator.Tests/SnapshotTests.cs` — snapshot + compile-smoke тесты

## Развертывание

Migrator — консольное .NET-приложение. Развёртывается как опубликованный exe:

```bash
dotnet publish Migrator.Cli -c Release -o ./publish
```

Не требует серверной части, базы данных, внешних сервисов.
Работает локально или в CI.

## Доступ к окружению

Локальная разработка. Локальный профиль. Внешний доступ к API не требуется.

## Ограничения агента

- **Никогда** не деплоить и не менять инфраструктуру — приложения нет.
- **Никогда** не запрашивать подтверждение у пользователя перед изменениями в коде.
- Если задача требует доступа к внешним ресурсам (API, базы, окружения) — сообщить пользователю и остановиться.

## Adapter-config/profile policy для агентов

- Project-specific знания держи в `adapter-config.json`, profile или scope, не в renderer.
- Для target-side enum/static helpers используй `TargetKnownTypes` / `TargetKnownIdentifiers`.
- Для Selenium-only roots используй `SourceOnlyIdentifiers` (`page`, `pagef`, `lightbox`, `modal`, `dialog`, `popup`, `Driver`, `WebDriver`).
- Не добавляй dummy declarations в renderer/generated output.
- Active target declarations из `TargetStatements` регистрируются renderer’ом как method-scoped target locals; не веди глобальный список локальных переменных в config.
- Перед изменениями migration profile прочитай `docs/agent-config-guidelines.md`.

## POM-index first

Перед массовым заполнением `adapter-config.json` по PageObject'ам используй режим `index-pom`:

```powershell
dotnet run --project .\Migrator.Cli -- --mode index-pom --input "<Selenium project or PageObject directory>" --out "pom-index" --format both
```

Читать подробности: `docs/pom-indexing.md`.

Правило: найденные POM-факты являются source truth, а `inferred-pom-candidates.json` — только черновик. Inferred candidates нельзя автоматически переносить в `adapter-config.json`: сначала найти POM/helper/source truth или спросить разработчика.

## Project-aware verify

Для настоящей компиляции generated Playwright-кода используй режим `verify-project`, а не только standalone `verify`. Он создаёт временный `.csproj` в `--out/project-verify`, подключает generated-файлы, project/package references из `adapter-config.json` (`Verification`), умеет искать ближайший `.csproj`, рекурсивные `ProjectReference`, `Directory.Build.props/targets`, `Directory.Packages.props`, и классифицирует build diagnostics по причинам. Исходный проект не меняется. Подробнее: `docs/project-verification.md`.

Агенту запрещено руками копировать generated files в продуктовый проект или править source project ради зелёной проверки. Если не хватает ссылок на инфраструктуру, добавляй их в `Verification.ProjectReferences` / `Verification.PackageReferences`.



## Explain TODO / Agent Next Task

После `migrate` или `verify-project` запускай режим объяснения TODO:

```powershell
dotnet run --project .\Migrator.Cli -- --mode explain-todo --input "<migration-output>" --out "todo-explanation" --format both
```

Он создаёт:

- `explain-todo.md/json` — почему остались TODO и какие действия дадут максимальный эффект;
- `agent-next-task.md` — готовую следующую задачу для агента.

Агент должен читать `agent-next-task.md`, но по умолчанию менять только `adapter-config.json`. Если отчёт говорит, что нужна правка C# мигратора, агент должен остановиться и сформировать escalation report.


## Migration workspace policy

Все generated/report артефакты держи внутри `migration/`. CLI по умолчанию кладёт относительные `--out` туда: `--out orchestration-7` → `migration/orchestration-7`.

Если указываешь `--out`, используй короткие имена (`orchestration-7`, `verify-project-4`) или явный путь `migration/...`. Не создавай output-папки в корне репозитория.

Подробнее: `docs/migration-workspace.md`.

## Agent safety loop

После каждой значимой правки `adapter-config.json` агент обязан:

1. сохранить предыдущую версию конфига;
2. запустить `--mode config-validate`;
3. запустить миграцию / `verify-project`;
4. запустить `--mode guard --before <previous-run> --after <new-run>`;
5. запустить `--mode config-diff --before <old-config> --after <new-config>`;
6. остановиться и дать отчёт на русском.

Если `config-validate` или `guard` падает — не продолжать. Нужно исправить или откатить последние изменения и объяснить причину.

Подробнее: `docs/agent-safety.md`.

---

## Milestone 3: переиспользование между проектами

Мигратор поддерживает несколько `--config` слоёв. Передавай базовый профиль первым, проектный профиль последним:

```powershell
dotnet run --project .\Migrator.Cli -- --mode migrate --input "<tests>" --config "profiles/infrastructure-base.adapter.json" --config "profiles/projects/project.adapter.json" --out "project-migrate" --format both
```

Правило: общие правила направления живут в base profile, локальные селекторы и исключения — в project profile. Подробнее: `docs/config-layering.md`, `docs/migration-profiles.md`, `docs/bootstrap-project.md`.

Новый режим для старта проекта:

```powershell
dotnet run --project .\Migrator.Cli -- --mode bootstrap-project --input "<tests>" --out "project-bootstrap" --format both
```

## Runtime readiness / smoke-plan

После `migrate`/`verify-project` можно запустить `--mode smoke-plan`, чтобы выбрать самые близкие к runtime запуску тесты. Режим читает generated `.cs`, `project-verify-report.json` и `explain-todo.json`, затем пишет `smoke-plan.md/json`, `runtime-checklist.md` и `agent-runtime-next-task.md`. Агент должен брать Level 4/5 кандидаты по одному, не запускать весь пакет сразу и не править generated `.cs` вручную. Подробности: `docs/runtime-readiness.md`.

## Milestone 6: правила для агента при упаковке tool

Если пользователь просит упаковать или опубликовать мигратор, работай по `docs/agent-playbooks/package-and-publish-tool.md`.

Обязательные правила:

- не публикуй пакет без явного разрешения пользователя;
- не записывай реальные токены/API keys в файлы;
- перед pack запускай `dotnet test --no-restore`;
- после pack проверяй локальную установку через `scripts/install-local-tool.ps1`;
- для команд рекомендуй local tool manifest, а не global install;
- публикуй preview-версии, пока CLI/config активно меняются.


## Milestone 7: Agent-first workflow

Для сложной миграции считай agent-first workflow основным пользовательским сценарием.

Перед началом агент обязан прочитать:

- `docs/agent-first-workflow.md`
- `docs/agent-roles.md`
- `docs/agent-command-set.md`
- `docs/agent-first-checklist.md`
- `docs/escalation-reports.md`
- `docs/agent-playbooks/run-agent-migration-iteration.md`
- `docs/agent-playbooks/escalate-to-developer.md`
- `docs/agent-playbooks/reuse-existing-profile.md`
- `docs/agent-playbooks/runtime-smoke-one-test.md`

Правило ролей:

- project-specific знания — в config/profile;
- временные выводы и состояние — в `migration/`;
- generic механика — в C# мигратора только после явного разрешения;
- все user-facing отчёты — на русском.

Если агент упирается в generic blocker, он не должен продолжать config churn. Нужно создать `migration/escalation-report.md` по `docs/escalation-reports.md`.

## Doctor / preflight

Перед первой миграцией нового проекта или пакета тестов запускай preflight-проверку:

```powershell
dotnet run --project .\Migrator.Cli -- --mode doctor --input "<tests>" --config "<profile.adapter.json>" --out "doctor" --format both
```

Режим ничего не меняет: он проверяет input, config layers, ближайший `.csproj`/`.sln`, `NuGet.config`, `Verification`, POM/source-truth кандидаты и доступность `dotnet`. Артефакты: `doctor-report.md/json` и `agent-doctor-next-task.md`. Подробности: `docs/doctor-mode.md`.


## Milestone 9 agent notes: smart TODO + schema

Generated TODO comments may contain `MIGRATOR:<CODE>` markers. Agents should use these codes to choose the next action:

- `MISSING_MAPPING` → inspect POM/source truth and add adapter mapping.
- `SOURCE_ONLY_IDENTIFIER` → do not mark the root as target-known; map the whole expression or leave TODO.
- `UNRESOLVED_SYMBOL` → find the first earlier TODO that blocked the symbol.
- `UNAVAILABLE_SYMBOLS` → add `TargetKnownTypes`/`TargetKnownIdentifiers` only when the symbol is truly available in target code.
- `UNRESOLVED_PLACEHOLDER` → fix `SourceMethodPattern`/`TargetStatements` placeholders.
- `TABLE_MAPPING_REQUIRED` → add `Tables` mapping with source-backed `RowTarget`.

Use `schemas/adapter-config.schema.json` in config/profile files for editor hints. JSON Schema does not replace `config-validate`; always run safety validation after config changes.

## Migration Board rule

После `migrate` / `verify-project` / `smoke-plan` агент должен смотреть `migration-board.html` или `migration-board.md`, если файл есть. Не заставляй пользователя читать сырые JSON/MD отчёты, если доска уже собрана. Используй блок `Recommended next actions` как следующий план.

## Profile match / reuse score

Для переиспользования профилей между похожими проектами используй режим `profile-match`:

```powershell
selenium-pw-migrator --mode profile-match --input "<tests>" --config "profiles/infrastructure-base.adapter.json" --config "profiles/projects/<project>.adapter.json" --out "profile-match-<project>" --format both
```

Он ничего не меняет, а оценивает, насколько текущий проект похож на уже готовый migration profile, какие правила профиля реально встречаются в source-коде и какие выражения остались не покрыты. Основной файл для агента: `agent-profile-reuse-task.md`.

## Milestone 12: runtime failure classifier and schema workflow

New command modes:

```powershell
selenium-pw-migrator --mode runtime-classify --input "migration/runtime-logs" --out runtime-failure-classification --format both
selenium-pw-migrator --mode config-schema --out schema --format both
```

`runtime-classify` reads runtime logs after a smoke run and groups failures into locator, timeout, assertion, navigation, auth/environment, setup, and browser-context categories. Use it before changing mappings after a failed Playwright run.

`config-schema` writes `adapter-config.schema.json` into the migration workspace for editor/agent usage. JSON Schema complements but does not replace `config-validate`.

See `docs/runtime-failure-classifier.md` and `docs/config-schema-workflow.md`.


## Playwright TypeScript target guardrails

- Use `--target ts` only when the user provides an existing Playwright TypeScript project via `--ts-project`.
- Never generate TS migrations "in vacuum" without package.json, tsconfig.json and playwright.config.*.
- Prefer TS-specific profile/config overrides; do not blindly reuse .NET TargetStatements.
- After `migrate --target ts`, run `verify-ts-project` before claiming the generated TS is usable.
- Do not edit generated `.spec.ts` manually. Fix mappings/profile rules instead.

## SOURCE_ONLY_IDENTIFIER pattern backlog rule

`SOURCE_ONLY_IDENTIFIER(page/pagef)` is a symptom, not a final root cause.

Agents must not conclude that all `page.*` TODO are manual or impossible to fix through config. The root `page` is source-only, but concrete expressions under it can often be mapped through `UiTargets`, `Methods`, `ParameterizedMethods`, `Tables`, `Pagination`, or `TestHost`.

Hard rules:

- Never group TODO only by root identifier (`page`, `pagef`, `lightbox`, `modal`).
- Never remove Selenium/POM roots from `SourceOnlyIdentifiers` only to reduce TODO.
- Never add Selenium/POM roots to `TargetKnownIdentifiers` unless they truly exist in target Playwright code.
- Before escalation, build a top-50 backlog by full source expression and normalized pattern.
- Escalate concrete generic blockers such as `ClickAndOpen<T>()` or `Table.Items.ElementAt(...)`, not the entire root `page`.

Read and follow `docs/agent-playbooks/source-only-pattern-backlog.md` whenever TODO are dominated by `SOURCE_ONLY_IDENTIFIER`.
