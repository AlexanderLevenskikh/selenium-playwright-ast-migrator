<#
.SYNOPSIS
Validates repository PowerShell and shell scripts without executing them.

.DESCRIPTION
The check parses source-of-truth scripts and workflow snippets only. Generated
release outputs and dogfood workspaces are excluded by default because they are
copies produced by packaging/smoke commands and can contain stale artifacts.

PowerShell files are parsed with the current PowerShell parser. Shell files are
checked with bash -n when bash is available. On Windows, Git Bash is preferred
and WSL's bash.exe is ignored by default because it can fail before parsing when
no WSL distro is configured.
#>
[CmdletBinding()]
param(
    [string]$Root = (Get-Location).Path,
    [switch]$IncludeGenerated,
    [switch]$SkipShell,
    [switch]$RequireShell
)

$ErrorActionPreference = "Stop"

function Resolve-RootPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

function Test-IsWindowsPlatform {
    if (Get-Variable -Name IsWindows -Scope Global -ErrorAction SilentlyContinue) {
        return [bool]$Global:IsWindows
    }

    return [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
}

function Convert-ToRepoPath([string]$Path, [string]$RootPath) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($RootPath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)

    if ([System.IO.Path].GetMethod("GetRelativePath", [type[]]@([string], [string]))) {
        $relative = [System.IO.Path]::GetRelativePath($fullRoot, $fullPath)
        return ($relative -replace '\\', '/')
    }

    # Windows PowerShell 5.1 runs on .NET Framework, where Path.GetRelativePath does not exist.
    # Fall back to Uri-based relative paths so local smoke checks still work on older shells.
    $rootWithSeparator = $fullRoot
    if (-not $rootWithSeparator.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootWithSeparator = $rootWithSeparator + [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = [System.Uri]::new($rootWithSeparator)
    $pathUri = [System.Uri]::new($fullPath)
    $relativeUri = $rootUri.MakeRelativeUri($pathUri).ToString()
    return ([System.Uri]::UnescapeDataString($relativeUri) -replace '\\', '/')
}

function Test-IsGeneratedPath([string]$RepoPath) {
    $normalized = $RepoPath.TrimStart('./')
    return (
        $normalized -like 'artifacts/*' -or
        $normalized -like 'npm/native/*' -or
        $normalized -like '.dogfood/*' -or
        $normalized -like 'bin/*' -or
        $normalized -like 'obj/*' -or
        $normalized -like 'TestResults/*' -or
        $normalized -like 'playwright-report/*'
    )
}

function Test-IsSourceScriptPath([string]$RepoPath) {
    $normalized = $RepoPath.TrimStart('./')
    return (
        $normalized -like 'scripts/*' -or
        $normalized -like 'templates/*' -or
        $normalized -like '.github/workflows/*'
    )
}

function Find-Bash {
    # On Linux/macOS, do not over-detect. GitHub Actions Ubuntu has bash in PATH,
    # but pwsh can expose native-command metadata differently across versions.
    # Returning plain `bash` is the most stable option; invocation below will fail
    # naturally if it is truly unavailable.
    if (-not (Test-IsWindowsPlatform)) {
        return 'bash'
    }

    $candidates = New-Object System.Collections.Generic.List[string]

    $programFiles = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique

    foreach ($root in $programFiles) {
        $candidates.Add((Join-Path $root 'Git/bin/bash.exe'))
        $candidates.Add((Join-Path $root 'Git/usr/bin/bash.exe'))
    }

    foreach ($command in @(Get-Command bash -All -ErrorAction SilentlyContinue)) {
        foreach ($candidate in @($command.Path, $command.Source)) {
            if (-not [string]::IsNullOrWhiteSpace($candidate)) {
                $candidates.Add($candidate)
            }
        }
    }

    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (-not (Test-Path $candidate)) { continue }

        $normalized = $candidate.Replace('/', '\')
        if ($normalized -like '*\Windows\System32\bash.exe' -or $normalized -like '*\WindowsApps\bash.exe') {
            continue
        }

        try {
            $version = & $candidate --version 2>&1 | Select-Object -First 1
            if ($LASTEXITCODE -eq 0 -and ([string]$version) -match 'bash') {
                return $candidate
            }
        }
        catch {
            continue
        }
    }

    return $null
}
$rootPath = Resolve-RootPath $Root
if (-not (Test-Path $rootPath)) {
    throw "Root path not found: $rootPath"
}

$failures = New-Object System.Collections.Generic.List[object]

$allScriptFiles = Get-ChildItem -LiteralPath $rootPath -Recurse -File |
    Where-Object { $_.Extension -in @(".ps1", ".sh") } |
    Where-Object {
        $repoPath = Convert-ToRepoPath $_.FullName $rootPath
        (Test-IsSourceScriptPath $repoPath) -and ($IncludeGenerated -or -not (Test-IsGeneratedPath $repoPath))
    } |
    Sort-Object FullName

$ps1Files = @($allScriptFiles | Where-Object { $_.Extension -ieq '.ps1' })
$shFiles = @($allScriptFiles | Where-Object { $_.Extension -ieq '.sh' })

Write-Host "Script validation root: $rootPath"
Write-Host "PowerShell scripts: $($ps1Files.Count)"
foreach ($file in $ps1Files) {
    $repoPath = Convert-ToRepoPath $file.FullName $rootPath
    Write-Host "PS1  $repoPath"

    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null

    foreach ($err in @($errors)) {
        $failures.Add([pscustomobject]@{
            Type = 'ps1'
            File = $repoPath
            Line = $err.Extent.StartLineNumber
            Column = $err.Extent.StartColumnNumber
            ErrorId = $err.ErrorId
            Message = $err.Message
            Text = $err.Extent.Text
        }) | Out-Null
    }
}

if (-not $SkipShell) {
    $bash = Find-Bash
    if (-not $bash) {
        $message = 'bash was not found. Install Git Bash or run on Linux/macOS to validate *.sh files.'
        if ($RequireShell) { throw $message }
        Write-Warning $message
    }
    else {
        Write-Host "Shell scripts: $($shFiles.Count)"
        Write-Host "Using bash: $bash"
        foreach ($file in $shFiles) {
            $repoPath = Convert-ToRepoPath $file.FullName $rootPath
            Write-Host "SH   $repoPath"

            if (Test-IsWindowsPlatform) {
                $source = [System.IO.File]::ReadAllText($file.FullName)
                $output = $source | & $bash -n -s 2>&1
            }
            else {
                $output = & $bash -n -- $file.FullName 2>&1
            }

            $exitCode = $LASTEXITCODE
            if ($exitCode -ne 0) {
                $failures.Add([pscustomobject]@{
                    Type = 'sh'
                    File = $repoPath
                    Line = ''
                    Column = ''
                    ErrorId = "bash-n-exit-$exitCode"
                    Message = ($output -join "`n")
                    Text = ''
                }) | Out-Null
            }
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "SCRIPT_VALIDATE_FAIL: $($failures.Count) issue(s)" -ForegroundColor Red
    $failures | Format-Table Type, File, Line, Column, ErrorId, Message -AutoSize
    throw 'Script validation failed.'
}

Write-Host "SCRIPT_VALIDATE_PASS: checked $($ps1Files.Count) PowerShell script(s) and $($shFiles.Count) shell script(s)." -ForegroundColor Green
