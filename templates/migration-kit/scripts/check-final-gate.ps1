[CmdletBinding()]
param(
    [string]$Workspace = "migration",
    [Parameter(Mandatory = $true)]
    [Alias("Run")]
    [string]$RunPath,
    [string]$RepoRoot = "."
)

$ErrorActionPreference = "Stop"
$workspaceFull = [IO.Path]::GetFullPath($Workspace)
$runFull = [IO.Path]::GetFullPath($RunPath)
$failures = [System.Collections.Generic.List[string]]::new()

function Add-Failure([string]$Message) {
    $failures.Add($Message)
}

function Read-JsonFile([string]$Path, [string]$Label) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Add-Failure "$Label is missing"
        return $null
    }
    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    } catch {
        Add-Failure "$Label is invalid JSON"
        return $null
    }
}

function Assert-EqualIdentity([string]$Label, $Actual, $Expected) {
    $actualText = [string]$Actual
    $expectedText = [string]$Expected
    if ([string]::IsNullOrWhiteSpace($actualText) -or
        [string]::IsNullOrWhiteSpace($expectedText) -or
        -not [string]::Equals($actualText, $expectedText, [StringComparison]::OrdinalIgnoreCase)) {
        Add-Failure "$Label mismatch"
    }
}

function Convert-ToRelativePath([string]$BasePath, [string]$Path) {
    $fullBase = [IO.Path]::GetFullPath($BasePath)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $separatorChars = [char[]]@(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    )
    $fullBase = $fullBase.TrimEnd($separatorChars)

    # Windows PowerShell 5.1 runs on .NET Framework and does not expose
    # System.IO.Path.GetRelativePath. Uri.MakeRelativeUri is available there
    # and on PowerShell 7, so use one implementation for both runtimes.
    $separator = [string][IO.Path]::DirectorySeparatorChar
    $baseWithSeparator = $fullBase
    if (-not $baseWithSeparator.EndsWith($separator, [StringComparison]::Ordinal)) {
        $baseWithSeparator += $separator
    }

    $baseUri = New-Object System.Uri -ArgumentList $baseWithSeparator
    $pathUri = New-Object System.Uri -ArgumentList $fullPath
    $relativeUri = $baseUri.MakeRelativeUri($pathUri).ToString()
    return ([Uri]::UnescapeDataString($relativeUri) -replace '\\', '/')
}

if (-not (Test-Path -LiteralPath $runFull -PathType Container)) {
    throw "STANDARD_RUN_MISSING: run directory does not exist: $runFull"
}

$reportPath = Join-Path $runFull "orchestration-report.json"
$generatedReportPath = Join-Path $runFull "generated/report.json"
$manifestPath = Join-Path $runFull "run-manifest.json"
$generatedRoot = Join-Path $runFull "generated"
$generatedHashPath = Join-Path $generatedRoot "target-tree.sha256"
$verificationReportPath = Join-Path $runFull "verify-project/project-verify-report.json"
$verificationEvidencePath = Join-Path $runFull "verify-project/verification-evidence.json"

$report = Read-JsonFile $reportPath "orchestration-report.json"
$generatedReport = Read-JsonFile $generatedReportPath "generated/report.json"
$manifest = Read-JsonFile $manifestPath "run-manifest.json"
$projectVerify = Read-JsonFile $verificationReportPath "verify-project/project-verify-report.json"
$projectEvidence = Read-JsonFile $verificationEvidencePath "verify-project/verification-evidence.json"

if ($null -ne $report) {
    $reportStatus = [string]$report.Status
    if ($reportStatus -notmatch '^(passed|PASS)$') {
        Add-Failure "orchestration status is $reportStatus"
    }
}

