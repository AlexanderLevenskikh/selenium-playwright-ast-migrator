# Prompt: resume stopped agent

```text
Продолжай предыдущую миграцию по agent-first workflow.

Сначала прочитай:
- migration/agent-state.md
- migration/pre-stop-checklist.md
- последний migration/*/agent-next-task.md, если есть
- последний migration/*/explain-todo.md, если есть
- последний migration/*/smoke-plan.md, если есть
- docs/agent-playbooks/source-only-pattern-backlog.md

Проверь, что работаешь с последним output, а не со старым отчётом.

Ограничения:
- Не меняй C# код мигратора.
- Не меняй source project.
- Не правь generated .cs вручную.
- Меняй только adapter-config/profile.
- Все отчёты и output держи внутри migration/.
- Пиши пользователю только на русском.

Сделай следующий безопасный шаг из agent-next-task.md.
Если TODO доминируют `SOURCE_ONLY_IDENTIFIER(page/pagef)`, не эскалируй root `page`: сначала построи source-pattern backlog по `docs/agent-playbooks/source-only-pattern-backlog.md`.
Если нужен C# fix или source truth не найден — создай migration/escalation-report.md и остановись.
```
