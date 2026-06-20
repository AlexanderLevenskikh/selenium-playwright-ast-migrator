# Migration safety playbook

Этот документ описывает правила безопасной config-driven миграции Selenium C# тестов в Playwright .NET.

Главная цель — агрессивно снижать активные TODO, но не превращать миграцию в генерацию ложного зелёного кода.

## Базовый цикл работы

Каждая итерация должна проходить через цикл:

```text
Analyze → Pattern Mining → Hypothesis → Safe Experiment → Validation → Decision → Report
```

1. **Analyze** — запусти миграцию/verify и собери TODO по категориям.
2. **Pattern Mining** — найди повторяющиеся группы: waits, assertions, navigation, PageObject wrappers, source-only identifiers, missing mappings, unsupported actions, external variables.
3. **Hypothesis** — для каждой группы реши: config или тикет на core migrator.
4. **Safe Experiment** — сначала пробуй config-only.
5. **Validation** — после изменения config запускай новый run и сравни результат.
6. **Decision** — оставляй изменение только если метрики улучшились и safety checks не ухудшились.
7. **Report** — обнови `migration/migration-progress.md`.

## Золотое правило

Project-specific behavior должен жить в `adapter-config.json`.

Core migrator code можно менять только если найдено реальное ограничение движка, которое нельзя безопасно решить config-ом. В agent-tool workflow агент не меняет core code вообще — он создаёт тикет.

## Что нельзя делать

Нельзя:

- изменять source Selenium project;
- вручную править generated `.cs` files;
- suppress-ить assertions без анализа;
- suppress-ить business actions без анализа;
- suppress-ить navigation/url/cookie/localStorage автоматически;
- менять C# migrator code вместо тикета;
- считать `0 TODO` успехом, если suppressions скрыли важную тестовую логику.

## Safe suppression policy

`SuppressedMethodPatterns` можно использовать только для кода, который подтверждённо не должен переезжать в Playwright в активном виде.

Безопасные кандидаты:

- legacy Selenium lifecycle;
- browser/window setup;
- устаревшие POM-navigation helpers, если целевой Playwright-проект использует другую архитектуру;
- source-only UI roots, которые в новой архитектуре заменяются fixtures/controls;
- setup helpers, которые уже выполняются другим способом;
- redundant waits, покрытые Playwright auto-waiting или fixture lifecycle.

Опасные кандидаты:

- assertions;
- проверки бизнес-данных;
- save/create/delete/update actions;
- navigation с важными параметрами;
- cookies/localStorage/sessionStorage;
- URL construction;
- authentication/authorization helpers;
- методы с `Assert`, `Should`, `Verify`, `Check`, `Validate`, `Exist`, `Create`, `Save`, `Delete`, `Open`, `GoTo`, `Navigate`.

Для опасных кандидатов сначала нужен mapping, отдельный анализ или тикет.

## WebDriver migration policy

Все `SOURCE_ONLY_IDENTIFIER(WebDriver)` нужно анализировать по смыслу.

### Можно suppress-ить

Если `WebDriver` используется только для lifecycle/browser management:

```csharp
WebDriver.Quit();
WebDriver.Close();
WebDriver.Dispose();
WebDriver.Manage().Window.Maximize();
WebDriver.Manage().Window.Size = ...;
```

Такие вызовы обычно не нужны в Playwright .NET, потому что lifecycle управляется fixture/browser context.

Решение:

```text
If WebDriver usage is only lifecycle/window management and has no assertions or test data impact,
add a SuppressedMethodPattern and document why it is safe.
```

### Нельзя suppress-ить автоматически

Не suppress-ить без анализа:

```csharp
WebDriver.Url
WebDriver.Navigate().GoToUrl(...)
WebDriver.Navigate().Refresh()
WebDriver.Manage().Cookies
LocalStorage
SessionStorage
IJavaScriptExecutor.ExecuteScript(...)
```

Эти вызовы могут быть частью реальной логики теста:

- проверка URL;
- переход на конкретную страницу;
- авторизация;
- установка токенов;
- подготовка localStorage;
- работа с cookies;
- refresh как часть сценария.

Возможные Playwright-аналоги:

```csharp
await Page.GotoAsync(url);
await Page.ReloadAsync();
Page.Url
await Context.AddCookiesAsync(...);
await Page.EvaluateAsync(...);
```

Если config не умеет выразить такой mapping, создай тикет на core migrator/config extension.

## URL and external variable policy

`EXTERNAL_URL_VARIABLE` нельзя просто suppress-ить.

URL constants почти всегда являются частью сценария.

Примеры:

```csharp
Urls.BaseUrl
Urls.LoginUrl
Urls.DocumentsUrl
Urls.RegistryUrl
```

Порядок анализа:

1. Найти source declaration, например `Urls.cs`.
2. Найти target-аналог в Playwright-проекте:
   - `TestSettings.BaseUrl`;
   - `EnvironmentConfig.BaseUrl`;
   - `AppSettings.BaseUrl`;
   - fixture/env helper;
   - existing navigation helper.
3. Если target-аналог есть — добавить mapping или `TargetKnownIdentifiers`.
4. Если target-аналога нет — создать тикет.

Не suppress-ить URL constants только ради снижения TODO.

Хороший тикет:

```md
## TS-X: Config-driven external variable mappings

Problem:
Source Selenium tests use external URL constants such as `Urls.BaseUrl`, but migrator cannot map them to target Playwright environment/config identifiers.

Expected:
Allow config mapping from source external variables to target expressions.

Example:
`Urls.BaseUrl` → `TestEnvironment.BaseUrl`

Why config is needed:
URL constants are project-specific, but the migrator needs a generic mechanism to substitute known external variables.
```

## PageObject architecture gaps and POM recovery

