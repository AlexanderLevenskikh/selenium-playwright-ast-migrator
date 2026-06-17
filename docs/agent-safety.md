# Agent safety: проверка правок агента

Milestone 2 добавляет страховочную сетку для работы агента с `adapter-config.json` и результатами миграции.

Главная идея: агент может активно наполнять конфиг, но его изменения должны проходить автоматическую проверку перед тем, как продолжать миграцию или отдавать результат человеку.

## Что появилось

### `config-validate`

Проверяет `adapter-config.json` не только на структурные ошибки, но и на опасные агентские правки.

```powershell
dotnet run --project .\Migrator.Cli -- --mode config-validate --config adapter-config.json --out config-validate
```

Пишет:

```text
migration/config-validate/config-validate-report.md
migration/config-validate/config-validate-report.json
```

Ловит, например:

- `page`, `pagef`, `Driver`, `WebDriver` случайно добавлены в `TargetKnownTypes` / `TargetKnownIdentifiers`;
- один и тот же symbol одновременно `SourceOnlyIdentifiers` и target-known;
- дубли mappings;
- `RawExpression` mappings, которые требуют ручного review;
- подозрительные `TargetStatements` с `dynamic`, `object`, `TODO`, `null!`;
- отсутствие `Verification` section для `verify-project`.

### `config-diff`

Сравнивает два конфига и показывает, что именно изменил агент.

```powershell
dotnet run --project .\Migrator.Cli -- --mode config-diff --before adapter-config.before.json --after adapter-config.json --out config-diff
```

Пишет:

```text
migration/config-diff/config-diff-report.md
migration/config-diff/config-diff-report.json
```

Полезно перед тем, как принимать агентские изменения. Отдельно подсвечивает риски: удаление `SourceOnlyIdentifiers`, добавление опасных target-known symbols, ослабление quality gates.

### `guard`

Сравнивает два прогона миграции и падает, если агент сделал хуже.

```powershell
dotnet run --project .\Migrator.Cli -- --mode guard --before migration/baseline --after migration/current --out guard
```

Пишет:

```text
migration/guard/guard-report.md
migration/guard/guard-report.json
```

Проверяет:

- TODO не выросли;
- unmapped targets не выросли;
- unsupported actions не выросли;
- syntax errors не выросли;
- `verify-project` не регресснул из `passed` в `failed`.

## Рекомендуемый цикл агента

После каждой итерации агент должен делать так:

```text
1. Сохранить копию adapter-config.json перед изменениями.
2. Изменить только adapter-config.json.
3. Запустить config-validate.
4. Запустить migrate / verify-project.
5. Запустить guard между baseline/previous и новым прогоном.
6. Запустить config-diff между старым и новым config.
7. Написать русский отчёт и спросить “Продолжить?”.
```

## Что делать, если guard упал

Не продолжать миграцию вслепую. Нужно:

1. посмотреть `guard-report.md`;
2. понять, какая метрика ухудшилась;
3. откатить или исправить последние изменения `adapter-config.json`;
4. повторить `config-validate` и `guard`.

## Важное ограничение

Эти режимы не доказывают, что тесты полностью корректны по смыслу. Они защищают от типовых регрессий агентского workflow: “TODO стало больше”, “source-only symbol стал active”, “quality gate ослабили”, “конфиг раздулся дублями”.
