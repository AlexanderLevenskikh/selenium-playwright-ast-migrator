Начинай работу с `migration/BOOTSTRAP.md`.

Порядок обязателен:

1. Прочитай `migration/BOOTSTRAP.md`.
2. Прочитай `migration/POLICIES.md`.
3. Создай или обнови:

    * `migration/agent-state.md`
    * `migration/pre-stop-checklist.md`
4. Перед любым запуском/остановкой сверяйся с `agent-state.md` и `pre-stop-checklist.md`.
5. Не останавливайся и не спрашивай “как продолжить?”, пока `pre-stop-checklist.md` не показывает `Stop allowed: yes`.
6. Если `Verify failed` или есть compile errors — не пиши `final-report.md`. Вместо этого создай/обнови `migration/blocked-report.md`, сгруппируй root causes и оформи `migration/migrator-tickets.md`.
7. Если `Verify passed`, но остались TODO — переходи в TODO-only phase: создай `migration/todo-audit.md`, классифицируй TODO, оформи `manual-review-items.md`, `deferred-items.md`, `migrator-tickets.md`.
8. Не меняй код мигратора, production code и generated files вручную. Работай только через config/profile и отчёты.
9. После каждого изменения config запускай fresh `orchestrate`, сравнивай метрики и откатывай изменение, если Verify/Syntax/Compile ухудшились.
10. Пиши пользователю только на русском.

Начинай автономно. Не проси подтверждения на безопасные действия. Остановись только при критическом блокере или когда `pre-stop-checklist.md` разрешает остановку.