Во многих проектах старый Selenium POM слой не переносится 1:1. Это нормально, но broad suppressions не должны быть первым действием агента.

Примеры source-only POM expressions:

```csharp
page.MenuItems.Error
page.Table.Rows.First()
lightbox.OkButton.Click()
modal.Close()
dialog.Confirm()
popup.WaitLoaded()
```

Если target Playwright-проект использует другую архитектуру controls/fixtures, такие вызовы могут быть source-only architecture gap. Но перед подавлением нужно выполнить POM recovery pass.

Короткое правило:

```text
Do not suppress POM expressions before trying to recover source truth.
```

Перед broad suppressions вроде:

```text
page.*.*
lightbox.*.*
modal.*.*
dialog.*.*
popup.*.*
```

агент обязан:

1. найти declaration в исходном Selenium POM;
2. извлечь selector evidence (`CreateControlByTid`, `WithDataTestId`, CSS, XPath, helper methods);
3. проверить target Playwright architecture;
4. если можно — добавить `UiTargets` / `Methods` / `ParameterizedMethods`;
5. если config недостаточно — создать target POM candidate в `migration/pom-candidates/`;
6. если перенос небезопасен — добавить documented suppression и записать причину в `migration/pom-recovery.md`.

Suppressions допустимы только если:

- это не assertion;
- это не бизнес-действие;
- это не единственный переход в сценарии;
- целевая архитектура действительно заменяет этот слой другим способом;
- POM recovery attempt задокументирован.

Правильная формулировка broad suppression:

```text
Old Selenium POM layer is intentionally not translated 1:1.
Target Playwright project uses a different control/page architecture.
Selectors were mined where possible; remaining expressions are manual follow-up.
```

Подробные правила: `docs/pom-recovery-policy.md`.

## Wait policy

Не все wait можно удалять.

### Можно elide

Обычно можно elide, если wait только дублирует Playwright actionability:

```csharp
WaitVisible()
WaitExistAndVisible()
WaitBecomeFalse() // если это loader/spinner/actionability wait
WaitTokens()      // если это loader/progress wait
```

Но только после анализа receiver-а.

### Нельзя elide автоматически

Не elide без mapping:

```csharp
WaitDisabled()
WaitNotExists()
WaitContainsText()
WaitValue()
WaitValueContains()
WaitOpened()
```

Почему:

- `WaitDisabled` проверяет состояние disabled, это может быть бизнес-условие.
- `WaitNotExists` может проверять исчезновение важного элемента.
- `WaitContainsText` и `WaitValue` — это assertions.
- `WaitOpened` может означать появление модалки/страницы, а не просто actionability.

Для таких методов лучше использовать `ParameterizedMethods` или `WaitPolicies` с явным смыслом.

## Assertion policy

Assertions нельзя suppress-ить ради чистого отчёта.

Опасные паттерны:

```csharp
Assert.*
Should().Be(...)
Should().BeEquivalentTo(...)
Should().Contain(...)
Should().NotBeNull()
Exist*
Check*
Validate*
Verify*
```

Если assertion не маппится:

1. Попробуй `ParameterizedMethods`.
2. Попробуй `RecognizerAliases`.
3. Попробуй target `Expect(...)`.
4. Если multiline fluent chain не матчится — заведи тикет на matcher.
5. Suppression допустима только если assertion относится к legacy setup/helper и явно не влияет на проверку сценария.

## Active TODO vs commented source TODO

Финальный критерий — `0 active TODO`.

Но explain/report может содержать TODO-like markers внутри закомментированного source-кода.

Это нормально, если:

- active generated code чистый;
- commented fragments сохранены только как traceability;
- suppressed source не участвует в выполнении;
- в отчёте явно написано, что это не active blockers.

Нельзя выдавать commented TODO за реальные blockers, если они не попадают в active generated code.

## verify-project timeout policy

Если `verify-project` падает по timeout на большом корпоративном solution, это не равно compile failure.

Формулировать результат нужно аккуратно:

```text
verify-project stopped by timeout while loading/checking a large solution,
but reported 0 diagnostics before termination.
```

Не писать:

```text
Full project build passed.
```

Если процесс не завершился, правильная формулировка:

```text
0 build diagnostics before verify-project timeout.
```

## Когда заводить тикет на migrator core

Тикет нужен, если config не может решить проблему безопасно.

Примеры core limitations:

- renderer генерирует невалидный C#;
- class/file naming неидемпотентен;
- matcher не понимает multiline fluent chains;
- result variable не регистрируется;
- source-only safety срабатывает раньше suppression;
- parser теряет tuple discard переменные;
- recognizer классифицирует action неправильно;
- config schema не позволяет выразить нужный mapping.

Тикет должен содержать:

```md
## TS-X: Title

**Problem**
Что сломано.

**Example**
Минимальный source code.

**Root cause**
Почему это происходит внутри migrator.

**Why config is not enough**
Почему нельзя решить adapter-config-ом.

**Expected behavior**
Как должно быть.

**Suggested minimal fix**
Какой минимальный reusable fix нужен.

**Impact**
Сколько TODO/build diagnostics уйдёт.

**Risks**
Что может сломаться.
```

## Финальный критерий качества

Миграция считается успешной не только при `0 TODO`.

Нужны все условия:

```text
0 active TODO
0 syntax errors
0 build diagnostics or 0 diagnostics before timeout
guard checks passed
config-diff reviewed
broad suppressions documented
business assertions not blindly suppressed
generated code not manually edited
source Selenium project untouched
```

## Главная мысль

Мигратор — не магическая кнопка.

Это инструмент, который делает миграцию:

- измеримой;
- повторяемой;
- безопасной;
- итерационной;
- управляемой через config.

Хорошая миграция — это не просто снижение TODO, а снижение TODO без потери смысла тестов.
