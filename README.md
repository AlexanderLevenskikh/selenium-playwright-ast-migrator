# Migrator — Selenium C# → Playwright .NET

Инструмент миграции UI-автотестов с Selenium WebDriver (C#/NUnit) на Playwright .NET.

## Проблема

Ручной перевод автотестов с Selenium на Playwright занимает много времени: нужно
перевести логику кликов, ввода, навигации, а также маппить элементы со старых
локаторов на `data-testid`. Migrator автоматизирует эту рутину, превращая исходный
Selenium-код в рабочий Playwright-скелет с TODO-комментариями для мест, требующих
ручной проверки.

## Pipeline

```
исходный файл (.cs)
    │
    ▼  [parse]
    │  Roslyn-парсер: AST → промежуточное представление (IR)
    ▼
    │  [recognize]
    │  Recognizer'ы: клик, ввод, assertion, wait, unsupported
    ▼
    │  [adapt]
    │  Adapter: маппинг source-выражений → Playwright-локаторы (через JSON-конфиг)
    ▼
    │  [render]
    │  Renderer: IR → сгенерированный C# код (Playwright .NET)
    ▼
    │  [report]
    │  ReportBuilder: статистика по конвертации
    ▼
сгенерированный файл (.generated.cs) + отчёт
```

## Установка

```bash
git clone <repo-url>
cd Migrator
dotnet restore
```

## Тесты

```bash
dotnet test
```

Запускает 33 теста: snapshot-проверки, unit-тесты парсера, compile-smoke-проверки.

## CLI-запуск

```bash
# Analyze — отчёт без генерации файлов
dotnet run --project Migrator.Cli -- --mode analyze --input ./OldTests --out ./analysis --format both

# Migrate — генерация Playwright C#
dotnet run --project Migrator.Cli -- --mode migrate --input ./OldTests --out ./Generated --config ./adapter-config.json

# Verify (экспериментальный) — проверка структуры сгенерированных файлов
dotnet run --project Migrator.Cli -- --mode verify --input ./Generated --out ./verify-report

# Quality gate
dotnet run --project Migrator.Cli -- --mode migrate --input ./OldTests --fail-on-unsupported --fail-on-todo
```

Публикация самодостаточного исполняемого файла:

```bash
dotnet publish Migrator.Cli -c Release -o ./publish
./publish/Migrator.Cli --mode migrate --input ./OldTests --config ./adapter-config.json
```

## Аргументы CLI

| Аргумент              | Описание                                                            |
|-----------------------|---------------------------------------------------------------------|
| `--mode`              | `analyze`, `migrate`, `verify` (по умолчанию `migrate`)             |
| `--input`             | Исходный `.cs`-файл или директория с тестами (обязательно)          |
| `--out`               | Выходная директория (опционально, авто-дефолт по режиму)            |
| `--config`            | Путь к JSON-конфигу адаптера (опционально)                          |
| `--format`            | Формат отчёта: `text`, `json`, `both` (по умолчанию `both`)         |
| `--fail-on-unsupported` | Exit code 2 если есть unsupported actions                   |
| `--fail-on-todo`      | Exit code 3 если есть TODO комментарии                              |

Коды выхода: `0` — успех; `1` — ошибка CLI; `2` — `--fail-on-unsupported`; `3` — `--fail-on-todo`.

## Как читать отчёт

После обработки каждого файла CLI выводит статистику:

```
Processed: path/to/Widget.cs
  Tests: 3
  Unsupported: 2
  Semantic: 8, SyntaxFallback: 4
  Mapped: 5, Unmapped: 1
  TODO comments: 6
  Output: path/to/WidgetPlaywright.cs
```

| Поле                  | Значение                                                                 |
|-----------------------|--------------------------------------------------------------------------|
| **Tests**             | Количество `[Test]` методов в файле                                     |
| **Unsupported**       | Действия, которые мигратор не умеет конвертировать (оставлены как TODO) |
| **Semantic**          | Действия, распознанные семантически (уверенный парсер)                   |
| **SyntaxFallback**    | Действия, распознанные по Roslyn AST без SemanticModel                  |
| **Mapped**            | Таргетов (элементов), успешно маппнутых через adapter-config             |
| **Unmapped**          | Таргетов, оставшихся неразрешёнными (TODO в сгенерированном коде)       |
| **TODO comments**     | Количество `// TODO:` комментариев в сгенерированном файле               |

Если в файле есть unsupported-действия, в начале сгенерированного файла появится
`// WARNING` с подсказкой проверить TODO-комментарии.
