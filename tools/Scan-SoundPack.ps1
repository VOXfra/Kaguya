param(
    [Parameter(Mandatory=$false)]
    [string]$SoundPackSfxPath = "",
    [string]$OutputDirectory = ".\VOX-SoundPack-Manifest"
)

$ErrorActionPreference = "Stop"

function Normalize-UserPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }
    return $Path.Trim().Trim('"')
}

function Resolve-SfxRoot {
    param([string]$Path)

    $candidate = Normalize-UserPath $Path
    if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Container)) {
        $resolved = (Resolve-Path -LiteralPath $candidate).Path
        if ((Split-Path -Leaf $resolved) -ieq 'sfx') { return $resolved }

        $direct = Join-Path $resolved 'mods\x64\audio\sfx'
        if (Test-Path -LiteralPath $direct -PathType Container) { return (Resolve-Path -LiteralPath $direct).Path }

        $direct2 = Join-Path $resolved 'x64\audio\sfx'
        if (Test-Path -LiteralPath $direct2 -PathType Container) { return (Resolve-Path -LiteralPath $direct2).Path }
    }

    Write-Host ''
    Write-Host '[INFO] Indique le dossier SFX du Sound Pack.' -ForegroundColor Yellow
    Write-Host 'Tu peux coller soit le dossier ...\mods\x64\audio\sfx,'
    Write-Host 'soit le dossier racine du pack si la structure standard est presente.'
    Write-Host ''

    while ($true) {
        $manual = Read-Host 'Dossier Sound Pack / SFX'
        $candidate = Normalize-UserPath $manual
        if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Container)) {
            $resolved = (Resolve-Path -LiteralPath $candidate).Path
            if ((Split-Path -Leaf $resolved) -ieq 'sfx') { return $resolved }
            $direct = Join-Path $resolved 'mods\x64\audio\sfx'
            if (Test-Path -LiteralPath $direct -PathType Container) { return (Resolve-Path -LiteralPath $direct).Path }
            $direct2 = Join-Path $resolved 'x64\audio\sfx'
            if (Test-Path -LiteralPath $direct2 -PathType Container) { return (Resolve-Path -LiteralPath $direct2).Path }
        }
        Write-Host '[ERREUR] Dossier SFX introuvable dans ce chemin. Reessaie.' -ForegroundColor Red
    }
}

$root = Resolve-SfxRoot $SoundPackSfxPath
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$out = (Resolve-Path -LiteralPath $OutputDirectory).Path

Write-Host "Sound Pack SFX root: $root"

$files = Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName
$rows = foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length) -replace '^[\\/]+',''
    $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
    [PSCustomObject]@{
        RelativePath = $relative
        Extension = $file.Extension.ToLowerInvariant()
        SizeBytes = $file.Length
        SizeMiB = [math]::Round($file.Length / 1MB, 3)
        SHA256 = $hash.Hash.ToLowerInvariant()
        LastWriteTime = $file.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss")
    }
}

$csv = Join-Path $out "SoundPackManifest.csv"
$rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding UTF8

$txt = Join-Path $out "SoundPackManifest.txt"
$lines = @(
    "VOX Sound Pack reference manifest",
    "Generated: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')",
    "Source: $root",
    "Files: $($rows.Count)",
    "Total bytes: $(($rows | Measure-Object -Property SizeBytes -Sum).Sum)",
    "",
    "This manifest contains names, sizes and hashes only. It does not extract, copy or redistribute audio from the RPF archives.",
    ""
)
foreach ($r in $rows) {
    $lines += ("{0}`t{1} bytes`t{2}" -f $r.RelativePath, $r.SizeBytes, $r.SHA256)
}
$lines | Set-Content -LiteralPath $txt -Encoding UTF8

Write-Host ''
Write-Host "Manifest CSV : $csv" -ForegroundColor Green
Write-Host "Manifest TXT : $txt" -ForegroundColor Green
Write-Host 'Envoie uniquement un de ces rapports ; inutile d envoyer le pack audio complet.'
