# Prompt: ask developer for escalation

```text
Сформируй escalation report для разработчика мигратора.

Не продолжай config-only итерации.
Не меняй C#.
Не правь generated .cs.

Используй шаблон из docs/escalation-reports.md.
Нужно включить:
- input path;
- config/profile stack;
- последнюю команду;
- symptom из report/verify-project/generated;
- root cause hypothesis;
- почему config-only не помогает;
- минимальный source fragment;
- generated fragment;
- ожидаемое поведение;
- риск;
- рекомендацию.

Отчёт сохрани в migration/escalation-report.md и кратко перескажи пользователю на русском.
```
