param(
    [string]$Version = "latest",
    [string]$BaseUrl = "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download",
    [string]$InstallDir = "$HOME/.selenium-pw-migrator",
    [string]$Runtime = "",
    [string]$ArchivePath = "",
    [string]$ChecksumsPath = "",
    [switch]$AddToUserPath
)

$ErrorActionPreference = "Stop"

function Resolve-Runtime {
    if (-not [string]::IsNullOrWhiteSpace($Runtime)) {
        return $Runtime
    }

    $isWindows = [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
    if (-not $isWindows) {
        throw "install-standalone.ps1 is intended for Windows. Use install-standalone.sh on Linux/macOS or pass -Runtime explicitly."
    }

    $arch = $env:PROCESSOR_ARCHITECTURE
    if ($arch -eq "AMD64" -or $arch -eq "x86_64") {
        return "win-x64"
    }

    throw "Unsupported Windows architecture: $arch. Published runtime: win-x64."
}

function Resolve-BaseUrl([string]$value, [string]$version) {
    if ($version -eq "latest") {
        return $value.TrimEnd('/')
    }

    if ($value -match "/releases/latest/download/?$") {
        return ($value -replace "/releases/latest/download/?$", "/releases/download/v$version").TrimEnd('/')
    }

    return $value.TrimEnd('/')
}

function Assert-Checksum([string]$FilePath, [string]$ExpectedChecksumsPath, [string]$ExpectedArchiveName) {
    if ([string]::IsNullOrWhiteSpace($ExpectedChecksumsPath)) {
        return
    }

    if (-not (Test-Path $ExpectedChecksumsPath)) {
        throw "Checksums file was not found: $ExpectedChecksumsPath"
    }

    $actual = (Get-FileHash -Algorithm SHA256 -Path $FilePath).Hash.ToLowerInvariant()
    $line = Get-Content $ExpectedChecksumsPath | Where-Object { $_ -match "\s$([Regex]::Escape($ExpectedArchiveName))$" } | Select-Object -First 1
    if (-not $line) {
        throw "No checksum entry for $ExpectedArchiveName in $ExpectedChecksumsPath"
    }

    $expected = ($line -split "\s+")[0].ToLowerInvariant()
    if ($expected -ne $actual) {
        throw "Checksum mismatch for $ExpectedArchiveName. Expected $expected, actual $actual."
    }

    Write-Host "Checksum verified."
}

$resolvedRuntime = Resolve-Runtime
$resolvedBaseUrl = Resolve-BaseUrl $BaseUrl $Version
$archiveName = if ($Version -eq "latest") {
    "selenium-pw-migrator-$resolvedRuntime.zip"
}
else {
    "selenium-pw-migrator-$Version-$resolvedRuntime.zip"
}

$binDir = Join-Path $InstallDir "bin"
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("migrator-install-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $temp | Out-Null
New-Item -ItemType Directory -Force -Path $binDir | Out-Null

try {
    if (-not [string]::IsNullOrWhiteSpace($ArchivePath)) {
        if (-not (Test-Path $ArchivePath)) {
            throw "ArchivePath was not found: $ArchivePath"
        }

        $archivePath = (Resolve-Path $ArchivePath).Path
        $archiveName = Split-Path -Leaf $archivePath
        Write-Host "Using local archive: $archivePath"

        if (-not [string]::IsNullOrWhiteSpace($ChecksumsPath)) {
            Assert-Checksum -FilePath $archivePath -ExpectedChecksumsPath $ChecksumsPath -ExpectedArchiveName $archiveName
        }
    }
    else {
        $archiveUrl = "$resolvedBaseUrl/$archiveName"
        $archivePath = Join-Path $temp $archiveName

        Write-Host "Downloading $archiveUrl"
        Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath

        $checksumsUrl = "$resolvedBaseUrl/checksums.sha256"
        $downloadedChecksumsPath = Join-Path $temp "checksums.sha256"
        try {
            Invoke-WebRequest -Uri $checksumsUrl -OutFile $downloadedChecksumsPath
            Assert-Checksum -FilePath $archivePath -ExpectedChecksumsPath $downloadedChecksumsPath -ExpectedArchiveName $archiveName
        }
        catch {
            Write-Warning "Checksum verification skipped: $($_.Exception.Message)"
        }
    }

    $extractDir = Join-Path $temp "extract"
    Expand-Archive -Path $archivePath -DestinationPath $extractDir -Force

    Remove-Item -Recurse -Force (Join-Path $binDir "*") -ErrorAction SilentlyContinue
    Copy-Item -Path (Join-Path $extractDir "*") -Destination $binDir -Recurse -Force

    $exe = Join-Path $binDir "selenium-pw-migrator.exe"
    if (-not (Test-Path $exe)) {
        throw "Installed executable was not found: $exe"
    }

    if ($AddToUserPath) {
        $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
        if ($null -eq $currentPath) { $currentPath = "" }
        $parts = $currentPath -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        if ($parts -notcontains $binDir) {
            $newPath = if ([string]::IsNullOrWhiteSpace($currentPath)) { $binDir } else { "$currentPath;$binDir" }
            [Environment]::SetEnvironmentVariable("Path", $newPath, "User")
            Write-Host "Added to user PATH: $binDir"
            Write-Host "Open a new terminal window before using selenium-pw-migrator from PATH."
        }
    }

    Write-Host "Installed Selenium Playwright Migrator to: $binDir"
    Write-Host "Run: $exe --version"
    if (-not $AddToUserPath) {
        Write-Host "To use from any terminal, add this directory to PATH: $binDir"
    }
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
