# Doctor mode

`doctor` — это preflight-проверка перед миграцией. Режим ничего не меняет в исходном проекте, `adapter-config` и generated `.cs`; он только проверяет, готово ли окружение и проектный контекст к запуску агента/мигратора.

## Когда запускать

Запускай `doctor`:

- перед первой миграцией нового пакета тестов;
- после переноса профиля на похожий проект;
- если `verify-project` падает из-за непонятных `CS0103`/`CS0246`/NuGet/MSBuild ошибок;
- перед тем как отдавать задачу агенту.

## Команда

```powershell
dotnet run --project .\Migrator.Cli -- `
  --mode doctor `
  --input "<Selenium tests or project directory>" `
  --config "profiles/infrastructure-base.adapter.json" `
  --config "profiles/projects/my-project.adapter.json" `
  --out "doctor-my-project" `
  --format both
```

Так как относительный `--out` автоматически кладётся в workspace, отчёты будут в:

```text
migration/doctor-my-project/
```

## Что проверяется

`doctor` проверяет:

- существует ли `--input`;
- есть ли `.cs` файлы под input;
- не выглядит ли input слишком широким или API-heavy;
- существуют ли все `--config` слои;
- не содержит ли merged config опасных правил;
- найден ли ближайший `.csproj`;
- найден ли ближайший `.sln`;
- найден ли `NuGet.config` рядом с repo root;
- найдены ли `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`;
- настроен ли `Verification`;
- резолвятся ли `Verification.ProjectReferences`;
- есть ли POM/source-truth кандидаты;
- доступен ли `dotnet`.

## Артефакты

Режим пишет:

```text
doctor-report.md
doctor-report.json
agent-doctor-next-task.md
```

`agent-doctor-next-task.md` можно давать агенту как стартовую задачу: он должен сначала исправить failed/warning checks или эскалировать их, а уже потом запускать тяжёлую миграцию.

## Как читать статусы

- `failed` — миграцию лучше не начинать: не найден input/config/project reference/dotnet и т.п.;
- `warning` — можно продолжать осознанно, но агент должен понимать риск;
- `passed` — проверка прошла;
- `info` — полезная диагностическая информация.

## Правило для агента

Если `doctor` вернул `failed`, агент не должен продолжать миграцию автоматически. Он должен:

1. прочитать `doctor-report.md`;
2. исправить config/Verification/input, если это разрешено;
3. если проблема вне его полномочий — сформировать escalation report;
4. снова запустить `--mode doctor`.

