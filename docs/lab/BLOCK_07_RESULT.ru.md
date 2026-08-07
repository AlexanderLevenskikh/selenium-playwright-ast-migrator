# Блок 7: seedable generation и metamorphic testing

Блок 7 добавляет к стабильному корпусу отдельный **generated/exploratory слой**. Он не меняет 30 постоянных сценариев и не превращает PR-gate в случайный fuzzing.

## Что реализовано

### `lab generate`

Команда генерирует семейство `p30-basic-login-metamorphic` из READY/PASS сценария `p01-basic-id-login`.

```powershell
selenium-pw-migrator lab generate `
  --corpus ./corpus/stable/vertical-slice `
  --base p01-basic-id-login `
  --seed 73001 `
  --count 6 `
  --out ./artifacts/lab/generated
```

Генератор использует не произвольный C# AST, а пять ограниченных бинарных измерений:

1. исходное имя локальной переменной / переименование;
2. `var` / явный `IWebElement`;
3. file-scoped / block namespace;
4. `Tests/` / перенос migration-файла в `Specs/`;
5. прямой `By` / `using` alias.

Шесть вариантов образуют pairwise-покрытие всех пар значений этих пяти измерений. Seed детерминированно меняет дизайн, порядок и generated identifiers, не используя свободный fuzzing.

### Воспроизводимость

`generation-manifest.json` сохраняет:

- seed семейства;
- base scenario и его content hash;
- версию генератора;
- dimensions каждого варианта;
- content hash каждого generated project;
- общий `corpusFingerprint`;
- runtime/.NET/OS/architecture/culture/version environment.

Повтор одного seed должен создавать тот же `corpusFingerprint` и те же hashes проектов.

### `lab metamorphic`

После обычного `lab run` команда сравнивает варианты одного семейства:

```powershell
selenium-pw-migrator lab metamorphic `
  --manifest ./artifacts/lab/generated/generation-manifest.json `
  --run ./artifacts/lab/generated-run `
  --out ./artifacts/lab/metamorphic `
  --save-candidates ./artifacts/lab/seed-candidates
```

Проверяются:

- expected/actual status;
- сохранность source fixture;
- source/target test counts;
- TODO/unmapped/unsupported/warnings;
- quality result;
- semantic oracle result;
- family диагностик `verify-project`.

Если semantics-preserving вариант меняет результат, `lab metamorphic` возвращает exit code `10`.

### Saved seed candidates

Полезный воспроизводимый сбой автоматически копируется в отдельный candidate pack:

```text
seed-candidates/<scenario-id>/
  candidate.json
  README.md
  scenario/
    scenario.json
    ...fixture files...
```

`candidate.json` хранит seed, dimensions, expected/actual status, причины и рекомендуемый уровень регрессии `saved-seed`.

`SOURCE_INVALID` и чистые infrastructure failures не сохраняются как полезные regression seeds: это дефекты генератора/окружения, а не доказанный migration counterexample.

## Автопроверка

```powershell
.\scripts\run-lab-block7.ps1 -SkipBrowserInstall
```

Скрипт:

1. собирает решение;
2. запускает unit/contract tests генератора;
3. дважды генерирует один seed;
4. сравнивает fingerprints и hashes;
5. валидирует оба generated corpus;
6. полностью запускает оба набора через source → migrate → verify-project → runtime → oracle → quality;
7. сравнивает outcomes двух повторов;
8. запускает metamorphic analyzer;
9. при полезной регрессии сохраняет candidate seed и завершает блок красным;
10. при полном совпадении печатает acceptance message Блока 7.

Generated corpus намеренно лежит в `artifacts/`, а не смешивается с `corpus/stable`.
