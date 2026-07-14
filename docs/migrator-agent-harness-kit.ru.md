# Migrator Agent Harness Kit

Это справочный документ, а не второй способ запуска агента. Каноническая процедура guarded-запуска OpenCode Desktop остаётся в `docs/guarded-opencode-desktop-runbook.ru.md`.

## Назначение

Kit превращает migration run в управляемый файловый workflow:

1. Машиночитаемая policy описывает, что агент может выполнять автоматически, а что запрещено.
2. Bootstrapper run создаёт `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md` и trace-файлы в `migration/runs/<run-id>/`.
3. Guard-скрипты проверяют scope, финальное качество и конфигурацию Harness.
4. Агент работает автономно только внутри этой границы.

## Принцип устройства

Промпты направляют поведение; скрипты обеспечивают соблюдение правил.

Агент может утверждать, что выполнил требования, но его финальный ответ не считается достоверным, пока не пройдут детерминированные проверки.

## Bootstrap

Подробности настройки для разных окружений см. в [Agent environments](agent-environments.ru.md).

Пользователь не должен вручную создавать подпапки `migration/` или `migration/runs/<run-id>/`. Предпочтительный bootstrap OpenCode выполняется одной командой из корня product repository:

```powershell
dotnet tool run selenium-pw-migrator -- kit bootstrap-opencode --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --opencode-install auto
```

Команда устанавливает или обновляет migration workspace, добавляет шаблоны OpenCode team, запускает `kit doctor` и устанавливает project-local конфигурацию OpenCode Desktop. Ручной fallback:

```bash
dotnet tool run selenium-pw-migrator -- kit update --workspace migration --source ./SeleniumTests --config migration/profiles/adapter-config.json --backup --with-team
dotnet tool run selenium-pw-migrator -- kit doctor --workspace migration
```

```powershell
.\migration\opencode-team\scripts\install-windows.ps1 -Mode ProjectDesktop
```

После этого `/supervised-task` или `/harness-run` управляет lifecycle run. Если подходящего active run нет, orchestrator обязан вызвать `migration/scripts/new-harness-run.ps1`, а затем продолжить работу по созданным run-файлам.

Иными словами, агент может начать «с нуля» после того, как установлены инструмент и project-local конфигурация OpenCode: дальше он сам управляет migration workspace и run artifacts. Первая установка конфигурации OpenCode остаётся одноразовым bootstrap-шагом, потому что до неё project-local роли ещё недоступны.

## Минимальный lifecycle

```text
new-harness-run.ps1
  -> создаёт run artifacts
  -> обновляет agent-state/current-ticket/run-ledger/handoff
agent читает autopilot-loop-prompt.txt
  -> работает внутри migration/**
  -> записывает trace events
  -> запускает scope/final/policy checks
check-final-gate.ps1
  -> решает, допустимо ли заявлять PASS/FINAL
```

## Правило autopilot

Агент не должен просить у пользователя разрешение на действия, уже разрешённые в `state/harness-policy.json` и конфигурации permissions OpenCode.

Он обязан остановиться с конкретным blocker при:

- записи вне разрешённых roots;
- изменении guard-скриптов, checksums или permissions;
- установке пакетов или обновлении зависимостей;
- сетевом доступе;
- `git commit`, `push`, `reset` или `clean`;
- разрушительном удалении или перемещении файлов;
- изменении реальных product/POM/Playwright project files в artifact-only mode.

## Файлы

```text
migration/
  AGENT_CONTRACT.md
  agent-state.md
  current-ticket.md
  state/
    harness-policy.json
    harness-run.json
    harness-events.jsonl
    harness-policy-result.md/json
    run-ledger.md
    handoff.md
    final-gate.md
  runs/
    run-001/
      Prompt.md
      Plan.md
      Implement.md
      Documentation.md
      trace.jsonl
```

## Зачем это нужно

Без Harness агент ведёт себя как тревожный начинающий разработчик и спрашивает разрешение перед каждой shell-командой.

С Harness разрешённый коридор задан явно: читать, искать, собирать, тестировать, мигрировать и записывать migration artifacts. Опасный коридор тоже задан явно: реальные project edits, изменения guardrails, `git push`, обновление зависимостей, secrets и network.

