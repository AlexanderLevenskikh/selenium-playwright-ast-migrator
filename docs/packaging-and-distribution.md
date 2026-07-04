# Упаковка и распространение мигратора

Документ описывает официальный способ распространять CLI как `dotnet tool`.

## Зачем это нужно

Пока мигратор запускается так:

```powershell
dotnet run --project .\Migrator.Cli -- --mode migrate ...
```

это удобно для разработки, но неудобно для команд: нужно клонировать репозиторий, собирать проект и помнить путь до CLI.

После упаковки команда получает обычную команду:

```powershell
selenium-pw-migrator --mode migrate --input "..." --config "..." --out "..."
```

или через local tool:

```powershell
dotnet tool run selenium-pw-migrator -- --mode migrate --input "..." --config "..." --out "..."
```

## Что упаковывается

Упаковывается проект:

```text
Migrator.Cli/Migrator.Cli.csproj
```

В `csproj` включены свойства:

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>selenium-pw-migrator</ToolCommandName>
<PackageId>SeleniumPlaywrightMigrator</PackageId>
```

`ToolCommandName` — имя команды после установки.

`PackageId` можно переопределить при pack, если форк или приватный preview-feed требует другой namespace:

```powershell
/p:PackageId=Acme.SeleniumPlaywrightMigrator
```

## Локальная упаковка

```powershell
.\scripts\pack-tool.ps1 -Version 0.0.0-preview.1
```

Или вручную:

```powershell
dotnet pack .\Migrator.Cli\Migrator.Cli.csproj `
  -c Release `
  -o .\artifacts\nuget `
  /p:Version=0.0.0-preview.1
```

Результат:

```text
artifacts/nuget/SeleniumPlaywrightMigrator.0.0.0-preview.1.nupkg
```

## Проверка пакета без публикации

Через временный локальный source:

```powershell
dotnet new tool-manifest --force

dotnet tool install SeleniumPlaywrightMigrator `
  --version 0.0.0-preview.1 `
  --add-source .\artifacts\nuget

dotnet tool run selenium-pw-migrator -- --help
```

Или скриптом:

```powershell
.\scripts\install-local-tool.ps1 -Version 0.0.0-preview.1
```

## Публикация в NuGet/feed

Для GitHub Actions публикации в nuget.org используется Trusted Publishing: workflow `publish-nuget.yml` получает короткоживущий ключ через `NuGet/login@v1` и передаёт его в `scripts/push-tool.sh` как `NUGET_API_KEY`.

Для локальной публикации задайте `NUGET_API_KEY` явно. Если source уже есть в `NuGet.config`:

```powershell
.\scripts\push-tool.ps1 `
  -Version 0.0.0-preview.1 `
  -Source https://api.nuget.org/v3/index.json `
  -ApiKey $env:NUGET_API_KEY
```

Или вручную:

```powershell
dotnet nuget push .\artifacts\nuget\SeleniumPlaywrightMigrator.0.0.0-preview.1.nupkg `
  --source https://api.nuget.org/v3/index.json `
  --api-key $env:NUGET_API_KEY
```

Для приватных preview-feed используйте правила вашего repository manager или credential provider.

## Установка из NuGet/feed

Для обычного использования — глобально, без клонирования репозитория:

```powershell
dotnet tool install --global SeleniumPlaywrightMigrator `
  --prerelease

selenium-pw-migrator --help
```

В репозитории проекта можно использовать local tool manifest; установленная preview-версия будет закреплена в `.config/dotnet-tools.json`:

```powershell
dotnet new tool-manifest

dotnet tool install SeleniumPlaywrightMigrator `
  --prerelease
```

После этого в репозитории появится:

```text
.config/dotnet-tools.json
```

Версия инструмента будет закреплена для проекта.

## Рекомендуемый способ для команд

Для миграции проектов лучше local tool, а не global install:

```powershell
dotnet tool restore

dotnet tool run selenium-pw-migrator -- `
  --mode verify-project `
  --input "Tests/DiscountsTests" `
  --config "profiles/infrastructure-base.adapter.json" `
  --config "profiles/projects/discounts.adapter.json" `
  --out "discounts-verify-project" `
  --format both
