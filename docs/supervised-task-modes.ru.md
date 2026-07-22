# `/supervised-task`

| Команда | Поведение |
|---|---|
| `/supervised-task` | Начать или возобновить стандартный полный запуск. |
| `/supervised-task continue` | Выполнить одну выгодную ограниченную доработку и повторить весь pipeline. |
| `/supervised-task <bounded запрос>` | Выполнить запрос без расширения source scope и прогнать обязательные проверки. |

Команда не поддерживает partition planning и acceptance state. Она останавливается на конкретном блокере, scope violation, решении человека, validation failure или повторном no-progress.
