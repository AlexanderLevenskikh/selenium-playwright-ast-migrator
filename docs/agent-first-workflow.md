# Agent-first workflow

Этот документ описывает основной способ работы с мигратором после Milestone 7.
Мигратор остаётся CLI-инструментом, но основным интерфейсом для сложной миграции становится агент: он читает отчёты, POM/source truth, предлагает изменения в profile/config и останавливается только в безопасных точках.

## Роли

| Роль | За что отвечает | Чего не делает |
|---|---|---|
| Пользователь / тестировщик | Выбирает пакет тестов, подтверждает смысл тестов, ревьюит понятные TODO | Не обязан понимать renderer, recognizer, AST, symbol tracking |
| Агент | Ведёт миграционную итерацию, читает POM, меняет config/profile, запускает проверки, пишет отчёт | Не правит C# мигратора без явного разрешения, не правит generated/source project |
| Разработчик мигратора | Чинит generic blockers, renderer/core/schema, принимает reusable profile changes | Не должен вручную править generated код ради зелёной сборки |

## Главная модель работы

```text
1. bootstrap-project / выбрать профиль
2. index-pom
3. baseline migrate или verify-project
4. explain-todo + agent-next-task
5. config-only iteration
6. config-validate + migrate/verify-project + guard + config-diff
7. smoke-plan
8. runtime proof по одному тесту
9. если config не помогает — escalation report разработчику
```

## Что агент может делать автономно

Агент может без отдельного подтверждения:

- читать исходный Selenium test project;
- читать POM, base classes, helpers, extension methods;
- запускать `migrate`, `verify`, `verify-project`, `index-pom`, `explain-todo`, `smoke-plan`;
- менять `adapter-config.json` или project profile;
- добавлять high-confidence mappings по найденному source truth;
- запускать `config-validate`, `config-diff`, `guard`;
- создавать отчёты внутри `migration/`.

## Когда агент обязан остановиться

Агент обязан остановиться и написать escalation report, если:

- нужно менять C# код мигратора;
- нужно менять исходный проект;
- нужна ручная правка generated `.cs`;
- source truth не найден, а mapping будет догадкой;
- `guard` показывает регрессию;
- `verify-project` падает из-за generic blocker мигратора;
- runtime failure требует продуктового знания, которого нет в источниках.

## Безопасные границы

Агент по умолчанию может менять только:

```text
adapter-config.json
profiles/**/*.adapter.json
migration/**/*.md
migration/**/*.json
```

Агенту запрещено менять:

```text
Migrator.*/*.cs          без явного разрешения
исходный Selenium project
generated/*.cs вручную
production code
секреты/NuGet tokens/NuGet.config с настоящими credentials
```

## Обязательный цикл после изменения config/profile

После каждого изменения config/profile агент должен выполнить:

```powershell
dotnet run --project .\Migrator.Cli -- --mode config-validate --config "<configs>" --out config-validate

dotnet run --project .\Migrator.Cli -- --mode migrate --input "<tests>" --config "<configs>" --out "current-run" --format both

# Если есть project context:
dotnet run --project .\Migrator.Cli -- --mode verify-project --input "<tests>" --config "<configs>" --out "current-verify-project" --format both

dotnet run --project .\Migrator.Cli -- --mode guard --before "migration/previous-run" --after "migration/current-run" --out guard

dotnet run --project .\Migrator.Cli -- --mode config-diff --before "adapter-config.before.json" --after "adapter-config.json" --out config-diff
```

Если используется установленный tool, замени `dotnet run --project .\Migrator.Cli --` на:

```powershell
selenium-pw-migrator
```

или:

```powershell
dotnet tool run selenium-pw-migrator --
```

## Формат сообщения пользователю после итерации

```text
## Этап завершён

### Цель этапа

### Что прочитал

### Что изменил

### Проверки

### Метрики до/после

### Что стало лучше

### Что осталось TODO

### Риски

### Нужна ли эскалация

### Следующий шаг

Продолжить?
```

## Принцип

Агент должен не просто выполнять команды, а переводить технические сигналы в понятные решения:

```text
Плохо:
CS0103 Product not found.

Хорошо:
Generated-код использует Product.Travel, но verify-project не видит этот тип.
Я проверю, это missing ProjectReference или safe target type, который надо добавить в TargetKnownTypes.
Если тип есть в проекте — добавлю его в config. Если нет — оставлю TODO или создам escalation report.
```

## Doctor / preflight

Перед первой миграцией нового проекта или пакета тестов запускай preflight-проверку:

```powershell
dotnet run --project .\Migrator.Cli -- --mode doctor --input "<tests>" --config "<profile.adapter.json>" --out "doctor" --format both
```

Режим ничего не меняет: он проверяет input, config layers, ближайший `.csproj`/`.sln`, `NuGet.config`, `Verification`, POM/source-truth кандидаты и доступность `dotnet`. Артефакты: `doctor-report.md/json` и `agent-doctor-next-task.md`. Подробности: `docs/doctor-mode.md`.

## Migration Board в agent-first workflow

После `verify-project`, `explain-todo` и `smoke-plan` агент должен ориентироваться по `migration-board.html/md`: это единая панель состояния миграции, root-cause TODO и runtime-кандидатов.

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

## SOURCE_ONLY_IDENTIFIER workflow

If `explain-todo` / `migration-board` shows that most TODO are `SOURCE_ONLY_IDENTIFIER` for `page`, `pagef`, `lightbox`, or `modal`, the agent must switch to pattern-backlog mode before escalating.

Required behavior:

1. Keep Selenium/POM roots in `SourceOnlyIdentifiers`.
2. Do not add these roots to `TargetKnownIdentifiers` unless the target Playwright project really defines them.
3. Extract full source expressions from TODO `Source:` lines.
4. Group by normalized pattern: loader/wait, click/open/modal, input/fill, assertions, table/list, navigation/WebDriver, modal/lightbox scope.
5. Apply safe high-frequency config/profile mappings first.
6. Escalate only concrete generic blockers, for example `ClickAndOpen<T>()` or `Table.Items.ElementAt(...)`.

Detailed playbook: `docs/agent-playbooks/source-only-pattern-backlog.md`.

## WaitPolicy note

Selenium explicit waits must be classified before generic source-only TODO handling. Actionability waits such as `WaitPresence`, `WaitVisible`, `WaitEnabled` are usually elided because Playwright auto-waits before actions/assertions. Product-state waits such as `ValidateLoading`, `WaitForLoaded`, table/grid/list refresh waits, modal/toast waits must be kept or converted to Playwright web-first assertions. Ambiguous waits become `[MIGRATOR:WAIT_REQUIRES_STATE_ASSERTION]`. See `docs/wait-policy.md`.

