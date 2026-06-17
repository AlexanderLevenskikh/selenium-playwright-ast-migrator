# Playbook: SOURCE_ONLY_IDENTIFIER pattern backlog

Цель: не превращать `SOURCE_ONLY_IDENTIFIER(page/pagef)` в бесполезную массовую эскалацию, а разложить TODO по реальным POM-выражениям и migration-паттернам.

## Почему это важно

`page`, `pagef`, `lightbox`, `modal`, `dialog`, `popup`, `Driver` и `WebDriver` обычно являются source-side Selenium/POM объектами. Их нельзя слепо добавлять в `TargetKnownIdentifiers`: это замаскирует проблему и может сгенерировать невалидный Playwright-код.

Но `SOURCE_ONLY_IDENTIFIER(page)` — это симптом, а не финальный root cause. Сам root `page` маппить нельзя, зато конкретные выражения под ним часто можно закрыть через profile/config:

```text
page.Loader.ValidateLoading()       -> Methods / wait mapping
page.Save.Click()                   -> UiTargets + click mapping
page.Filter.Name.SendKeys(value)    -> UiTargets + fill mapping
page.Table.Items.ElementAt(i).Text  -> Tables/list recognizer or table mapping
page.AddReasons.ClickAndOpen<T>()   -> click/open/modal mapping or migrator recognizer
```

## Hard rules

- Не группируй TODO только по root identifier (`page`, `pagef`, `lightbox`, `modal`).
- Не делай вывод “TODO невозможно убрать через config”, пока не построен backlog по full source expression и pattern.
- Не удаляй `page`, `pagef`, `lightbox`, `modal`, `WebDriver` из `SourceOnlyIdentifiers` без явного developer approval.
- Не добавляй Selenium/POM roots в `TargetKnownIdentifiers`, если они реально не существуют в target Playwright code.
- Не создавай fake mappings только ради уменьшения TODO.
- Не правь generated `.cs` / `.spec.ts` вручную.

## Required analysis

Из последнего migration output прочитай:

- generated files с smart TODO markers `[MIGRATOR:*]`;
- `report.json`;
- `explain-todo.json` / `explain-todo.md`;
- `unmapped-targets.json`;
- `unsupported-actions.json`;
- текущий `adapter-config.json` / profile stack.

Для каждого `SOURCE_ONLY_IDENTIFIER` нужно попытаться извлечь не только root, а наиболее конкретное source expression из `Source:` строки.

## Pattern buckets

Группируй TODO минимум по таким buckets:

| Bucket | Examples | Typical fix |
|---|---|---|
| loader/wait | `*.Loader.*`, `ValidateLoading`, `Wait*` | `Methods` / `ParameterizedMethods` |
| click/open/modal | `Click`, `ClickAndOpen<T>`, `Open*` | `UiTargets`, method mapping, or recognizer |
| input/fill | `SendKeys`, `InputText`, `ManualInputValue`, `SelectValue` | `UiTargets` + action mapping |
| text/value assertion | `.Text.Should`, `.Should().Be`, `Assert.AreEqual` | assertion mapping / method mapping |
| visibility assertion | `Displayed`, `Should().BeTrue`, `Should().BeFalse` | visibility assertion mapping |
| table/list | `Table`, `Items`, `Rows`, `ElementAt`, `Cells` | `Tables`/`Pagination` or migrator recognizer |
| navigation/url/webdriver | `WebDriver`, `Url`, `Navigate`, `GoTo` | `TestHost`, URL mapping, or manual |
| modal/lightbox scope | `modal.*`, `lightbox.*`, `dialog.*` | modal scope mapping / recognizer |

## Output table

Перед правками config создай таблицу top-50:

| Field | Meaning |
|---|---|
| `Rank` | по count desc и fixability |
| `Full source expression` | конкретное выражение, не только root |
| `Normalized pattern` | обобщённый паттерн |
| `Count` | сколько TODO покрывает |
| `Example source line` | 1 пример |
| `Current marker` | например `SOURCE_ONLY_IDENTIFIER` |
| `Fixability` | `config-only`, `profile-method`, `table-recognizer`, `migrator-change`, `manual` |
| `Recommended config section` | `UiTargets`, `Methods`, `ParameterizedMethods`, `Tables`, `Pagination`, `TestHost`, `none` |
| `Risk` | low/medium/high |

## Safe iteration

1. Возьми самый частотный low-risk паттерн.
2. Найди source truth в POM/helper/base class/target Playwright tests.
3. Добавь минимальный config/profile mapping.
4. Запусти `config-validate`.
5. Запусти `migrate` или `verify-project`.
6. Запусти `explain-todo` и `migration-board`.
7. Сравни before/after:
   - total TODO;
   - TODO by marker;
   - active generated statements;
   - compile errors;
   - smoke candidates.
8. Если стало хуже — откати change.

## When to escalate

Создавай escalation report только после pattern backlog, если:

- top pattern требует generic recognizer/renderer change;
- source truth отсутствует;
- config-only вариант приводит к невалидному target code;
- TODO каскадом зависит от unsupported action вроде `ClickAndOpen<T>()`, который мигратор пока не умеет раскрывать.

Escalation должен быть по конкретному pattern, а не по root `page` целиком.

Плохо:

