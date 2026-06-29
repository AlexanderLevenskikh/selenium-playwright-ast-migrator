param(
    [string]$Version = "0.6.0-preview.1",
    [string]$PackageId = "SeleniumPlaywrightAstMigrator",
    [string]$PackageDirectory = "artifacts/nuget",
    [string]$Input = "Migrator.Tests/TestFiles"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$source = if ([System.IO.Path]::IsPathRooted($PackageDirectory)) { $PackageDirectory } else { Join-Path $root $PackageDirectory }
$inputPath = if ([System.IO.Path]::IsPathRooted($Input)) { $Input } else { Join-Path $root $Input }

if (-not (Test-Path $source)) {
    throw "Package source not found: $source. Run scripts/pack-tool.ps1 first."
}

if (-not (Test-Path $inputPath)) {
    throw "Smoke input path not found: $inputPath"
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("selenium-pw-migrator-tool-smoke-" + [System.Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null

try {
    Push-Location $temp
    try {
        dotnet new tool-manifest | Out-Host
        dotnet tool install $PackageId --version $Version --add-source $source --ignore-failed-sources | Out-Host

        dotnet tool run selenium-pw-migrator -- --help | Out-Host

        $doctorOut = Join-Path $temp "doctor"
        dotnet tool run selenium-pw-migrator -- --mode doctor --input $inputPath --out $doctorOut --format both | Out-Host

        $doctorReport = Join-Path $doctorOut "doctor-report.md"
        if (-not (Test-Path $doctorReport)) {
            throw "Doctor smoke did not produce expected report: $doctorReport"
        }
    }
    finally {
        Pop-Location
    }

    Write-Host "Local dotnet-tool smoke passed for $PackageId $Version"
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
