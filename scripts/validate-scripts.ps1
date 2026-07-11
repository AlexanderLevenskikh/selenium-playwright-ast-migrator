<#
.SYNOPSIS
Validates repository PowerShell and shell scripts without executing them.

.DESCRIPTION
The check parses source-of-truth scripts and workflow snippets. Generated
release outputs and dogfood workspaces are excluded by default. Pass -Workspace
to additionally validate the scripts currently installed in one or more migration
workspaces; this catches stale workspace copies after a tool update.

PowerShell files are parsed with the current PowerShell parser. Shell files are
checked with bash -n when bash is available. On Windows, Git Bash is preferred
and WSL's bash.exe is ignored by default because it can fail before parsing when
no WSL distro is configured.
#>
[CmdletBinding()]
param(
    [string]$Root = (Get-Location).Path,
    [string[]]$Workspace = @(),
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
    $separatorChars = [char[]]@(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar
    )
    $fullRoot = [System.IO.Path]::GetFullPath($RootPath).TrimEnd($separatorChars)

    # Windows PowerShell 5.1 runs on .NET Framework, where Path.GetRelativePath does not exist.
    # Use the Uri implementation on every supported PowerShell version. Besides being portable,
    # this avoids overload-binding differences between Windows PowerShell 5.1 and PowerShell 7.
    $directorySeparator = [string][System.IO.Path]::DirectorySeparatorChar
    $rootWithSeparator = $fullRoot
    if (-not $rootWithSeparator.EndsWith($directorySeparator, [System.StringComparison]::Ordinal)) {
        $rootWithSeparator = $rootWithSeparator + $directorySeparator
    }

    $rootUri = New-Object System.Uri -ArgumentList $rootWithSeparator
    $pathUri = New-Object System.Uri -ArgumentList $fullPath
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

$sourceScriptFiles = @(Get-ChildItem -LiteralPath $rootPath -Recurse -File |
    Where-Object { $_.Extension -in @(".ps1", ".sh") } |
    Where-Object {
        $repoPath = Convert-ToRepoPath $_.FullName $rootPath
        (Test-IsSourceScriptPath $repoPath) -and ($IncludeGenerated -or -not (Test-IsGeneratedPath $repoPath))
    })

# Plain PowerShell arrays are intentional here. Windows PowerShell 5.1 can throw
# a non-diagnostic "Argument types do not match" when generic lists are combined
# with array-subexpressions and pipeline enumeration.
$workspacePaths = @()
foreach ($workspaceValue in $Workspace) {
    if ([string]::IsNullOrWhiteSpace($workspaceValue)) { continue }
    $resolvedWorkspace = Resolve-RootPath $workspaceValue
    if (-not (Test-Path $resolvedWorkspace)) { throw "Workspace path not found: $resolvedWorkspace" }
    $workspacePaths += $resolvedWorkspace
}

if ($workspacePaths.Count -eq 0) {
    $autoWorkspace = Join-Path $rootPath "migration"
    if (Test-Path (Join-Path $autoWorkspace "scripts")) {
        $workspacePaths += [System.IO.Path]::GetFullPath($autoWorkspace)
    }
}

$workspaceScriptFiles = @()
foreach ($workspacePath in ($workspacePaths | Sort-Object -Unique)) {
    $scriptsPath = Join-Path $workspacePath "scripts"
    if (-not (Test-Path $scriptsPath)) {
        Write-Warning "Workspace scripts directory not found: $scriptsPath"
        continue
    }
    $workspaceScriptFiles += @(Get-ChildItem -LiteralPath $scriptsPath -Recurse -File |
        Where-Object { $_.Extension -in @(".ps1", ".sh") })
}

$allScriptFiles = @()
$allScriptFiles += $sourceScriptFiles
$allScriptFiles += $workspaceScriptFiles
$allScriptFiles = @($allScriptFiles | Sort-Object FullName -Unique)
$ps1Files = @($allScriptFiles | Where-Object { $_.Extension -ieq '.ps1' })
$shFiles = @($allScriptFiles | Where-Object { $_.Extension -ieq '.sh' })

function Get-DisplayPath([string]$Path) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    foreach ($workspacePath in ($workspacePaths | Sort-Object Length -Descending)) {
        $workspacePrefix = $workspacePath.TrimEnd([char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)) + [System.IO.Path]::DirectorySeparatorChar
        if ($fullPath.StartsWith($workspacePrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
            return "workspace:" + (Convert-ToRepoPath $fullPath $workspacePath)
        }
    }
    return Convert-ToRepoPath $fullPath $rootPath
}

Write-Host "Script validation root: $rootPath"
foreach ($workspacePath in ($workspacePaths | Sort-Object -Unique)) { Write-Host "Installed workspace: $workspacePath" }
Write-Host "PowerShell scripts: $($ps1Files.Count)"
foreach ($file in $ps1Files) {
    $repoPath = Get-DisplayPath $file.FullName
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
            $repoPath = Get-DisplayPath $file.FullName
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
