# Режимы `/supervised-task`

`/supervised-task` — команда OpenCode, устанавливаемая через `kit bootstrap-opencode`. Слова после неё — **аргументы, которые интерпретирует prompt команды**, а не вложенные CLI-подкоманды `selenium-pw-migrator`.

Установленный source of truth: `.opencode/commands/supervised-task.md`; шаблон: `templates/opencode-team/global/.config/opencode/commands/supervised-task.md`.

## Базовые режимы запуска

| Команда | Алиасы | Когда использовать | Ожидаемое поведение |
|---|---|---|---|
| `/supervised-task` | нет | Безопасно продолжить по сохранённому state | Читает current ticket, continuation, final gate, wave и memory state; выбирает следующую bounded-задачу без широкого меню. По умолчанию останавливается на следующем свежем успешном checkpoint. |
| `/supervised-task <bounded запрос>` | свободный текст | Передать supervisor конкретную ограниченную задачу | Сохраняет state-aware dispatch, scope, review, risk, recovery и final-gate правила. Не обходит активный blocker или current ticket. |
| `/supervised-task waves` | `/supervised-task wave`, `/supervised-task wavefront`, `/supervised-task start waves` | Запустить или подготовить bounded wavefront migration | Разрешает configured source, при необходимости обновляет/bootstrap kit, запускает doctor и auto-tuning без агентов, строит affinity-aware wave-план, материализует первую pending wave и работает только в её локальном scope. |
| `/supervised-task waves fresh` | `/supervised-task fresh waves`, `/supervised-task restart waves` | Архивировать разросшийся pilot и начать чистый bounded wavefront | Запускает `start-fresh-wavefront-run.*`, архивирует plan/runs/volatile state в `migration/archive/**`, сохраняет project memory и source scope, затем перепланирует с однотестовой smoke-wave. |
| `/supervised-task continue` | нет | Продолжить после сохранённого checkpoint `FINAL_STOPPED_FOR_REVIEW` | Запускает закрытый post-final цикл: research, review исследования, task slicing, change review и максимум одну bounded executor-задачу после одобрения. Обычный `/supervised-task` тоже возобновляет этот сохранённый state. |
| `/supervised-task continue <topic or task>` | нет | Продолжить с конкретной темой исследования или bounded-задачей | Использует текст как research topic или requested task, но не обходит current ticket, review, scope, policy, risk и remediation budgets. |
| `/supervised-task sentinel` | `/supervised-task inspect`, `/supervised-task qa` | Явно запустить процессную/форензик-проверку | Экспортирует session evidence, вызывает `harness-sentinel`, завершает `sentinel-inspection.json` и маршрутизирует agent-executable findings в bounded follow-up tickets. Этот режим всегда одноразовый. |

## Профили Harness

Используй `--execution-profile`, чтобы выбрать объём оркестрации Harness для текущего запуска:

| Профиль | Пример команды | Поведение |
|---|---|---|
| `fast` | `/supervised-task waves --execution-profile fast` | **Режим по умолчанию и облегчённый Harness.** Сначала executor; reviewer/watchdog/sentinel подключаются только по risk, policy, no-progress или требованиям final handoff. |
| `standard` | `/supervised-task waves --execution-profile standard` | Сбалансированный режим. Executor и reviewer ожидаются всегда; watchdog/sentinel остаются условными до необходимости. |
| `audit` | `/supervised-task waves --execution-profile audit` | **Полный Harness.** Обязательны executor, reviewer, watchdog и sentinel. |

Модификатор работает также без `waves`, с `continue` и с `continuous`:

```text
/supervised-task --execution-profile fast
/supervised-task continue --execution-profile standard
/supervised-task continuous --execution-profile fast
/supervised-task waves continuous --execution-profile audit
```

Если модификатор не указан, для нового run выбирается `fast`. Уже созданная wave сохраняет неизменяемый профиль из `execution-policy.json`. Чтобы сменить профиль, создай свежую wave/run; не переписывай policy вручную.

Во всех профилях остаются обязательными scope enforcement, validation, final reviewer, final sentinel и final gate. Профиль меняет стоимость оркестрации, а не гарантии безопасности.

## Модификатор continuous

Добавь `continuous` или `--continuation auto`, чтобы текущий запуск не останавливался на checkpoint, после которого обычно пришлось бы вручную вводить `/supervised-task continue`.

Формы полностью эквивалентны:

```text
/supervised-task continuous
/supervised-task --continuation auto

/supervised-task continue continuous
/supervised-task continue --continuation auto

/supervised-task waves continuous
/supervised-task waves --continuation auto

/supervised-task waves fresh continuous
/supervised-task waves fresh --continuation auto
```

Модификатор разбирается отдельно от базового режима:

