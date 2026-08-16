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
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
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

function Get-TextSha256([string]$Text) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Text)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([Convert]::ToHexString($sha.ComputeHash($bytes))).ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-FileSha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return "" }
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Normalize-PathIdentity([string]$Path) {
    $normalized = [IO.Path]::GetFullPath($Path).Replace('\', '/').TrimEnd('/')
    if ([IO.Path]::DirectorySeparatorChar -eq '\') {
        $normalized = $normalized.ToLowerInvariant()
    }
    return $normalized
}

function Get-PathIdentitySha256([string]$Path) {
    return Get-TextSha256 (Normalize-PathIdentity $Path)
}

function Convert-ToRelativePath([string]$BasePath, [string]$Path) {
    $fullBase = [IO.Path]::GetFullPath($BasePath)
    $fullPath = [IO.Path]::GetFullPath($Path)
    $separatorChars = [char[]]@(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    )
    $fullBase = $fullBase.TrimEnd($separatorChars)

    # Windows PowerShell 5.1 does not expose Path.GetRelativePath.
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

function Get-GeneratedCsTreeSha256([string]$GeneratedRoot) {
    if (-not (Test-Path -LiteralPath $GeneratedRoot -PathType Container)) { return "" }

    $entries = [System.Collections.Generic.List[string]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $GeneratedRoot -Recurse -File -Filter "*.cs")) {
        $relative = Convert-ToRelativePath $GeneratedRoot $file.FullName
        $hash = Get-FileSha256 $file.FullName
        $entries.Add("$relative`t$hash")
    }
    $entries.Sort([StringComparer]::Ordinal)
    return Get-TextSha256 ($entries -join "`n")
}

function Get-FinalGateProofSha256($Gate) {
    $material = @(
        "standard-run-final-gate/v3",
        [string]$Gate.status,
        [string]$Gate.workspacePathSha256,
        [string]$Gate.runPathSha256,
        [string]$Gate.autonomyStateFileSha256,
        [string]$Gate.autonomyLedgerSequence,
        [string]$Gate.autonomyLedgerEntrySha256,
        [string]$Gate.autonomyLedgerStateSha256,
        [string]$Gate.autonomyInvocationId,
        [string]$Gate.autonomyCurrentStateHash,
        [string]$Gate.sourceSha256,
        [string]$Gate.configSha256,
        [string]$Gate.targetSha256,
        [string]$Gate.toolSha256,
        [string]$Gate.environmentSha256,
        [string]$Gate.orchestrationReportFileSha256,
        [string]$Gate.generatedReportFileSha256,
        [string]$Gate.runManifestFileSha256,
        [string]$Gate.projectVerifyReportFileSha256,
        [string]$Gate.verificationEvidenceFileSha256,
        [string]$Gate.verificationEvidenceSha256,
        [string]$Gate.generatedCsTreeSha256
    ) -join "`n"
    return Get-TextSha256 $material
}

function Test-PathUnderRoot([string]$Root, [string]$Candidate) {
    $separatorChars = [char[]]@(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar
    )
    $rootFull = [IO.Path]::GetFullPath($Root).TrimEnd($separatorChars) + [IO.Path]::DirectorySeparatorChar
    $candidateFull = [IO.Path]::GetFullPath($Candidate)
    return $candidateFull.StartsWith($rootFull, [StringComparison]::OrdinalIgnoreCase)
}

if (-not (Test-Path -LiteralPath $runFull -PathType Container)) {
    throw "STANDARD_RUN_MISSING: run directory does not exist: $runFull"
}

$runsRoot = Join-Path $workspaceFull "runs"
if (-not (Test-PathUnderRoot $runsRoot $runFull)) {
    Add-Failure "run path is outside workspace/runs"
}

$reportPath = Join-Path $runFull "orchestration-report.json"
$generatedReportPath = Join-Path $runFull "generated/report.json"
$manifestPath = Join-Path $runFull "run-manifest.json"
$generatedRoot = Join-Path $runFull "generated"
$generatedHashPath = Join-Path $generatedRoot "target-tree.sha256"
$verificationReportPath = Join-Path $runFull "verify-project/project-verify-report.json"
$verificationEvidencePath = Join-Path $runFull "verify-project/verification-evidence.json"
$autonomyStatePath = Join-Path $workspaceFull "state/autonomy-state.json"
$ledgerAnchorPath = Join-Path $workspaceFull "evidence/autonomy-ledger/anchor.json"

$report = Read-JsonFile $reportPath "orchestration-report.json"
$generatedReport = Read-JsonFile $generatedReportPath "generated/report.json"
$manifest = Read-JsonFile $manifestPath "run-manifest.json"
$projectVerify = Read-JsonFile $verificationReportPath "verify-project/project-verify-report.json"
$projectEvidence = Read-JsonFile $verificationEvidencePath "verify-project/verification-evidence.json"
$autonomyState = Read-JsonFile $autonomyStatePath "state/autonomy-state.json"
$ledgerAnchor = Read-JsonFile $ledgerAnchorPath "evidence/autonomy-ledger/anchor.json"

if ($null -ne $report) {
    $reportStatus = [string]$report.Status
    if ($reportStatus -notmatch '^(passed|PASS)$') {
        Add-Failure "orchestration status is $reportStatus"
    }
}

$verificationStatus = "NOT_RUN"
$manifestTarget = ""
$sourceSha256 = ""
$configSha256 = ""
$toolSha256 = ""
$environmentSha256 = ""

if ($null -ne $manifest) {
    if ([string]$manifest.SchemaVersion -ne "migrator-run-manifest/v2") {
        Add-Failure "run-manifest schema is unsupported"
    }

    $manifestStatus = [string]$manifest.Status
    if ($manifestStatus -notmatch '^(passed|PASS)$') {
        Add-Failure "run-manifest status is $manifestStatus"
    }

    $sourceSha256 = [string]$manifest.SourceSha256
    $configSha256 = [string]$manifest.ConfigSha256
    $manifestTarget = [string]$manifest.TargetSha256
    $toolSha256 = [string]$manifest.Tool.IdentitySha256
    $environmentSha256 = [string]$manifest.Environment.IdentitySha256

    foreach ($pair in @(
        @("run-manifest sourceSha256", $sourceSha256),
        @("run-manifest configSha256", $configSha256),
        @("run-manifest targetSha256", $manifestTarget),
        @("run-manifest toolSha256", $toolSha256),
        @("run-manifest environmentSha256", $environmentSha256)
    )) {
        if ([string]::IsNullOrWhiteSpace([string]$pair[1])) {
            Add-Failure "$($pair[0]) is missing"
        }
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
        if ([string]::IsNullOrWhiteSpace([string]$internal.EvidenceSha256)) {
            Add-Failure "run-manifest internal verification evidenceSha256 is missing"
        }
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

                $actualFileHash = Get-FileSha256 $candidate
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
    if ([string]::IsNullOrWhiteSpace([string]$projectEvidence.EvidenceSha256)) {
        Add-Failure "verify-project evidenceSha256 is missing"
    }

    if ($null -ne $manifest) {
        Assert-EqualIdentity "verify-project sourceSha256" $projectEvidence.SourceSha256 $manifest.SourceSha256
        Assert-EqualIdentity "verify-project configSha256" $projectEvidence.ConfigSha256 $manifest.ConfigSha256
        Assert-EqualIdentity "verify-project targetSha256" $projectEvidence.TargetSha256 $manifest.TargetSha256
        Assert-EqualIdentity "verify-project toolSha256" $projectEvidence.ToolSha256 $manifest.Tool.IdentitySha256
        Assert-EqualIdentity "verify-project environmentSha256" $projectEvidence.EnvironmentSha256 $manifest.Environment.IdentitySha256
    }
}

$autonomyStateFileSha256 = Get-FileSha256 $autonomyStatePath
$autonomyInvocationId = ""
$autonomyCurrentStateHash = ""
if ($null -ne $autonomyState) {
    if ([string]$autonomyState.schemaVersion -ne "standard-migration-autonomy/v3") {
        Add-Failure "autonomy state schema is unsupported"
    }
    if ([bool]$autonomyState.cycleInProgress) {
        Add-Failure "autonomy state has an active remediation cycle"
    }
    if ([bool]$autonomyState.rollbackRequired) {
        Add-Failure "autonomy state has a pending rollback"
    }
    if (-not [bool]$autonomyState.proofLedgerRequired) {
        Add-Failure "autonomy state proof ledger is not required"
    }
    if ([string]$autonomyState.status -eq "COMPLETE") {
        Add-Failure "autonomy state is already COMPLETE"
    }

    $autonomyInvocationId = [string]$autonomyState.invocationId
    $autonomyCurrentStateHash = [string]$autonomyState.currentStateHash
    if ([string]::IsNullOrWhiteSpace($autonomyInvocationId)) {
        Add-Failure "autonomy invocationId is missing"
    }
}

$ledgerSequence = 0
$ledgerEntrySha256 = ""
$ledgerStateSha256 = ""
if ($null -ne $ledgerAnchor) {
    if ([string]$ledgerAnchor.schemaVersion -ne "standard-migration-autonomy-ledger-anchor/v1") {
        Add-Failure "autonomy ledger anchor schema is unsupported"
    }
    $ledgerSequence = [long]$ledgerAnchor.sequence
    $ledgerEntrySha256 = [string]$ledgerAnchor.entrySha256
    $ledgerStateSha256 = [string]$ledgerAnchor.stateSha256
    if ($ledgerSequence -lt 1 -or
        [string]::IsNullOrWhiteSpace($ledgerEntrySha256) -or
        [string]::IsNullOrWhiteSpace($ledgerStateSha256)) {
        Add-Failure "autonomy ledger anchor identity is incomplete"
    }
}

$status = if ($failures.Count -eq 0) { "PASS" } else { "FAIL" }
$stateDir = Join-Path $workspaceFull "state"
New-Item -ItemType Directory -Force -Path $stateDir | Out-Null

$result = [ordered]@{
    schemaVersion = "standard-run-final-gate/v3"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    status = $status
    workspacePathSha256 = Get-PathIdentitySha256 $workspaceFull
    runPath = $runFull
    runPathSha256 = Get-PathIdentitySha256 $runFull
    autonomyStateFileSha256 = $autonomyStateFileSha256
    autonomyLedgerSequence = $ledgerSequence
    autonomyLedgerEntrySha256 = $ledgerEntrySha256
    autonomyLedgerStateSha256 = $ledgerStateSha256
    autonomyInvocationId = $autonomyInvocationId
    autonomyCurrentStateHash = $autonomyCurrentStateHash
    sourceSha256 = $sourceSha256
    configSha256 = $configSha256
    targetSha256 = $manifestTarget
    toolSha256 = $toolSha256
    environmentSha256 = $environmentSha256
    verificationStatus = $verificationStatus
    orchestrationReportFileSha256 = Get-FileSha256 $reportPath
    generatedReportFileSha256 = Get-FileSha256 $generatedReportPath
    runManifestFileSha256 = Get-FileSha256 $manifestPath
    projectVerifyReportFileSha256 = Get-FileSha256 $verificationReportPath
    verificationEvidenceFileSha256 = Get-FileSha256 $verificationEvidencePath
    verificationEvidenceSha256 = if ($null -ne $projectEvidence) { [string]$projectEvidence.EvidenceSha256 } else { "" }
    generatedCsTreeSha256 = Get-GeneratedCsTreeSha256 $generatedRoot
    failures = @($failures)
    finalGateSha256 = ""
}
$result.finalGateSha256 = Get-FinalGateProofSha256 ([pscustomobject]$result)

$resultPath = Join-Path $stateDir "final-gate-result.json"
$result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resultPath -Encoding utf8

$md = @(
    "# Standard run final gate",
    "",
    "- Status: ``$status``",
    "- Run: ``$runFull``",
    "- Target: ``$manifestTarget``",
    "- Verification: ``$verificationStatus``",
    "- Final gate proof: ``$($result.finalGateSha256)``",
    "- Autonomy ledger entry: ``$ledgerEntrySha256``"
)
if ($failures.Count -gt 0) {
    $md += ""
    $md += "## Failures"
    $md += @($failures | ForEach-Object { "- $_" })
}
$md -join [Environment]::NewLine | Set-Content -LiteralPath (Join-Path $stateDir "final-gate.md") -Encoding utf8

if ($status -eq "PASS") {
    Write-Host "STANDARD_RUN_FINAL_GATE_PASS"
    Write-Host "Final gate: $($result.finalGateSha256)"
    Write-Host "Ledger entry: $ledgerEntrySha256"
    exit 0
}

Write-Error ("STANDARD_RUN_FINAL_GATE_FAIL: " + ($failures -join "; "))
exit 2
