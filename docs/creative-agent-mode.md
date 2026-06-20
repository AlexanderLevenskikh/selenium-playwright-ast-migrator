Работай в **Creative Mode**.

Твоя цель — максимально продвигать миграцию Selenium C# → Playwright, используя интеллект, pattern mining и маленькие безопасные эксперименты. Creative Mode не означает хаос: ты можешь предлагать гипотезы и искать нестандартные решения, но обязан сохранять проверяемость, откатываемость и честную отчётность.



## Tool boundary для Creative Mode

Если миграция запускается через compiled CLI bundle, агент обязан считать мигратор внешним black-box tool.

Разрешено менять config/profile и migration docs. Запрещено искать или править C# source code мигратора. Если найдено ограничение core migrator — создать тикет в `migration/migrator-tickets.md`.

Перед началом прочитай:

- `docs/agent-tool-boundary.md`
- `docs/migration-safety-playbook.md`

Особенно строго соблюдай правила для `WebDriver`, URL/external variables, cookies/localStorage, assertions и broad POM suppressions.

Assertion suppression запрещён. Не добавляй в `SuppressedMethodPatterns` broad patterns вроде `*.*.Should(*)`, `*.*.Should()`, `*Assert*`, `*Expect*`, `*Wait().EqualTo(*)`. Они скрывают смысл тестов и создают ложно-зелёную миграцию. Если assertion не мигрируется — добавляй mapping, оставляй manual/failing TODO или заводи тикет.

Interaction suppression тоже опасен: `*lightbox.*.Click(*)`, `*modal.*.SendKeys(*)`, `*.*.Fill(*)`, `*.*.SetValue(*)`, `*.*.Hover*` мешают UiTarget/Method mappings. Сначала пытайся маппить действие.

Перед broad POM suppressions обязательно выполни POM recovery pass: найди исходные POM declarations, извлеки selector evidence, проверь target architecture, добавь config mappings или создай candidates в `migration/pom-candidates/`. Подробности: `docs/pom-recovery-policy.md`.

## Главный принцип

Creative Mode разрешает творчески искать migration strategy, но не разрешает творчески выдумывать факты.

Особенно строго:

* locator должен быть evidence-based;
* selector нельзя угадывать по имени POM-свойства;
* readiness нельзя завышать;
* TODO — это симптом, а не финальный диагноз.

`SOURCE_ONLY_IDENTIFIER(page/pagef)` — это не доказательство, что строку нельзя мигрировать.
`page` / `pagef` — source-side Selenium/POM root, но конкретные выражения под ними могут быть маппабельны:

* `page.SaveButton`
* `page.Filter.Name`
* `page.Loader`
* `page.Table`
* `page.AddReasons`
* `page.MenuItems.SideMenuButtonSearch`

Никогда не группируй TODO только по root `page` / `pagef`. Всегда группируй по полному source expression и normalized pattern.

## POM recovery before suppression

Broad suppressions по `page.*.*`, `lightbox.*.*`, `modal.*.*`, `dialog.*.*`, `popup.*.*` разрешены только после POM recovery attempt.

Перед тем как suppress-ить POM expression:

1. найди declaration в source Selenium POM;
2. извлеки selector evidence (`CreateControlByTid`, `WithDataTestId`, CSS, XPath, helper methods);
3. проверь target Playwright conventions;
4. если можно — добавь `UiTargets` / `Methods` / `ParameterizedMethods`;
5. если config недостаточно — создай candidate file в `migration/pom-candidates/`;
6. обнови `migration/pom-recovery.md`;
7. только после этого добавляй documented suppression, если перенос небезопасен.

Цель — не обязательно перевести старый POM 1:1. Цель — не потерять source truth: selectors, data-tid, helper semantics, component hierarchy и reusable actions.


## Перед началом

Всегда сначала прочитай:

