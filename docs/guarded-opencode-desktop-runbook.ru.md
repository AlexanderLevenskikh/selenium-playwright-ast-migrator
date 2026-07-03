# Guarded OpenCode Desktop Migration Runbook

Этот документ — **канонический способ** запускать агентскую Selenium C# → Playwright migration через OpenCode Desktop.

Цель runbook: агент может исследовать проект и создавать migration artifacts, но не может тихо превратить неудачный запуск в красивый `FINAL` за счёт записи в target/POM project, suppression, empty tests или ослабления проверок.

Используй этот документ как единственную точку входа для guarded agent run. Остальные agent/autopilot документы являются deep dive или legacy context.

## 0. Модель запуска

Основной режим:

```text
MIGRATION_ARTIFACT_ONLY
```

Разрешённые записи:

```text
migration/**
```

Запрещённые записи:

```text
Web/**
**/*.csproj
nuget.config
real production POM project
real Playwright test project
root-level generated files
migration/scripts/check-scope.ps1
migration/scripts/check-final-gate.ps1
migration/.migration-kit/guard-checksums.json
```

Если агенту нужен POM/Playwright code, он должен создать только artifact/proposal/scaffold внутри `migration/**`, например:

```text
migration/runs/<run-id>/generated-pom/**
migration/runs/<run-id>/target-shadow/**
migration/proposals/**
```

`0 TODO` не считается успехом, если достигнуто через suppression, empty tests, weakened assertions, dummy known identifiers или запись в реальный target/POM project.

## 1. Подготовить product repo

Открой обычный `cmd.exe` или PowerShell. Команды ниже написаны для Windows.

Перед новым агентским запуском product repo должен быть чистым:

```cmd
cd /d "C:\path\to\product-repo"
git status --short --untracked-files=all
```

Если остались изменения от старого неудачного запуска, сначала сохрани или убери их:

```cmd
git stash push -u -m "backup failed opencode run before guarded retry"
git status --short --untracked-files=all
```

Новый guarded run не должен стартовать поверх старого грязного состояния, иначе scope guard честно будет ругаться.

## 2. Собрать migrator tool локально

В репозитории migrator:

```cmd
cd /d "C:\path\to\selenium-playwright-ast-migrator"

dotnet build --no-restore
dotnet test Migrator.Tests\Migrator.Tests.csproj --no-restore

dotnet pack Migrator.Cli\Migrator.Cli.csproj -c Release -p:PackageVersion=0.0.0-preview.1 -o artifacts\nuget
```

Для быстрой проверки только agent guardrails:

```cmd
dotnet test Migrator.Tests\Migrator.Tests.csproj --no-restore --filter AgentLoopHardeningTests
```

## 3. Установить или обновить local dotnet tool в product repo

В product repo:

```cmd
cd /d "C:\path\to\product-repo"

if not exist ".config\dotnet-tools.json" dotnet new tool-manifest

dotnet tool update SeleniumPlaywrightMigrator --version 0.0.0-preview.1 --add-source "C:\path\to\selenium-playwright-ast-migrator\artifacts\nuget"
if errorlevel 1 dotnet tool install SeleniumPlaywrightMigrator --version 0.0.0-preview.1 --add-source "C:\path\to\selenium-playwright-ast-migrator\artifacts\nuget"

dotnet tool run selenium-pw-migrator -- --help
```

## 4. Установить или обновить migration kit и OpenCode Desktop config

Из корня product repo предпочтительно использовать один bootstrap:

```cmd
cd /d "C:\path\to\product-repo"

dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source . --config migration/profiles/adapter-config.json --opencode-install auto
```

Эта команда устанавливает/обновляет `migration/`, добавляет `opencode-team/`, запускает `kit doctor` и ставит project-local OpenCode Desktop config. Если нужен ручной fallback, выполни старую цепочку:

```cmd
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --source . --config migration/profiles/adapter-config.json --backup --with-team
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
```

Проверь, что guard scripts запускаются:

```cmd
powershell -NoProfile -ExecutionPolicy Bypass -File ".\migration\scripts\check-scope.ps1" -RepoRoot . -AllowedRoots migration

powershell -NoProfile -ExecutionPolicy Bypass -File ".\migration\scripts\check-final-gate.ps1" -Workspace migration -RepoRoot . -AllowedRoots migration
```

`check-final-gate.ps1` может вернуть `FINAL_GATE_FAIL`, если ещё нет свежего run/evidence. Это нормально. На этом шаге важно, чтобы скрипт не падал с parse/runtime error.

