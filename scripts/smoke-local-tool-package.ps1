param(
    [string]$Version = "0.6.0-preview.1",
    [string]$PackageId = "SeleniumPlaywrightMigrator",
    [string]$PackageDirectory = "artifacts/nuget",
    [Alias("Input")]
    [string]$SmokeInput = "Migrator.Tests/TestFiles"
)

$ErrorActionPreference = "Stop"

function Invoke-DotnetChecked {
    param([Parameter(ValueFromRemainingArguments = $true)] [string[]]$DotnetArgs)

    & dotnet @DotnetArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($DotnetArgs -join ' ') failed with exit code $LASTEXITCODE"
    }
}

$root = Split-Path -Parent $PSScriptRoot
$source = if ([System.IO.Path]::IsPathRooted($PackageDirectory)) { $PackageDirectory } else { Join-Path $root $PackageDirectory }
$inputPath = if ([System.IO.Path]::IsPathRooted($SmokeInput)) { $SmokeInput } else { Join-Path $root $SmokeInput }

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
        Invoke-DotnetChecked new tool-manifest

        Invoke-DotnetChecked tool install $PackageId --version $Version --add-source $source --ignore-failed-sources

        Invoke-DotnetChecked selenium-pw-migrator --help

        $doctorOut = Join-Path $temp "doctor"
        Invoke-DotnetChecked selenium-pw-migrator --mode doctor --input $inputPath --out $doctorOut --format both

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
