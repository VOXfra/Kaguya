[CmdletBinding()]
param(
    [string]$Rdr2Path = ""
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Section([string]$Text) {
    Write-Host ''
    Write-Host '============================================================'
    Write-Host "  $Text"
    Write-Host '============================================================'
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (-not (Test-Administrator)) {
    $args = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $PSCommandPath + '"'))
    if ($Rdr2Path) { $args += @('-Rdr2Path', ('"' + $Rdr2Path + '"')) }
    Start-Process powershell.exe -Verb RunAs -ArgumentList ($args -join ' ')
    exit
}

$Root = Split-Path -Parent $PSCommandPath
$Helper = Join-Path $Root 'helper\ArchiveExplorer.exe'
$Compat = Join-Path $Root 'bridge\Rdr2YcdCompat.exe'
$Packer = Join-Path $Root 'bridge\vox-rpf-pack.exe'
$Runtime = Join-Path $Root 'runtime\RDR2AnimationBridgeVI.dll'
$Work = Join-Path $Root '_work-direct'
$Ready = Join-Path $Root 'READY-FOR-GTA'
$OivPath = Join-Path $Ready 'VOX-RDR2-to-GTAV-FIRST-BRIDGE.oiv'

Section 'VOX RDR2 -> GTA V DIRECT ANIMATION BRIDGE'
Write-Host 'No scan batch. This run takes one real RDR2 animation all the way to a GTA V mod package.'

foreach ($required in @($Helper, $Compat, $Packer, $Runtime)) {
    if (-not (Test-Path $required)) { throw "Missing bridge component: $required" }
}

if (-not $Rdr2Path) {
    $defaults = @(
        'C:\Jeux\Red Dead Redemption 2',
        'C:\Program Files\Rockstar Games\Red Dead Redemption 2',
        'C:\Program Files (x86)\Steam\steamapps\common\Red Dead Redemption 2',
        'D:\SteamLibrary\steamapps\common\Red Dead Redemption 2'
    )
    $Rdr2Path = $defaults | Where-Object { Test-Path (Join-Path $_ 'RDR2.exe') } | Select-Object -First 1
    if (-not $Rdr2Path) { $Rdr2Path = Read-Host 'RDR2 folder containing RDR2.exe' }
}
$Rdr2Path = [IO.Path]::GetFullPath($Rdr2Path.Trim('"'))
$RdrExe = Join-Path $Rdr2Path 'RDR2.exe'
if (-not (Test-Path $RdrExe)) { throw "RDR2.exe not found in $Rdr2Path" }

if (Test-Path $Work) { Remove-Item $Work -Recurse -Force }
if (Test-Path $Ready) { Remove-Item $Ready -Recurse -Force }
New-Item -ItemType Directory -Path $Work, $Ready -Force | Out-Null

$env:PATH = "$Rdr2Path;$Root\helper;$env:PATH"

$common = @(
    (Join-Path $Rdr2Path 'anim_0.rpf'),
    (Join-Path $Rdr2Path 'x64\anim_0.rpf'),
    (Join-Path $Rdr2Path 'packs\anim_0.rpf'),
    (Join-Path $Rdr2Path 'x64\packs\anim_0.rpf')
)
$Anim0 = $common | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Anim0) {
    Write-Host '[RPF8] Locating anim_0.rpf in your RDR2 install...'
    $Anim0 = Get-ChildItem -Path $Rdr2Path -Filter 'anim_0.rpf' -File -Recurse -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty FullName -First 1
}
if (-not $Anim0) { throw 'anim_0.rpf was not found.' }
Write-Host "[RPF8] $Anim0"

$proc = Get-Process -Name RDR2 -ErrorAction SilentlyContinue | Select-Object -First 1
$StartedRdr2 = $false
if (-not $proc) {
    Write-Host '[RDR2] Starting RDR2 only long enough to obtain the local TFIT keys...'
    Start-Process -FilePath $RdrExe -WorkingDirectory $Rdr2Path | Out-Null
    $StartedRdr2 = $true
    for ($i = 0; $i -lt 90 -and -not $proc; $i++) {
        Start-Sleep -Seconds 1
        $proc = Get-Process -Name RDR2 -ErrorAction SilentlyContinue | Select-Object -First 1
    }
}
if (-not $proc) { throw 'RDR2.exe did not start.' }
Start-Sleep -Seconds 8

