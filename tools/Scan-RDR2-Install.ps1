param(
    [Parameter(Mandatory=$false)]
    [string]$Root = "",
    [Parameter(Mandatory=$false)]
    [string]$OutFile = "RDR2-reference-manifest.csv"
)

$ErrorActionPreference = 'Stop'

function Find-Rdr2Root {
    param([string]$Explicit)
    if ($Explicit -and (Test-Path $Explicit)) { return (Resolve-Path $Explicit).Path }

    $candidates = @(
        'C:\\Program Files\\Rockstar Games\\Red Dead Redemption 2',
        'C:\\Program Files (x86)\\Steam\\steamapps\\common\\Red Dead Redemption 2',
        'D:\\SteamLibrary\\steamapps\\common\\Red Dead Redemption 2',
        'E:\\SteamLibrary\\steamapps\\common\\Red Dead Redemption 2',
        'D:\\Games\\Red Dead Redemption 2',
        'E:\\Games\\Red Dead Redemption 2'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path (Join-Path $candidate 'RDR2.exe')) { return (Resolve-Path $candidate).Path }
    }
    throw 'RDR2 installation not found automatically. Re-run with -Root "X:\\...\\Red Dead Redemption 2".'
}

$rootPath = Find-Rdr2Root $Root
Write-Host "RDR2 root: $rootPath"

$interestingExtensions = @('.rpf','.exe','.dll','.ini','.xml','.meta','.ymt','.ycd','.ytd','.ydd','.yft','.awc','.rel')
$files = Get-ChildItem -LiteralPath $rootPath -File -Recurse -ErrorAction SilentlyContinue | Where-Object {
    $interestingExtensions -contains $_.Extension.ToLowerInvariant()
}

$rows = foreach ($file in $files) {
    $relative = $file.FullName.Substring($rootPath.Length).TrimStart('\\')
    [pscustomobject]@{
        RelativePath = $relative
        Extension    = $file.Extension.ToLowerInvariant()
        SizeBytes    = $file.Length
        LastWriteUtc = $file.LastWriteTimeUtc.ToString('o')
    }
}

$rows | Sort-Object RelativePath | Export-Csv -LiteralPath $OutFile -NoTypeInformation -Encoding UTF8

$summary = $rows | Group-Object Extension | Sort-Object Name | ForEach-Object {
    [pscustomobject]@{ Extension=$_.Name; Count=$_.Count; TotalBytes=(($_.Group | Measure-Object SizeBytes -Sum).Sum) }
}
$summaryPath = [System.IO.Path]::ChangeExtension($OutFile, '.summary.csv')
$summary | Export-Csv -LiteralPath $summaryPath -NoTypeInformation -Encoding UTF8

Write-Host "Manifest: $OutFile"
Write-Host "Summary : $summaryPath"
Write-Host 'No RDR2 game file was modified or copied.'