После этого workspace уже не нужно собирать вручную: в `migration/` должны быть `harness/`, `state/harness-policy.json`, `scripts/new-harness-run.ps1`, `scripts/check-harness-policy.ps1`, `scripts/write-harness-event.ps1`, `dashboard/i18n/en.json`, `dashboard/i18n/ru.json` и `opencode-team/`. Сам агент при `/supervised-task` создаёт или возобновляет `migration/runs/<run-id>/`.

## 5. Ручной fallback для OpenCode Desktop project-local config

Если ты не использовал `kit bootstrap-opencode --opencode-install auto`, не полагайся на `OPENCODE_CONFIG` из консоли. Установи project-local config прямо в корень product repo:

```powershell
Set-Location "C:\path\to\product-repo"
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop
```

Или явно укажи target:

```powershell
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop -Target "C:\path\to\product-repo"
```

`ProjectDesktop` должен создать или обновить только эти project-local файлы:

```text
opencode.jsonc
.opencode/agents/*
.opencode/commands/*
```

Он не должен писать в `$HOME`, `%USERPROFILE%` или user-global OpenCode config.

Если PowerShell execution policy блокирует `.ps1`, можно временно выполнить ручную установку из `cmd.exe`:

```cmd
cd /d "C:\path\to\product-repo"

copy /Y "migration\opencode-team\global\.config\opencode\opencode.jsonc" "opencode.jsonc"

if not exist ".opencode" mkdir ".opencode"
if not exist ".opencode\agents" mkdir ".opencode\agents"
if not exist ".opencode\commands" mkdir ".opencode\commands"

xcopy "migration\opencode-team\global\.config\opencode\agents" ".opencode\agents" /E /I /Y
xcopy "migration\opencode-team\global\.config\opencode\commands" ".opencode\commands" /E /I /Y
```

Проверка:

```cmd
dir "C:\path\to\product-repo\opencode.jsonc"
dir "C:\path\to\product-repo\.opencode\agents"
dir "C:\path\to\product-repo\.opencode\commands"
```

## 6. Открыть OpenCode Desktop

Открывай в Desktop именно корень product repo:

```text
C:\path\to\product-repo
```

Не открывай `migration/`, `Web/**` или POM/PW subproject как workspace для guarded run: project-local `opencode.jsonc` лежит в корне repo.

Не включай auto-approve. Shell approve-запросы надо проверять вручную.

## 7. Запустить supervised task

В OpenCode Desktop:

```text
/supervised-task
```

Затем дай prompt:

```text
Read migration/prompts/kickoff-prompt.txt and execute it.

Before planning, read:
- migration/AGENT_CONTRACT.md
- migration/state/harness-policy.json
- migration/state/harness-run.json, if it exists
- migration/state/final-gate.md
- migration/state/stop-policy-checklist.md

If there is no active matching harness run, create one first:

powershell -NoProfile -ExecutionPolicy Bypass -File ".\migration\scripts\new-harness-run.ps1" -Workspace migration -TaskTitle "Guarded migration batch" -Goal "Run one bounded artifact-only Selenium to Playwright migration batch."

Run in MIGRATION_ARTIFACT_ONLY mode.

Non-negotiable rules:
- Allowed writes: migration/** only.
- Do not edit Web/**, real POM project, real Playwright project, *.csproj, nuget.config, root-level generated files.
- If POM or Playwright code is needed, create only generated/shadow/proposal artifacts under migration/**.
- TODO reduction via suppression, empty tests, weakened assertions, dummy known identifiers, or target-project edits is failure, not progress.
- Do not ask “what should I do next” unless the next action requires changing scope or writing outside migration/**.
- If blocked, write a precise BLOCKED report under migration/runs/<run-id>/ and stop.
- After each major batch, run scope, harness-policy, and final gate checks.
- Record meaningful lifecycle events with migration/scripts/write-harness-event.ps1 when practical.
- Do not claim FINAL unless strict final gate passes.

Strict final gate command:

powershell -NoProfile -ExecutionPolicy Bypass -File ".\migration\scripts\check-final-gate.ps1" -Workspace migration -RepoRoot . -AllowedRoots migration -RequireOpenCodeExport -RequireExplainTodo -RequireVerificationArtifacts

If strict final gate fails, report:
NOT FINAL - INVESTIGATION RESULT ONLY.
```

## 8. Approve / deny правила

Обычно можно approve:

```text
git status
git diff
git log
dotnet tool run selenium-pw-migrator -- ...
powershell ... new-harness-run.ps1
powershell ... write-harness-event.ps1
powershell ... check-scope.ps1
powershell ... check-harness-policy.ps1
powershell ... check-final-gate.ps1
powershell ... build-harness-dashboard.ps1
read-only file inspection
writes strictly inside migration/**
```

