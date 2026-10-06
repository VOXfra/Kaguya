param(
    [string]$Python = "python",
    [switch]$FetchReferenceOffsets
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$Input = Join-Path $Root "input"
$Out = Join-Path $Root "output"
$FW1360 = Join-Path $Input "fw1360"
$FW1400 = Join-Path $Input "fw1400"
$Offsets = Join-Path $Input "offsets-13.60.js"

New-Item -ItemType Directory -Force -Path $Input,$Out,$FW1360,$FW1400 | Out-Null

if ($FetchReferenceOffsets -and -not (Test-Path $Offsets)) {
    $url = "https://raw.githubusercontent.com/soniciso1/relapse-dev/main/offsets/13.60.js"
    Write-Host "[GET] $url"
    Invoke-WebRequest -Uri $url -OutFile $Offsets
}

if (-not (Get-ChildItem -Path $FW1360 -File -Recurse -ErrorAction SilentlyContinue)) {
    Write-Host "[WAIT] Put decrypted 13.60 modules in: $FW1360"
}
if (-not (Get-ChildItem -Path $FW1400 -File -Recurse -ErrorAction SilentlyContinue)) {
    Write-Host "[WAIT] Put decrypted 14.00 modules in: $FW1400"
}

& $Python (Join-Path $Root "inventory.py") $FW1360 -o (Join-Path $Out "inventory-1360.json")
& $Python (Join-Path $Root "inventory.py") $FW1400 -o (Join-Path $Out "inventory-1400.json")
& $Python (Join-Path $Root "anchor_map.py") $FW1360 $FW1400 -o (Join-Path $Out "anchor-map.json")

if (Test-Path $Offsets) {
    & $Python (Join-Path $Root "translate_offsets.py") (Join-Path $Out "anchor-map.json") $Offsets -o (Join-Path $Out "offset-candidates.json")
} else {
    Write-Host "[INFO] No offsets-13.60.js yet. Run with -FetchReferenceOffsets or place the file manually."
}

Write-Host "[DONE] Output: $Out"
