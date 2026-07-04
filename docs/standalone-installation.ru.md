# Установка standalone-версии

Standalone-дистрибутив — рекомендуемый вариант для пользователей, которым нужно просто запускать CLI без установки .NET SDK или .NET Runtime.

Мигратор публикуется как self-contained bundle под конкретную платформу. Это намеренно не single-file executable: MSBuild-свойство `PublishSingleFile` оставлено выключенным (`PublishSingleFile=false`), потому что CLI использует Roslyn, project-reference DLL и resource-файлы, которые должны лежать рядом с исполняемым файлом. При этом пользователю всё равно не нужен установленный .NET, если bundle собран с `--self-contained true`.

## Релизные артефакты

Релиз содержит:

```text
selenium-pw-migrator-<version>-win-x64.zip
selenium-pw-migrator-<version>-linux-x64.tar.gz
selenium-pw-migrator-<version>-osx-x64.tar.gz
selenium-pw-migrator-<version>-osx-arm64.tar.gz
selenium-pw-migrator-win-x64.zip
selenium-pw-migrator-linux-x64.tar.gz
selenium-pw-migrator-osx-x64.tar.gz
selenium-pw-migrator-osx-arm64.tar.gz
checksums.sha256
standalone-release-manifest.json
```

Внутри каждого архива лежит исполняемый файл, зависимые DLL/resource-файлы, license/security-файлы, `README_STANDALONE.md` и `standalone-manifest.json`.

## Внутренний Nexus/static release directory

Install-скрипты не привязаны к GitHub Releases. Они поддерживают обычную HTTP-директорию, например внутренний Nexus raw repository или любой статический файловый хостинг. В директории должны лежать release-архивы и `checksums.sha256` одним плоским списком:

```text
https://nexus.example/repository/migrator/releases/v0.0.0-preview.1/
  selenium-pw-migrator-0.0.0-preview.1-win-x64.zip
  selenium-pw-migrator-0.0.0-preview.1-linux-x64.tar.gz
  selenium-pw-migrator-0.0.0-preview.1-osx-x64.tar.gz
  selenium-pw-migrator-0.0.0-preview.1-osx-arm64.tar.gz
  checksums.sha256
  standalone-release-manifest.json
  install-standalone.ps1
  install-standalone.sh
```

Используй то же значение `-Version`/`VERSION`, которое есть в именах архивов. Если в директории лежат alias-архивы без версии, например `selenium-pw-migrator-win-x64.zip`, передай `latest` или не указывай версию.

## Собрать архивы локально

```powershell
./scripts/package-standalone.ps1 `
  -Version 0.0.0-preview.1
```

Результат появится здесь:

```text
artifacts/release/
```

Для быстрой локальной проверки одного runtime:

```powershell
./scripts/publish-standalone.ps1 `
  -Runtime win-x64 `
  -Version 0.0.0-preview.1
```

## Установка на Windows

Из GitHub Releases:

```powershell
irm https://raw.githubusercontent.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/main/scripts/install-standalone.ps1 | iex
```

Для внутреннего Nexus или другой папки с релизными архивами укажи base URL явно:

```powershell
./scripts/install-standalone.ps1 `
  -Version 0.0.0-preview.1 `
  -BaseUrl https://nexus.example/repository/migrator/releases/v0.0.0-preview.1
```

По умолчанию установка идёт сюда:

```text
%USERPROFILE%\.selenium-pw-migrator\bin
```

Windows-установщик по умолчанию добавляет эту папку в user `PATH` и также подставляет её в `PATH` текущей PowerShell-сессии. Используй `-SkipUserPathUpdate`, только если нужно установить файлы без изменения `PATH`.

Проверка:

```powershell
selenium-pw-migrator --version
selenium-pw-migrator --help
```

В standalone-версии `--version` показывает канал поставки и runtime metadata:

```text
selenium-pw-migrator 0.0.0-preview.1+<commit>
commit: <commit>
build: <utc timestamp>
distribution: standalone
runtime: win-x64
self-contained: true
publish-single-file: false
framework: .NET ...
```

Если команда не находится, открой новое окно терминала и проверь `Get-Command selenium-pw-migrator -All`. Чтобы standalone выигрывал у global dotnet tool, папка standalone-установки должна быть выше `%USERPROFILE%\.dotnet\tools`.

## Установка на Linux/macOS

```bash
curl -fsSL https://raw.githubusercontent.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/main/scripts/install-standalone.sh | sh
```

Для внутренней папки с релизами:

```bash
./scripts/install-standalone.sh \
  --version 0.0.0-preview.1 \
  --base-url https://nexus.example/repository/migrator/releases/v0.0.0-preview.1
```

Для локальной smoke-проверки из скачанного архива:

```bash
./scripts/install-standalone.sh \
  --archive-path artifacts/release/selenium-pw-migrator-0.0.0-preview.1-linux-x64.tar.gz \
  --checksums-path artifacts/release/checksums.sha256
```

По умолчанию установка идёт сюда:

```text
~/.selenium-pw-migrator/bin
```

Добавить в `PATH`:

```bash
export PATH="$HOME/.selenium-pw-migrator/bin:$PATH"
```

## Ручная установка

1. Скачать архив под свою платформу.
2. Проверить `checksums.sha256`.
3. Распаковать весь архив в постоянную папку.
4. Добавить эту папку в `PATH`.
5. Держать все файлы вместе; не копировать отдельно только exe.

## Проверка архива

```powershell
./scripts/verify-standalone-package.ps1 `
  -ArchivePath artifacts/release/selenium-pw-migrator-0.0.0-preview.1-win-x64.zip `
  -ChecksumsPath artifacts/release/checksums.sha256
```

`-RunHelp` стоит использовать только для архива, который совпадает с текущей OS/runtime.

## Удаление

Windows:

```powershell
Remove-Item -Recurse -Force "$HOME/.selenium-pw-migrator"
```

Linux/macOS:

```bash
rm -rf ~/.selenium-pw-migrator
```

Если папка была добавлена в `PATH`, удали её оттуда отдельно.

## Когда лучше dotnet tool

`dotnet tool` удобнее, если проект хочет закрепить версию CLI в `.config/dotnet-tools.json` или у всех пользователей уже установлен .NET SDK. См. [Tool installation](tool-installation.md).
