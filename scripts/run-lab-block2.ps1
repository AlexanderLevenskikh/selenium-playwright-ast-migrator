param(
  [string]$Configuration = "Release",
  [int]$Port = 0
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$corpus = Join-Path $root "corpus\stable\vertical-slice"
$artifacts = Join-Path $root "artifacts\lab\block-02"
$readyFile = Join-Path $artifacts "lab-app-ready.json"
$stdout = Join-Path $artifacts "lab-app.stdout.log"
$stderr = Join-Path $artifacts "lab-app.stderr.log"
$cliDll = Join-Path $root "Migrator.Cli\bin\$Configuration\net10.0\Migrator.Cli.dll"

New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
Remove-Item $readyFile, $stdout, $stderr -Force -ErrorAction SilentlyContinue

Write-Host "Building Migrator solution..."
& dotnet build (Join-Path $root "Migrator.sln") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Migrator solution build failed with exit code $LASTEXITCODE." }

$server = $null
$previousBaseUrl = $env:MIGRATOR_LAB_APP_URL
try {
  $serverArgs = @(
    ('"' + $cliDll + '"'),
    "lab", "app", "serve",
    "--port", $Port,
    "--ready-file", ('"' + $readyFile + '"')
  )
  $server = Start-Process -FilePath "dotnet" -ArgumentList $serverArgs -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr

  $deadline = (Get-Date).AddSeconds(30)
  while (-not (Test-Path $readyFile)) {
    if ($server.HasExited) {
      $errorText = if (Test-Path $stderr) { Get-Content $stderr -Raw } else { "" }
      throw "LabApp exited before becoming ready. $errorText"
    }
    if ((Get-Date) -ge $deadline) { throw "Timed out waiting for LabApp ready file: $readyFile" }
    Start-Sleep -Milliseconds 200
  }

  $ready = Get-Content $readyFile -Raw | ConvertFrom-Json
  $env:MIGRATOR_LAB_APP_URL = $ready.baseUrl
  Write-Host "LabApp: $($ready.baseUrl)"

  $health = Invoke-WebRequest -UseBasicParsing -Uri ($ready.baseUrl + "health") -TimeoutSec 10
  if ($health.StatusCode -ne 200) { throw "LabApp health check returned $($health.StatusCode)." }

  $scenarioFiles = Get-ChildItem -Path $corpus -Filter "scenario.json" -Recurse -File | Sort-Object FullName
  foreach ($scenarioFile in $scenarioFiles) {
    $scenario = Get-Content $scenarioFile.FullName -Raw | ConvertFrom-Json
    $project = Join-Path $scenarioFile.Directory.FullName $scenario.project.entryProject
    Write-Host "Testing fixture: $($scenario.id) -> $($scenario.project.entryProject)"
    & dotnet test $project -c $Configuration --nologo
    if ($LASTEXITCODE -ne 0) { throw "Fixture failed: $($scenario.id) (exit code $LASTEXITCODE)." }
  }

  & dotnet $cliDll lab validate `
    --corpus $corpus `
    --out (Join-Path $artifacts "contracts") `
    --fail-on-planned
  if ($LASTEXITCODE -ne 0) { throw "Lab contract validation failed with exit code $LASTEXITCODE." }

  Write-Host "Block 2 passed: 7 ready fixtures, LabApp health OK, all source tests passed."
}
finally {
  if ($server -and -not $server.HasExited) {
    Stop-Process -Id $server.Id -Force -ErrorAction SilentlyContinue
    $server.WaitForExit(5000) | Out-Null
  }
  $env:MIGRATOR_LAB_APP_URL = $previousBaseUrl
}