function Extract-RpfEntry {
    param([string]$Archive, [string]$Entry, [string]$Destination, [switch]$FindKeys)
    $env:SWAGE_VERIFY_RPF = $Archive
    $env:SWAGE_EXTRACT_ENTRY = $Entry
    $env:SWAGE_EXTRACT_OUT = $Destination
    if ($FindKeys) { $env:SWAGE_FIND_KEYS = '1' } else { Remove-Item Env:SWAGE_FIND_KEYS -ErrorAction SilentlyContinue }
    try {
        & $Helper
        if ($LASTEXITCODE -ne 0) { throw "Archive extraction failed with code $LASTEXITCODE" }
        if (-not (Test-Path $Destination)) { throw "Archive extraction did not create $Destination" }
    }
    finally {
        Remove-Item Env:SWAGE_VERIFY_RPF -ErrorAction SilentlyContinue
        Remove-Item Env:SWAGE_EXTRACT_ENTRY -ErrorAction SilentlyContinue
        Remove-Item Env:SWAGE_EXTRACT_OUT -ErrorAction SilentlyContinue
        Remove-Item Env:SWAGE_FIND_KEYS -ErrorAction SilentlyContinue
    }
}

Section '1/4 - Pull one generic RDR2 locomotion animation'
$LocoRpf = Join-Path $Work 'clip_mech_loco_m.rpf'
$SourceYcd = Join-Path $Work '0386705F-rdr2.ycd'
# RPF8 hashes the full normalized path without extension, not only the basename.
# JOAAT("anim/ingame/clip_mech_loco_m") = 60D83661.
# If ArchiveExplorer has rdr2_files.txt, Swage exposes the resolved path; otherwise it exposes hash/60D83661.rpf.
$SwageNames = Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'ArchiveExplorer\rdr2_files.txt'
if (Test-Path $SwageNames) {
    $LocoEntry = 'anim/ingame/clip_mech_loco_m.rpf'
    Write-Host '[RPF8] Using resolved archive path: anim/ingame/clip_mech_loco_m.rpf'
} else {
    $LocoEntry = 'hash/60D83661.rpf'
    Write-Host '[RPF8] Using deterministic archive hash: 60D83661'
}
# 0386705F is an unresolved YCD entry listed inside clip_mech_loco_m.rpf.
Extract-RpfEntry -Archive $Anim0 -Entry $LocoEntry -Destination $LocoRpf -FindKeys
Extract-RpfEntry -Archive $LocoRpf -Entry 'hash/0386705F.ycd' -Destination $SourceYcd

if ($StartedRdr2) {
    try {
        $proc.Refresh()
        if (-not $proc.HasExited) {
            Write-Host '[RDR2] Source animation acquired. Asking RDR2 to close cleanly...'
            $null = $proc.CloseMainWindow()
            $null = $proc.WaitForExit(8000)
        }
    } catch { }
}

Section '2/4 - Convert RSC8 ClipDictionary to GTA V RSC7/YCD'
$Converted = Join-Path $Work 'converted'
New-Item -ItemType Directory -Path $Converted -Force | Out-Null
& $Compat $SourceYcd $Converted
if ($LASTEXITCODE -ne 0) {
    $detail = Join-Path $Converted 'bridge-error.txt'
    if (Test-Path $detail) { Get-Content $detail | Select-Object -First 30 }
    throw 'RDR2 -> GTA V YCD compatibility conversion failed.'
}
$ManifestPath = Join-Path $Converted 'bridge-manifest.json'
$GtaYcd = Join-Path $Converted 'vox_rdr2_bridge.ycd'
if (-not (Test-Path $ManifestPath) -or -not (Test-Path $GtaYcd)) { throw 'Bridge converter did not produce its GTA V output.' }
$Manifest = Get-Content $ManifestPath -Raw | ConvertFrom-Json
$Clip = [string]$Manifest.FirstClip
if ([string]::IsNullOrWhiteSpace($Clip)) { throw 'Converted YCD contains no playable clip name.' }
Write-Host "[YCD] GTA V clip selected: $Clip" -ForegroundColor Green

Section '3/4 - Build GTA V DLC + Enhanced runtime'
$DlcDir = Join-Path $Work 'dlc'
New-Item -ItemType Directory -Path $DlcDir -Force | Out-Null
$DlcRpf = Join-Path $DlcDir 'dlc.rpf'
& $Packer $GtaYcd $DlcRpf
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $DlcRpf)) { throw 'Could not build GTA V dlc.rpf.' }

