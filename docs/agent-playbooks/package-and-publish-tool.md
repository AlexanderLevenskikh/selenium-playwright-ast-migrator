# Agent playbook: package and publish the migrator tool

Используй этот playbook только если пользователь явно попросил упаковать/опубликовать мигратор.

## Запрещено

- Не публикуй пакет без явного разрешения пользователя.
- Не создавай и не записывай реальные токены/API keys в файлы.
- Не коммить credentials.
- Не меняй production/test projects.

## Проверка перед pack

```powershell
dotnet test --no-restore
```

## Pack

```powershell
.\scripts\pack-tool.ps1 -Version 0.6.0-preview.1
```

## Local install smoke

```powershell
.\scripts\install-local-tool.ps1 -Version 0.6.0-preview.1
```

Проверка:

```powershell
dotnet tool run selenium-pw-migrator -- --help
```

## Publish

Публикуй только после подтверждения пользователя:

```powershell
.\scripts\push-tool.ps1 -Version 0.6.0-preview.1 -Source https://api.nuget.org/v3/index.json -ApiKey $env:NUGET_API_KEY
```

## Финальный отчёт

Пиши на русском:

```text
## Упаковка завершена
- PackageId:
- Version:
- nupkg:
- local install smoke:
- publish status:
- next step:
```
