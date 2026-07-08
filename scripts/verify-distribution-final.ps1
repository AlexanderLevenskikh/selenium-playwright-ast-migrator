<#
.SYNOPSIS
Run final distribution sanity checks before publishing or piloting the migrator.

.EXAMPLE
./scripts/verify-distribution-final.ps1

.EXAMPLE
./scripts/verify-distribution-final.ps1 -Version 0.0.0-preview.8 -RunPackagingSmoke

.EXAMPLE
./scripts/verify-distribution-final.ps1 -RunNpmRegistrySmoke -NpmPackage selenium-pw-migrator@preview -NpmRegistry https://nexus.example/repository/npm-group/ -StandaloneBaseUrl https://nexus.example/repository/migrator-releases/v0.0.0-preview.8
#>
param(
    [string]$Version = "0.0.0-preview.8",
    [string]$Configuration = "Release",
    [switch]$SkipDotnetTests,
    [switch]$RunPackagingSmoke,
    [switch]$RunNpmRegistrySmoke,
    [string]$NpmPackage = "selenium-pw-migrator@preview",
    [string]$NpmRegistry = "",
    [string]$StandaloneBaseUrl = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

function Invoke-Step([string]$Name, [scriptblock]$Body) {
    Write-Host ""
    Write-Host "== $Name =="
    & $Body
}

Invoke-Step "Git diff whitespace check" {
    git diff --check
}

Invoke-Step "Shell script executable bits" {
    $bad = git ls-files -s -- "*.sh" | ForEach-Object {
        if ($_ -match '^(\d+)\s+\S+\s+\d+\s+(.+\.sh)$' -and $Matches[1] -ne '100755') {
            "$($Matches[1]) $($Matches[2])"
        }
    }

    if ($bad) {
        $bad | ForEach-Object { Write-Host "Non-executable shell script: $_" }
        throw "One or more tracked .sh files are not 100755. Run: git ls-files -- '*.sh' | ForEach-Object { git update-index --chmod=+x `$_ }"
    }

    Write-Host "All tracked .sh files are executable in git."
}

Invoke-Step "Node syntax checks" {
    if (Get-Command node -ErrorAction SilentlyContinue) {
        node -c npm/scripts/install.js
        node -c npm/bin/selenium-pw-migrator.js
    }
    else {
        Write-Host "node not found; skipping npm wrapper syntax checks."
    }
}

Invoke-Step "Bash syntax checks" {
    if (Get-Command bash -ErrorAction SilentlyContinue) {
        $bashScripts = @(
            "scripts/pack-npm-wrapper.sh",
            "scripts/publish-npm-wrapper.sh",
            "scripts/smoke-npm-registry-install.sh",
            "scripts/diagnose-install.sh",
            "scripts/verify-distribution-final.sh"
        )

        $trackedShellScripts = git ls-files -- "*.sh"
        foreach ($script in $trackedShellScripts) {
            if ($bashScripts -notcontains $script) {
                $bashScripts += $script
            }
        }

        foreach ($script in $bashScripts) {
            bash -n $script
            if ($LASTEXITCODE -ne 0) {
                throw "bash -n failed for $script with exit code $LASTEXITCODE"
            }
        }
    }
    else {
        Write-Host "bash not found; skipping bash -n checks."
    }
}

Invoke-Step "PowerShell parser checks" {
    $parseFailures = @()
    Get-ChildItem -Path . -Recurse -File -Filter "*.ps1" |
        Where-Object {
            $normalizedPath = $_.FullName -replace '\\', '/'
            $normalizedPath -notmatch '/(bin|obj|artifacts|node_modules|\.git)/' -and
                $normalizedPath -notmatch '/migration/runs/'
        } |
        ForEach-Object {
            $tokens = $null
            $errors = $null
            [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref]$tokens, [ref]$errors) | Out-Null
            if ($errors -and $errors.Count -gt 0) {
                foreach ($error in $errors) {
                    $parseFailures += "{0}:{1}:{2}: {3}" -f $_.FullName, $error.Extent.StartLineNumber, $error.Extent.StartColumnNumber, $error.Message
                }
            }
        }

    if ($parseFailures.Count -gt 0) {
        $parseFailures | ForEach-Object { Write-Host "PowerShell parse error: $_" }
        throw "One or more PowerShell scripts have parser errors."
    }

    Write-Host "All tracked PowerShell scripts parse successfully."
}

if (-not $SkipDotnetTests) {
    Invoke-Step "dotnet test" {
        dotnet test Migrator.sln -c $Configuration
    }
}

Invoke-Step "Install diagnostics script syntax" {
    ./scripts/diagnose-install.ps1 -SkipPackageManagers
}

if ($RunPackagingSmoke) {
    Invoke-Step "Standalone + npm wrapper local packaging smoke" {
        ./scripts/package-standalone.ps1 -Version $Version -Runtimes win-x64
        ./scripts/smoke-npm-wrapper.ps1 `
            -Version $Version `
            -Runtime win-x64 `
            -ArchivePath "artifacts/release/selenium-pw-migrator-$Version-win-x64.zip" `
            -ChecksumsPath "artifacts/release/checksums.sha256"
        ./scripts/pack-npm-wrapper.ps1 -Version $Version
    }
}

if ($RunNpmRegistrySmoke) {
    Invoke-Step "Published npm registry/Nexus install smoke" {
        $args = @("-Package", $NpmPackage)
        if (-not [string]::IsNullOrWhiteSpace($NpmRegistry)) {
            $args += @("-Registry", $NpmRegistry)
        }
        if (-not [string]::IsNullOrWhiteSpace($StandaloneBaseUrl)) {
            $args += @("-StandaloneBaseUrl", $StandaloneBaseUrl)
        }
        ./scripts/smoke-npm-registry-install.ps1 @args
    }
}

Write-Host ""
Write-Host "Final distribution verification completed."
