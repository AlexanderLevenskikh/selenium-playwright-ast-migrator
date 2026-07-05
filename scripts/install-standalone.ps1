param(
    [string]$Version = "latest",
    [string]$BaseUrl = "https://github.com/AlexanderLevenskikh/selenium-playwright-ast-migrator/releases/latest/download",
    [string]$InstallDir = "$HOME/.selenium-pw-migrator",
    [string]$Runtime = "",
    [string]$ArchivePath = "",
    [string]$ChecksumsPath = "",
    [switch]$AddToUserPath,
    [switch]$SkipUserPathUpdate,
    [switch]$RemoveDotnetTool,
    [switch]$Uninstall
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

# Resolve-BaseUrl supports both GitHub Releases and generic release directories such as Nexus/static HTTP folders.
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

function Normalize-PathForCompare([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return ""
    }

    return $PathValue.Trim().TrimEnd([char[]]@('\', '/'))
}


function Split-PathList([string]$PathValue) {
    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return @()
    }

    return @($PathValue -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
}

function Join-PathList([object[]]$Parts) {
    if ($null -eq $Parts -or $Parts.Count -eq 0) {
        return ""
    }

    return ($Parts -join ";")
}

function Ensure-PathEntryFirst([string]$PathValue, [string]$PathToPrepend) {
    $normalizedTarget = Normalize-PathForCompare $PathToPrepend
    if ([string]::IsNullOrWhiteSpace($normalizedTarget)) {
        return $PathValue
    }

    $parts = Split-PathList $PathValue
    $kept = New-Object System.Collections.Generic.List[string]
    foreach ($part in $parts) {
        $normalizedPart = Normalize-PathForCompare $part
        if ([string]::Equals($normalizedPart, $normalizedTarget, [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $kept.Add($part)
    }

    $ordered = New-Object System.Collections.Generic.List[string]
    $ordered.Add($PathToPrepend)
    foreach ($part in $kept) {
        $ordered.Add($part)
    }

    return (Join-PathList $ordered.ToArray())
}

function Ensure-StandalonePathPriority([string]$PathToPrepend) {
    $currentUserPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($null -eq $currentUserPath) { $currentUserPath = "" }

    $newUserPath = Ensure-PathEntryFirst -PathValue $currentUserPath -PathToPrepend $PathToPrepend
    if ($newUserPath -ne $currentUserPath) {
        [Environment]::SetEnvironmentVariable("Path", $newUserPath, "User")
        Write-Host "Prepended to user PATH: $PathToPrepend"
    }
    else {
        Write-Host "User PATH already starts with standalone bin: $PathToPrepend"
    }

    $currentProcessPath = $env:Path
    if ($null -eq $currentProcessPath) { $currentProcessPath = "" }
    $newProcessPath = Ensure-PathEntryFirst -PathValue $currentProcessPath -PathToPrepend $PathToPrepend
    if ($newProcessPath -ne $currentProcessPath) {
        $env:Path = $newProcessPath
        Write-Host "Prepended to current session PATH: $PathToPrepend"
    }
    else {
        Write-Host "Current session PATH already starts with standalone bin: $PathToPrepend"
    }
}

function Invoke-RemoveDotnetTool([string]$PackageId) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if (-not $dotnet) {
        Write-Host "dotnet was not found; skipping dotnet tool uninstall."
        return
    }

    $globalTools = dotnet tool list --global 2>$null | Out-String
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not inspect global dotnet tools; skipping dotnet tool uninstall."
        return
    }

    if ($globalTools -notmatch "(?im)^\s*$([Regex]::Escape($PackageId))\s") {
        Write-Host "Global dotnet tool was not installed: $PackageId"
        return
    }

    Write-Host "Removing global dotnet tool so standalone wins PATH resolution: $PackageId"
    dotnet tool uninstall --global $PackageId
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to uninstall global dotnet tool: $PackageId"
    }
}

function Remove-UserPathEntry([string]$PathToRemove) {
    $normalizedTarget = Normalize-PathForCompare $PathToRemove
    if ([string]::IsNullOrWhiteSpace($normalizedTarget)) {
        return
    }

    $currentPath = [Environment]::GetEnvironmentVariable("Path", "User")
    if ($null -eq $currentPath) { $currentPath = "" }

    $parts = $currentPath -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $kept = New-Object System.Collections.Generic.List[string]
    $removed = $false

    foreach ($part in $parts) {
        $normalizedPart = Normalize-PathForCompare $part
        if ([string]::Equals($normalizedPart, $normalizedTarget, [StringComparison]::OrdinalIgnoreCase)) {
            $removed = $true
            continue
        }

        $kept.Add($part)
    }

    if ($removed) {
        [Environment]::SetEnvironmentVariable("Path", ($kept -join ";"), "User")
        Write-Host "Removed from user PATH: $PathToRemove"
    }
    else {
        Write-Host "User PATH did not contain: $PathToRemove"
    }

    $processParts = $env:Path -split ";" | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $processKept = New-Object System.Collections.Generic.List[string]
    $processRemoved = $false
    foreach ($part in $processParts) {
        $normalizedPart = Normalize-PathForCompare $part
        if ([string]::Equals($normalizedPart, $normalizedTarget, [StringComparison]::OrdinalIgnoreCase)) {
            $processRemoved = $true
            continue
        }

        $processKept.Add($part)
    }

    if ($processRemoved) {
        $env:Path = $processKept -join ";"
        Write-Host "Removed from current session PATH: $PathToRemove"
    }
}

function Assert-SafeInstallDir([string]$Directory) {
    if ([string]::IsNullOrWhiteSpace($Directory)) {
        throw "InstallDir cannot be empty."
    }

    $fullPath = [System.IO.Path]::GetFullPath($Directory)
    $homePath = [System.IO.Path]::GetFullPath($HOME)
    $rootPath = [System.IO.Path]::GetPathRoot($fullPath)

    if ([string]::Equals((Normalize-PathForCompare $fullPath), (Normalize-PathForCompare $homePath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to uninstall from the user home directory: $fullPath"
    }

    if ([string]::Equals((Normalize-PathForCompare $fullPath), (Normalize-PathForCompare $rootPath), [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to uninstall from a drive root: $fullPath"
    }

    return $fullPath
}

function Invoke-UninstallStandalone([string]$Directory) {
    $fullInstallDir = Assert-SafeInstallDir $Directory
    $resolvedBinDir = Join-Path $fullInstallDir "bin"

    Remove-UserPathEntry $resolvedBinDir

    if (Test-Path $fullInstallDir) {
        Remove-Item -Recurse -Force $fullInstallDir
        Write-Host "Removed Selenium Playwright Migrator standalone installation: $fullInstallDir"
    }
    else {
        Write-Host "Standalone installation directory was already absent: $fullInstallDir"
    }

    Write-Host "Open a new terminal window if another terminal still sees selenium-pw-migrator."
}


if ($Uninstall) {
    Invoke-UninstallStandalone -Directory $InstallDir
    return
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
        try {
            Invoke-WebRequest -Uri $archiveUrl -OutFile $archivePath
        }
        catch {
            throw "Failed to download standalone archive from $archiveUrl. For private Nexus/static release folders, verify that -BaseUrl points at the directory containing $archiveName and checksums.sha256. Original error: $($_.Exception.Message)"
        }

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

    $shouldUpdateUserPath = -not $SkipUserPathUpdate
    if ($AddToUserPath) {
        # Backward-compatible explicit opt-in. PATH updates are now the default
        # for the Windows installer, but this switch remains valid for old docs/scripts.
        $shouldUpdateUserPath = $true
    }

    if ($shouldUpdateUserPath) {
        Ensure-StandalonePathPriority -PathToPrepend $binDir
        Write-Host "Open a new terminal window if another terminal does not see selenium-pw-migrator yet."
    }

    if ($RemoveDotnetTool) {
        Invoke-RemoveDotnetTool -PackageId "SeleniumPlaywrightMigrator"
    }

    Write-Host "Installed Selenium Playwright Migrator to: $binDir"
    Write-Host "Run: $exe --version"
    Write-Host "Diagnose PATH priority: Get-Command selenium-pw-migrator -All; where.exe selenium-pw-migrator"
    if ($SkipUserPathUpdate) {
        Write-Host "PATH update was skipped. To use from any terminal, add this directory to PATH: $binDir"
    }
}
finally {
    Remove-Item -Recurse -Force $temp -ErrorAction SilentlyContinue
}
