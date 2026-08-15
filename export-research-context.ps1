param(
    [string]$Root = ".",
    [string]$OutDir = ".\artifacts\research-bundles",

    # Один или несколько migration workspace из реальных проектов.
    [string[]]$MigrationPath = @(),

    [int]$RecentRuns = 3,
    [int]$MaxFileSizeMB = 5
)

$ErrorActionPreference = "Stop"

$Root = (Resolve-Path $Root).Path
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$bundleName = "migrator-research-$timestamp"
$temp = Join-Path ([IO.Path]::GetTempPath()) $bundleName
$zip = Join-Path $Root "$OutDir\$bundleName.zip"

Write-Host "== Migrator research context export =="
Write-Host "Root: $Root"

Remove-Item $temp -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $temp | Out-Null

$maxBytes = $MaxFileSizeMB * 1MB

# ----------------------------------------------------------------------
# Exclusions
# ----------------------------------------------------------------------

$excludedDirectories = @(
    ".git",
    ".vs",
    ".idea",
    "bin",
    "obj",
    "node_modules",
    "TestResults",
    "packages",
    ".packages",
    ".nuget",
    "coverage",
    "playwright-report",
    "blob-report",
    "dist",
    "publish"
)

$excludedExtensions = @(
    ".dll",
    ".exe",
    ".pdb",
    ".so",
    ".dylib",
    ".a",
    ".lib",
    ".nupkg",
    ".snupkg",
    ".zip",
    ".7z",
    ".rar",
    ".tar",
    ".gz",
    ".tgz",
    ".bin",
    ".cache"
)

# Большие generated/release каталоги, которые для исследования кода обычно бесполезны.
$excludedPathFragments = @(
    "\artifacts\release\",
    "\artifacts\nuget\",
    "\artifacts\standalone\",
    "\artifacts\packages\"
)

