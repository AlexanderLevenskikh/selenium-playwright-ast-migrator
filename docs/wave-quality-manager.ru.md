# Контроллер качества волн и менеджер миграции

Wavefront-конвейер теперь разделяет **измерение**, **управленческое решение** и **разрешение масштабироваться**.

Сгенерированная волна считается черновиком. Она не становится принятой только потому, что файлы существуют, syntax verify прошёл, TODO меньше фиксированного лимита или показатели semantic/fallback выглядят приемлемо.

## Граничный цикл

```text
wave-local миграция
→ проверка ограниченной волны
→ измерение generated-результата
→ решение migration-wave-manager
→ исправление той же волны / разделение / честная остановка
  ИЛИ менеджер предлагает принять волну
→ привязанные к метрикам final reviewer + sentinel + scope audit
→ неизменяемая квитанция принятия
→ следующая волна
```

Команды:

```powershell
selenium-pw-migrator migration measure-wave --out migration/runs/wave-001
selenium-pw-migrator migration record-wave-decision `
  --out migration/runs/wave-001 `
  --decision REMEDIATE_CURRENT_WAVE `
  --pattern "HELPER_METHOD_REQUIRES_MAPPING: shared login helper" `
  --reason "Переиспользуемый блокер затрагивает 18 выбранных тестов"
# После исправления, повторной генерации и выполненной validation:
selenium-pw-migrator migration record-wave-remediation `
  --out migration/runs/wave-001 `
  --pattern "HELPER_METHOD_REQUIRES_MAPPING: shared login helper" `
  --result COMPLETED
selenium-pw-migrator migration accept-wave --out migration/runs/wave-001
selenium-pw-migrator migration check-wave-acceptance --out migration/runs/wave-001
```

`migration run-wave` не материализует следующую волну, пока у каждой предыдущей волны нет валидного `wave-acceptance.json`, а fingerprint метрик и hash generated-дерева не совпадают с текущими артефактами. Финальная проверка повторно валидирует всю цепочку материализованных волн, поэтому drift ранней принятой волны отменяет итоговый success.

## Сохранённые метрики

Контроллер не удаляет прежние показатели: semantic actions, syntax fallback, общее число действий, TODO, unmapped targets и размер волны. Они остаются полезной диагностикой и отображаются в dashboard.

Для принятия используются outcome-метрики, пересчитанные из generated-кода:

- выбранные, сгенерированные, готовые, черновые и пустые тесты, включая отсутствующие selected tests и неожиданные out-of-wave tests;
- число блокирующих TODO и активных placeholder/suppression statements;
- уникальные корневые блокирующие паттерны и оценка каскадов;
- активные assertions в generated-коде относительно source;
- наличие активного поведения в каждом тесте, включая выявление assertion-only/comment-only заглушек;
- статус детерминированной проверки волны;
- hashes source-scope tree, generated tree, selected tests, config, metrics и manager decision;
- использованные remediation-циклы, no-progress streak и остаток бюджета профиля.

Редактируемые migration reports остаются наблюдаемыми входами, но не дают разрешение принять волну. Validation receipt должен совпадать с текущим input fingerprint generated-кода, config, selected tests, policy и версии инструмента. Результат remediation также вычисляется CLI по before/after метрикам: вручную объявить `COMPLETED` без улучшения нельзя.

## Полномочия менеджера

`migration-wave-manager` может выбрать:

- `ACCEPT_WAVE`;
- `ACCEPT_WITH_SCAFFOLDING`;
- `REMEDIATE_CURRENT_WAVE`;
- `SCAFFOLD_CURRENT_ROOT`;
- `SPLIT_WAVE`;
- `DEFER_SOFT_DEBT`;
- `STOP_BUDGET_EXHAUSTED`;
- `REQUEST_HUMAN_DECISION`.

Он не может отменять hard invariants: пустые тесты, blocking root TODO, потерянные assertions, отсутствие активного мигрированного поведения, провал validation, выход за scope, stale evidence или несовпадение fingerprints.

Решение менеджера само по себе ещё не является принятием. `accept-wave` требует hash-chained `COMPLETED` receipt, подтверждающий, что `migration-wave-manager` действительно завершил работу для текущего metrics fingerprint, затем актуальные receipts от final reviewer и sentinel, привязанные к metrics/decision fingerprint, после чего заново вычисляет role scope audit. Remediation ledger также связан sequence/hash-цепочкой и fail-closed при редактировании, перестановке, обрезании или повреждении. Поэтому прямой запуск CLI или манипуляция историей не позволяют обойти границу.

Корневые паттерны ранжируются по переиспользуемому профиту:

```text
expected payoff = occurrences × affected tests × severity × confidence / estimated cost
```

Поэтому shared setup/helper/POM/wait/assertion/recognizer исправления выигрывают у удаления отдельных TODO-комментариев.

## Калибровка перед масштабированием

Планировщик сохраняет однотестовый lifecycle smoke, после чего создаёт одну ограниченную representative calibration-wave. `--representatives-per-cluster` теперь реально выбирает представителей распространённых кластеров в рамках hard wave budget. Пока calibration-wave не принята, affinity-packed scale waves не запускаются.

## Execution profiles

Профили меняют церемонию и ограниченный remediation-бюджет, но не требования к качеству:

- `fast`: до 2 manager-guided remediation-циклов;
- `standard`: до 4;
- `audit`: до 6.

При исчерпании бюджета честный результат — `DRAFT_WITH_DEBT` / `FINAL_WITH_LIMITATIONS`, а не искусственно зелёный final pass.

## Dashboard

Live dashboard читает `wave-quality-metrics.json`, `wave-manager-decision.json` и `wave-acceptance.json`. Он показывает ready/draft тесты, корневые блокирующие паттерны, решение менеджера и наличие валидной квитанции принятия.

## Сбалансированный протокол scaffolding

Контроллер специально не разрешает две крайности: глушить все сложные зависимости подряд и тратить весь бюджет на достижение 100% runtime-готовности любой ценой.

1. Для helper/POM-корня сначала выполняется одна ограниченная попытка `REMEDIATE_CURRENT_WAVE`. Простые, детерминированные маппинги, методы и побочные эффекты мигрируются нормально.
2. CLI записывает `COMPLETED` только если исчез именно выбранный точный корень; посторонняя или частичная уборка считается `NO_PROGRESS`. Только после этого manager может выбрать `SCAFFOLD_CURRENT_ROOT`, раньше это решение запрещено.
3. Executor добавляет один точный `ScaffoldMethods` или узкий квалифицированный шаблон с wildcard только в имени метода, например `TariffSettingsHelper.*`. Catch-all и `*.Method` запрещены.
4. Renderer сохраняет присваивание и `await`, ставит `[MIGRATOR:SCAFFOLD]` и гарантированно падает при запуске. Правдоподобные `default`, `false`, пустые коллекции и `Task.CompletedTask` запрещены.
5. Assertions, API Selenium/Playwright, селекторы, ожидания и произвольные неизвестные выражения scaffold-ить нельзя. Suppression остаётся отдельной политикой с доказательствами.
6. `maxScaffoldRoots` и `maxScaffoldOnlyTestRatio` ограничивают этот выход. Превышение ведёт к `SPLIT_WAVE`, реализации или честной остановке.
7. Структурно завершённая wave с заглушками принимается только через `ACCEPT_WITH_SCAFFOLDING`; `runtimeReady` остаётся `false`.

В результате мигратор закрывает массовую рутину переписывания тестов, а редкие проектные helper-ы остаются компактной и явно обозначенной очередью для отдельного умного агента или разработчика.
