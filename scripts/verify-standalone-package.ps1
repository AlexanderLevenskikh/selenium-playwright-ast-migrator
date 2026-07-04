param(
    [Parameter(Mandatory=$true)]
    [string]$ArchivePath,
    [string]$ChecksumsPath = "",
    [switch]$RunHelp
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ArchivePath)) {
    throw "Standalone archive was not found: $ArchivePath"
}

$archive = Get-Item $ArchivePath

if (-not [string]::IsNullOrWhiteSpace($ChecksumsPath)) {
    if (-not (Test-Path $ChecksumsPath)) {
        throw "Checksums file was not found: $ChecksumsPath"
    }

    $actual = (Get-FileHash -Algorithm SHA256 -Path $archive.FullName).Hash.ToLowerInvariant()
    $line = Get-Content $ChecksumsPath | Where-Object { $_ -match "\s$([Regex]::Escape($archive.Name))$" } | Select-Object -First 1
    if (-not $line) {
        throw "No checksum entry found for $($archive.Name) in $ChecksumsPath"
    }

    $expected = ($line -split "\s+")[0].ToLowerInvariant()
    if ($expected -ne $actual) {
        throw "Checksum mismatch for $($archive.Name). Expected $expected, actual $actual."
    }
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("migrator-standalone-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
try {
    if ($archive.Name.EndsWith(".zip", [StringComparison]::OrdinalIgnoreCase)) {
        Expand-Archive -Path $archive.FullName -DestinationPath $temp -Force
    }
    elseif ($archive.Name.EndsWith(".tar.gz", [StringComparison]::OrdinalIgnoreCase)) {
        $tar = Get-Command tar -ErrorAction SilentlyContinue
        if (-not $tar) {
            throw "tar command was not found. It is required to verify $($archive.Name)."
        }
        & tar -xzf $archive.FullName -C $temp
        if ($LASTEXITCODE -ne 0) {
            throw "tar extract failed with exit code $LASTEXITCODE"
        }
    }
    else {
        throw "Unsupported archive extension: $($archive.Name)"
    }

    foreach ($required in @("README_STANDALONE.md", "standalone-manifest.json")) {
        if (-not (Test-Path (Join-Path $temp $required))) {
            throw "Archive does not contain required file: $required"
        }
    }

    $candidateExe = Join-Path $temp "selenium-pw-migrator.exe"
    $candidateAppHost = Join-Path $temp "selenium-pw-migrator"
    $candidateDll = Join-Path $temp "Migrator.Cli.dll"

    if (-not (Test-Path $candidateExe) -and -not (Test-Path $candidateAppHost) -and -not (Test-Path $candidateDll)) {
        throw "Archive does not contain selenium-pw-migrator executable or Migrator.Cli.dll."
    }

    if ($RunHelp) {
        if (Test-Path $candidateExe) {
            & $candidateExe --help | Out-Host
        }
        elseif (Test-Path $candidateAppHost) {
            if (-not $IsWindows) {
                chmod +x $candidateAppHost
            }
            & $candidateAppHost --help | Out-Host
        }
        else {
            dotnet $candidateDll --help | Out-Host
        }

        if ($LASTEXITCODE -ne 0) {
            throw "Standalone help smoke failed with exit code $LASTEXITCODE"
        }
    }
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}

Write-Host "Standalone archive verified: $($archive.FullName)"