function Test-IsExcludedPath {
    param([string]$Path)

    $relative = $Path.Substring($Root.Length).TrimStart("\", "/")
    $parts = $relative -split "[\\/]"

    foreach ($part in $parts) {
        if ($excludedDirectories -contains $part) {
            return $true
        }
    }

    foreach ($fragment in $excludedPathFragments) {
        if ($Path.IndexOf($fragment, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }

    return $false
}

function Test-IsUsefulFile {
    param([IO.FileInfo]$File)

    if (Test-IsExcludedPath $File.FullName) {
        return $false
    }

    if ($excludedExtensions -contains $File.Extension.ToLowerInvariant()) {
        return $false
    }

    if ($File.Length -gt $maxBytes) {
        return $false
    }

    return $true
}

# ----------------------------------------------------------------------
# Very lightweight secret sanitization for text files.
# Originals are NEVER modified.
# ----------------------------------------------------------------------

function Get-SanitizedText {
    param([string]$Path)

    $text = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop

    $patterns = @(
        '(?im)(api[_-]?key\s*[:=]\s*)["'']?[^"'']+\b',
        '(?im)(access[_-]?token\s*[:=]\s*)["'']?[^"'']+\b',
        '(?im)(auth[_-]?token\s*[:=]\s*)["'']?[^"'']+\b',
        '(?im)(bearer\s+)[A-Za-z0-9._~+/=-]{12,}',
        '(?im)(password\s*[:=]\s*)["'']?[^"'']+\b',
        '(?im)(client[_-]?secret\s*[:=]\s*)["'']?[^"'']+\b',
        '(?im)(private[_-]?key\s*[:=]\s*)["'']?[^"'']+\b'
    )

    foreach ($pattern in $patterns) {
        $text = [regex]::Replace(
            $text,
            $pattern,
            '$1<REDACTED>'
        )
    }

    return $text
}

$textExtensions = @(
    ".cs", ".csproj", ".sln",
    ".ps1", ".psm1", ".psd1",
    ".json", ".jsonc",
    ".md", ".txt",
    ".yml", ".yaml",
    ".xml", ".props", ".targets",
    ".ts", ".js", ".mjs",
    ".py", ".java",
    ".sh",
    ".gitignore", ".editorconfig"
)

# ----------------------------------------------------------------------
# 1. Source/config snapshot
# ----------------------------------------------------------------------

Write-Host "== Copy source/config =="

$sourceOut = Join-Path $temp "repository"

Get-ChildItem -LiteralPath $Root -Recurse -File |
    Where-Object { Test-IsUsefulFile $_ } |
    ForEach-Object {
        $file = $_

        $relative = $file.FullName.Substring($Root.Length).TrimStart("\", "/")

        # Не копируем весь artifacts ниже — нужные artifacts собираются отдельно.
        if ($relative -match '^artifacts[\\/]') {
            return
        }

        $destination = Join-Path $sourceOut $relative
        $destinationDir = Split-Path $destination -Parent
        New-Item -ItemType Directory -Force -Path $destinationDir | Out-Null

        if ($textExtensions -contains $file.Extension.ToLowerInvariant() -or
            $file.Name -in @(".gitignore", ".editorconfig")) {

            try {
                $content = Get-SanitizedText $file.FullName
                [IO.File]::WriteAllText(
                    $destination,
                    $content,
                    [Text.UTF8Encoding]::new($false)
                )
            }
            catch {
                Copy-Item -LiteralPath $file.FullName -Destination $destination
            }
        }
        else {
            Copy-Item -LiteralPath $file.FullName -Destination $destination
        }
    }

# ----------------------------------------------------------------------
# 2. Git context
# ----------------------------------------------------------------------

Write-Host "== Git context =="

$gitOut = Join-Path $temp "git"
New-Item -ItemType Directory -Force -Path $gitOut | Out-Null

Push-Location $Root
try {
    git status --short |
        Out-File "$gitOut\status.txt" -Encoding utf8

    git status |
        Out-File "$gitOut\status-full.txt" -Encoding utf8

    git log -30 `
        --date=iso `
        --pretty=format:"%h %ad %an %s" |
        Out-File "$gitOut\recent-commits.txt" -Encoding utf8

    git diff --stat |
        Out-File "$gitOut\diff-stat.txt" -Encoding utf8

    git diff |
        Out-File "$gitOut\working-tree.patch" -Encoding utf8

    git diff --cached |
        Out-File "$gitOut\staged.patch" -Encoding utf8

    git rev-parse HEAD |
        Out-File "$gitOut\head.txt" -Encoding ascii

    git branch --show-current |
        Out-File "$gitOut\branch.txt" -Encoding utf8
}
finally {
    Pop-Location
}

# ----------------------------------------------------------------------
# 3. Environment
# ----------------------------------------------------------------------

Write-Host "== Environment =="

$envOut = Join-Path $temp "environment"
New-Item -ItemType Directory -Force -Path $envOut | Out-Null

dotnet --info |
    Out-File "$envOut\dotnet-info.txt" -Encoding utf8

dotnet --list-sdks |
    Out-File "$envOut\dotnet-sdks.txt" -Encoding utf8

dotnet --list-runtimes |
    Out-File "$envOut\dotnet-runtimes.txt" -Encoding utf8

@"
Timestamp: $(Get-Date -Format o)
OS: $([Environment]::OSVersion)
64-bit OS: $([Environment]::Is64BitOperatingSystem)
64-bit process: $([Environment]::Is64BitProcess)
PowerShell: $($PSVersionTable.PSVersion)
Root: $Root
"@ | Out-File "$envOut\host.txt" -Encoding utf8

# ----------------------------------------------------------------------
# 4. Most useful migration/run artifacts
# ----------------------------------------------------------------------

Write-Host "== Migration artifacts =="

$artifactOut = Join-Path $temp "artifacts"
New-Item -ItemType Directory -Force -Path $artifactOut | Out-Null

$importantArtifactNames = @(
    "run-manifest.json",
    "semantic-index.json",
    "semantic-index.sha256",
    "verification-evidence.json",
    "verify-report.json",
    "project-verify-report.json",
    "generated-report.json",
    "orchestration-report.json",
    "unmapped-targets.json",
    "unsupported-actions.json",
    "mapping-proposals.json",
    "mapping-proposals.md",
    "adapter-config.draft.json",
    "autonomy-state.json",
    "remediation-evaluation.json",
    "remediation-cycle-guard.json",
    "standard-migration-smoke.json"
)

$artifactRoot = Join-Path $Root "artifacts"

if (Test-Path $artifactRoot) {
    Get-ChildItem $artifactRoot -Recurse -File |
        Where-Object {
            ($importantArtifactNames -contains $_.Name) -and
            $_.Length -le $maxBytes
        } |
        ForEach-Object {
            $relative = $_.FullName.Substring($artifactRoot.Length).TrimStart("\", "/")
            $destination = Join-Path $artifactOut $relative

            New-Item `
                -ItemType Directory `
                -Force `
                -Path (Split-Path $destination -Parent) |
                Out-Null

            try {
                $content = Get-SanitizedText $_.FullName
                [IO.File]::WriteAllText(
                    $destination,
                    $content,
                    [Text.UTF8Encoding]::new($false)
                )
            }
            catch {
                Copy-Item $_.FullName $destination
            }
        }
}

# ----------------------------------------------------------------------
# 5. Migration workspace state, if repository contains one
# ----------------------------------------------------------------------

# ----------------------------------------------------------------------
# 5. External migration workspaces
# ----------------------------------------------------------------------

Write-Host "== Migration workspaces =="

# Если явно ничего не передали, для удобства попробуем migration/ рядом с Root.
if ($MigrationPath.Count -eq 0) {
    foreach ($candidate in @(
        (Join-Path $Root "migration"),
        (Join-Path $Root ".migration")
    )) {
        if (Test-Path $candidate) {
            $MigrationPath += $candidate
        }
    }
}

$workspaceIndex = 0

foreach ($workspaceArg in $MigrationPath) {
    if ([string]::IsNullOrWhiteSpace($workspaceArg)) {
        continue
    }

    if (-not (Test-Path -LiteralPath $workspaceArg)) {
        Write-Warning "Migration workspace not found: $workspaceArg"
        continue
    }

    $workspace = (Resolve-Path -LiteralPath $workspaceArg).Path
    $workspaceIndex++

    # Например:
    # C:\Projects\MarketerWeb\migration
    # →
    # migration-workspaces\01-MarketerWeb\
    $workspaceParent = Split-Path $workspace -Parent
    $projectName = Split-Path $workspaceParent -Leaf

    if ([string]::IsNullOrWhiteSpace($projectName)) {
        $projectName = "workspace"
    }

    $safeProjectName = $projectName -replace '[^a-zA-Z0-9._-]', '_'
    $bundleWorkspaceName = "{0:D2}-{1}" -f $workspaceIndex, $safeProjectName

    $workspaceOut = Join-Path `
        $temp `
        ("migration-workspaces\" + $bundleWorkspaceName)

    Write-Host "Workspace:"
    Write-Host "  source: $workspace"
    Write-Host "  bundle: migration-workspaces\$bundleWorkspaceName"

    New-Item `
        -ItemType Directory `
        -Force `
        -Path $workspaceOut |
        Out-Null

    # Сохраняем исходный путь workspace — исследователю полезно понимать,
    # откуда именно приехали artifacts.
    @"
Project: $projectName
MigrationPath: $workspace
CapturedAt: $(Get-Date -Format o)
"@ | Out-File `
        (Join-Path $workspaceOut "workspace-info.txt") `
        -Encoding utf8

    Get-ChildItem `
        -LiteralPath $workspace `
        -Recurse `
        -File |
        Where-Object {

            $file = $_

            # Не берём тяжёлые binary/generated вещи.
            if ($file.Length -gt $maxBytes) {
                return $false
            }

            if ($excludedExtensions -contains $file.Extension.ToLowerInvariant()) {
                return $false
            }

            $relative = $file.FullName.Substring($workspace.Length).TrimStart("\", "/")

            # Самые полезные части migration workspace.
            return (
                $relative -match '^(state|profiles|runs|reports|evidence|handoff)[\\/]' -or

                $file.Name -match
                '(manifest|report|evidence|semantic|autonomy|remediation|config|ticket|handoff|proposal|unmapped|unsupported|verify)'
            )
        } |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1000 |
        ForEach-Object {

            $file = $_

            $relative = $file.FullName.Substring($workspace.Length).TrimStart("\", "/")
            $destination = Join-Path $workspaceOut $relative

            New-Item `
                -ItemType Directory `
                -Force `
                -Path (Split-Path $destination -Parent) |
                Out-Null

            try {
                $content = Get-SanitizedText $file.FullName

                [IO.File]::WriteAllText(
                    $destination,
                    $content,
                    [Text.UTF8Encoding]::new($false)
                )
            }
            catch {
                Copy-Item `
                    -LiteralPath $file.FullName `
                    -Destination $destination
            }
        }
}

# ----------------------------------------------------------------------
# 6. Repository structure without binary noise
# ----------------------------------------------------------------------

Write-Host "== Repository inventory =="

$inventory = Get-ChildItem $Root -Recurse -File |
    Where-Object { Test-IsUsefulFile $_ } |
    ForEach-Object {
        [PSCustomObject]@{
            Path = $_.FullName.Substring($Root.Length).TrimStart("\", "/")
            Size = $_.Length
            ModifiedUtc = $_.LastWriteTimeUtc.ToString("o")
        }
    } |
    Sort-Object Path

$inventory |
    ConvertTo-Json -Depth 3 |
    Out-File "$temp\repository-files.json" -Encoding utf8

# Useful overview of files omitted because they were huge.
Get-ChildItem $Root -Recurse -File |
    Where-Object {
        -not (Test-IsExcludedPath $_.FullName) -and
        $_.Length -gt $maxBytes
    } |
    Sort-Object Length -Descending |
    Select-Object -First 100 `
        @{ N = "SizeMB"; E = { [Math]::Round($_.Length / 1MB, 2) } },
        @{ N = "Path"; E = { $_.FullName.Substring($Root.Length).TrimStart("\", "/") } } |
    Format-Table -AutoSize |
    Out-String |
    Out-File "$temp\large-files-skipped.txt" -Encoding utf8

# ----------------------------------------------------------------------
# 7. README for researcher
# ----------------------------------------------------------------------

@"
# Migrator research bundle

Created: $(Get-Date -Format o)

Contains:

- repository/           source and configuration files
- git/                  HEAD, status, recent history and uncommitted diff
- environment/          .NET and PowerShell environment
- artifacts/            useful migration/verification/semantic artifacts only
- migration-workspace/  state/run metadata when present
- repository-files.json lightweight source inventory
- large-files-skipped.txt

Intentionally excluded:

- .git
- bin / obj
- node_modules
- binaries and symbols
- standalone/release packages
- NuGet packages
- archives
- TestResults
- IDE state
- large files over ${MaxFileSizeMB} MB

Text content is copied through best-effort secret redaction.
Original repository files are never modified.
"@ | Out-File "$temp\README-RESEARCH-BUNDLE.md" -Encoding utf8

# ----------------------------------------------------------------------
# 8. Zip
# ----------------------------------------------------------------------

Write-Host "== Create ZIP =="

$zipDir = Split-Path $zip -Parent
New-Item -ItemType Directory -Force -Path $zipDir | Out-Null

Remove-Item $zip -Force -ErrorAction SilentlyContinue

Compress-Archive `
    -Path "$temp\*" `
    -DestinationPath $zip `
    -CompressionLevel Optimal

$sizeMB = [Math]::Round((Get-Item $zip).Length / 1MB, 2)

Remove-Item $temp -Recurse -Force

Write-Host ""
Write-Host "RESEARCH_BUNDLE_READY"
Write-Host "File: $zip"
Write-Host "Size: $sizeMB MB"