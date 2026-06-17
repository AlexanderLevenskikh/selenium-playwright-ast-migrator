# Playbook: escalate to developer

Цель: остановить config-only итерации и передать разработчику понятный пакет проблемы.

## Когда использовать

- Нужна правка renderer/core/parser.
- Не хватает нового mapping/action type.
- verify-project показывает ошибку, которую нельзя решить references/config.
- Agent guard показывает регрессию, но причина не в последнем mapping.
- POM/source truth отсутствует или противоречив.

## Шаги

1. Останови config churn: не добавляй новые mappings.
2. Найди минимальный source fragment.
3. Найди generated fragment.
4. Найди строки в отчётах (`report.json`, `explain-todo.json`, `project-verify-report.json`).
5. Создай `migration/escalation-report.md` по шаблону из `docs/escalation-reports.md`.
6. Обнови `migration/agent-state.md`: phase = blocked/escalation.
7. Сообщи пользователю на русском, что именно нужно от разработчика.

## Запрещено

- маскировать проблему `TargetKnownIdentifiers`;
- добавлять dummy declarations;
- переносить source-only symbols в target-known;
- править generated manually.
