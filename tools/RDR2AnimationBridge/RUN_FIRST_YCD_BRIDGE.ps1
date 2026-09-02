[CmdletBinding()]
param(
    [string]$GamePath = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

function Write-Section([string]$Text) {
    Write-Host ""
    Write-Host "============================================================"
    Write-Host "  $Text"
    Write-Host "============================================================"
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'))
    if ($GamePath) {
        $args += @('-GamePath', ('"' + $GamePath + '"'))
    }
    Start-Process powershell.exe -Verb RunAs -ArgumentList ($args -join ' ')
    exit
}

$Root = Split-Path -Parent $PSCommandPath
$Helper = Join-Path $Root 'helper\ArchiveExplorer.exe'
$Work = Join-Path $Root '_work'
$Out = Join-Path $Root 'RDR2-YCD-SAMPLES'
$Logs = Join-Path $Root 'logs'
$ResultZip = Join-Path $Root 'VOX-RDR2-FIRST-YCD-BRIDGE-RESULTS.zip'

Write-Section 'VOX RDR2 -> GTA V Animation Bridge - First real YCD batch'
Write-Host 'This run reads your local RDR2 files only.'
Write-Host 'It extracts 15 complete RSC8/YCD samples for bridge development.'

if (-not (Test-Path $Helper)) {
    throw "Missing helper: $Helper"
}

if (-not $GamePath) {
    $default = 'C:\Jeux\Red Dead Redemption 2'
    if (Test-Path (Join-Path $default 'RDR2.exe')) {
        $GamePath = $default
    } else {
        $GamePath = Read-Host 'RDR2 folder containing RDR2.exe'
    }
}
$GamePath = [IO.Path]::GetFullPath($GamePath.Trim('"'))
$RdrExe = Join-Path $GamePath 'RDR2.exe'
if (-not (Test-Path $RdrExe)) {
    throw "RDR2.exe not found in: $GamePath"
}

foreach ($dir in @($Work, $Out, $Logs)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}
if (Test-Path $ResultZip) { Remove-Item $ResultZip -Force }

# Oodle is loaded by name by the patched helper. Put the user's game directory
# first so no Oodle DLL is redistributed with this tool.
$env:PATH = "$GamePath;$Root\helper;$env:PATH"

# Locate anim_0.rpf without assuming one Rockstar install layout.
$common = @(
    (Join-Path $GamePath 'anim_0.rpf'),
    (Join-Path $GamePath 'x64\anim_0.rpf'),
    (Join-Path $GamePath 'packs\anim_0.rpf'),
    (Join-Path $GamePath 'x64\packs\anim_0.rpf')
)
$Anim0 = $common | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Anim0) {
    Write-Host '[SCAN] Searching the RDR2 install for anim_0.rpf...'
    $Anim0 = Get-ChildItem -Path $GamePath -Filter 'anim_0.rpf' -File -Recurse -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName -First 1
}
if (-not $Anim0) {
    throw 'anim_0.rpf was not found in the RDR2 installation.'
}
Write-Host "[RPF8] anim_0.rpf: $Anim0"

# RDR2's TFIT2 keys exist in the running process. Start the game if necessary;
# if Rockstar Launcher intercepts the executable, the process detection below
# still waits for the actual RDR2.exe.
$proc = Get-Process -Name RDR2 -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $proc) {
    Write-Host '[RDR2] RDR2.exe is not running. Starting it...'
    Start-Process -FilePath $RdrExe -WorkingDirectory $GamePath | Out-Null
    for ($i = 0; $i -lt 90 -and -not $proc; $i++) {
        Start-Sleep -Seconds 1
        $proc = Get-Process -Name RDR2 -ErrorAction SilentlyContinue | Select-Object -First 1
    }
}
if (-not $proc) {
    throw 'RDR2.exe did not start. Launch Story Mode, then rerun this file.'
}
Write-Host "[RDR2] Process detected (PID $($proc.Id))."
Start-Sleep -Seconds 8

