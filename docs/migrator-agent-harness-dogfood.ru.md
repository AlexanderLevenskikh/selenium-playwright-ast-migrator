# Dogfood Migrator Agent Harness

Этот документ описывает первый воспроизводимый dogfood-прогон для Migrator Agent Harness Kit.

Цель не в том, чтобы доказать, что любая миграция работает идеально. Цель — проверить, что сам harness устанавливается, создаёт resumable run, проходит guardrails и может использоваться агентом без рутинных вопросов “продолжать?”.

## Целевая задача dogfood

Используй маленькую docs/template-only задачу. Хороший первый вариант:

```text
Создать или обновить один reference/evidence artifact для Harness Kit без правок product source files.
```

Allowed roots для dogfood внутри репозитория Migrator:

```text
migration/**
docs/**
templates/migration-kit/**
templates/opencode-team/**
scripts/**
Migrator.Tests/**
```

Эти roots шире обычного product migration run, потому что dogfood выполняется внутри самого репозитория Migrator. В обычном установленном product workspace режим остаётся artifact-only: записи только под `migration/**`.

## Smoke script

Из корня репозитория:

```powershell
pwsh .\scripts\run-harness-dogfood-smoke.ps1 -Clean
```

На macOS/Linux при установленном PowerShell:

```bash
./scripts/run-harness-dogfood-smoke.sh -Clean
```

Скрипт:

1. устанавливает migration kit в `.dogfood/migration`;
2. создаёт resumable harness run;
3. проверяет обязательные Harness Kit файлы;
4. запускает `check-harness-policy.ps1` с явными dogfood allowed roots;
5. пишет evidence в `.dogfood/migration/evidence/`.

## Команда для агента

В OpenCode:

```text
/dogfood-harness
```

Команда просит orchestrator выполнить маленькую проверку Harness Kit, поддерживать run artifacts, писать trace/events и останавливаться только на настоящем blocker.

## Evidence

После smoke проверь:

```text
.dogfood/migration/state/harness-run.json
.dogfood/migration/state/harness-events.jsonl
.dogfood/migration/state/harness-policy-result.md
.dogfood/migration/evidence/harness-dogfood-smoke.md
.dogfood/migration/runs/<run-id>/trace.jsonl
```

## Критерии pass

Dogfood считается успешным, когда:

- kit установлен без пропущенных Harness Kit файлов;
- `new-harness-run.ps1` создал `Prompt.md`, `Plan.md`, `Implement.md`, `Documentation.md` и `trace.jsonl`;
- `check-harness-policy.ps1` завершился с кодом `0`;
- в `harness-events.jsonl` есть хотя бы одно событие `dogfood-smoke`;
- для разрешённых действий не нужен рутинный вопрос “продолжать?”;
- ошибки пишутся как evidence, а не прячутся в чате.

## English-first

Каноническая версия этого документа — английская. Русская версия вторична. Машинные dogfood events должны использовать стабильные language-neutral коды: `dogfood-smoke-started`, `dogfood-smoke-pass`, `dogfood-smoke-fail`.
