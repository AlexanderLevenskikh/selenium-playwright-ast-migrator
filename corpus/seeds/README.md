# Saved regression seeds

Эта папка предназначена только для **проверенных и вручную принятых** seed-кейсов.

`lab metamorphic --save-candidates ...` сначала пишет кандидатов в `artifacts/lab/...`; не коммитьте каждый автоматически найденный вариант сюда.

Seed переводится в постоянную регрессию только после triage:

1. source fixture валиден и стабильно проходит;
2. повтор того же seed воспроизводит тот же migration outcome;
3. failure не является чистой infrastructure problem;
4. сохранён минимальный понятный repro или принято решение оставить его как saved seed;
5. указан regression level: unit-test, project fixture или saved seed.

Reducer и автоматизация promotion относятся к Блоку 8.

После Блока 8 candidate можно подготовить к принятию командой:

```powershell
selenium-pw-migrator lab reduce --candidate <candidate> --out ./artifacts/lab/reduced
selenium-pw-migrator lab promote --repro ./artifacts/lab/reduced/scenario --level saved-seed --out ./artifacts/lab/promoted
```

`lab promote` создаёт reviewed artifact; перенос в `corpus/seeds` остаётся явным действием владельца после triage.
