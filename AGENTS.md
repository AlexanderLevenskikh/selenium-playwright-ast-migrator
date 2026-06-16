# Migrator — AGENTS.md

## Обзор

Migrator — .NET 8 CLI-инструмент, преобразующий Selenium C# автотесты (NUnit) в Playwright .NET.
Pipeline: **parse → recognize → IR → adapt → render → report**.

## Архитектура (кратко)

| Проект | Ответственность |
|---|---|
| `Migrator.Core` | Модели IR, интерфейсы, пайплайн, отчёт. **Ничего** от Roslyn/Selenium/Playwright. |
| `Migrator.Roslyn` | Парсер (Roslyn AST → IR), recognizer'ы |
| `Migrator.SeleniumCSharp` | Адаптер (JSON-конфиг → маппинг таргетов) |
| `Migrator.PlaywrightDotNet` | Рендерер (IR → C# Playwright .NET) |
| `Migrator.Cli` | Точка входа, командная строка |
| `Migrator.Tests` | Тесты (xUnit), compile-smoke |

Core — только модели и контракты. Не класть в Core: зависимости от фреймворков, логику распознавания, конкретные реализации.

## Ключевые файлы

- `Migrator.Roslyn/RoslynTestFileParser.cs` — парсер, регистрация recognizer'ов (`CreateDefaultRecognizers`)
- `Migrator.Roslyn/Recognizers/` — recognizer'ы (Click, SendKeys, Wait, Assert, и др.)
- `Migrator.PlaywrightDotNet/PlaywrightDotNetRenderer.cs` — генерация C#
- `Migrator.SeleniumCSharp/DefaultProjectAdapter.cs` — маппинг `TargetKind` из JSON
- `Migrator.Core/Models/` — `TestAction`, `TargetExpression`, `TargetKind`, `RecognitionConfidence`
- `Migrator.Tests/SnapshotTests.cs` — snapshot + compile-smoke тесты

## Развертывание

Migrator — консольное .NET-приложение. Развёртывается как опубликованный exe:

```bash
dotnet publish Migrator.Cli -c Release -o ./publish
```

Не требует серверной части, базы данных, внешних сервисов.
Работает локально или в CI.

## Доступ к окружению

Локальная разработка. Локальный профиль. Внешний доступ к API не требуется.

## Ограничения агента

- **Никогда** не деплоить и не менять инфраструктуру — приложения нет.
- **Никогда** не запрашивать подтверждение у пользователя перед изменениями в коде.
- Если задача требует доступа к внешним ресурсам (API, базы, окружения) — сообщить пользователю и остановиться.

## Adapter-config/profile policy для агентов

- Project-specific знания держи в `adapter-config.json`, profile или scope, не в renderer.
- Для target-side enum/static helpers используй `TargetKnownTypes` / `TargetKnownIdentifiers`.
- Для Selenium-only roots используй `SourceOnlyIdentifiers`.
- Не добавляй dummy declarations в renderer/generated output.
- Active target declarations из `TargetStatements` регистрируются renderer’ом как method-scoped target locals; не веди глобальный список локальных переменных в config.
- Перед изменениями migration profile прочитай `docs/agent-config-guidelines.md`.