* `migration/agent-state.md`
* `migration/pre-stop-checklist.md`
* последний `orchestration-report.md`
* последний `explain-todo.md`
* последний `agent-next-task.md`
* `unmapped-targets.json`
* `unsupported-actions.json`
* `verify-project-report.md`, если есть
* `migration-board.html` / `migration-board.md`, если есть
* `config-diff`, если есть
* `migrator-tickets.md`, если есть

После чтения коротко сформулируй:

* текущие метрики;
* главный blocker;
* 2–3 наиболее перспективных pattern-группы;
* один маленький эксперимент, который ты сделаешь первым.

## Циклический workflow

Повторяй цикл на каждой итерации.

### 1. Analyze

Прочитай последние отчёты и выпиши baseline:

* files processed;
* tests found;
* actions found;
* mapped / unmapped;
* TODO total;
* TODO by marker;
* compile / verify status;
* smoke candidates;
* top repeated TODO patterns.

Не делай выводы только по root identifier.

Плохо:

* `page`: 1540 TODO, невозможно исправить

Хорошо:

* `page.Loader.ValidateLoading()` → wait/product-state mapping
* `page.AddReasons.ClickAndOpen<T>()` → click/open/modal pattern
* `page.Table.Items.ElementAt(i).Text` → table/list recognizer
* `page.Filter.Name.SendKeys(value)` → UiTarget + fill mapping
* `page.Save.Click()` → UiTarget + click mapping

### 2. Pattern Mining

Найди повторяющиеся TODO-паттерны по полному source expression.

Группируй минимум по таким категориям:

* loader/wait:

  * `*.Loader.*`
  * `ValidateLoading`
  * `WaitLoaded`
  * `WaitVisible`
  * `WaitPresence`
  * `WaitEnabled`
* click/open/modal:

  * `Click`
  * `ClickAndOpen<T>`
  * `ClickAndFollow<T>`
  * `Open*`
* input/fill:

  * `SendKeys`
  * `InputText`
  * `ManualInputValue`
  * `SelectValue`
  * `Enter`
* assertion:

  * `.Should().Be`
  * `.Should().HaveHtmlText`
  * `.Should().BeEnabled`
  * `Assert.AreEqual`
  * text/value/visibility assertions
* table/list:

  * `Table`
  * `Items`
  * `Rows`
  * `ElementAt`
  * `Cells`
* modal/lightbox scope:

  * `modal.*`
  * `lightbox.*`
* navigation:

  * `Browser.GoToPage<T>`
  * `GoToPageWithUserAccessRight<T>`
  * `Navigation.Open*`
* webdriver/url:

  * `WebDriver`
  * `Url`
  * `Navigate`
  * `Refresh`

Сделай top-20 или top-50 backlog:

* full source expression;
* normalized pattern;
* count;
* example source line;
* TODO marker;
* likely fix type;
* confidence;
* expected impact.

## 3. Hypothesis Generation

Для выбранного паттерна предложи 2–3 гипотезы.

Возможные типы решения:

* `UiTargets`
* `Methods`
* `ParameterizedMethods`
* `Tables`
* `Pagination`
* `Scopes`
* `TestHost`
* `SourceOnlyIdentifiers`
* `TargetKnownTypes`
* `TargetKnownIdentifiers`
* TS-specific profile override
* migrator ticket: recognizer/renderer/parser fix required

Не выбирай решение “добавить source-side объект в TargetKnownIdentifiers”, если этот объект реально не существует в target Playwright проекте.

Нельзя слепо добавлять в `TargetKnownIdentifiers`:

* `page`
* `pagef`
* `modal`
* `lightbox`
* `WebDriver`
* Selenium PageObject instances

## 4. Locator Source Truth Rules

Creative Mode не даёт права угадывать локаторы.

Перед созданием или изменением Playwright locator-а всегда установи источник истины.

### Главное правило

POM property name is not selector.

Пример:

```csharp
page.MenuItems.SideMenuDocumentsAgreements.Click()

Нельзя автоматически превращать в:

```ts
await page.locator('[data-tid="SideMenuDocumentsAgreements"]').click();
```

`SideMenuDocumentsAgreements` — это имя свойства PageObject, а не selector.

Сначала нужно провалиться в объявление свойства:

```csharp
public Link SideMenuDocumentsAgreements => CreateControlByTid<Link>("t_sideMenu_item_agreements");
```

Затем нужно провалиться в helper:

```csharp
private TControl CreateControlByTid<TControl>(string tid)
{
    return controlFactory.CreateControl<TControl>(
        Container.Search(x => x.WithDataTestId(tid)));
}
```

Только после этого можно сделать вывод:

```ts
await page.locator('[data-test-id="t_sideMenu_item_agreements"]').click();
```

### Selector provenance

Для каждого нового locator-а укажи provenance:

* `ProvenFromPOM` — найдено в Selenium POM/property/helper;
* `ProvenFromTargetCode` — найдено в существующем Playwright/React-коде;
* `ProvenFromDOMConvention` — доказано через общий helper/convention;
* `InferredLowConfidence` — предположение, нельзя использовать без runtime/check;
* `Unknown` — locator не найден, нужен TODO или ticket.

Запрещено добавлять active locator с `InferredLowConfidence` или `Unknown`, если нет отдельного runtime-доказательства.

### Attribute rules

Не выбирай атрибут по привычке.

В проекте могут одновременно существовать:

* `data-tid`
* `data-test`
* `data-test-id`

Для кода продукта приоритет — `data-test-id`, если source truth показывает `WithDataTestId(...)`.

Для компонентов библиотек `react-ui` / `ovr-ui` могут использоваться другие атрибуты. Нельзя переносить правило одного слоя на другой без проверки.

Примеры:

```csharp
WithDataTestId("abc")
```

обычно означает:

```ts
page.locator('[data-test-id="abc"]')
```

```csharp
WithTid("abc")
```

может означать:

```ts
page.locator('[data-tid="abc"]')
```

Но это нужно подтверждать по helper/control factory.

### Required lookup order

Когда встречаешь Selenium expression:

```csharp
page.SomeControl.Click()
page.MenuItems.SomeItem.Click()
modal.SomeInput.SendKeys(...)
```

сделай lookup в таком порядке:

1. Найди POM property declaration.
2. Если property вызывает helper (`CreateControlByTid`, `CreateControl`, `Search`, `WithDataTestId`, `WithTid`, `WithDataTid`, etc.) — раскрой helper.
3. Определи реальный selector value.
4. Определи правильный attribute name.
5. Проверь, есть ли уже аналогичный locator/helper в существующем Playwright TS проекте.
6. Только после этого генерируй locator.

### Bad / Good examples

Bad:

```ts
await page.locator('[data-tid="SideMenuDocumentsAgreements"]').click();
```

Reason: `SideMenuDocumentsAgreements` is a POM property name, not a selector.

Good:

```ts
await page.locator('[data-test-id="t_sideMenu_item_agreements"]').click();
```

Reason: selector value was proven from:

```csharp
CreateControlByTid<Link>("t_sideMenu_item_agreements")
```

and helper uses:

```csharp
WithDataTestId(tid)
```

### If source truth is missing

Do not invent selectors.

Instead:

1. Leave a TODO in migration notes.
2. Add the unresolved control to selector backlog.
3. Use a skipped test only if needed.
4. Report exact missing source expression.

Format:

```text
Selector unresolved:
- Source expression: page.MenuItems.SideMenuDocumentsAgreements
- Tried:
  - POM property declaration
  - helper expansion
  - target Playwright code search
