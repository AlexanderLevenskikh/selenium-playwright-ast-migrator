[CmdletBinding()]
param(
    [string]$Root = '.',
    [string]$Configuration = 'Release',
    [ValidateSet('Unit', 'Contract', 'Scenario', 'E2E', 'All')]
    [string]$Layer = 'Unit',
    [string]$Output = 'artifacts/test-layers',
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$rootPath = (Resolve-Path $Root).Path
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $rootPath $Output }
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$project = Join-Path $rootPath 'Migrator.Tests/Migrator.Tests.csproj'

function Get-PowerShellExecutable {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($pwsh) { return $pwsh.Source }

    try {
        $currentProcessPath = (Get-Process -Id $PID -ErrorAction Stop).Path
        if ($currentProcessPath -and (Test-Path $currentProcessPath)) { return $currentProcessPath }
    }
    catch { }

    $windowsPowerShell = Get-Command powershell.exe -ErrorAction SilentlyContinue
    if ($windowsPowerShell) { return $windowsPowerShell.Source }

    $windowsPowerShell = Get-Command powershell -ErrorAction SilentlyContinue
    if ($windowsPowerShell) { return $windowsPowerShell.Source }

    throw 'PowerShell host executable was not found. Install PowerShell 7 or run this script from Windows PowerShell.'
}

function Get-TrxTestCount([string]$TrxPath) {
    if (-not (Test-Path $TrxPath)) {
        throw "Test result file was not created: $TrxPath"
    }

    [xml]$trx = Get-Content -Raw $TrxPath
    $namespaceManager = New-Object Xml.XmlNamespaceManager($trx.NameTable)
    $namespaceManager.AddNamespace('t', 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010')
    return @($trx.SelectNodes('//t:UnitTestResult', $namespaceManager)).Count
}

function Invoke-TestFilter {
    param(
        [string]$Name,
        [string]$Filter,
        [int]$MinimumTests = 1
    )

    $resultPath = Join-Path $outputPath $Name.ToLowerInvariant()
    New-Item -ItemType Directory -Force -Path $resultPath | Out-Null
    $trxPath = Join-Path $resultPath "$Name.trx"
    Remove-Item $trxPath -Force -ErrorAction SilentlyContinue

    $args = @(
        'test', $project,
        '--configuration', $Configuration,
        '--no-restore',
        '--filter', $Filter,
        '--results-directory', $resultPath,
        '--logger', "trx;LogFileName=$Name.trx"
    )
    if ($NoBuild) { $args += '--no-build' }

    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "$Name test layer failed with exit code $LASTEXITCODE." }

    $testCount = Get-TrxTestCount $trxPath
    if ($testCount -lt $MinimumTests) {
        $buildHint = if ($NoBuild) { ' The --no-build assembly may be stale; rebuild Migrator.Tests and retry.' } else { '' }
        throw "$Name test layer discovered $testCount test(s), expected at least $MinimumTests.$buildHint"
    }

    Write-Host "TEST_LAYER_${Name}_COUNT=$testCount"
}

function Invoke-E2E {
    $cliDll = Join-Path $rootPath "Migrator.Cli/bin/$Configuration/net10.0/Migrator.Cli.dll"
    if (-not (Test-Path $cliDll)) {
        $cliDll = Get-ChildItem (Join-Path $rootPath 'Migrator.Cli/bin') -Filter Migrator.Cli.dll -Recurse -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }

    $smokeOutput = Join-Path $outputPath 'e2e-validation-host'
    $powerShellExecutable = Get-PowerShellExecutable
    & $powerShellExecutable -NoProfile -ExecutionPolicy Bypass -File (Join-Path $rootPath 'scripts/run-validation-host-smoke.ps1') `
        -Root $rootPath -Configuration $Configuration -Output $smokeOutput -CliDll $cliDll
    if ($LASTEXITCODE -ne 0) { throw "E2E validation-host smoke failed with exit code $LASTEXITCODE." }
}

# xUnit/VSTest 2.5.x can occasionally omit a class-level custom trait from filtered
# discovery on an already-built assembly. The Unit layer therefore combines the
# explicit trait with the *UnitTests naming convention and refuses a zero-test pass.
$unitFilter = 'Layer=Unit|FullyQualifiedName~UnitTests'

switch ($Layer) {
    'Unit' { Invoke-TestFilter 'Unit' $unitFilter }
    'Contract' { Invoke-TestFilter 'Contract' 'Layer=Contract' }
    'Scenario' { Invoke-TestFilter 'Scenario' 'Layer=Scenario' }
    'E2E' { Invoke-E2E }
    'All' {
        Invoke-TestFilter 'Unit' $unitFilter
        Invoke-TestFilter 'Contract' 'Layer=Contract'
        Invoke-TestFilter 'Scenario' 'Layer=Scenario'
        Invoke-E2E
    }
}

Write-Host "TEST_LAYER_$($Layer.ToUpperInvariant())_PASS"
Write-Host "Artifacts: $outputPath"
exit 0
