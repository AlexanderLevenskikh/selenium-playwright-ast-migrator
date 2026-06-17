# Escalation reports

Escalation report нужен, когда агент не должен продолжать config-only итерации и требуется разработчик, владелец тестов или продуктовый контекст.

## Когда создавать

Создавай `migration/escalation-report.md`, если:

- нужен generic C# fix мигратора;
- source truth не найден;
- mapping будет догадкой;
- `verify-project` падает из-за missing references, которые агент не может уверенно добавить;
- runtime failure требует знания продукта;
- `guard` показывает регрессию;
- агенту нужно разрешение на изменение C#.

## Шаблон

```markdown
# Escalation report

## Кому

- Разработчик мигратора / владелец тестов / продуктовый разработчик

## Кратко

Одно предложение: что блокирует миграцию.

## Контекст

- Input:
- Config/profile:
- Последний output:
- Команда запуска:

## Симптом

Что видим в отчётах/generated/verify-project/runtime.

## Root cause hypothesis

Почему это происходит.

## Почему не решается config-only

Что агент уже проверил и почему mapping/profile не помогает.

## Минимальный пример source

```csharp
// source Selenium fragment
```

## Что генерируется сейчас

```csharp
// generated Playwright fragment
```

## Что должно быть

```csharp
// expected target behavior or TODO policy
```

## Затронутые файлы/механизмы

- adapter-config / renderer / parser / verify-project / POM / product project

## Риск

Что будет, если продолжить без исправления.

## Рекомендация

Самый маленький безопасный следующий шаг.
```

## Правило

Escalation report должен быть понятен человеку, который не читал весь чат агента.
Не используй формулировки вроде “оно не работает”; показывай конкретный source/generated/report пример.
