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

Для настоящей компиляции generated Playwright-кода используй режим `verify-project`, а не только standalone `verify`. Он создаёт временный `.csproj` в `--out/project-verify`, подключает generated-файлы и project/package references из `adapter-config.json` (`Verification`). Исходный проект не меняется. Подробнее: `docs/project-verification.md`.

Агенту запрещено руками копировать generated files в продуктовый проект или править source project ради зелёной проверки. Если не хватает ссылок на инфраструктуру, добавляй их в `Verification.ProjectReferences` / `Verification.PackageReferences`.

