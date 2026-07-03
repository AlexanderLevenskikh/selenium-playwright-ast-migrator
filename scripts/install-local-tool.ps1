param(
    [string]$Version = "0.0.0",
    [string]$PackageId = "SeleniumPlaywrightMigrator",
    [string]$PackageDirectory = "artifacts/nuget"
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
$source = Join-Path $root $PackageDirectory

if (-not (Test-Path $source)) {
    throw "Package source not found: $source. Run scripts/pack-tool.ps1 first."
}

Push-Location $root
try {
    if (-not (Test-Path ".config/dotnet-tools.json")) {
        Invoke-DotnetChecked new tool-manifest
    }

    $installed = dotnet tool list | Select-String $PackageId
    if ($installed) {
        Invoke-DotnetChecked tool update $PackageId --version $Version --add-source $source
    }
    else {
        Invoke-DotnetChecked tool install $PackageId --version $Version --add-source $source
    }

    Invoke-DotnetChecked tool run selenium-pw-migrator -- --help
}
finally {
    Pop-Location
}
