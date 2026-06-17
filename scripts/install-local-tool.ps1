param(
    [string]$Version = "0.6.0-preview.1",
    [string]$PackageId = "SeleniumPlaywrightAstMigrator",
    [string]$PackageDirectory = "artifacts/nuget"
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root $PackageDirectory

if (-not (Test-Path $source)) {
    throw "Package source not found: $source. Run scripts/pack-tool.ps1 first."
}

Push-Location $root
try {
    if (-not (Test-Path ".config/dotnet-tools.json")) {
        dotnet new tool-manifest
    }

    $installed = dotnet tool list | Select-String $PackageId
    if ($installed) {
        dotnet tool update $PackageId --version $Version --add-source $source
    }
    else {
        dotnet tool install $PackageId --version $Version --add-source $source
    }

    dotnet tool run selenium-pw-migrator -- --help
}
finally {
    Pop-Location
}
