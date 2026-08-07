# Политика автоматического triage и исправлений Migrator Lab

Блок 8 завершает первую версию полигона. Его задача — не дать агенту «исправить всё», а превращать каждый воспроизводимый сбой в ограниченный пакет работы.

## Решение по уровню регрессии

| Наблюдение | Основной уровень | Дополнительно |
|---|---|---|
| Локальный recognizer/parser/renderer pattern; один файл, нет project topology | `unit-test` | минимальный repro остаётся evidence |
| Ошибка проявляется только при `ProjectReference`, CPM, multi-targeting, build props/targets или verify harness | `project-fixture` | focused unit/contract test для причины |
| Ошибка найдена generated/metamorphic слоем и форма кода важна для воспроизведения | `saved-seed` | после понимания причины добавить unit-test, если его можно выразить компактно |
| Ошибка зависит от нескольких проектов/жизненного цикла/реального runtime | `project-fixture` | stable scenario, если класс риска критичен |
| Только инфраструктурный сбой или невалидный source fixture | не регрессия мигратора | чинится infrastructure/fixture layer |
| Недетерминированность | ручное расследование | сначала стабилизировать harness/oracle, потом классифицировать продуктовый дефект |

`lab promote` не генерирует фиктивный unit-test. Для уровня `unit-test` он сохраняет минимальный repro и план проверки, а bounded task pack требует от агента добавить конкретный focused assertion в `Migrator.Tests`.

## Когда агент может исправлять кластер автоматически

`AUTO_FIX_ELIGIBLE` допустим только если одновременно выполнено всё:

1. source fixture валиден и его тесты проходят;
2. результат воспроизводим;
3. expected status — `PASS`, actual — доказанная регрессия;
4. это не `SOURCE_INVALID`, `INFRASTRUCTURE_FAILURE` и не `NON_DETERMINISTIC`;
5. кластер ограничен максимум тремя вероятными компонентами мигратора;
6. исправление не требует менять expected status, semantic oracle или quality budget;
7. сценарий не пересекает сознательно unsupported boundary (`IJavaScriptExecutor`, Actions, dynamic/raw и т. п.);
8. в task pack есть минимальный repro, evidence, релевантный код и definition of done.

Во всех остальных случаях — `MANUAL_REVIEW`.

## Запрещённые «исправления»

Агент не должен ради зелёного результата:

- повышать `todoMax`, `unmappedMax`, `unsupportedMax`, `warningsMax` без отдельного доказательства неверного контракта;
- менять `PASS` на unsupported/regression;
- удалять semantic oracle;
- скрывать diagnostics;
- добавлять retry, чтобы спрятать недетерминированность;
- менять соседние кластеры «заодно»;
- принимать вывод предыдущего агента за доказательство без кода и артефактов.

## Порядок проверки после исправления

1. focused unit/contract test;
2. минимальный repro из task pack;
3. все сценарии текущего кластера;
4. затронутый smoke/PR feature set;
5. полный stable corpus;
6. перед значимым релизом — редкий real-project release gate.

## Real-project gate

Настоящий проект не запускается на каждом PR. Перед значимым релизом владелец сохраняет evidence-файл с:

- project/revision;
- revision мигратора;
- временем проверки;
- `PASS`/`FAIL`;
- ссылками/путями на retained evidence.

`lab release-gate` принимает релиз только если stable corpus зелёный, real-project evidence имеет `PASS` и не старше заданного окна (по умолчанию 14 дней).