```

Плюсы local tool:

- версия фиксируется в репозитории;
- разные проекты могут использовать разные версии мигратора;
- CI воспроизводимее;
- не нужно просить пользователей ставить tool глобально.

## Что не решает упаковка

NuGet/tool решает доставку CLI, но не решает project-aware compilation сам по себе.

Для реальной проверки всё равно нужен `Verification` в `adapter-config`:

```json
{
  "Verification": {
    "BaseDirectory": "C:/repo/project",
    "BuildWorkingDirectory": "C:/repo/project",
    "ProjectReferences": [
      "Web/MarketerWeb/MarketerWeb.UIFunctionalTests/MarketerWeb.UIFunctionalTests.csproj"
    ],
    "AutoDiscoverNearestProject": true,
    "AutoDiscoverProjectReferences": true,
    "AutoDiscoverBuildFiles": true
  }
}
```

## Правила безопасности публикации

- Не коммитьте токены/API keys.
- Не храните реальные credentials в `NuGet.config.template`.
- Публикуйте preview-версии с суффиксом `-preview.N`, пока формат CLI/config меняется.
- Перед публикацией запускайте `dotnet test --no-restore`.
- Перед публикацией проверяйте локальную установку из `artifacts/nuget`.
- Не публикуйте пакет, если `config-validate` / `guard` показывают регрессии на пилотном проекте.


## Standalone CLI bundle для агента

Для AI-agent migration workflow лучше не давать агенту repository source code мигратора.

Вместо этого соберите отдельную папку с:

- single-file `migrator.exe`;
- JSON schema;
- agent-facing docs;
- first prompt template;
- wrapper template.

Команда:

```powershell
.\scripts\package-agent-cli-bundle.ps1 `
  -Runtime win-x64 `
  -Output artifacts/agent-cli-bundle
```

Результат:

```text
artifacts/agent-cli-bundle/tool/
  migrator.exe
  README_AGENT_TOOL.md
  templates/migration-kit/AGENT_CONTRACT.md
  templates/migration-kit/prompts/kickoff-prompt.txt
  templates/migration-kit/state/final-gate.md
  templates/migration-kit/scripts/check-scope.ps1
  templates/migration-kit/scripts/check-final-gate.ps1
  schemas/
    adapter-config.schema.json
  docs/
    guarded-opencode-desktop-runbook.ru.md
    ...
```

Эту папку можно скопировать в целевой Playwright-проект:

```text
<target-playwright-project>/tools/migrator/
```

И дать агенту путь именно до неё:

```text
<TARGET_PLAYWRIGHT_PROJECT_PATH>\tools\migrator
```

Агент должен запускать:

```powershell
.\tools\migrator\migrator.exe --mode migrate ...
```

а не:

```powershell
dotnet run --project Migrator.Cli ...
```

Так агент работает с мигратором как с black-box CLI и не может случайно начать править renderer/parser/recognizers вместо создания тикета.

Подробнее:

- `docs/agent-tool-boundary.md`
- `docs/guarded-opencode-desktop-runbook.ru.md`

## Windows PowerShell execution policy

Если PowerShell отказывается запускать скрипт упаковки и показывает ошибку вроде:

```text
Файл ...\scripts\package-agent-cli-bundle.ps1 не имеет цифровой подписи.
Невозможно выполнить сценарий в указанной системе.
```

Запустите скрипт с временным обходом политики выполнения только для текущего процесса:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass

.\scripts\package-agent-cli-bundle.ps1 `
  -Runtime win-x64 `
  -Output artifacts\agent-cli-bundle
```

Это изменение действует только в текущем окне PowerShell. После закрытия терминала системная политика выполнения останется прежней.

Если файл был скачан из интернета или распакован из архива, Windows могла пометить его как небезопасный. В таком случае сначала разблокируйте файл:

```powershell
Unblock-File .\scripts\package-agent-cli-bundle.ps1
```

Или разблокируйте все файлы в распакованной папке проекта:

```powershell
Get-ChildItem -Recurse | Unblock-File
```

Также можно запустить скрипт одной командой без предварительного изменения политики в текущей сессии:

```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\package-agent-cli-bundle.ps1 `
  -Runtime win-x64 `
  -Output artifacts\agent-cli-bundle
```

Для PowerShell 7 используйте:

```powershell
pwsh -ExecutionPolicy Bypass -File .\scripts\package-agent-cli-bundle.ps1 `
  -Runtime win-x64 `
  -Output artifacts\agent-cli-bundle
```

Не рекомендуется глобально включать `Unrestricted`, если вы точно не понимаете, зачем это нужно. Для запуска этого скрипта достаточно временного `Bypass` на уровне текущего процесса.

## CI gates

GitHub Actions keeps the pull-request path split by intent in `.github/workflows/ci.yml`:

- `Test fast suite` — restore/build plus the normal unit, parser, renderer, docs, packaging, and architecture tests. It excludes process-heavy CLI tests with `Shard!=Cli`.
- `Test CLI process suite` — restore/build plus CLI subprocess/orchestrator tests marked with `Shard=Cli`. These tests execute the already-built `Migrator.Cli.dll` and fail fast on process timeouts.
- `Pack and smoke dotnet tool` — runs after both test shards, packs the dotnet tool, verifies `.nupkg` contents, installs the package into a temporary local tool manifest, then runs `--help` and `--mode doctor` from the installed tool.
- `Build and smoke agent bundle` — runs after both test shards, publishes the standalone bundle, verifies required docs/templates/schema files, validates `MANIFEST.sha256`, and runs help smoke from the published output.

These gates are intentionally separate from distribution checks so package failures are visible as release/distribution failures, not just generic unit-test failures.

