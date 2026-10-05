# Selenium → Playwright Migrator — desktop GUI

Electron-приложение, которое убирает консоль из повседневной миграции: выбираете папку
с Selenium-тестами и выходную папку, запускаете `analyze`/`migrate`/`run` кнопкой,
смотрите покрытие, ревьюите остатки (`requires_review`/`unsupported`/`ambiguous`)
и открываете сгенерированные файлы — всё без CLI-команд.

Приложение — тонкая обёртка над существующим консольным инструментом
`selenium-pw-migrator`: само оно миграцию не делает, а запускает CLI как подпроцесс
и показывает его артефакты.

## Что умеет

- **Запуск прогонов** — режимы `analyze`, `migrate`, `run`; флаги `--input`,
  `--config`, `--out`, `--target`, `--target-test-framework`, `--generation-policy`
  и доп. аргументы. Вывод CLI стримится в окно «Лог» живьём.
- **Покрытие** — вкладка «Покрытие» показывает `coverage-report.json`
  (`migrator-coverage/v1`): сводку (files/tests/constructs/transformed/requires_review/
  unsupported/ambiguous/parse_error/excluded), состояние survey/transformation и
  пофайловую таблицу с количествами.
- **Ревью** — вкладка «Ревью» перечисляет все конструкции не в состоянии `transformed`
  с причиной и строкой источника. Каждую можно пометить «просмотрено» и оставить заметку.
  Отметки хранятся локально (см. ниже) и не трогают артефакты миграции.
- **Файлы** — список сгенерированных `*Playwright.cs`/`*Playwright.ts` с открытием
  в редакторе по умолчанию и показом в проводнике.
- **Старые CLI** — если в выходной папке нет `coverage-report.json` (CLI без учёта
  покрытия), вкладка «Покрытие» показывает сводную таблицу из `report.json`.

## Как это собирается и запускается

Требования: Node.js 18+ и установленный CLI (`npm i -g selenium-pw-migrator` или
standalone-архив; см. раздел «Где CLI»). 

```powershell
cd desktop
npm install

# dev-режим (HMR + пересборка main)
npm run dev

# production-сборка в dist/ + dist-electron/
npm run build

# запуск собранной версии
npm start

# Windows-инсталлятор (NSIS) в desktop/release/
npm run package:win
```

Готовый инсталлятор:
`desktop/release/SeleniumPlaywrightMigrator-Setup-0.1.0-x64.exe`.

## Проверка (smoke)

Автоматизированная проверка не открывает окно долго: `--smoke` прогоняет `main` без
UI-окна (обнаружение CLI + чтение coverage), `--smoke-ui` дополнительно создаёт окно
и ждёт сигнала `renderer:ready` от смонтированного React-дерева.

```powershell
npm run build

# CLI найден + coverage-report.json читается (Files=1 на сэмпле examples/simple)
npx electron . --smoke --smoke-out ../examples/simple-example-out
# => MIGRATOR-DESKTOP-SMOKE {"cli":{"ok":true,...},"coverageOk":true,"coverageFiles":1}

# то же + реальная загрузка окна и рендера
npx electron . --smoke --smoke-ui --smoke-out ../examples/simple-example-out
# => ... "ui":true
```

Сэмпл `examples/simple-example-out` генерируется CLI из `examples/simple`:

```powershell
<путь-к-свежему-CLI> --mode analyze `
  --input examples/simple/input `
  --out examples/simple-example-out `
  --config examples/simple/adapter-config.json
```

## Где CLI

Порядок поиска исполняемого файла `selenium-pw-migrator`:

1. Поле «Путь к CLI» в настройках приложения (кнопка «Авто» его сбрасывает).
2. PATH (`where selenium-pw-migrator` / `which`).
3. `~/.selenium-pw-migrator/bin/selenium-pw-migrator.exe` (стандартная папка standalone).

CLI должен быть той версии, которая пишет `coverage-report.json` (учёт покрытия),
иначе приложение покажет сводку из `report.json` и предложит обновить инструмент.

## Куда что пишется

- Настройки приложения: `%APPDATA%/selenium-pw-migrator-desktop/settings.json`
  (userData Electron).
- Отметки ревью: `%APPDATA%/selenium-pw-migrator-desktop/review-state.json`, ключ —
  sha256 абсолютного пути к выходной папке + `coverageSha256` отчёта. Артефакты
  миграции приложение не изменяет.
- Артефакты прогонов: в выбранной выходной папке (как у CLI).

## Команды CLI, которые приложение вызывает

```
selenium-pw-migrator --mode analyze  --input <dir> [--config <json>] --out <dir> --format both
selenium-pw-migrator --mode migrate  --input <dir> [--config <json>] --out <dir> --format both [...]
selenium-pw-migrator --mode run      --input <dir> [--config <json>] --out <dir> --format both [...]
```

## Структура проекта

```
desktop/
  electron/main.ts        main-процесс: окно, IPC, spawn CLI, чтение артефактов, sidecar ревью
  electron/preload.cts    contextBridge (sandbox: true)
  electron/types.ts       общий контракт IPC и типов отчётов
  src/                    React-рендерер (vite)
  electron-builder.yml    NSIS-упаковка
```

Безопасность: `contextIsolation: true`, `nodeIntegration: false`, `sandbox: true`;
IPC-обработчики проверяют отправителя (`isTrustedSender`), пути из рендерера не
используются для произвольного доступа к файлам.
