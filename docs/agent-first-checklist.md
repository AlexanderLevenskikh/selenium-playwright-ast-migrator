# Agent-first checklist

Используй этот чеклист перед началом и перед остановкой агента.

## Перед началом

- [ ] Прочитан `start.md`
- [ ] Прочитан `bootstrap.md`
- [ ] Прочитан `POLICIES.md`
- [ ] Прочитан `AGENTS.md`
- [ ] Прочитан `docs/agent-first-workflow.md`
- [ ] Прочитан `docs/agent-command-set.md`
- [ ] Создан/обновлён `migration/agent-state.md`
- [ ] Создан/обновлён `migration/pre-stop-checklist.md`
- [ ] Выбран input tests path
- [ ] Выбран config/profile stack
- [ ] Выбран workspace/output name

## Перед config-изменением

- [ ] Найден source truth
- [ ] Mapping high-confidence или явно помечен как draft
- [ ] Изменение делается в project profile, если оно project-specific
- [ ] Source-only identifiers не превращаются в target-known
- [ ] Локальные переменные метода не добавляются в config вручную

## После config-изменения

- [ ] Сохранён old config
- [ ] Запущен `config-validate`
- [ ] Запущен `migrate` или `verify-project`
- [ ] Запущен `guard`
- [ ] Запущен `config-diff`
- [ ] Обновлён `agent-state.md`
- [ ] Пользователю дан русский отчёт

## Перед остановкой

- [ ] Нет незадокументированных compile errors
- [ ] Нет незадокументированных guard failures
- [ ] Есть next step или escalation report
- [ ] Пользователь не оставлен с сырым английским отчётом
- [ ] В конце есть понятный вопрос: `Продолжить?` или конкретная просьба

## Doctor / preflight

Перед первой миграцией нового проекта или пакета тестов запускай preflight-проверку:

```powershell
dotnet run --project .\Migrator.Cli -- --mode doctor --input "<tests>" --config "<profile.adapter.json>" --out "doctor" --format both
```

Режим ничего не меняет: он проверяет input, config layers, ближайший `.csproj`/`.sln`, `NuGet.config`, `Verification`, POM/source-truth кандидаты и доступность `dotnet`. Артефакты: `doctor-report.md/json` и `agent-doctor-next-task.md`. Подробности: `docs/doctor-mode.md`.

