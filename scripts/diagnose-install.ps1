<#
.SYNOPSIS
Diagnose which selenium-pw-migrator installation the current shell resolves.

.DESCRIPTION
The migrator can be installed as a standalone CLI, npm wrapper, dotnet global tool,
or dotnet local tool. Do not start diagnostics with dotnet tool list only: first
inspect PATH resolution and then inspect package-manager state.

.EXAMPLE
./scripts/diagnose-install.ps1

.EXAMPLE
./scripts/diagnose-install.ps1 -CommandName selenium-pw-migrator
#>
param(
    [string]$CommandName = "selenium-pw-migrator",
    [switch]$SkipPackageManagers
)

$ErrorActionPreference = "Continue"

function Write-Section([string]$Title) {
    Write-Host ""
    Write-Host "== $Title =="
}

function Invoke-BestEffort([string]$Label, [scriptblock]$Body) {
    Write-Host ""
    Write-Host "> $Label"
    try {
        & $Body
    }
    catch {
        Write-Host "WARN: $($_.Exception.Message)"
    }
}

Write-Host "Selenium Playwright Migrator installation diagnostics"
Write-Host "Command: $CommandName"
Write-Host "Rule: inspect actual PATH resolution before package-manager lists."

Write-Section "Resolved commands"
Invoke-BestEffort "Get-Command $CommandName -All" {
    Get-Command $CommandName -All | Format-Table -AutoSize CommandType, Name, Source, Version
}

if ($IsWindows -or $env:OS -eq "Windows_NT") {
    Invoke-BestEffort "where.exe $CommandName" {
        where.exe $CommandName
    }
}
else {
    Invoke-BestEffort "command -v $CommandName" {
        command -v $CommandName
    }
    Invoke-BestEffort "which -a $CommandName" {
        which -a $CommandName
    }
}

Write-Section "Version metadata"
Invoke-BestEffort "$CommandName --version" {
    & $CommandName --version
}

if (-not $SkipPackageManagers) {
    Write-Section "dotnet tool state"
    Invoke-BestEffort "dotnet tool list --global" {
        dotnet tool list --global
    }
    Invoke-BestEffort "dotnet tool list --local" {
        dotnet tool list --local
    }

    Write-Section "npm wrapper state"
    Invoke-BestEffort "npm list -g selenium-pw-migrator --depth=0" {
        npm list -g selenium-pw-migrator --depth=0
    }
    Invoke-BestEffort "npm config get registry" {
        npm config get registry
    }
    Invoke-BestEffort "npm config get prefix" {
        npm config get prefix
    }
    Invoke-BestEffort "npm config get selenium-pw-migrator-base-url" {
        npm config get selenium-pw-migrator-base-url
    }
}

Write-Section "How to read this output"
Write-Host "The first resolved command is what this shell will run."
Write-Host "Use --version metadata to identify the distribution: standalone, npm wrapper payload, or dotnet tool."
Write-Host "If multiple commands exist, fix PATH priority before reinstalling anything."
