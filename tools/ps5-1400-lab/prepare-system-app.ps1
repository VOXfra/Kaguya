param(
    [string]$InputRoot = "",
    [string]$Python = "python"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$DefaultInput = Join-Path $Root "input\system-app"
$Out = Join-Path $Root "output"
$Stage = Join-Path $Out "system-app-stage"
$Report = Join-Path $Out "system-app-report.json"

if ([string]::IsNullOrWhiteSpace($InputRoot)) {
    $InputRoot = $DefaultInput
}

New-Item -ItemType Directory -Force -Path $Out | Out-Null

if (-not (Test-Path $InputRoot)) {
    New-Item -ItemType Directory -Force -Path $InputRoot | Out-Null
    Write-Host "[WAIT] Put an already-decrypted NPXS system app under:"
    Write-Host "       $InputRoot"
    Write-Host "[INFO] Expected examples: NPXSxxxxx\eboot.bin or system_ex\app\NPXSxxxxx\eboot.bin"
    exit 2
}

& $Python (Join-Path $Root "system_app_intake.py") $InputRoot -o $Report --stage $Stage
exit $LASTEXITCODE
