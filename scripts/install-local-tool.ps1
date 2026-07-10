param(
    [string]$Version = "",
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

function Unblock-PathIfSupported {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    if (-not (Test-Path $Path)) { return }
    if (-not (Get-Command Unblock-File -ErrorAction SilentlyContinue)) { return }

    try {
        Unblock-File -Path $Path -ErrorAction SilentlyContinue
    }
    catch {
        Write-Warning "Could not unblock ${Path}: $($_.Exception.Message)"
    }
}

function Unblock-LocalToolInputs {
    param([string]$PackageSource)

    Unblock-PathIfSupported (Join-Path $root "dotnet-tools.json")
    Unblock-PathIfSupported (Join-Path $root ".config/dotnet-tools.json")

    if (Test-Path $PackageSource) {
        Get-ChildItem -Path $PackageSource -Filter "*.nupkg" -File -ErrorAction SilentlyContinue | ForEach-Object {
            Unblock-PathIfSupported $_.FullName
        }
    }
}

function Test-ToolManifestExists {
    return ((Test-Path ".config/dotnet-tools.json") -or (Test-Path "dotnet-tools.json"))
}

function Get-LocalPackageVersion {
    param([System.IO.FileInfo]$PackageFile)

    $escapedId = [regex]::Escape($PackageId)
    $pattern = "^$escapedId\.(?<version>.+)\.nupkg$"
    $match = [regex]::Match($PackageFile.Name, $pattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $match.Success) { return $null }
    return $match.Groups["version"].Value
}

function Get-LocalToolPackages {
    if (-not (Test-Path $source)) { return @() }

    return @(Get-ChildItem -Path $source -Filter "*.nupkg" -File |
        ForEach-Object {
            $packageVersion = Get-LocalPackageVersion -PackageFile $_
            if (-not [string]::IsNullOrWhiteSpace($packageVersion)) {
                [pscustomobject]@{
                    Version = $packageVersion
                    Path = $_.FullName
                    LastWriteTimeUtc = $_.LastWriteTimeUtc
                }
            }
        })
}

function Resolve-LocalPackageVersion {
    $packages = Get-LocalToolPackages
    if ($packages.Count -eq 0) {
        throw "No local $PackageId packages found in $source. Run scripts/pack-tool.ps1 -Version <version> first."
    }

    if (-not [string]::IsNullOrWhiteSpace($Version)) {
        $match = @($packages | Where-Object { $_.Version -eq $Version })
        if ($match.Count -gt 0) { return $Version }

        $available = @($packages | Sort-Object Version | ForEach-Object { $_.Version }) -join ", "
        throw "Local package $PackageId version $Version was not found in $source. Available local version(s): $available. Run scripts/pack-tool.ps1 -Version $Version first, or omit -Version to install the newest local package."
    }

    $latest = $packages | Sort-Object LastWriteTimeUtc, Version -Descending | Select-Object -First 1
    Write-Host "No -Version supplied; using newest local package: $($latest.Version)"
    return $latest.Version
}

$root = Split-Path -Parent $PSScriptRoot
$source = Join-Path $root $PackageDirectory

if (-not (Test-Path $source)) {
    throw "Package source not found: $source. Run scripts/pack-tool.ps1 first."
}

$resolvedVersion = Resolve-LocalPackageVersion
Unblock-LocalToolInputs -PackageSource $source

Push-Location $root
try {
    if (-not (Test-ToolManifestExists)) {
        Invoke-DotnetChecked new tool-manifest
    }

    $installed = dotnet tool list | Select-String $PackageId
    if ($installed) {
        Invoke-DotnetChecked tool update $PackageId --version $resolvedVersion --add-source $source
    }
    else {
        Invoke-DotnetChecked tool install $PackageId --version $resolvedVersion --add-source $source
    }

    Invoke-DotnetChecked tool run selenium-pw-migrator -- --help
}
finally {
    Pop-Location
}