function Invoke-Helper {
    param(
        [Parameter(Mandatory=$true)][string]$Archive,
        [Parameter(Mandatory=$true)][string]$Entry,
        [Parameter(Mandatory=$true)][string]$Destination,
        [Parameter(Mandatory=$true)][string]$LogName,
        [switch]$FindKeys
    )

    $env:SWAGE_VERIFY_RPF = $Archive
    $env:SWAGE_EXTRACT_ENTRY = $Entry
    $env:SWAGE_EXTRACT_OUT = $Destination
    if ($FindKeys) { $env:SWAGE_FIND_KEYS = '1' } else { Remove-Item Env:SWAGE_FIND_KEYS -ErrorAction SilentlyContinue }

    $log = Join-Path $Logs $LogName
    & $Helper 2>&1 | Tee-Object -FilePath $log
    $exitCode = $LASTEXITCODE

    Remove-Item Env:SWAGE_VERIFY_RPF -ErrorAction SilentlyContinue
    Remove-Item Env:SWAGE_EXTRACT_ENTRY -ErrorAction SilentlyContinue
    Remove-Item Env:SWAGE_EXTRACT_OUT -ErrorAction SilentlyContinue
    Remove-Item Env:SWAGE_FIND_KEYS -ErrorAction SilentlyContinue

    if ($exitCode -ne 0) {
        throw "Archive helper failed with code $exitCode. See $log"
    }
    if (-not (Test-Path $Destination)) {
        throw "Expected output was not created: $Destination"
    }
}

function Test-Rsc8 {
    param([string]$Path, [string]$Hash, [string]$Label, [string]$Family)
    if (-not (Test-Path $Path)) {
        return [ordered]@{ family=$Family; hash=$Hash; label=$Label; success=$false; bytes=0; magic=''; reason='missing' }
    }
    $bytes = [IO.File]::ReadAllBytes($Path)
    $magic = if ($bytes.Length -ge 4) { [Text.Encoding]::ASCII.GetString($bytes, 0, 4) } else { '' }
    $ok = ($magic -eq 'RSC8' -and $bytes.Length -gt 32)
    return [ordered]@{
        family=$Family
        hash=$Hash
        label=$Label
        success=$ok
        bytes=$bytes.Length
        magic=$magic
        sha256=(Get-FileHash -Algorithm SHA256 $Path).Hash.ToLowerInvariant()
        reason=$(if ($ok) { 'ok' } else { 'expected RSC8 header' })
    }
}

Write-Section '1/3 - Extract nested animation archives + discover keys'
$DoorsRpf = Join-Path $Work 'clip_mech_doors.rpf'
$LocoRpf = Join-Path $Work 'clip_mech_loco_m.rpf'

# JOAAT("clip_mech_doors") = 3EF80FE7
# JOAAT("clip_mech_loco_m") = 1C5D5822
Invoke-Helper -Archive $Anim0 -Entry 'hash/3EF80FE7.rpf' -Destination $DoorsRpf -LogName '01-doors-archive.log' -FindKeys
Invoke-Helper -Archive $Anim0 -Entry 'hash/1C5D5822.rpf' -Destination $LocoRpf -LogName '02-locomotion-archive.log'