```text
page: 1540 TODO, невозможно исправить.
```

Хорошо:

```text
page.AddReasons.ClickAndOpen<CatalogStopReasonsModalPage>() встречается 47 раз.
Нужен generic recognizer: click source locator, declare modal/dialog scope, allow following modal.* mappings.
```

## Prompt: corrective instruction for an agent

```text
Ты сделал неверный стратегический вывод по TODO.

Не группируй TODO только по root identifier (`page`, `pagef`, `lightbox`, `modal`). Root-level статистика полезна только как симптом, но бесполезна для исправления.

Правильная стратегия:

1. `page`, `pagef`, `lightbox`, `modal`, `WebDriver` нельзя слепо добавлять в `TargetKnownIdentifiers`.
   Это source-side Selenium/POM объекты. Если сделать их target-known, мигратор начнёт генерировать невалидный Playwright-код и замаскирует проблему.

2. `SOURCE_ONLY_IDENTIFIER(page/pagef)` не означает, что TODO невозможно убрать через config.
   Нельзя маппить сам root `page`, но можно и нужно маппить конкретные выражения:
   - `page.SaveButton`
   - `page.Filter.Name`
   - `page.Table`
   - `page.MenuItems.SideMenuButtonSearch`
   - `page.AddReasons`
   - `page.Loader`

3. Сначала сгруппируй TODO не по root, а по полному source expression и по pattern.

Нужно построить таблицу top-50 повторяющихся выражений/паттернов:

- full source expression
- normalized pattern
- count
- example source line
- category
- can be fixed by config? yes/no/maybe
- recommended config section: UiTargets / Methods / ParameterizedMethods / Tables / Pagination / TestHost / requires migrator change
- priority

Паттерны для группировки:

- loader/wait: `*.Loader.*`, `ValidateLoading`, `Wait*`
- click/open/modal: `Click`, `ClickAndOpen<T>`, `Open*`
- input/fill: `SendKeys`, `InputText`, `ManualInputValue`, `SelectValue`
- text/value assertion: `.Text.Should`, `.Should().Be`, `Assert.AreEqual`
- visibility assertion: `Displayed`, `Should().BeTrue`, `Should().BeFalse`
- table/list: `Table`, `Items`, `Rows`, `ElementAt`, `Cells`
- navigation/url/webdriver: `WebDriver`, `Url`, `Navigate`, `GoTo`
- modal/lightbox scoped controls: `modal.*`, `lightbox.*`

Не делай вывод “TODO не убрать через config”, пока не построишь эту таблицу.

Результат нужен не как escalation report, а как migration backlog:

1. Config-only fixes — можно закрыть маппингами.
2. Profile method fixes — можно закрыть Methods/ParameterizedMethods.
3. Table/list recognizer fixes — нужна доработка мигратора.
4. Manual leftovers — реально ручные случаи.

После этого предложи первые 20 config changes с максимальным эффектом.
```

## Prompt: next migration iteration

```text
Следующая итерация: reduce TODO by source-pattern backlog, not by root identifier.

Input:
- generated migration output
- report.json
- explain-todo.json / explain-todo.md
- unmapped-targets.json
- unsupported-actions.json
- adapter-config.json

Goal:
Reduce actionable TODO count by improving adapter-config/profile mappings.

Rules:
- Do not edit generated files manually.
- Do not remove `page`, `pagef`, `lightbox`, `modal`, `WebDriver` from `SourceOnlyIdentifiers`.
- Do not add Selenium/POM variables to `TargetKnownIdentifiers` unless they really exist in target Playwright code.
- Do not create fake mappings just to silence TODO.
- Prefer high-frequency patterns over one-off fixes.
- If a TODO depends on an upstream TODO, fix upstream first.

Process:
1. Extract all TODO with `[MIGRATOR:*]`.
2. For `SOURCE_ONLY_IDENTIFIER`, parse the full source line and extract the most specific source expression, not only the root.
3. Group by normalized pattern.
4. Produce top-50 backlog sorted by count and fixability.
5. For each top item decide:
   - UiTargets mapping
   - Methods mapping
   - ParameterizedMethods mapping
   - Tables/Pagination mapping
   - TestHost/setup mapping
   - requires migrator recognizer
   - manual
6. Apply only safe config/profile changes.
7. Run:
   - config-validate
   - migrate
   - verify-project
   - explain-todo
   - migration-board
8. Compare before/after:
   - total TODO
   - active generated statements
   - TODO by marker
   - compile errors
   - smoke candidates

Success criteria:
- TODO decreases because real patterns were mapped.
- Generated active code increases.
- No new compile errors.
- No source-only identifiers are promoted to target-known without proof.
- The report explains which patterns were fixed and which still require migrator changes.
```

## WaitPolicy note

Selenium explicit waits must be classified before generic source-only TODO handling. Actionability waits such as `WaitPresence`, `WaitVisible`, `WaitEnabled` are usually elided because Playwright auto-waits before actions/assertions. Product-state waits such as `ValidateLoading`, `WaitForLoaded`, table/grid/list refresh waits, modal/toast waits must be kept or converted to Playwright web-first assertions. Ambiguous waits become `[MIGRATOR:WAIT_REQUIRES_STATE_ASSERTION]`. See `docs/wait-policy.md`.