| Continuous-команда | Значение |
|---|---|
| `/supervised-task continuous` | Обычное state-aware продолжение, но без паузы на свежем успешном checkpoint, пока есть разрешённая runtime-ом работа. |
| `/supervised-task <bounded request> continuous` | Выполнить bounded-запрос и продолжать через последующие разрешённые continuation-state. |
| `/supervised-task continue continuous` | Возобновить post-final loop и последовательно потреблять одобренные bounded tickets/checkpoints. |
| `/supervised-task waves continuous` | Запустить/возобновить wavefront и переходить к следующим допустимым waves без ручного `continue`. |
| `/supervised-task waves fresh continuous` | Сначала архивировать и перепланировать, затем выполнять новый wavefront непрерывно. |

`continuous` сохраняется в state активного run, поэтому compaction чата или новая OpenCode-сессия не должны незаметно выключать режим. При этом он остаётся локальным для run: реальное terminal condition, явный `stop`/`pause` или fresh/restarted run очищает его. Режим не изменяет execution profile, policy, role budgets, remediation budgets, scope, review, sentinel, validation или final gate и не вызывает рекурсивно новую slash-команду.

`sentinel`, `inspect` и `qa` остаются одноразовыми forensic-режимами даже при наличии continuous-модификатора.

## Через какие состояния проходит continuous

После каждого bounded action режим перечитывает machine-readable state и продолжает через:

- `CONTINUE_REQUIRED`;
- `SAFE_CHECKPOINT`;
- свежий успешный `FINAL`/PASS checkpoint, если осталась одобренная работа;
- сохранённый `FINAL_STOPPED_FOR_REVIEW`;
- следующую pending wave только после очистки current-ticket, gate, sentinel, scope и wave-quality-budget state.
- завершённый backlog, если quality remediation ещё остаётся: запустить `slice-gate-followups` и создать следующий bounded ticket вместо handoff;
- `CONTAMINATED_BY_FULL_SCOPE_RERUN`: отдельно сохранить полный exploratory draft, восстановить точные wave-local evidence и перезапустить materialized wave wrapper.

Checkpoint по-прежнему записывается в evidence и не превращается в `DONE`; он лишь перестаёт быть обязательной пользовательской паузой. Формулировка «ровно один bounded action» относится к одному безопасно проверяемому циклу, а не ко всему continuous-вызову: после каждого ticket оркестратор повторно запускает gate-проверки, перечитывает state и начинает следующий разрешённый цикл.

## Жёсткие условия остановки

Любой режим, включая continuous, останавливается при:

- `DONE`;
- `FINAL_WITH_LIMITATIONS` или `WAVE_REMEDIATION_BUDGET_EXHAUSTED`;
- `HUMAN_DECISION_REQUIRED`;
- конкретном состоянии `BLOCKED` / `BLOCKED_*`, для которого нет agent-executable remediation;
- critical risk или подтверждённом scope violation;
- отказе permission на запись;
- malformed, tampered или противоречивом runtime/evidence;
- `NO_PROGRESS_DETECTED` после исчерпания разрешённой смены стратегии;
- отсутствии обязательного пользовательского ввода;
- исчерпании role, remediation, loop, time или другого autonomous budget;
- явной команде пользователя остановиться.

`BLOCKED_BY_WAVE_QUALITY_BUDGET` — особое нетерминальное routing-состояние, когда в нём есть actionable remediation `nextAction` и remediation budget ещё не исчерпан: оно блокирует следующую wave, но continuous продолжает bounded tickets для mappings/config/POM/recognizers.

Continuous — это автоматическое продолжение, а не бесконечное выполнение.

## Обычная семантика остановки

Без continuous-модификатора свежий успешный `FINAL` один раз останавливается для review и рекомендует `/supervised-task continue`. Следующий zero-argument вызов или `/supervised-task continue` возобновляет работу, когда state уже `FINAL_STOPPED_FOR_REVIEW`.

`FINAL_WITH_LIMITATIONS` / `WAVE_REMEDIATION_BUDGET_EXHAUSTED` всегда является жёсткой автономной остановкой. Используй `/supervised-task waves fresh`, чтобы архивировать pilot и начать заново, либо явно разреши изменение remediation budget.

## Обновление существующего workspace

После установки новой версии Migrator обнови guarded runtime scripts и OpenCode command pack:

```powershell
selenium-pw-migrator kit bootstrap-opencode `
  --workspace migration `
  --source <selenium-source> `
  --opencode-install none
```

Если workspace уже существует, `bootstrap-opencode` работает в update-режиме: создаёт backup, перезаписывает kit-owned runtime scripts **и управляемый command pack `migration/opencode-team/**`**, обновляет guard checksums, а затем заново синхронизирует `.opencode/agents` и `.opencode/commands` в корне репозитория. Для обновления управляемых OpenCode-команд `--force` больше не нужен; project-owned файлы вроде корневого `AGENTS.md` сохраняют более осторожные правила.

## Проверяй установленные scripts, а не только templates

Repository validation проверяет source tree Migrator. Чтобы дополнительно проверить сгенерированную или установленную копию workspace:

```powershell
pwsh ./scripts/validate-scripts.ps1 -Root . -Workspace <путь-к-product-repo>/migration -RequireShell
```

Внутри product repository используй workspace-local validator:

```powershell
pwsh ./migration/scripts/validate-installed-scripts.ps1 -Workspace migration -RequireShell
```
