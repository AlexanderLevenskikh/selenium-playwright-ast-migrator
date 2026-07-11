[CmdletBinding()]
param(
    [string]$Workspace = "migration",
    [switch]$SkipShell,
    [switch]$RequireShell
)

$ErrorActionPreference = "Stop"

function Resolve-FullPath([string]$Path) {
    if ([System.IO.Path]::IsPathRooted($Path)) { return [System.IO.Path]::GetFullPath($Path) }
    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

function Test-IsWindowsPlatform {
    if (Get-Variable -Name IsWindows -Scope Global -ErrorAction SilentlyContinue) { return [bool]$Global:IsWindows }
    return [System.Environment]::OSVersion.Platform -eq [System.PlatformID]::Win32NT
}

function Convert-ToWorkspacePath([string]$Path, [string]$WorkspacePath) {
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($WorkspacePath).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    if ([System.IO.Path].GetMethod("GetRelativePath", [type[]]@([string], [string]))) {
        return ([System.IO.Path]::GetRelativePath($fullRoot, $fullPath) -replace '\\', '/')
    }
    $rootWithSeparator = $fullRoot
    if (-not $rootWithSeparator.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootWithSeparator += [System.IO.Path]::DirectorySeparatorChar
    }
    $relative = ([System.Uri]::new($rootWithSeparator)).MakeRelativeUri([System.Uri]::new($fullPath)).ToString()
    return ([System.Uri]::UnescapeDataString($relative) -replace '\\', '/')
}

function Find-Bash {
    if (-not (Test-IsWindowsPlatform)) { return "bash" }
    $candidates = New-Object System.Collections.Generic.List[string]
    $programFiles = @($env:ProgramFiles, ${env:ProgramFiles(x86)}, $env:ProgramW6432) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique
    foreach ($root in $programFiles) {
        $candidates.Add((Join-Path $root "Git/bin/bash.exe")) | Out-Null
        $candidates.Add((Join-Path $root "Git/usr/bin/bash.exe")) | Out-Null
    }
    foreach ($command in @(Get-Command bash -All -ErrorAction SilentlyContinue)) {
        foreach ($candidate in @($command.Path, $command.Source)) {
            if (-not [string]::IsNullOrWhiteSpace($candidate)) { $candidates.Add($candidate) | Out-Null }
        }
    }
    foreach ($candidate in @($candidates | Select-Object -Unique)) {
        if (-not (Test-Path $candidate)) { continue }
        $normalized = $candidate.Replace('/', '\')
        if ($normalized -like '*\Windows\System32\bash.exe' -or $normalized -like '*\WindowsApps\bash.exe') { continue }
        try {
            $version = & $candidate --version 2>&1 | Select-Object -First 1
            if ($LASTEXITCODE -eq 0 -and ([string]$version) -match 'bash') { return $candidate }
        }
        catch { continue }
    }
    return $null
}

$workspacePath = Resolve-FullPath $Workspace
$scriptsPath = Join-Path $workspacePath "scripts"
if (-not (Test-Path $scriptsPath)) { throw "Workspace scripts directory not found: $scriptsPath" }

$files = @(Get-ChildItem -LiteralPath $scriptsPath -Recurse -File | Where-Object { $_.Extension -in @('.ps1', '.sh') } | Sort-Object FullName)
$ps1Files = @($files | Where-Object { $_.Extension -ieq '.ps1' })
$shFiles = @($files | Where-Object { $_.Extension -ieq '.sh' })
$failures = New-Object System.Collections.Generic.List[object]

Write-Host "Installed script validation workspace: $workspacePath"
Write-Host "PowerShell scripts: $($ps1Files.Count)"
foreach ($file in $ps1Files) {
    $relative = Convert-ToWorkspacePath $file.FullName $workspacePath
    Write-Host "PS1  $relative"
    $tokens = $null
    $errors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($file.FullName, [ref]$tokens, [ref]$errors) | Out-Null
    foreach ($err in @($errors)) {
        $failures.Add([pscustomobject]@{
            Type = 'ps1'; File = $relative; Line = $err.Extent.StartLineNumber; Column = $err.Extent.StartColumnNumber
            ErrorId = $err.ErrorId; Message = $err.Message; Text = $err.Extent.Text
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
            $relative = Convert-ToWorkspacePath $file.FullName $workspacePath
            Write-Host "SH   $relative"
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
                    Type = 'sh'; File = $relative; Line = ''; Column = ''; ErrorId = "bash-n-exit-$exitCode"
                    Message = ($output -join "`n"); Text = ''
                }) | Out-Null
            }
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "WORKSPACE_SCRIPT_VALIDATE_FAIL: $($failures.Count) issue(s)" -ForegroundColor Red
    $failures | Format-Table Type, File, Line, Column, ErrorId, Message -AutoSize
    throw 'Installed workspace script validation failed.'
}

Write-Host "WORKSPACE_SCRIPT_VALIDATE_PASS: checked $($ps1Files.Count) PowerShell script(s) and $($shFiles.Count) shell script(s)." -ForegroundColor Green