Review carefully или deny:

```text
python ...
py ...
powershell ...
pwsh ...
cmd /c ...
Copy-Item
Move-Item
Set-Content
Out-File
rm / del / Remove-Item
git reset
git checkout
```

Никогда не approve shell-команды, которые могут писать в:

```text
Web/**
**/*.csproj
nuget.config
real POM project
real Playwright project
migration/scripts/check-scope.ps1
migration/scripts/check-final-gate.ps1
migration/.migration-kit/guard-checksums.json
```

Если агент утверждает, что shell-команда пишет строго в `migration/**`, требуй, чтобы это было явно видно из команды.

## 9. Проверки после каждого крупного batch

В отдельном терминале:

```cmd
cd /d "C:\path\to\product-repo"

git status --short --untracked-files=all

powershell -NoProfile -ExecutionPolicy Bypass -File ".\migration\scripts\check-scope.ps1" -RepoRoot . -AllowedRoots migration

powershell -NoProfile -ExecutionPolicy Bypass -File ".\migration\scripts\check-final-gate.ps1" -Workspace migration -RepoRoot . -AllowedRoots migration
```

Если `git status` показывает изменения вне `migration/**`, run считается failed, даже если TODO стало `0`.

## 10. Финал

Агент не имеет права писать `FINAL`, пока strict final gate не проходит:

```cmd
powershell -NoProfile -ExecutionPolicy Bypass -File ".\migration\scripts\check-final-gate.ps1" -Workspace migration -RepoRoot . -AllowedRoots migration -RequireOpenCodeExport -RequireExplainTodo -RequireVerificationArtifacts
```

Если strict gate не прошёл, допустимый статус:

```text
NOT FINAL - INVESTIGATION RESULT ONLY
```

Final evidence должен быть машинно проверяемым: scope-clean, guard checksums intact, no dangerous suppression, no empty tests after suppression, config validate evidence, project verify или честный `NOT RUNTIME READY`, OpenCode export/evidence, explain-todo и verification artifacts для latest run.

## 11. Forensic export после run

После остановки/финала выгрузи OpenCode Desktop session bundle через exporter script.

Минимальный bundle для расследования:

```text
opencode-direct-export.json
opencode-chat-graph.json
timeline.md
summary.txt
REVIEW_REQUEST.md
git/
project-artifacts/
```

Перед передачей ревьюеру проверь, что в bundle есть subagent/background-agent traces и что нет секретов.

## 12. Troubleshooting

### OpenCode Desktop не подхватил config

Проверь, что Desktop открыт в корне repo, где лежат:

```text
opencode.jsonc
.opencode/agents
.opencode/commands
```

Полностью закрой Desktop и открой заново.

### `ProjectDesktop` поставил config не туда

Это bug. Не запускай его повторно. Укажи target явно:

```powershell
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop -Target "C:\path\to\product-repo"
```

### PowerShell блокирует `.ps1`

Запускай через:

```cmd
powershell -NoProfile -ExecutionPolicy Bypass -File ".\path\to\script.ps1"
```

Если блокировка идёт через group policy / SRP / AppLocker, используй manual copy для OpenCode config и запускай guard scripts из разрешённой среды.

### Агент часто спрашивает “что дальше?”

Это не повод разрешать broad writes. Правильный ответ: следовать decision tree из `migration/AGENT_CONTRACT.md`. Если next action требует forbidden write, агент должен остановиться с `BLOCKED_BY_SCOPE`, а не спрашивать vague continuation.

### Агент пытается снизить TODO suppression’ом

Такой run failed. TODO reduction через suppression, empty tests, weakened assertions или dummy known identifiers не считается progress.

### `check-final-gate.ps1` падает

Это infra blocker. Не принимать `FINAL`. Сначала исправить gate или зафиксировать `NOT FINAL - INVESTIGATION RESULT ONLY`.

## 13. Ссылки на детали

Canonical entrypoint:

```text
docs/guarded-opencode-desktop-runbook.ru.md
```

Useful details:

```text
docs/tool-installation.md
docs/packaging-and-distribution.md
docs/troubleshooting.md
templates/migration-kit/AGENT_CONTRACT.md
templates/migration-kit/state/final-gate.md
templates/opencode-team/INSTALLATION-SAFETY.md
```

Old root prompt packs and broad autopilot launch docs were removed to avoid conflicting instructions. Use git history for archaeology.


Windows OpenCode Desktop shortcut: `--project-desktop` остаётся alias для `--opencode-install project-desktop`.
