# Инкрементальный конвейер миграции

Инкрементальный конвейер работает поверх неизменяемого workspace волны из migration fast path. Он не позволяет повторно исследовать, генерировать и валидировать неизменившуюся работу, сохраняя существующие границы безопасности: scope guard, reviewer, sentinel и final gate.

## Артефакты

| Артефакт | Назначение |
|---|---|
| `run-context.json` | Неизменяемая привязка manifest, execution policy, selected tests, source scope, config, исходного состояния `generated/`, cache root и версии контракта инструмента. |
| `change-set.json` | Детерминированная дельта `generated/` после последнего checkpoint. |
| `validation-plan.json` | Требуемый scope проверки, рекомендуемые команды, точный fingerprint входов и решение по кэшу. |
| `validation-result.json` | Фактически выполненная проверка. Переиспользуемый PASS требует явной команды и точного fingerprint входов. |
| `checkpoints/<id>/checkpoint.json` | Восстанавливаемый снимок output и наблюдаемого состояния validation/review. Checkpoint не означает завершение проекта. |
| `latest-checkpoint.json` | Ссылка на последний checkpoint. |
| `resume-decision.json` | Детерминированное следующее действие без повторной материализации source scope. |
| `review/review-bundle.json` | Полная и инкрементальная дельты, свежесть validation, TODO/unmapped, risk flags и ссылки на evidence. |

`validation-host-result.json` фиксирует решение host, детерминированные проверки, запуски процессов, длительности и safety-инварианты. stdout/stderr процессов сохраняются в `validation/processes/<invocation-id>/`, а неизменяемые summaries host — в `validation/host-runs/`, поэтому агенту не нужно пересказывать или повторно интерпретировать их.

Общий кэш validation хранится в `<workspace>/.cache/validation/<input-fingerprint>.json`. В него попадает только успешная реально выполненная проверка. Выполненный validation scope обязан покрывать рассчитанное влияние (`changed-files`, `project`, `full` или `artifacts`); недовалидированный PASS отклоняется. FAIL и устаревшие результаты не переиспользуются.

## Типовой поток

```powershell
selenium-pw-migrator migration run-wave `
  --plan migration/plan `
  --wave wave-001 `
  --workspace migration `
  --out migration/runs/wave-001 `
  --execution-profile fast

./migration/runs/wave-001/run-migrate.ps1

# Основной путь: один host планирует, исполняет, пишет evidence и создаёт checkpoint для реального PASS.
selenium-pw-migrator migration validate `
  --out migration/runs/wave-001 `
  --validation-project ./Target.Tests/Target.Tests.csproj

selenium-pw-migrator migration build-review-bundle `
  --out migration/runs/wave-001

selenium-pw-migrator migration resume-wave `
  --out migration/runs/wave-001
```

Низкоуровневые `validation-plan` и `record-validation` остаются для восстановления или импорта внешнего evidence. Это не обычный путь агента: раздельные planning, execution и recording позволяют случайно записать недостаточный или неверно описанный результат.

`resume-wave` возвращает одно ограниченное действие:

- `execute-migration`, если output пуст;
- `review-uncheckpointed-changes`, если после checkpoint появились изменения;
- `plan-validation`, если проверка отсутствует, завершилась FAIL или устарела;
- `build-review-bundle`, если validation свежая, а пакет reviewer устарел;
- `final-review-and-gate`, когда инкрементальные предварительные условия актуальны.

## Корректность кэша

Ключ кэша включает:

- fingerprint неизменяемого `run-context.json`;
- fingerprint manifest и execution policy;
- hash selected tests;
- текущий hash config;
- текущий hash дерева `generated/`;
- версию контракта инструмента.

Изменение любого входа делает прежний результат непригодным. Кэшированный PASS принимается только при точном совпадении `inputFingerprint`.

## Анализ влияния

Планировщик классифицирует изменения как:

- `none`;
- `full-project` для solution/project/build-файлов;
- `changed-dotnet-files`;
- `changed-typescript-files`;
- `artifacts-only`.

Это рекомендация по исполнению, а не разрешение ослаблять проверки проекта. Project gates, scope checks, reviewer, sentinel и final gate остаются обязательным источником истины.

## Возобновление и review

Checkpoint хранит hashes и список сгенерированных файлов, но не переводит wave или task в `DONE`. `resume-wave` не копирует исходники повторно и не изменяет manifest.

Review bundle содержит одновременно:

- совокупные изменения с начала wave;
- изменения после последнего checkpoint.

Он лишь подготавливает вход для reviewer и не заменяет final review, sentinel inspection или final gate.
