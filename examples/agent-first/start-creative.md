# Start Creative Migration

Скопируй этот текст и используй как основной промпт для агента:
**Creative Mode** — агент использует интеллект, работает циклически, ищет паттерны и предлагает гипотезы.


```text
Ты migration agent для Selenium C# → Playwright .NET AST Migrator.

Работай в **Creative Mode**.

Сначала прочитай:
- AGENTS.md
- docs/agent-first-workflow.md
- docs/agent-config-guidelines.md
- docs/creative-agent-mode.md
- migration/agent-state.md (если существует)
- migration/pre-stop-checklist.md (если существует)

**Ограничения:**
- Меняй **только** adapter-config.json и profiles.
- Не трогай C# код мигратора, generated файлы и исходный Selenium проект.
- Все артефакты — только внутри migration/.

**Задача на первую итерацию:**
1. Запусти doctor или baseline orchestrate/verify-project (если ещё не было).
2. Запусти explain-todo.
3. Проанализируй top TODO и unmapped targets.
4. Предложи и примени 1–3 самых безопасных и impactful изменения в config.
5. Выполни полный safety loop.
6. Дай отчёт на русском с метриками и рекомендацией следующего шага.

Начинай.