$Ini = @"
[Bridge]
Dict=vox_rdr2_bridge
Clip=$Clip
Key=F8
"@
$IniPath = Join-Path $Work 'RDR2AnimationBridgeVI.ini'
$Ini | Set-Content -Encoding ASCII $IniPath

Section '4/4 - Build installable OIV package'
$Package = Join-Path $Work 'oiv'
$Content = Join-Path $Package 'content'
$PackDlc = Join-Path $Content 'voxrdr2bridge'
New-Item -ItemType Directory -Path $PackDlc -Force | Out-Null
Copy-Item $DlcRpf (Join-Path $PackDlc 'dlc.rpf')
Copy-Item $Runtime (Join-Path $Content 'RDR2AnimationBridgeVI.dll')
Copy-Item $IniPath (Join-Path $Content 'RDR2AnimationBridgeVI.ini')

$Assembly = @"
<?xml version="1.0" encoding="UTF-8"?>
<package version="2.2" id="{A3725DD7-1D9A-4C9A-9A32-0A9C8A6C2A10}" target="Five">
  <metadata>
    <name>VOX RDR2 to GTA V Animation Bridge</name>
    <version><major>0</major><minor>1</minor><tag>FIRST BRIDGE</tag></version>
    <author><displayName>VOX / Kaguya</displayName></author>
    <description><![CDATA[First direct RDR2 animation compatibility bridge for GTA V Enhanced.]]></description>
  </metadata>
  <colors>
    <headerBackground useBlackTextColor="False">`$FF151515</headerBackground>
    <iconBackground>`$FF202020</iconBackground>
  </colors>
  <content>
    <add source="voxrdr2bridge\dlc.rpf">mods\update\x64\dlcpacks\voxrdr2bridge\dlc.rpf</add>
    <add source="RDR2AnimationBridgeVI.dll">scripts\RDR2AnimationBridgeVI.dll</add>
    <add source="RDR2AnimationBridgeVI.ini">scripts\RDR2AnimationBridgeVI.ini</add>
    <archive path="mods\update\update.rpf" createIfNotExist="False" type="RPF7">
      <xml path="common\data\dlclist.xml">
        <add append="Last" xpath="/SMandatoryPacksData/Paths">
          <Item>dlcpacks:\voxrdr2bridge\</Item>
        </add>
      </xml>
    </archive>
  </content>
</package>
"@
$Assembly | Set-Content -Encoding UTF8 (Join-Path $Package 'assembly.xml')

$TempZip = Join-Path $Ready 'bridge-package.zip'
if (Test-Path $TempZip) { Remove-Item $TempZip -Force }
Compress-Archive -Path (Join-Path $Package '*') -DestinationPath $TempZip -CompressionLevel Optimal
Move-Item $TempZip $OivPath -Force

$Fallback = Join-Path $Ready 'MANUAL-FALLBACK'
New-Item -ItemType Directory -Path (Join-Path $Fallback 'voxrdr2bridge') -Force | Out-Null
Copy-Item $DlcRpf (Join-Path $Fallback 'voxrdr2bridge\dlc.rpf')
Copy-Item $Runtime (Join-Path $Fallback 'RDR2AnimationBridgeVI.dll')
Copy-Item $IniPath (Join-Path $Fallback 'RDR2AnimationBridgeVI.ini')
Set-Content -Encoding ASCII (Join-Path $Fallback 'DLCLIST-LINE.txt') '<Item>dlcpacks:\voxrdr2bridge\</Item>'

@"
VOX RDR2 -> GTA V FIRST DIRECT BRIDGE

The source RDR2 animation was converted into a GTA V RSC7 YCD and packaged.
Clip selected: $Clip

NEXT:
1. Install VOX-RDR2-to-GTAV-FIRST-BRIDGE.oiv into the GTA V Enhanced mods folder.
2. Launch Story Mode.
3. Press F8.

This is the first real compatibility attempt. Distortion is useful information: it means the
YCD loaded and the next step is bone retargeting rather than archive/format work.
"@ | Set-Content -Encoding UTF8 (Join-Path $Ready 'README-FIRST-TEST.txt')

Write-Host ''
Write-Host '[READY] The RDR2 animation reached a GTA V installable package.' -ForegroundColor Green
Write-Host "[READY] $OivPath" -ForegroundColor Cyan
Write-Host '[NEXT] Install the OIV, launch GTA V Enhanced Story Mode, press F8.' -ForegroundColor Cyan

try {
    Start-Process $OivPath | Out-Null
} catch {
    Start-Process explorer.exe -ArgumentList ('/select,"' + $OivPath + '"') | Out-Null
}