The repository also has `.github/workflows/full-validation.yml` for manual and nightly validation. It runs the unfiltered test suite, release doctor, dotnet-tool package smoke, and agent-bundle smoke, then uploads release-doctor/package/bundle artifacts for inspection.

## Manual NuGet publish workflow

Actual publication is intentionally manual. Use:

```text
.github/workflows/publish-nuget.yml
```

The workflow is started with `workflow_dispatch` and accepts:

- `version` — exact package version, for example `0.0.0-preview.1`;
- `source` — usually `https://api.nuget.org/v3/index.json`;
- `dry_run` — default `true`, which builds, packs, verifies, smokes, and uploads the `.nupkg` artifact without publishing;
- `create_github_release` — default `true`; when `dry_run=false`, creates or updates the GitHub release after the NuGet push.

For a real publish:

1. run the workflow once with `dry_run=true`;
2. check that build/test/package/smoke all pass;
3. configure a nuget.org Trusted Publishing policy for this repository, `publish-nuget.yml`, and the `nuget-production` environment;
4. add `NUGET_USER` as a GitHub Actions repository or environment secret with the nuget.org profile name;
5. run the workflow again with the same version and `dry_run=false`;
6. optionally protect the `nuget-production` environment for a final human approval gate;
7. after a successful publish, the workflow creates or updates `v<version>` on GitHub, loads notes from `docs/release-notes/v<version>.md` when present, falls back to `CHANGELOG.md`, and attaches the verified `.nupkg`.

Do not pass API keys through workflow inputs and do not commit credentials into `NuGet.config`.

## Проверка содержимого `.nupkg`

Перед публикацией пакет проверяется отдельным скриптом:

```powershell
./scripts/verify-nupkg-contents.ps1 `
  -PackagePath artifacts/nuget/SeleniumPlaywrightMigrator.0.0.0-preview.1.nupkg
```

Linux/macOS вариант:

```bash
scripts/verify-nupkg-contents.sh artifacts/nuget/SeleniumPlaywrightMigrator.0.0.0-preview.1.nupkg
```

Проверка падает, если пакет не содержит публичные обязательные файлы или содержит локальные/private artifacts вроде `.agent-state`, `.migration`, `artifacts`, `bin`, `obj`, `.env`, `.local.json`.

## Smoke локальной установки

После pack пакет нужно установить именно как tool, а не запускать из source tree:

```powershell
./scripts/smoke-local-tool-package.ps1 -Version 0.0.0-preview.1
```

Linux/macOS вариант:

```bash
scripts/smoke-local-tool-package.sh 0.0.0-preview.1
```

Smoke создает временный `dotnet-tools.json`, ставит пакет из `artifacts/nuget`, запускает `--help`, запускает `--mode doctor` на тестовых fixtures и проверяет, что появился `doctor-report.md`.

## Bundle manifest и checksums

`package-agent-cli-bundle.ps1` теперь пишет два файла в корень bundle:

```text
MANIFEST.sha256
manifest.json
```

`MANIFEST.sha256` нужен для быстрой проверки целостности архива. `manifest.json` удобен для автоматической проверки в CI и для release artifacts.

Проверка bundle:

```powershell
./scripts/verify-agent-cli-bundle.ps1 `
  -BundleDirectory artifacts/agent-cli-bundle/tool `
  -RunHelp
```

`-RunHelp` дополнительно запускает CLI из published output и проверяет, что help доступен без repository source.

## Standalone public release archives

For users who should not install .NET SDK/runtime, build self-contained standalone bundles:

```powershell
./scripts/package-standalone.ps1 `
  -Version 0.0.0-preview.1
```

The script publishes runtime-specific self-contained folders and packages them into:

```text
artifacts/release/
  selenium-pw-migrator-0.0.0-preview.1-win-x64.zip
  selenium-pw-migrator-0.0.0-preview.1-linux-x64.tar.gz
  selenium-pw-migrator-0.0.0-preview.1-osx-x64.tar.gz
  selenium-pw-migrator-0.0.0-preview.1-osx-arm64.tar.gz
  selenium-pw-migrator-win-x64.zip
  selenium-pw-migrator-linux-x64.tar.gz
  selenium-pw-migrator-osx-x64.tar.gz
  selenium-pw-migrator-osx-arm64.tar.gz
  checksums.sha256
  standalone-release-manifest.json
```

`PublishSingleFile=false` is intentional. The migrator is Roslyn-heavy and must keep project-reference assemblies/resources next to the executable. The bundle still does not require .NET on the target machine when built self-contained.

Manual smoke for one archive:

```powershell
./scripts/verify-standalone-package.ps1 `
  -ArchivePath artifacts/release/selenium-pw-migrator-0.0.0-preview.1-win-x64.zip `
  -ChecksumsPath artifacts/release/checksums.sha256
```

Installation docs: [Standalone installation](standalone-installation.md) / [RU](standalone-installation.ru.md).

## Release process

Полный чеклист preview/stable release, публикации и rollback описан в [`docs/release-process.md`](release-process.md).
