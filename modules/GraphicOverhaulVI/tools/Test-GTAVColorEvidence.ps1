param(
    [Parameter(Mandatory = $true)]
    [string]$TargetScript
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

try {
    if (-not (Test-Path -LiteralPath $TargetScript -PathType Leaf)) {
        throw "Target script not found: $TargetScript"
    }
    if ($PSVersionTable.PSVersion.Major -lt 5) {
        throw "Windows PowerShell 5.1 or newer is required. Detected: $($PSVersionTable.PSVersion)"
    }

    $tokens = $null
    $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile(
        $TargetScript,
        [ref]$tokens,
        [ref]$parseErrors
    )

    if (@($parseErrors).Count -gt 0) {
        Write-Host "PowerShell parser found errors:" -ForegroundColor Red
        foreach ($parseError in $parseErrors) {
            $line = 0
            $column = 0
            if ($null -ne $parseError.Extent) {
                $line = $parseError.Extent.StartLineNumber
                $column = $parseError.Extent.StartColumnNumber
            }
            Write-Host (" - line {0}, column {1}: {2}" -f $line, $column, $parseError.Message) -ForegroundColor Red
        }
        exit 90
    }

    Write-Host ("Syntax OK under Windows PowerShell {0}." -f $PSVersionTable.PSVersion) -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ("Preflight error: {0}" -f $_.Exception.Message) -ForegroundColor Red
    exit 91
}
