# Роли в agent-first миграции

## Пользователь / тестировщик

Отвечает за:

- выбрать пакет тестов;
- подтвердить, что найденный POM/source truth соответствует смыслу теста;
- проверить итоговые TODO/manual-review items;
- запускать runtime smoke по инструкции агента;
- принимать или отклонять project-specific mappings.

Не обязан:

- понимать renderer/core/parser;
- чинить CS0103/CS0246 на уровне мигратора;
- писать adapter-config с нуля;
- разбираться в Roslyn/AST.

## Агент

Отвечает за:

- читать `start.md`, `bootstrap.md`, `POLICIES.md`, `AGENTS.md`;
- вести `migration/agent-state.md`;
- работать через `migration/` workspace;
- читать POM/source truth;
- предлагать и добавлять config/profile mappings;
- запускать safety-команды;
- писать русские отчёты;
- эскалировать generic blockers разработчику.

Не имеет права:

- молча менять C# мигратора;
- править generated `.cs` вручную;
- менять исходный Selenium/product project;
- вставлять сырые ответы подагентов на английском;
- добавлять mappings без source truth;
- скрывать compile errors через dummy declarations.

## Разработчик мигратора

Отвечает за:

- generic renderer/core fixes;
- расширение schema adapter-config;
- новые recognizer/action types;
- packaging/release мигратора;
- reusable base profiles;
- review агентских правок.

Разработчик должен отличать:

```text
project-specific knowledge → config/profile
универсальная механика → code мигратора
объяснение/диагностика → reports/docs
```

## Критерии передачи разработчику

Передавать разработчику, если:

- проблема повторяется в нескольких проектах;
- config-only режим даёт хрупкий workaround;
- нужен новый mapping type;
- renderer выпускает source-only code active;
- target locals/known symbols/source-only policies работают неверно;
- `verify-project` требует улучшения discovery/classification.
