# OpenCode Desktop: обычная миграция

## 1. Bootstrap

Из корня продуктового репозитория:

```powershell
selenium-pw-migrator kit bootstrap-opencode `
  --workspace migration `
  --source .\LegacyTests `
  --opencode-install project-desktop
```

Проверь:

```powershell
selenium-pw-migrator kit doctor --workspace migration
```

## 2. Запуск из OpenCode

В диалоге выполни:

```text
/supervised-task
```

Команда должна определить настройки, при необходимости сделать небольшой representative pilot, затем выполнить один полный `selenium-pw-migrator run`, настоящий `verify-project` и final gate. Запуск использует один линейный стандартный flow без отдельной машины состояний.

## 3. Ручной эквивалент

```powershell
selenium-pw-migrator run `
  --input .\LegacyTests `
  --config migration\profiles\adapter-config.json `
  --out migration\runs\run-001 `
  --format both

selenium-pw-migrator verify-project `
  --input .\LegacyTests `
  --config migration\profiles\adapter-config.json `
  --out migration\runs\run-001\verify-project `
  --format both

.\migration\scripts\check-final-gate.ps1 `
  -Workspace migration `
  -Run migration\runs\run-001 `
  -RepoRoot .
```

## 4. Если что-то сломалось

Не создавай недостающие JSON-отчёты вручную. Прочитай текущие reports/logs, исправь одну первопричину и повтори полный запуск. Если проект реально нельзя собрать или запустить, зафиксируй честный blocker/`NOT RUNNABLE` с командой и stderr.

## 5. Граница безопасности

Source Selenium и продуктовый проект по умолчанию read-only. Агент пишет только в migration workspace и в явно разрешённые файлы. Перед handoff reviewer проверяет diff, generated test bodies, TODO-категории и свежесть `verify-project`.
