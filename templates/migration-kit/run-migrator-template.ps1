param(
    [ValidateSet("doctor", "config-validate", "orchestrate", "migrate", "verify", "verify-project", "explain-todo", "migration-board", "smoke-plan")]
    [string]$Mode = "doctor",

    [string]$Input = "{{SOURCE}}",
    [string]$Config = "{{CONFIG}}",
    [string]$Out = "{{OUTPUT}}",
    [string]$Format = "both",
    [string]$Tool = "selenium-pw-migrator"
)

$ErrorActionPreference = "Stop"

$args = @("--mode", $Mode, "--format", $Format)

if ($Mode -eq "config-validate") {
    $args += @("--config", $Config)
}
elseif ($Mode -eq "migration-board" -or $Mode -eq "explain-todo" -or $Mode -eq "smoke-plan") {
    $args += @("--input", $Out, "--config", $Config, "--out", $Out)
}
else {
    $args += @("--input", $Input, "--config", $Config, "--out", $Out)
}

& $Tool @args
exit $LASTEXITCODE