## Dogfood smoke

Для первой repository-level проверки используй `docs/migrator-agent-harness-dogfood.md` / `docs/migrator-agent-harness-dogfood.ru.md` и `scripts/run-harness-dogfood-smoke.ps1`. Smoke устанавливает kit в `.dogfood/migration`, создаёт run, записывает events и проверяет `check-harness-policy.ps1` с явными dogfood allowed roots.

## English-first и локализация dashboard

Английский является каноническим языком публичной документации Harness Kit, prompts, labels отчётов, event codes и терминологии dashboard.

Русский поддерживается как secondary localization через документы `*.ru.md` и словари dashboard, например `en.json` / `ru.json`.

Машиночитаемые данные должны оставаться language-neutral. Храни стабильные английские codes, например `final-gate-pass`, `scope-guard-failed` или `harness-policy-pass`; переводи только UI labels и документацию.

Dashboard по умолчанию должен быть английским и предоставлять переключатель языка:

```text
Language: English / Русский
```

## Harness dashboard

Используй `docs/migrator-agent-harness-dashboard.md` / `docs/migrator-agent-harness-dashboard.ru.md` и `scripts/run-harness-dashboard-smoke.ps1`, чтобы создать статический dashboard из active harness run.

Установленный workspace содержит:

```text
migration/dashboard/
  i18n/
    en.json
    ru.json
  harness/
    index.html
    harness-dashboard.json
    harness-dashboard.md
```

English используется по умолчанию. Русский доступен через переключатель `languageSelect`. JSON dashboard остаётся language-neutral.

Windows shortcut для OpenCode Desktop: `--project-desktop` остаётся alias для `--opencode-install project-desktop`.

## Строгий протокол продолжения Harness

После нефинального final gate прочитай `migration/state/continuation-decision.json`. Если там `CONTINUE_REQUIRED`, состояние `NOT FINAL` не является точкой остановки: до user-facing handoff нужно выполнить следующий bounded action внутри `migration/**`. Свежий checkpoint `FINAL` в default mode один раз останавливается для review. Если invocation запущен с `continuous` или `--continuation auto`, checkpoint сохраняется, после чего агент сразу входит в следующий guarded bounded cycle или допустимую следующую wave. Любой последующий `/supervised-task`, когда `harness-run.json` уже находится в `FINAL_STOPPED_FOR_REVIEW`, автоматически возобновляет закрытый post-final loop. Остановка обязательна при guard/scope/policy blocker, human decision, critical risk, malformed evidence, missing input, no-progress/plateau, limitations или исчерпании autonomous budget.

## Auto-next dispatch для `/supervised-task`

`/supervised-task` задуман как основная кнопка migration workflow для тестировщика и может запускаться без аргументов. Команда читает `continuation-decision.json`, `final-gate-result.json`, `current-ticket.md` и последние run evidence, после чего либо продолжает обязательное нефинальное действие, либо останавливается для review после checkpoint `FINAL`.

После свежего `FINAL` default mode не должен предлагать пользователю широкое меню и не должен начинать новый ticket в том же checkpoint. Он объясняет паузу и рекомендует одну команду `/supervised-task continue`. Continuous mode записывает тот же checkpoint и может начать следующий guarded cycle без пользовательской паузы. При следующем invocation, когда workspace уже находится в `FINAL_STOPPED_FOR_REVIEW`, zero-argument `/supervised-task` запускает или возобновляет закрытый post-final цикл research → research-lead → task-slicing → change-review. Implementation начинается только после одобренного research, появления `migration/current-ticket.md`, одобрения change-reviewer, конкретного implementation request или разрешённого bounded auto-continuation.

Когда final gate проходит, `check-final-gate.ps1` обновляет `migration/state/harness-run.json` до `FINAL_STOPPED_FOR_REVIEW`, если файл существует. В default mode нужно объяснить, почему SUCCESS checkpoint поставлен на паузу, и рекомендовать `/supervised-task continue`. В `continuous` / `--continuation auto` тот же checkpoint сохраняется, но state немедленно перечитывается, и выполнение продолжается до реального terminal condition.
