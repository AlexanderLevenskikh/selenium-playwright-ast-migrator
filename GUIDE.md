# Руководство: Autopilot Loop

Эта версия архива очищена для тестирования нового режима Autopilot Loop.

Старые human-checkpoint инструкции удалены, чтобы агент не получал конфликтующие правила.

## Как начать

1. Открой репозиторий в агенте.
2. Дай агенту prompt из `.agent-loops/kickoff-prompt.txt`.
3. Вставь конкретный текущий блок: ошибку теста, TODO-категорию, verify-report или migration gap.
4. Агент должен сам выбрать безопасную реализацию, прогнать проверки и продолжать до verified/blocker.

## Главные файлы

- `.agent-loops/README.md`
- `.agent-loops/01-autopilot-loop.md`
- `.agent-loops/03-stop-policy.md`
- `AGENTS.md`
- `docs/autopilot-loop.md`

## Обязательное поведение агента

- Не спрашивать, какой технический вариант выбрать.
- Не спрашивать, продолжать ли.
- Если статус `CONTINUE_AUTONOMOUSLY`, продолжать.
- Останавливаться только по stop-policy.
- Подтверждать результат build/test/verify, а не словами.
