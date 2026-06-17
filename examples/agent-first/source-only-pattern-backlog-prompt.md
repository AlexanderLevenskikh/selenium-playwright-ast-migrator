# Prompt: SOURCE_ONLY_IDENTIFIER pattern backlog

Use this prompt when an agent claims that `page/pagef` TODO cannot be fixed through config.

```text
Ты migration agent для Selenium C# → Playwright AST Migrator.

Текущая проблема: TODO доминируют `SOURCE_ONLY_IDENTIFIER(page/pagef)`.

Не делай root-level вывод “page невозможно исправить”. Это симптом, а не финальный root cause.

Прочитай:
- AGENTS.md
- docs/agent-playbooks/safety-rules.md
- docs/agent-playbooks/source-only-pattern-backlog.md
- docs/explain-todo.md
- последний migration output
- report.json
- explain-todo.json / explain-todo.md
- unmapped-targets.json
- unsupported-actions.json
- adapter-config.json / profiles

Ограничения:
- Не правь generated files вручную.
- Не меняй C# мигратора.
- Не меняй source Selenium project.
- Не удаляй `page`, `pagef`, `lightbox`, `modal`, `WebDriver` из `SourceOnlyIdentifiers`.
- Не добавляй Selenium/POM roots в `TargetKnownIdentifiers`, если они реально не существуют в target Playwright code.
- Не создавай fake mappings ради уменьшения TODO.

Задача:
1. Извлеки все TODO с `[MIGRATOR:*]`.
2. Для `SOURCE_ONLY_IDENTIFIER` извлеки полный `Source:` expression, а не только root.
3. Сгруппируй top-50 по normalized pattern:
   - loader/wait
   - click/open/modal
   - input/fill
   - text/value assertion
   - visibility assertion
   - table/list/ElementAt
   - navigation/url/WebDriver
   - modal/lightbox scope
4. Для каждого pattern укажи:
   - count
   - example source line
   - можно ли закрыть config-only
   - recommended config section
   - риск
   - нужен ли migrator recognizer
5. Предложи первые 20 safe config/profile changes с максимальным эффектом.
6. Выполни только low-risk config changes с найденным source truth.
7. Запусти config-validate, migrate/verify-project, explain-todo, migration-board, guard/config-diff.
8. Дай отчёт на русском с before/after metrics.

Эскалируй только конкретные generic patterns, например:
- `ClickAndOpen<T>()`
- `Table.Items.ElementAt(...)`
- modal/lightbox scope propagation

Не эскалируй весь root `page`.
```
