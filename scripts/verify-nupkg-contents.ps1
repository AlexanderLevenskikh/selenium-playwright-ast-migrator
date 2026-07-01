param(
    [Parameter(Mandatory = $true)] [string]$PackagePath,
    [string]$PackageId = "SeleniumPlaywrightMigrator"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PackagePath)) {
    throw "Package not found: $PackagePath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem

$resolvedPackage = (Resolve-Path $PackagePath).Path
$archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName.Replace('\\', '/') })

    $requiredExact = @(
        'README_TOOL.md',
        'LICENSE',
        'SECURITY.md',
        'CONTRIBUTING.md',
        'CHANGELOG.md',
        'assets/icon.png',
        'schemas/adapter-config.schema.json'
    )

    foreach ($required in $requiredExact) {
        if (-not ($entries -contains $required)) {
            throw "Package is missing required public file: $required"
        }
    }

    $requiredPatterns = @(
        '^tools/net8\.0/any/Migrator\.Cli\.(exe|dll)$',
        '^tools/net8\.0/any/Migrator\.Core\.dll$',
        '^tools/net8\.0/any/Migrator\.Roslyn\.dll$',
        '^tools/net8\.0/any/Migrator\.PlaywrightDotNet\.dll$',
        '^tools/net8\.0/any/Migrator\.PlaywrightTypeScript\.dll$',
        '^tools/net8\.0/any/Migrator\.SeleniumCSharp\.dll$',
        '^templates/migration-kit/README\.md$',
        '^templates/migration-kit/prompts/kickoff-prompt\.txt$',
        '^scripts/install-migration-kit\.ps1$'
    )

    foreach ($pattern in $requiredPatterns) {
        if (-not ($entries | Where-Object { $_ -match $pattern } | Select-Object -First 1)) {
            throw "Package is missing an entry matching: $pattern"
        }
    }

    $forbiddenPatterns = @(
        '(^|/)\.agent-state(/|$)',
        '(^|/)\.migration(/|$)',
        '(^|/)migration(/|$)',
        '(^|/)artifacts(/|$)',
        '(^|/)bin(/|$)',
        '(^|/)obj(/|$)',
        '(^|/)TestResults(/|$)',
        '(^|/)\.git(/|$)',
        '(^|/)\.vs(/|$)',
        '(^|/)\.idea(/|$)',
        '(^|/)node_modules(/|$)',
        '(^|/)\.env(\.|/|$)',
        '(^|/)NuGet\.config$',
        '\.local\.json$',
        '\.(zip|7z|rar)$',
        '^templates/migration-kit/migration-kit/',
        '^templates/codex/codex/',
        '^templates/loops-library/.*/loops-library/',
        '^templates/opencode-team/.*/opencode-team/'
    )

    foreach ($entry in $entries) {
        foreach ($pattern in $forbiddenPatterns) {
            if ($entry -match $pattern) {
                throw "Package contains forbidden local/private artifact: $entry"
            }
        }
    }

    $nuspec = $entries | Where-Object { $_ -like "*.nuspec" } | Select-Object -First 1
    if (-not $nuspec) {
        throw "Package does not contain a .nuspec file."
    }

    Write-Host "Package content verification passed: $resolvedPackage"
    Write-Host "Entries checked: $($entries.Count)"
}
finally {
    $archive.Dispose()
}
