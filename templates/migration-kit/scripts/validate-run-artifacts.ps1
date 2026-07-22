[CmdletBinding()]
param([string]$RunPath)
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrWhiteSpace($RunPath)) { throw "-RunPath is required" }
$runFull = [IO.Path]::GetFullPath($RunPath)
$required = @("orchestration-report.json", "generated/report.json")
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $runFull $_)) })
if ($missing.Count -gt 0) { throw "STANDARD_RUN_ARTIFACTS_INVALID: missing $($missing -join ', ')" }
foreach ($relative in $required) { Get-Content -LiteralPath (Join-Path $runFull $relative) -Raw | ConvertFrom-Json | Out-Null }
Write-Host "STANDARD_RUN_ARTIFACTS_PASS"
