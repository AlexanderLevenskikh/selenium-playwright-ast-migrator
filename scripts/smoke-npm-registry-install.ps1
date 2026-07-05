<#
.SYNOPSIS
Runs an isolated npm registry install smoke for selenium-pw-migrator.

.EXAMPLE
./scripts/smoke-npm-registry-install.ps1 -Package selenium-pw-migrator@preview

.EXAMPLE
./scripts/smoke-npm-registry-install.ps1 `
  -Package selenium-pw-migrator@0.0.0-preview.8 `
  -Registry https://nexus.example/repository/npm-group/ `
  -StandaloneBaseUrl https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
#>
param(
    [string]$Package = "selenium-pw-migrator@preview",
    [string]$Registry = "https://registry.npmjs.org/",
    [string]$StandaloneBaseUrl = "",
    [string]$Runtime = "",
    [string[]]$CliArgs = @("--version"),
    [switch]$KeepTemp
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw "npm is required for registry install smoke."
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("selenium-pw-migrator-npm-smoke-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force $tempRoot | Out-Null

try {
    Push-Location $tempRoot

    Write-Host "npm registry smoke directory: $tempRoot"
    Write-Host "Package: $Package"
    Write-Host "Registry: $Registry"

    npm init -y | Out-Null

    $installArgs = @("install", $Package, "--registry=$Registry")
    if (-not [string]::IsNullOrWhiteSpace($StandaloneBaseUrl)) {
        $installArgs += "--selenium-pw-migrator-base-url=$StandaloneBaseUrl"
        Write-Host "Standalone base URL: $StandaloneBaseUrl"
    }
    if (-not [string]::IsNullOrWhiteSpace($Runtime)) {
        $installArgs += "--selenium-pw-migrator-runtime=$Runtime"
        Write-Host "Runtime override: $Runtime"
    }

    & npm @installArgs
    if ($LASTEXITCODE -ne 0) {
        throw "npm install failed with exit code $LASTEXITCODE"
    }

    $binName = if ($IsWindows -or $env:OS -eq "Windows_NT") { "selenium-pw-migrator.cmd" } else { "selenium-pw-migrator" }
    $binPath = Join-Path $tempRoot (Join-Path "node_modules/.bin" $binName)
    if (-not (Test-Path $binPath)) {
        throw "Installed npm wrapper binary was not found: $binPath"
    }

    Write-Host "Running: $binPath $($CliArgs -join ' ')"
    & $binPath @CliArgs
    if ($LASTEXITCODE -ne 0) {
        throw "selenium-pw-migrator smoke command failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location -ErrorAction SilentlyContinue
    if ($KeepTemp) {
        Write-Host "Keeping npm registry smoke directory: $tempRoot"
    } else {
        Remove-Item -Recurse -Force $tempRoot -ErrorAction SilentlyContinue
    }
}