$verificationStatus = "NOT_RUN"
$manifestTarget = ""
if ($null -ne $manifest) {
    if ([string]$manifest.SchemaVersion -ne "migrator-run-manifest/v2") {
        Add-Failure "run-manifest schema is unsupported"
    }

    $manifestStatus = [string]$manifest.Status
    if ($manifestStatus -notmatch '^(passed|PASS)$') {
        Add-Failure "run-manifest status is $manifestStatus"
    }

    $manifestTarget = [string]$manifest.TargetSha256
    if ([string]::IsNullOrWhiteSpace($manifestTarget)) {
        Add-Failure "run-manifest targetSha256 is missing"
    }

    if ($null -eq $manifest.Verification) {
        Add-Failure "run-manifest internal verification evidence is missing"
    } else {
        $internal = $manifest.Verification
        if ([string]$internal.Kind -ne "generated-verify") {
            Add-Failure "run-manifest internal verification kind is $($internal.Kind)"
        }
        if ([string]$internal.Status -notmatch '^(passed|PASS)$' -or [int]$internal.ExitCode -ne 0) {
            Add-Failure "run-manifest internal verification status is $($internal.Status) (exit $($internal.ExitCode))"
        }
        Assert-EqualIdentity "internal verification sourceSha256" $internal.SourceSha256 $manifest.SourceSha256
        Assert-EqualIdentity "internal verification configSha256" $internal.ConfigSha256 $manifest.ConfigSha256
        Assert-EqualIdentity "internal verification targetSha256" $internal.TargetSha256 $manifest.TargetSha256
        Assert-EqualIdentity "internal verification toolSha256" $internal.ToolSha256 $manifest.Tool.IdentitySha256
        Assert-EqualIdentity "internal verification environmentSha256" $internal.EnvironmentSha256 $manifest.Environment.IdentitySha256
    }

    if (-not (Test-Path -LiteralPath $generatedRoot -PathType Container)) {
        Add-Failure "generated directory is missing"
    } else {
        $targetFiles = @($manifest.TargetFiles)
        if ($targetFiles.Count -eq 0) {
            Add-Failure "run-manifest target file identities are missing"
        } else {
            $expectedRelative = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
            $generatedRootFull = [IO.Path]::GetFullPath($generatedRoot).TrimEnd([char[]]@([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)) + [IO.Path]::DirectorySeparatorChar
            foreach ($targetFile in $targetFiles) {
                $relative = ([string]$targetFile.RelativePath).Replace('\', '/').TrimStart('/')
                if ([string]::IsNullOrWhiteSpace($relative) -or -not $expectedRelative.Add($relative)) {
                    Add-Failure "invalid or duplicate target file identity: $relative"
                    continue
                }

                $candidate = [IO.Path]::GetFullPath((Join-Path $generatedRoot ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
                if (-not $candidate.StartsWith($generatedRootFull, [StringComparison]::OrdinalIgnoreCase)) {
                    Add-Failure "target file escapes generated root: $relative"
                    continue
                }
                if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                    Add-Failure "target file is missing: $relative"
                    continue
                }

                $actualFileHash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
                Assert-EqualIdentity "target file hash ($relative)" $actualFileHash $targetFile.ContentSha256
            }

            $actualGenerated = @(Get-ChildItem -LiteralPath $generatedRoot -Recurse -File -Filter "*.cs" | ForEach-Object {
                Convert-ToRelativePath $generatedRoot $_.FullName
            })
            foreach ($relative in $actualGenerated) {
                if (-not $expectedRelative.Contains($relative)) {
                    Add-Failure "unexpected generated target file: $relative"
                }
            }
        }
    }

    if (-not (Test-Path -LiteralPath $generatedHashPath -PathType Leaf)) {
        Add-Failure "generated/target-tree.sha256 is missing"
    } else {
        $generatedMarker = (Get-Content -LiteralPath $generatedHashPath -Raw).Trim()
        Assert-EqualIdentity "generated target-tree.sha256" $generatedMarker $manifest.TargetSha256
    }
}

if ($null -ne $projectVerify) {
    $verificationStatus = [string]$projectVerify.Status
    if ($verificationStatus -notmatch '^(passed|PASS)$' -or [int]$projectVerify.ExitCode -ne 0) {
        Add-Failure "verify-project status is $verificationStatus (exit $($projectVerify.ExitCode))"
    }
}

if ($null -ne $projectEvidence) {
    if ([string]$projectEvidence.SchemaVersion -ne "migrator-verification-evidence/v1") {
        Add-Failure "verify-project evidence schema is unsupported"
    }
    if ([string]$projectEvidence.Kind -ne "dotnet-build-exact-target") {
        Add-Failure "verify-project evidence is not exact-target evidence (kind=$($projectEvidence.Kind))"
    }
    if ([string]$projectEvidence.Status -notmatch '^(passed|PASS)$' -or [int]$projectEvidence.ExitCode -ne 0) {
        Add-Failure "verify-project evidence status is $($projectEvidence.Status) (exit $($projectEvidence.ExitCode))"
    }

    if ($null -ne $manifest) {
        Assert-EqualIdentity "verify-project sourceSha256" $projectEvidence.SourceSha256 $manifest.SourceSha256
        Assert-EqualIdentity "verify-project configSha256" $projectEvidence.ConfigSha256 $manifest.ConfigSha256
        Assert-EqualIdentity "verify-project targetSha256" $projectEvidence.TargetSha256 $manifest.TargetSha256
        Assert-EqualIdentity "verify-project toolSha256" $projectEvidence.ToolSha256 $manifest.Tool.IdentitySha256
        Assert-EqualIdentity "verify-project environmentSha256" $projectEvidence.EnvironmentSha256 $manifest.Environment.IdentitySha256
    }
}

$status = if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }
$stateDir = Join-Path $workspaceFull "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null
$result = [ordered]@{
    schemaVersion = "standard-run-final-gate/v2"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    status = $status
    runPath = $runFull
    targetSha256 = $manifestTarget
    verificationStatus = $verificationStatus
    verificationEvidenceSha256 = if ($null -ne $projectEvidence) { [string]$projectEvidence.EvidenceSha256 } else { "" }
    failures = @($failures)
}
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $stateDir "final-gate-result.json") -Encoding utf8

$md = @(
    "# Standard run final gate",
    "",
    "- Status: ``$status``",
    "- Run: ``$runFull``",
    "- Target: ``$manifestTarget``",
    "- Verification: ``$verificationStatus``"
)
if ($failures.Count -gt 0) {
    $md += ""
    $md += "## Failures"
    $md += @($failures | ForEach-Object { "- $_" })
}
$md -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $stateDir "final-gate.md") -Encoding utf8

if ($status -eq "PASS") {
    Write-Host "STANDARD_RUN_FINAL_GATE_PASS"
    exit 0
}
Write-Error ("STANDARD_RUN_FINAL_GATE_FAIL: " + ($failures -join "; "))
exit 2
