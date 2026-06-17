Начинай работу с `bootstrap.md`.

Порядок обязателен:

1. Прочитай `bootstrap.md`.
2. Прочитай `POLICIES.md`.
3. Создай или обнови:

    * `migration/agent-state.md`
    * `migration/pre-stop-checklist.md`
4. Перед любым запуском/остановкой сверяйся с `agent-state.md` и `pre-stop-checklist.md`.
5. Не останавливайся и не спрашивай “как продолжить?”, пока `pre-stop-checklist.md` не показывает `Stop allowed: yes`.
6. Если `Verify failed` или есть compile errors — не пиши `final-report.md`. Вместо этого создай/обнови `migration/blocked-report.md`, сгруппируй root causes и оформи `migration/migrator-tickets.md`.
7. Если `Verify passed`, но остались TODO — переходи в TODO-only phase: создай `migration/todo-audit.md`, классифицируй TODO, оформи `manual-review-items.md`, `deferred-items.md`, `migrator-tickets.md`.
8. Не меняй код мигратора, production code и generated files вручную. Работай только через config/profile и отчёты.
9. После каждого изменения config запускай fresh `orchestrate`, сравнивай метрики и откатывай изменение, если Verify/Syntax/Compile ухудшились.
10. Пиши пользователю только на русском.

Начинай автономно. Не проси подтверждения на безопасные действия. Остановись только при критическом блокере или когда `pre-stop-checklist.md` разрешает остановку.

Дополнительно перед config-итерациями:

11. Прочитай `docs/agent-config-guidelines.md`.
12. Project-specific знания добавляй в `adapter-config.json` / profile, не в renderer.
13. Для valid target-side enum/static helpers используй `TargetKnownTypes` / `TargetKnownIdentifiers`.
14. Для старых Selenium/POM roots используй `SourceOnlyIdentifiers` или честные TODO, не dummy declarations.
15. Не перечисляй локальные переменные метода в config: active target declarations регистрируются renderer’ом автоматически.
16. Если config-only режим упёрся в generic blocker, создай `migration/migrator-tickets.md` и остановись или запроси разрешение на generic-fix.

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


## Migration workspace

Все рабочие артефакты создавай только внутри `migration/`. CLI по умолчанию сам помещает относительный `--out` в workspace: `--out orchestration-7` станет `migration/orchestration-7`.

Читать подробности: `docs/migration-workspace.md`.

Запрещено создавать рядом с кодом мигратора папки вида `orchestration-7`, `verify-project-4`, `pom-index`, `todo-explanation` вне `migration/`.

## Safety commands for agent workflow

Перед продолжением после правок конфига:

```powershell
dotnet run --project .\Migrator.Cli -- --mode config-validate --config adapter-config.json --out config-validate
```

Для ревью изменений:

```powershell
dotnet run --project .\Migrator.Cli -- --mode config-diff --before adapter-config.before.json --after adapter-config.json --out config-diff
```

Для проверки, что новый прогон не стал хуже:

```powershell
dotnet run --project .\Migrator.Cli -- --mode guard --before migration/previous --after migration/current --out guard
```

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

## Запуск через установленный dotnet tool

Если мигратор установлен как local tool, команды выглядят так:

```powershell
dotnet tool restore
dotnet tool run selenium-pw-migrator -- --mode migrate --input "<tests>" --config "<config>" --out "migration-run" --format both
```

Если мигратор установлен глобально:

```powershell
selenium-pw-migrator --mode migrate --input "<tests>" --config "<config>" --out "migration-run" --format both
```

Упаковка и установка описаны в `docs/packaging-and-distribution.md` и `docs/tool-installation.md`.


## Milestone 7: agent-first workflow

Перед сложной миграцией дополнительно прочитай:

```text
docs/agent-first-workflow.md
docs/agent-roles.md
docs/agent-command-set.md
docs/agent-first-checklist.md
docs/escalation-reports.md
```

Если продолжаешь остановленную миграцию, используй prompt из:

```text
examples/agent-first/resume-agent-prompt.md
```

Если нужен разработчик, не продолжай config-only итерации. Создай `migration/escalation-report.md` по шаблону из `docs/escalation-reports.md`.

## Doctor / preflight

Перед первой миграцией нового проекта или пакета тестов запускай preflight-проверку:

```powershell
dotnet run --project .\Migrator.Cli -- --mode doctor --input "<tests>" --config "<profile.adapter.json>" --out "doctor" --format both
```

Режим ничего не меняет: он проверяет input, config layers, ближайший `.csproj`/`.sln`, `NuGet.config`, `Verification`, POM/source-truth кандидаты и доступность `dotnet`. Артефакты: `doctor-report.md/json` и `agent-doctor-next-task.md`. Подробности: `docs/doctor-mode.md`.


## Milestone 9 start reminder

Before editing `adapter-config.json`, enable schema hints where possible:

```json
{
  "$schema": "./schemas/adapter-config.schema.json"
}
```

When reading generated code, use `MIGRATOR:<CODE>` TODO markers to decide the next action. See:

```text
docs/smart-todo-comments.md
docs/json-schema.md
```

## После прогона

Если появился `migration-board.html`, начинай анализ с него. В нём собраны TODO, verify-project, smoke candidates и следующий рекомендуемый шаг.

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

