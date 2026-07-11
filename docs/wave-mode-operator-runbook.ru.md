# Операторский runbook для wave mode

Это инструкция по эксплуатации уже созданного migration workspace. Она не заменяет канонический guarded launch flow: для настройки OpenCode Desktop и свежего репозитория начинай с [`guarded-opencode-desktop-runbook.ru.md`](guarded-opencode-desktop-runbook.ru.md).

Используй этот документ, когда `/supervised-task waves` или `/supervised-task continue` уже создали состояние в `migration/**`, и нужно понять, что делать дальше без гадания. Полная таблица режимов и алиасов: [`supervised-task-modes.ru.md`](supervised-task-modes.ru.md).

## Модель в голове

Wave mode — это не слепой batch-migrator. Это закрытый цикл:

```text
wave run
  -> verify / scope / final gate
  -> sentinel inspection при необходимости
  -> gate-followup slicer
  -> current-ticket executor loop
  -> wave quality budget
  -> mapping research memory
  -> feedback bundle, если нужно улучшать сам мигратор
```

Главное правило: **не стартовать следующую wave, пока есть unresolved gate, current ticket, high/critical sentinel finding или blocked wave-quality budget.**

## Обычный старт

Из корня product repo:

```bash
selenium-pw-migrator kit bootstrap-opencode --workspace migration --source ./SeleniumTests --opencode-install auto
```

Потом открыть репозиторий в OpenCode и выполнить:

```text
/supervised-task waves
```

Wave scope — файловый. Если wave берёт 3 source files, внутри всё равно может быть 20 tests и сотни actions. Отчёты должны честно писать `source files`, `tests`, `actions`, `TODOs` и `syntax-fallback ratio`.

## Безопасное продолжение

Для существующего workspace:

```text
/supervised-task continue
```

или просто:

```text
/supervised-task
```

Dispatcher должен выбирать следующий bounded action в таком порядке:

1. Если есть `migration/current-ticket.md`, сначала завершить или заблокировать этот ticket.
2. Если final gate заблокирован и ticket ещё не создан, выполнить `migration/scripts/slice-gate-followups.ps1 -Workspace migration`.
3. Если есть open high/critical agent-executable sentinel findings, назначить их на bounded tickets.
4. Если wave quality budget заблокирован, собрать mapping research memory до следующей wave.
5. Только когда gates, tickets, findings и budgets чистые — выбирать следующую wave.

## Если final gate заблокирован

Смотри:

```text
migration/state/final-gate-result.json
migration/state/continuation-decision.json
migration/runs/<run-id>/Documentation.md
migration/runs/<run-id>/artifact-hygiene.md
```

Если `continuationStatus` или `allowedNextAction` говорит `BLOCKED_BY_GATE` и текущего ticket ещё нет:

```powershell
migration/scripts/slice-gate-followups.ps1 -Workspace migration
```

или на macOS/Linux/WSL:

```bash
migration/scripts/slice-gate-followups.sh -Workspace migration
```

Это создаёт:

```text
migration/current-ticket.md
migration/state/backlog/gate-followup-tasks.jsonl
migration/state/backlog/gate-followup-backlog.md
migration/state/current-ticket-status.json
```

После этого запускай `/supervised-task continue`. Агент должен провести `current-ticket.md` через reviewer/executor, а не начинать новую wave.

## Lifecycle current-ticket

`current-ticket.md` — активная bounded repair-задача. Её состояние хранится тут:

```text
migration/state/current-ticket-status.json
migration/state/current-ticket-ledger.jsonl
migration/runs/<run-id>/tickets/<ticket-id>.json
```

Статусы:

```text
READY -> IN_PROGRESS -> REVIEW_READY -> DONE
READY -> IN_PROGRESS -> BLOCKED
```

Полезные ручные команды:

```powershell
migration/scripts/update-current-ticket-status.ps1 -Workspace migration -Status IN_PROGRESS -Source operator
migration/scripts/update-current-ticket-status.ps1 -Workspace migration -Status BLOCKED -Source operator -Reason "Requires source cleanup outside migration/**"
```

`DONE` должен ставиться только после reviewer/final-gate evidence.

## Sentinel findings

Sentinel findings — append-only факты. Их lifecycle пишется отдельно:

```text
migration/state/sentinel-finding-ledger.jsonl
migration/state/sentinel-finding-status.json
migration/runs/<run-id>/sentinel/sentinel-finding-lifecycle.jsonl
```

Статусы:

```text
OPEN
ASSIGNED
FIX_ATTEMPTED
VERIFIED
CLOSED
BLOCKED
NON_AGENT_EXECUTABLE
ACCEPTED_RISK
```

High/critical agent-executable findings блокируют финальный успех, пока не станут `VERIFIED`, `CLOSED`, `NON_AGENT_EXECUTABLE` или `ACCEPTED_RISK` с evidence.

