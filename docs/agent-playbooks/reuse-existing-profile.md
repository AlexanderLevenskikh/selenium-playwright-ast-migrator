# Playbook: reuse existing migration profile

Цель: применить готовый base profile к похожему проекту и добавить только project-specific overrides.

## Входные данные

- source Selenium tests нового проекта;
- base profile, например `profiles/infrastructure-base.adapter.json`;
- project profile, например `profiles/projects/new-project.adapter.json`.

## Шаги

1. Запусти `bootstrap-project` для нового проекта.
2. Запусти `index-pom` и сравни POM patterns с базовым профилем.
3. Запусти baseline migrate с base profile + empty project profile.
4. Запусти `explain-todo`.
5. Добавляй только project-specific overrides.
6. Если base mapping не подходит, сначала докажи это source truth.
7. Не копируй common mappings в project profile без необходимости.
8. После каждой итерации запускай safety loop.

## Хороший признак

- Большая часть mappings применяется из base profile.
- Project profile содержит только локальные селекторы/исключения.
- `config-diff` показывает небольшие и понятные изменения.

## Плохой признак

- Агент переписывает base mappings в project profile без причины.
- Project profile превращается в копию base profile.
- TODO уменьшается за счёт low-confidence guesses.
