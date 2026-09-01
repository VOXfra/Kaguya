param(
    [Parameter(Mandatory=$true)]
    [string]$SoundPackSfxPath,
    [string]$OutputDirectory = ".\\VOX-SoundPack-Manifest"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path $SoundPackSfxPath).Path
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$out = (Resolve-Path $OutputDirectory).Path

$files = Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName
$rows = foreach ($file in $files) {
    $relative = $file.FullName.Substring($root.Length).TrimStart('\\','/')
    $hash = Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256
    [PSCustomObject]@{
        RelativePath = $relative
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

Write-Host "Manifest written to: $out"
Write-Host "Send only SoundPackManifest.csv or SoundPackManifest.txt for reference analysis; do not upload the 1.5+ GB audio pack."