## Шумная wave / quality budget

После wave запусти или доверь final gate:

```powershell
migration/scripts/evaluate-wave-quality-budget.ps1 -Workspace migration
```

Он пишет `wave-quality-budget/v1`:

```text
migration/state/wave-quality-budget.json
migration/state/wave-quality-budget.md
migration/runs/<run-id>/wave-quality-budget.json
migration/runs/<run-id>/wave-quality-budget.md
```

Если статус `BLOCKED_BY_WAVE_QUALITY_BUDGET`, не начинай следующую wave. Сначала:

```powershell
migration/scripts/collect-mapping-research-memory.ps1 -Workspace migration
```

Он пишет `mapping-research-memory/v1`:

```text
migration/state/mapping-research-memory.json
migration/state/mapping-research-memory.md
migration/state/mapping-research-candidates.jsonl
```

Используй эту память, чтобы создать одну bounded improvement task: config mapping, POM mapping, recognizer improvement, renderer improvement или verify-project harness fix.

## Verify-project failures

Начинай с:

```text
migration/runs/<run-id>/project-verify-report.json
migration/runs/<run-id>/project-verify-report.md
migration/runs/<run-id>/project-verify-harness.csproj
```

Отчёт содержит `verify-project-harness/v1`: CPM detection, skipped build files, imported build files, package-version mode, snapshot path и SHA256. Для `NU1008` проверь, что temporary harness отключил central package management и пропустил `Directory.Packages.props`.

## Artifact hygiene

Перед final handoff:

```powershell
migration/scripts/validate-run-artifacts.ps1 -Workspace migration
```

`artifact-hygiene/v1` должен подтвердить:

```text
Plan.md не загрязнён shell/write payloads
Documentation.md не пишет success, когда final gate blocked
migration-board/wave-status содержат run-id и wave-id
session export имеет статус REAL_EXPORT или UNAVAILABLE_WITH_REASON
```

Если проверка падает, сначала исправь отчёты.

## Feedback bundle для автора мигратора

Если проблема похожа на gap самого мигратора, а не на cleanup проекта, собери безопасный bundle:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration
```

или на macOS/Linux/WSL:

```bash
migration/scripts/create-feedback-bundle.sh -Workspace migration
```

Bundle использует schema `feedback-bundle/v1`, по умолчанию исключает project source и generated `.cs` samples, и пишет `manifest.json`. Перед отправкой проверь `manifest.json`. Generated samples включай только осознанно:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration -IncludeGeneratedSamples -MaxGeneratedSamples 3
```

Хороший feedback bundle обычно содержит:

```text
mapping-research-memory.json/md
mapping-research-candidates.jsonl
wave-quality-budget.json/md
project-verify-report.json/md
project-verify-harness.csproj
migration-board.md/json
explain-todo.md
sentinel report/findings при необходимости
```

## Таблица решений оператора

| Ситуация | Что делать | Чего не делать |
|---|---|---|
| Есть `migration/current-ticket.md` | Завершить, проверить или заблокировать ticket | Стартовать новую wave |
| `final-gate-result.json` FAIL/BLOCKED | Запустить `slice-gate-followups` | Писать, что run complete |
| High/critical sentinel finding OPEN | Назначить/закрыть finding lifecycle | Игнорировать и продолжать |
| `BLOCKED_BY_WAVE_QUALITY_BUDGET` | Собрать mapping research memory | Генерировать ещё шумные файлы |
| `verify-project` упал | Смотреть report + harness snapshot | Считать generated code verified |
| Отчёт пишет “complete”, но gate красный | Запустить artifact hygiene и поправить docs | Верить summary |
| Пользователь хочет улучшить мигратор | Собрать feedback bundle | Просить весь приватный repo |

## Быстрый checklist перед следующей wave

```text
[ ] нет active current-ticket, или он DONE/BLOCKED с причиной
[ ] final gate не показывает unresolved hard failures
[ ] high/critical sentinel findings закрыты или явно routed
[ ] wave-quality-budget PASS или собрана mapping research memory
[ ] verify-project status понятен
[ ] artifact-hygiene/v1 PASS
[ ] Documentation.md пишет NOT FINAL, если gates blocked
```

## Проверка качества wave-плана

Перед первой волной прочитай `migration/plan/wave-tuning.md`. Профиль `auto` выполняет статический эксперимент без агентов и должен уменьшать число дорогих циклов ролей за счёт группировки тестов одного файла/POM. Одиночной по замыслу является только smoke-wave. `PASS`, `SOFT_LIMIT_EXCEEDED` и `HEAVY_SINGLE_TEST` разрешают выполнение; `BLOCKED` означает превышение широкой жёсткой границы и требует перепланирования.
