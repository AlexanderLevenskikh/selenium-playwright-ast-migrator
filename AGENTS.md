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
- Для Selenium-only roots используй `SourceOnlyIdentifiers`.
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

