# Playbook: runtime smoke one test

Цель: безопасно довести до runtime один тест-кандидат, выбранный `smoke-plan`.

## Шаги

1. Запусти `smoke-plan` по последнему `verify-project` output.
2. Возьми первого Level 4/5 кандидата.
3. Прочитай `runtime-checklist.md`.
4. Не запускай весь пакет.
5. Запусти один тест.
6. Классифицируй failure:
   - locator problem;
   - wait/flaky problem;
   - assertion mismatch;
   - test data/setup;
   - product behavior;
   - migration bug.
7. Если fix — config/profile mapping, внеси его и прогони safety loop.
8. Если fix требует ручного Playwright-кода/generated edit — создай manual review item.

## Запрещено

- запускать весь пакет вместо одного теста;
- фиксить runtime через sleep без причины;
- менять generated manually;
- скрывать failed assertions.
