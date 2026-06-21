# OpenCode Agent Team Template

Готовая структура для схемы:

- `orchestrator` — главный агент/тимлид, сам не редактирует файлы.
- `executor` — исполнитель, делает маленькие scoped-правки.
- `watchdog` — контролёр правил/политик/дисциплины, read-only.
- `reviewer` — ревьюер качества текущего diff, read-only.
- `/supervised-task` — команда для задачи через orchestrator + watchdog + reviewer.
- `/checkpoint` — ручная команда для проверки текущего состояния watchdog'ом.
- `AGENTS.md` — проектные правила, которые кладутся в корень репозитория.

## Куда копировать

### Глобально

Скопируй содержимое:

```text
global/.config/opencode/
```

в:

```text
~/.config/opencode/
```

На Windows обычно:

```text
%USERPROFILE%\.config\opencode\
```

### В проект

Скопируй:

```text
project-template/AGENTS.md
```

в корень нужного репозитория.

## Быстрая установка

### Windows PowerShell

Из корня распакованного архива:

```powershell
.\scripts\install-windows.ps1
```

### macOS/Linux/Git Bash

```bash
bash ./scripts/install-unix.sh
```

## Как пользоваться

В opencode:

```text
/supervised-task исправить UnsupportedAction для page.Pagination.Forward, без широкого рефакторинга
```

Ручной контроль:

```text
/checkpoint
```

Ручной вызов агента:

```text
@watchdog проверь текущую работу на соответствие AGENTS.md и задаче пользователя
```

```text
@reviewer проверь текущий git diff концептуально и найди блокеры
```

```text
@executor реализуй только минимальный фикс, не трогай соседние категории
```

## Идея workflow

1. Orchestrator понимает задачу и составляет план.
2. Watchdog проверяет план на соответствие правилам.
3. Executor делает минимальный patch.
4. Watchdog проверяет, не уехал ли исполнитель.
5. Reviewer проверяет diff.
6. Executor исправляет только блокеры.
7. Orchestrator выдаёт честный финальный отчёт.

## Важное

Это не “агент по таймеру”. Watchdog вызывается:
- автоматически orchestrator'ом по чекпоинтам;
- вручную через `@watchdog`;
- вручную через `/checkpoint`.

Для жёсткой защиты опасные действия лучше запрещать permissions, а не просто просить агента быть хорошим.
