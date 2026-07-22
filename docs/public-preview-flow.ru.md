# Public preview flow

1. Установить или обновить CLI/standalone bundle.
2. Выполнить bootstrap migration kit с реальным путём к Selenium source.
3. Запустить `kit doctor`.
4. При необходимости откалибровать mappings на небольшом representative pilot.
5. Выполнить один полный `selenium-pw-migrator run` по настроенному source scope.
6. Запустить `verify-project` для generated project.
7. Проверить dashboard, TODO root causes, unsupported actions, artifact hygiene и final gate.
8. Исправить максимум одну самую выгодную первопричину и повторить полный запуск.

Pilot нужен для калибровки, а не для доказательства полного покрытия. Отсутствующий project-verification report не является PASS. Feedback bundle включает reports, mapping memory, TODO explanations и verification evidence, но по умолчанию не включает закрытый source.

Ежедневная процедура описана в [standard migration flow](standard-migration-flow.ru.md).
