# Playbook: run agent migration iteration

Цель: провести одну безопасную итерацию миграции через config/profile без изменения C# кода мигратора.

## Входные данные

- путь к source Selenium tests;
- один или несколько `--config` слоёв;
- предыдущий output folder, если есть;
- предыдущая версия config/profile для diff.

## Шаги

1. Прочитай `agent-next-task.md` из последнего output, если он есть.
2. Найди top issue с максимальным impact.
   If TODO are dominated by `SOURCE_ONLY_IDENTIFIER`, do **not** group by `page/pagef`; first run the pattern-backlog analysis from `docs/agent-playbooks/source-only-pattern-backlog.md`.
3. Найди source truth в POM/helper/base class.
4. Внеси минимальное изменение в project profile/config.
5. Запусти `config-validate`.
6. Запусти `migrate` или `verify-project`.
7. Запусти `explain-todo`.
8. Запусти `smoke-plan`, если `verify-project` прошёл или почти прошёл.
9. Запусти `guard` против предыдущего output.
10. Запусти `config-diff`.
11. Обнови `migration/agent-state.md`.
12. Дай отчёт пользователю на русском.

## Acceptance criteria

- C# код не изменён.
- generated `.cs` не изменены вручную.
- source project не изменён.
- Все output находятся внутри `migration/`.
- Есть before/after metrics.
- Есть next step.
- Если основная проблема `SOURCE_ONLY_IDENTIFIER`, есть top source-pattern backlog, а не только root-level статистика `page/pagef`.
