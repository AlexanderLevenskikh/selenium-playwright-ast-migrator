# Public preview flow

`public-preview-flow/v1`

Это короткий публичный маршрут для первого использования мигратора без потери safety/evidence-свойств, которые нужны для больших миграций.

## Safe-by-default story

Мигратор намеренно не продаётся как “one-shot converter”. Публичный preview-flow такой:

```text
install
  -> doctor install
  -> playground или product start
  -> pilot / wave
  -> verify и final gate
  -> current-ticket follow-up loop, если gate красный
  -> mapping research memory для шумных волн
  -> feedback-bundle/v1, если пользователь хочет прислать evidence
```

Главное обещание — **evidence before scale**:

- generated files считаются черновиками, пока `verify-project` и final gate не дали evidence;
- wave mode file-scoped и останавливается при `BLOCKED_BY_GATE`, активном `current-ticket.md`, high/critical sentinel findings или `BLOCKED_BY_WAVE_QUALITY_BUDGET`;
- шумные wave уходят в `mapping-research-memory/v1`, а не плодят ещё больше TODO;
- `feedback-bundle/v1` не включает исходники проекта по умолчанию и даёт пользователю `manifest.json` для проверки перед отправкой.

## Пятиминутный disposable path

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator playground --out playground --target-test-framework xunit --generation-policy conservative
```

Это путь, если нужно просто посмотреть CLI, отчёты и sample generated output.

## Product repository path

```bash
npm install -g selenium-pw-migrator@preview
selenium-pw-migrator doctor install
selenium-pw-migrator start --input ./SeleniumTests --agent opencode --workspace migration
```

Дальше следуй `migration/next-commands.md`. Для OpenCode workspace команда `/supervised-task waves` запускает wavefront workflow, а обычный `/supervised-task` продолжает следующий bounded action.

Для ежедневной эксплуатации см. [операторский runbook для wave mode](wave-mode-operator-runbook.ru.md). Это публичная инструкция по `BLOCKED_BY_GATE`, `current-ticket.md`, sentinel finding lifecycle, noisy waves, mapping research memory и feedback bundle handoff.

## Если run красный

Не стартуй новую wave просто потому, что код сгенерировался. Правило такое:

| Состояние | Следующее действие |
|---|---|
| `BLOCKED_BY_GATE` | Запусти или выполни `slice-gate-followups.ps1`; затем исполни `migration/current-ticket.md`. |
| `current-ticket.md` существует | Доведи ticket до `DONE`, `BLOCKED` или review-ready состояния с evidence. |
| high/critical sentinel finding открыт | Проведи его через lifecycle или явно пометь как non-agent-executable / accepted risk с evidence. |
| `BLOCKED_BY_WAVE_QUALITY_BUDGET` | Запусти `collect-mapping-research-memory.ps1` и улучшай mappings/config/recognizers перед новой wave. |
| `verify-project` упал | Изучи `project-verify-report.*` и `project-verify-harness.csproj`; generated code не считается verified. |
| пользователь хочет сообщить о плохой миграции | Запусти `create-feedback-bundle.ps1` и отправь safe zip после проверки `manifest.json`. |

## Feedback loop для улучшения мигратора

Правильный user-to-author handoff — не приватный репозиторий, а feedback bundle:

```powershell
migration/scripts/create-feedback-bundle.ps1 -Workspace migration
```

или:

```bash
migration/scripts/create-feedback-bundle.sh -Workspace migration
```

Bundle превращает реальную боль проекта в безопасные reusable evidence: TODO clusters, unresolved symbols, verify harness snapshots, wave quality budget, sentinel findings и mapping research candidates. Автор мигратора может сделать минимальный synthetic fixture, добавить regression test и улучшить мигратор без доступа к закрытому suite.

## Public preview readiness checklist

Перед публикацией или demo preview-build проверь:

- `scripts/verify-distribution-final.ps1` проходит локально;
- `README.md` и `README.ru.md` показывают путь install -> doctor -> playground/start -> wave -> feedback bundle;
- `docs/public-preview-flow.md` и `docs/wave-mode-operator-runbook.md` связаны из `docs/README.md`;
- `feedback-bundle/v1`, `mapping-research-memory/v1`, `verify-project-harness/v1` и `artifact-hygiene/v1` задокументированы;
- release notes не обещают полную автоматическую конвертацию, а обещают measurable/reviewable migration с безопасными follow-up loops.