Write-Section '2/3 - Extract complete RSC8 YCD samples'
$Targets = @(
    [ordered]@{ family='doors'; archive=$DoorsRpf; hash='78B43BB4'; label='locked shoulder push crouch handle right' },
    [ordered]@{ family='doors'; archive=$DoorsRpf; hash='ABD68E1C'; label='locked shoulder push crouch handle left' },
    [ordered]@{ family='doors'; archive=$DoorsRpf; hash='02CBB8A2'; label='locked generic kick fail' },
    [ordered]@{ family='doors'; archive=$DoorsRpf; hash='1EFBD06E'; label='locked generic kick success' },
    [ordered]@{ family='doors'; archive=$DoorsRpf; hash='1750D0E8'; label='locked generic barge action' },
    [ordered]@{ family='doors'; archive=$DoorsRpf; hash='E6439B1D'; label='locked generic barge fail' },
    [ordered]@{ family='doors'; archive=$DoorsRpf; hash='79C87AD7'; label='generic unarmed door interaction' },
    [ordered]@{ family='doors'; archive=$DoorsRpf; hash='B8920710'; label='locked generic kick dictionary' },
    [ordered]@{ family='doors'; archive=$DoorsRpf; hash='F3D0411F'; label='two handed door interaction' },

    [ordered]@{ family='locomotion'; archive=$LocoRpf; hash='0386705F'; label='generic action unarmed idle' },
    [ordered]@{ family='locomotion'; archive=$LocoRpf; hash='01CBE7B1'; label='cowboy normal unarmed idle' },
    [ordered]@{ family='locomotion'; archive=$LocoRpf; hash='050E66E1'; label='Arthur action longarm idle' },
    [ordered]@{ family='locomotion'; archive=$LocoRpf; hash='05B29ED2'; label='Arthur avoid unarmed soft walk' },
    [ordered]@{ family='locomotion'; archive=$LocoRpf; hash='0955AABF'; label='Arthur normal unarmed walk interior' },
    [ordered]@{ family='locomotion'; archive=$LocoRpf; hash='0DC9FCFB'; label='Arthur tired low stamina walk' }
)

$Results = New-Object System.Collections.Generic.List[object]
$index = 0
foreach ($target in $Targets) {
    $index++
    $familyDir = Join-Path $Out $target.family
    New-Item -ItemType Directory -Path $familyDir -Force | Out-Null
    $dest = Join-Path $familyDir ($target.hash + '.ycd')
    Write-Host "[$index/$($Targets.Count)] $($target.hash) - $($target.label)"
    try {
        Invoke-Helper -Archive $target.archive -Entry ("hash/{0}.ycd" -f $target.hash) -Destination $dest -LogName ("ycd-{0}.log" -f $target.hash)
        $Results.Add([pscustomobject](Test-Rsc8 -Path $dest -Hash $target.hash -Label $target.label -Family $target.family))
    } catch {
        $Results.Add([pscustomobject][ordered]@{
            family=$target.family; hash=$target.hash; label=$target.label; success=$false; bytes=0; magic=''; sha256=''; reason=$_.Exception.Message
        })
        Write-Warning $_.Exception.Message
    }
}

Write-Section '3/3 - Validate + package one result ZIP'
$okCount = @($Results | Where-Object { $_.success }).Count
$summary = [ordered]@{
    tool='VOX RDR2 Animation Bridge'
    stage='first-complete-ycd-batch'
    source_archive=$Anim0
    total=$Targets.Count
    rsc8_ok=$okCount
    failed=$Targets.Count-$okCount
    generated_at=(Get-Date).ToUniversalTime().ToString('o')
    samples=$Results
}
$summary | ConvertTo-Json -Depth 8 | Set-Content -Encoding UTF8 (Join-Path $Out 'RESULT.json')
$Results | Export-Csv -NoTypeInformation -Encoding UTF8 (Join-Path $Out 'RESULT.csv')

if ($okCount -eq 0) {
    Write-Host '[FAIL] No complete RSC8 YCD was extracted.' -ForegroundColor Red
} else {
    Write-Host "[OK] $okCount/$($Targets.Count) complete RSC8 YCD samples extracted." -ForegroundColor Green
}

$PackageRoot = Join-Path $Work 'package'
New-Item -ItemType Directory -Path $PackageRoot -Force | Out-Null
Copy-Item $Out (Join-Path $PackageRoot 'RDR2-YCD-SAMPLES') -Recurse
Copy-Item $Logs (Join-Path $PackageRoot 'logs') -Recurse
Compress-Archive -Path (Join-Path $PackageRoot '*') -DestinationPath $ResultZip -CompressionLevel Optimal -Force

Write-Host ""
Write-Host 'Send back this single file:' -ForegroundColor Cyan
Write-Host $ResultZip -ForegroundColor Cyan
Write-Host ""
Read-Host 'Press Enter to close'
