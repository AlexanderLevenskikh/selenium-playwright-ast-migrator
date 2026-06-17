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

