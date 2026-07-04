param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [string]$ReleaseDir = "artifacts/release",
    [string]$ScriptsDir = "scripts",
    [string[]]$Runtimes = @("win-x64", "linux-x64", "osx-x64", "osx-arm64"),
    [switch]$SkipInstallScripts
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$releasePath = if ([System.IO.Path]::IsPathRooted($ReleaseDir)) { $ReleaseDir } else { Join-Path $root $ReleaseDir }
$scriptsPath = if ([System.IO.Path]::IsPathRooted($ScriptsDir)) { $ScriptsDir } else { Join-Path $root $ScriptsDir }

if (-not (Test-Path $releasePath)) {
    throw "Release directory was not found: $releasePath"
}

$checksumsPath = Join-Path $releasePath "checksums.sha256"
$manifestPath = Join-Path $releasePath "standalone-release-manifest.json"

foreach ($required in @($checksumsPath, $manifestPath)) {
    if (-not (Test-Path $required)) {
        throw "Required release artifact was not found: $required"
    }
}

$checksumLines = Get-Content $checksumsPath
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json

if ($manifest.version -ne $Version) {
    throw "Manifest version mismatch. Expected '$Version', actual '$($manifest.version)'."
}

$manifestRuntimes = @($manifest.runtimes)
foreach ($runtime in $Runtimes) {
    if ($manifestRuntimes -notcontains $runtime) {
        throw "Manifest does not list runtime: $runtime"
    }

    $extension = if ($runtime.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)) { ".zip" } else { ".tar.gz" }
    $versionedName = "selenium-pw-migrator-$Version-$runtime$extension"
    $aliasName = "selenium-pw-migrator-$runtime$extension"

    foreach ($fileName in @($versionedName, $aliasName)) {
        $path = Join-Path $releasePath $fileName
        if (-not (Test-Path $path)) {
            throw "Expected release archive was not found: $path"
        }

        $line = $checksumLines | Where-Object { $_ -match "\s$([Regex]::Escape($fileName))$" } | Select-Object -First 1
        if (-not $line) {
            throw "checksums.sha256 does not contain entry for $fileName"
        }

        $expected = ($line -split "\s+")[0].ToLowerInvariant()
        $actual = (Get-FileHash -Algorithm SHA256 -Path $path).Hash.ToLowerInvariant()
        if ($expected -ne $actual) {
            throw "Checksum mismatch for $fileName. Expected $expected, actual $actual."
        }
    }

    $artifact = @($manifest.artifacts) | Where-Object { $_.runtime -eq $runtime } | Select-Object -First 1
    if (-not $artifact) {
        throw "Manifest does not contain artifact entry for runtime: $runtime"
    }
    if ($artifact.file -ne $versionedName) {
        throw "Manifest file mismatch for $runtime. Expected $versionedName, actual $($artifact.file)."
    }
    if ($artifact.alias -ne $aliasName) {
        throw "Manifest alias mismatch for $runtime. Expected $aliasName, actual $($artifact.alias)."
    }
}

if (-not $SkipInstallScripts) {
    foreach ($installer in @("install-standalone.ps1", "install-standalone.sh")) {
        $path = Join-Path $scriptsPath $installer
        if (-not (Test-Path $path)) {
            throw "Standalone install script was not found: $path"
        }
    }
}

Write-Host "Release artifacts verified: $releasePath"
