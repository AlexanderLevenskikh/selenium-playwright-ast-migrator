# Agent-safe command set

Этот документ фиксирует команды, которые агент может запускать в agent-first workflow.

## Discovery / bootstrap

```powershell
selenium-pw-migrator --mode bootstrap-project --input "<tests>" --out "project-bootstrap" --format both

selenium-pw-migrator --mode index-pom --input "<Selenium project or POM dir>" --out "pom-index" --format both
```

## Migration / verification

```powershell
selenium-pw-migrator --mode migrate --input "<tests>" --config "<config>" --out "migration-run" --format both

selenium-pw-migrator --mode verify --input "<source Selenium tests>" --config "<config>" --out "verify-run" --format both

selenium-pw-migrator --mode verify-project --input "<source Selenium tests>" --config "<config>" --out "verify-project-run" --format both
```

## Explanation / next task

```powershell
selenium-pw-migrator --mode explain-todo --input "migration/<run>" --out "todo-explanation" --format both

selenium-pw-migrator --mode smoke-plan --input "migration/<run>" --out "smoke-plan" --format both
```

## Safety

```powershell
selenium-pw-migrator --mode config-validate --config "<config>" --out "config-validate"

selenium-pw-migrator --mode config-diff --before "<old-config>" --after "<new-config>" --out "config-diff"

selenium-pw-migrator --mode guard --before "migration/<old-run>" --after "migration/<new-run>" --out "guard"
```

## Packaging

Packaging/publish команды агент запускает только по явному запросу пользователя. См. `docs/agent-playbooks/package-and-publish-tool.md`.

## Запрещённые действия

Агенту запрещено:

- копировать generated files в source project;
- запускать массовый runtime всего пакета, если `smoke-plan` не выбрал кандидатов;
- пушить NuGet package без разрешения;
- менять C# мигратора без разрешения;
- менять source project;
- удалять отчёты для скрытия регрессии.

## Правило workspace

Все относительные `--out` должны попадать в `migration/`. Используй короткие имена:

```powershell
--out "discounts-run-7"
```

а не папки рядом с кодом. CLI сам создаст:

```text
migration/discounts-run-7
```

## Doctor / preflight

Перед первой миграцией нового проекта или пакета тестов запускай preflight-проверку:

```powershell
dotnet run --project .\Migrator.Cli -- --mode doctor --input "<tests>" --config "<profile.adapter.json>" --out "doctor" --format both
```

Режим ничего не меняет: он проверяет input, config layers, ближайший `.csproj`/`.sln`, `NuGet.config`, `Verification`, POM/source-truth кандидаты и доступность `dotnet`. Артефакты: `doctor-report.md/json` и `agent-doctor-next-task.md`. Подробности: `docs/doctor-mode.md`.


## Smart TODO codes

Agents may read generated code and use `[MIGRATOR:<CODE>]` TODO markers as input for the next iteration. The code explains why a line stayed TODO and which class of action is expected. See `docs/smart-todo-comments.md`.

### migration-board

Read-only команда для сборки HTML-доски по существующим артефактам:

```powershell
dotnet run --project .\Migrator.Cli -- --mode migration-board --input migration/verify-project --out board --format both
```

Агент должен использовать её для финальной сводки итерации.

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

## Source-only pattern backlog command policy

There is no separate CLI command for source-only pattern backlog yet. Use existing artifacts from `migrate`, `explain-todo`, and `migration-board`, then apply `docs/agent-playbooks/source-only-pattern-backlog.md` manually.

Required command sequence after a config/profile attempt:

```powershell
selenium-pw-migrator --mode config-validate --config "<profile-stack>" --out config-validate
selenium-pw-migrator --mode migrate --input "<tests>" --config "<profile-stack>" --out "<run>" --format both
selenium-pw-migrator --mode explain-todo --input "migration/<run>" --out "<run>-explain" --format both
selenium-pw-migrator --mode guard --before "migration/<before>" --after "migration/<run>" --out "<run>-guard"
```

If `SOURCE_ONLY_IDENTIFIER` remains dominant, report top source-patterns, not only root identifiers.
