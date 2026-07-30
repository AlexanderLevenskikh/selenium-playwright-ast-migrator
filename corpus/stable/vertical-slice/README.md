# Migrator Lab vertical slice

Этот каталог содержит семь детерминированных Selenium/NUnit-проектов первого
вертикального среза полигона.

- `scenario.json` задаёт ожидаемый статус, oracle, бюджеты качества и точные файлы;
- `source.migrationFiles` отделяет код, который должен получить мигратор, от browser
  bootstrap исходного runtime-проекта;
- `implementation.contentHash` защищает готовую фикстуру от незаметного изменения;
- все browser-тесты используют один `LabApp`, запускаемый текущим CLI;
- `lab run` копирует fixture, проверяет restore/build/test и запускает существующий
  migration pipeline только на `source.migrationFiles`.

Проверка на Windows:

```powershell
.\scripts\run-lab-block3.ps1
```

Требуются .NET SDK 10 и Google Chrome. Обычно Selenium Manager сам получает подходящий
ChromeDriver. В изолированной сети можно заранее указать:

```powershell
$env:MIGRATOR_LAB_CHROMEDRIVER_DIRECTORY = "C:\tools\chromedriver"
$env:MIGRATOR_LAB_CHROME_BINARY = "C:\Program Files\Google\Chrome\Application\chrome.exe"
.\scripts\run-lab-block3.ps1
```
