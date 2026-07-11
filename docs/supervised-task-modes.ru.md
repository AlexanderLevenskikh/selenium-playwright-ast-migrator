# Режимы `/supervised-task`

`/supervised-task` — команда OpenCode, устанавливаемая через `kit bootstrap-opencode`. Слова после неё — **аргументы, которые интерпретирует prompt команды**, а не вложенные CLI-подкоманды `selenium-pw-migrator`.

Установленный source of truth: `.opencode/commands/supervised-task.md`; шаблон в Migrator: `templates/opencode-team/global/.config/opencode/commands/supervised-task.md`.

## Справочник команд

| Команда | Алиасы | Когда использовать | Ожидаемое поведение |
|---|---|---|---|
| `/supervised-task` | нет | Безопасно продолжить по сохранённому state | Читает current ticket, continuation, final gate, wave и memory state; выбирает следующую bounded-задачу без широкого меню. |
| `/supervised-task waves` | `/supervised-task wave`, `/supervised-task wavefront`, `/supervised-task start waves` | Запустить или подготовить bounded wavefront migration | Разрешает configured source, при необходимости обновляет/bootstrap kit, запускает doctor, выполняет auto-tuning эксперимент без агентов, пишет `wave-tuning.md/json`, строит affinity-aware wave-план, материализует первую pending wave и работает только в её локальном scope. |
| `/supervised-task waves fresh` | `/supervised-task fresh waves`, `/supervised-task restart waves` | Остановить разросшийся pilot и начать чистый bounded wavefront | Запускает `start-fresh-wavefront-run.*`, архивирует plan/runs/volatile state в `migration/archive/**`, сохраняет project memory и source scope, затем перепланирует с однотестовой smoke-wave. |
| `/supervised-task continue` | нет | Продолжить после сохранённого checkpoint `FINAL_STOPPED_FOR_REVIEW` | Запускает закрытый post-final цикл: research, review исследования, task slicing, change review и максимум одну bounded executor-задачу после одобрения. Обычный `/supervised-task` в этом state делает то же самое. |
| `/supervised-task continue <тема или задача>` | нет | Продолжить с конкретной темой исследования или bounded-задачей | Использует текст как research topic или requested task, но не обходит current ticket, review, scope, policy и remediation budgets. |
| `/supervised-task sentinel` | `/supervised-task inspect`, `/supervised-task qa` | Явно запустить процессную/форензик-проверку | Экспортирует session evidence, вызывает `harness-sentinel`, завершает `sentinel-inspection.json` и маршрутизирует agent-executable findings в bounded follow-up tickets. |
| `/supervised-task <bounded запрос>` | свободный текст | Передать supervisor конкретную ограниченную задачу | Сохраняет state-aware dispatch, scope, review и final-gate правила. Не обходит активный blocker или current ticket. |

## Важная семантика остановки

Свежий успешный `FINAL` один раз останавливается для review. Следующий zero-argument вызов или `/supervised-task continue` продолжает работу, когда state уже `FINAL_STOPPED_FOR_REVIEW`.

`FINAL_WITH_LIMITATIONS` / `WAVE_REMEDIATION_BUDGET_EXHAUSTED` — жёсткая автономная остановка. Используй `/supervised-task waves fresh`, чтобы архивировать pilot и начать заново, либо явно разреши изменение remediation budget.

## Обновление существующего workspace

После установки новой версии Migrator обнови guarded runtime scripts и OpenCode command pack:

```powershell
selenium-pw-migrator kit bootstrap-opencode `
  --workspace migration `
  --source <selenium-source> `
  --opencode-install none
```

Если workspace уже существует, `bootstrap-opencode` работает в update-режиме: создаёт backup, перезаписывает kit-owned runtime scripts, обновляет guard checksums и заново применяет repository-root OpenCode commands.

## Проверяй установленные scripts, а не только исходные templates

Проверка репозитория валидирует source tree Migrator. Чтобы проверить ещё и копии, установленные в workspace:

```powershell
pwsh ./scripts/validate-scripts.ps1 -Root . -Workspace <путь-к-product-repo>/migration -RequireShell
```

Из корня product repository используй workspace-local validator:

```powershell
pwsh ./migration/scripts/validate-installed-scripts.ps1 -Workspace migration -RequireShell
```

Это важно после обновлений: исходный template может быть валиден, а старая копия `migration/scripts/*.ps1` — оставаться повреждённой. Final gate сначала проверяет PowerShell-синтаксис установленных scripts и только затем запускает artifact hygiene.
