param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [string]$OutputDir = "artifacts/npm"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$sourceDir = Join-Path $repoRoot "npm"
$outputPath = if ([System.IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $repoRoot $OutputDir }
$stagingDir = Join-Path $outputPath "package"

if (-not (Get-Command npm -ErrorAction SilentlyContinue)) {
    throw "npm is required to package the npm wrapper."
}

Remove-Item -Recurse -Force $stagingDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stagingDir | Out-Null

Copy-Item -Recurse -Force (Join-Path $sourceDir "bin") $stagingDir
Copy-Item -Recurse -Force (Join-Path $sourceDir "scripts") $stagingDir
Copy-Item -Force (Join-Path $sourceDir "README.md") $stagingDir
Copy-Item -Force (Join-Path $sourceDir "package.json") $stagingDir

$packageJsonPath = Join-Path $stagingDir "package.json"
$packageJson = Get-Content $packageJsonPath -Raw | ConvertFrom-Json
$packageJson.version = $Version
$packageJson | ConvertTo-Json -Depth 10 | Set-Content -Encoding UTF8 $packageJsonPath

New-Item -ItemType Directory -Force $outputPath | Out-Null
Push-Location $stagingDir
try {
    npm pack --pack-destination $outputPath
}
finally {
    Pop-Location
}

Write-Host "npm wrapper package artifacts: $outputPath"
Get-ChildItem $outputPath -Filter "*.tgz" | Sort-Object Name | ForEach-Object { Write-Host "  $($_.Name)" }