- Result: no proven selector
- Next: inspect POM/control factory or DOM
```

### Locator confidence report

For every migrated TS file, include locator confidence summary:

| File | Proven locators | Inferred locators | Unknown locators |
| ---- | --------------: | ----------------: | ---------------: |

If `Inferred locators > 0` or `Unknown locators > 0`, do not call the file runtime-ready.

## 5. Safe Experiment

Сделай ровно одно маленькое изменение.

Разрешены изменения:

* `adapter-config.json`
* profile config files
* migration-specific helper config
* generated reports внутри `migration/`
* `migration/migrator-tickets.md`

По умолчанию запрещено:

* редактировать C# код мигратора;
* редактировать generated `.cs`;
* редактировать исходный Selenium проект;
* делать крупный refactor;
* менять много unrelated mappings за один шаг;
* заглушать TODO фейковыми mappings;
* выдумывать selectors без source truth.

Если ты работаешь в существующем Playwright TypeScript проекте и задача явно состоит в TS draft migration, можно создавать новые `.spec.ts` и helper-файлы только в согласованной папке Playwright-проекта или в `migration/ts-draft/`. Нельзя менять существующие тесты без явного разрешения.

## 6. WaitPolicy

Не переноси Selenium waits механически.

Разделяй wait-паттерны:

### Safe to elide

Ожидания actionability перед действием обычно удаляются, потому что Playwright сам ждёт кликабельность/видимость/стабильность элемента:

* `WaitPresence`
* `WaitVisible`
* `WaitEnabled`
* wait перед `Click`
* wait перед `SendKeys`
* wait перед `Fill`
* wait перед простым assertion

Но удаление должно быть осознанным и отражённым в отчёте как elided wait, а не как потерянная строка.

### Product-state wait

Ожидания состояния продукта нельзя удалять вслепую. Их нужно конвертировать в Playwright assertion:

* loader disappears;
* table loaded;
* rows count changed;
* modal opened;
* toast appeared/disappeared;
* URL changed;
* download/popup happened;
* server/db-driven refresh finished.

Примеры:

* `page.Loader.ValidateLoading()` → wait for loader hidden
* `page.Table.WaitForLoaded()` → wait for table visible/loaded
* `page.Table.Rows.Count.Should().Be(n)` → expect rows to have count
* `ClickAndOpen<T>()` → click + modal/page state wait

### Ambiguous wait

Не переносить `Thread.Sleep`, `WaitForTimeout`, generic backend polling как timeout.

Вместо этого создать TODO/ticket:

`WAIT_REQUIRES_STATE_ASSERTION`

с объяснением, какое состояние нужно найти.

## 7. Validation

После каждого изменения запускай полный safety loop:

1. `config-validate`
2. `migrate` или `orchestrate`
3. `verify-project` / `verify-ts-project`, если применимо
4. `guard`
5. `config-diff`
6. `explain-todo`
7. `migration-board`, если доступен

Сравни before/after:

* mapped;
* unmapped;
* TODO total;
* TODO by marker;
* active generated statements;
* compile errors;
* verify status;
* smoke candidates;
* runtime readiness;
* new warnings/regressions.

## 8. Decision

Если метрики улучшились и нет регрессий:

* оставь изменение;
* зафиксируй почему оно безопасно;
* обнови `migration/agent-state.md`.

Если метрики ухудшились:

* откати изменение;
* сохрани причину отката;
* попробуй следующую гипотезу.

Если метрики не изменились:

* не делай вид, что задача решена;
* объясни, почему гипотеза не сработала;
* попробуй другой pattern или создай ticket.

## 9. Migrator tickets

Если нужна правка recognizer/parser/renderer, не лезь в C# код мигратора в Creative Mode.

Создай или обнови:

`migration/migrator-tickets.md`

Каждый ticket должен содержать:

* ID;
* название;
* affected count;
* minimal Selenium source example;
* current action classification;
* current generated output / TODO marker;
* почему config/profile не может это исправить;
* expected IR/action type;
* expected generated output;
* regression test idea;
* priority;
* confidence.

Хороший ticket — это не “много TODO из-за page”, а конкретный blocker:

* `Browser.GoToPage<T>` parsed as `RawStatementAction`;
* `ClickAndOpen<T>` assignment loses modal scope;
* `ElementAt(i)` should become `Nth(i)`;
* product wait should become Playwright assertion;
* modal/lightbox scoped controls need scope mapping;
* selector cannot be proven from POM/helper/source truth.

## 10. Readiness levels

Не завышай статус результата.

Разделяй уровни готовности:

* `generated` — файл создан;
* `compile-ready` — C# / TypeScript компилируется;
* `list-ready` — Playwright видит тесты через `--list`;
* `smoke-ready` — тест выглядит пригодным для запуска на стенде;
* `runtime-proven` — тест реально прошёл;
* `behavior-reviewed` — логика сверена с Selenium source.

Не называй миграцию завершённой, если есть только compile-ready.

Правильная формулировка:

* “compile-ready draft готов”
* “runtime smoke ещё нужен”
* “логика требует выборочной сверки”
* “N тестов skipped, причины такие-то”

## 11. TypeScript migration rules

Если работаешь с Playwright TypeScript проектом:

* используй существующий стиль проекта;
* используй существующие fixtures;
* не изобретай новый test framework;
* не генерируй standalone-тесты в вакууме;
* учитывай `playwright.config.ts`;
* учитывай `tsconfig.json`;
* используй существующие helpers;
* проверяй TypeScript compile;
* запускай `playwright test --list`, если возможно;
* отделяй compile-ready от runtime-proven;
* не создавай locator без source truth;
* не используй имя POM-свойства как selector;
* проверяй атрибут: `data-tid` / `data-test` / `data-test-id`.

Для каждого TS файла в отчёте укажи:

* source Selenium file;
* target TS file;
* source test count;
* generated test count;
* skipped count;
* skipped reasons;
* locator confidence summary;
* confidence: high / medium / low;
* required runtime checks.

## 12. Reporting

В конце каждой итерации дай краткий отчёт на русском.

Формат:

### Что сделал

* одно изменение;
* какой pattern пытался закрыть;
* какие config/profile файлы изменены;
* если писал TS draft — какие файлы созданы/изменены.

### Before / After

| Metric           | Before | After |
| ---------------- | -----: | ----: |
| Mapped           |        |       |
| Unmapped         |        |       |
| TODO total       |        |       |
| Compile errors   |        |       |
| Smoke candidates |        |       |

### Locator confidence, если применимо

| File | Proven locators | Inferred locators | Unknown locators |
| ---- | --------------: | ----------------: | ---------------: |

### Решение

* keep / rollback / ticket created

### Почему

* что улучшилось;
* что не изменилось;
* какие риски;
* какие assumptions были сделаны.

### Следующий шаг

* один конкретный следующий эксперимент;
* или blocker/ticket, если дальше config-only прогресс невозможен.

## 13. Когда спрашивать пользователя

Не спрашивай после каждого успешного маленького цикла.

Продолжай сам, если:

* safety loop зелёный;
* изменение маленькое;
* метрики улучшаются;
* нет generic blocker;
* locator source truth доказан.

Спрашивай пользователя, если:

* нужен C# migrator change;
* надо менять существующие Playwright TS тесты;
* надо выбрать между несколькими рискованными стратегиями;
* обнаружен generic blocker;
* runtime поведение отличается от Selenium;
* locator нельзя доказать по POM/helper/source truth;
* требуется доступ к стенду/секретам/ручной проверке.

## Короткая цель Creative Mode

Не просто уменьшить TODO любой ценой.

Цель — превращать хаотичную миграцию в проверяемый backlog:

1. config/profile fixes;
2. TS draft migration;
3. recognizer/parser/renderer tickets;
4. runtime smoke candidates;
5. честные leftovers.

Creative Mode должен быть смелым в поиске решений, но строгим к фактам.


## Placeholder model before writing mappings

Before adding a `Methods` or `ParameterizedMethods` entry, use the nouns/verbs model:

```text
UiTargets translate source objects (nouns).
Methods / ParameterizedMethods translate actions (verbs).
```

Use `{TARGET}` for active generated Playwright code. Use `{source}` mostly as source evidence in comments or diagnostics. If `{TARGET}` cannot be resolved, mine the source POM or add a `UiTargets` mapping first instead of emitting active code that references old Selenium objects.

See [`docs/profile/placeholder-mental-model.md`](profile/placeholder-mental-model.md).
