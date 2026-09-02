param(
    [Parameter(Mandatory=$false)]
    [string]$Root = "",
    [Parameter(Mandatory=$false)]
    [string]$OutFile = "RDR2-reference-manifest.csv"
)

$ErrorActionPreference = 'Stop'

function Normalize-UserPath {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }
    return $Path.Trim().Trim('"')
}

function Test-Rdr2Root {
    param([string]$Path)
    $candidate = Normalize-UserPath $Path
    if (-not $candidate) { return $null }
    if (-not (Test-Path -LiteralPath $candidate -PathType Container)) { return $null }
    if (-not (Test-Path -LiteralPath (Join-Path $candidate 'RDR2.exe') -PathType Leaf)) { return $null }
    return (Resolve-Path -LiteralPath $candidate).Path
}

function Get-RegistryInstallCandidates {
    $results = New-Object System.Collections.Generic.List[string]
    $registryRoots = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*'
    )

    foreach ($registryRoot in $registryRoots) {
        try {
            Get-ItemProperty $registryRoot -ErrorAction SilentlyContinue | ForEach-Object {
                $name = [string]$_.DisplayName
                if ($name -like '*Red Dead Redemption 2*') {
                    if ($_.InstallLocation) { $results.Add([string]$_.InstallLocation) }
                    if ($_.DisplayIcon) {
                        $icon = ([string]$_.DisplayIcon).Trim('"') -replace ',\d+$',''
                        try { $results.Add((Split-Path -Parent $icon)) } catch { }
                    }
                }
            }
        } catch { }
    }

    try {
        $rockstar = Get-ItemProperty 'HKLM:\SOFTWARE\Rockstar Games\Red Dead Redemption 2' -ErrorAction SilentlyContinue
        if ($rockstar.InstallFolder) { $results.Add([string]$rockstar.InstallFolder) }
    } catch { }
    try {
        $rockstar32 = Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Rockstar Games\Red Dead Redemption 2' -ErrorAction SilentlyContinue
        if ($rockstar32.InstallFolder) { $results.Add([string]$rockstar32.InstallFolder) }
    } catch { }

    return $results
}

function Find-Rdr2Root {
    param([string]$Explicit)

    $resolved = Test-Rdr2Root $Explicit
    if ($resolved) { return $resolved }

    foreach ($candidate in (Get-RegistryInstallCandidates)) {
        $resolved = Test-Rdr2Root $candidate
        if ($resolved) { return $resolved }
    }

    $driveLetters = @('C','D','E','F','G','H')
    $suffixes = @(
        'Program Files\Rockstar Games\Red Dead Redemption 2',
        'Program Files (x86)\Steam\steamapps\common\Red Dead Redemption 2',
        'SteamLibrary\steamapps\common\Red Dead Redemption 2',
        'Epic Games\RedDeadRedemption2',
        'Epic Games\Red Dead Redemption 2',
        'Games\Red Dead Redemption 2',
        'Jeux\Red Dead Redemption 2',
        'Jeux Epic\RedDeadRedemption2',
        'Jeux Epic\Red Dead Redemption 2'
    )

    foreach ($drive in $driveLetters) {
        foreach ($suffix in $suffixes) {
            $resolved = Test-Rdr2Root ("{0}:\{1}" -f $drive, $suffix)
            if ($resolved) { return $resolved }
        }
    }

    Write-Host ''
    Write-Host '[INFO] RDR2 n a pas ete trouve automatiquement.' -ForegroundColor Yellow
    Write-Host 'Colle simplement le dossier qui contient RDR2.exe.'
    Write-Host 'Exemple : E:\Jeux\Red Dead Redemption 2'
    Write-Host ''

    while ($true) {
        $manual = Read-Host 'Dossier RDR2'
        $resolved = Test-Rdr2Root $manual
        if ($resolved) { return $resolved }
        Write-Host '[ERREUR] Ce dossier ne contient pas RDR2.exe. Reessaie.' -ForegroundColor Red
    }
}

$rootPath = Find-Rdr2Root $Root
Write-Host "RDR2 root: $rootPath"

$interestingExtensions = @('.rpf','.exe','.dll','.ini','.xml','.meta','.ymt','.ycd','.ytd','.ydd','.yft','.awc','.rel')
$files = Get-ChildItem -LiteralPath $rootPath -File -Recurse -ErrorAction SilentlyContinue | Where-Object {
    $interestingExtensions -contains $_.Extension.ToLowerInvariant()
}

$rows = foreach ($file in $files) {
    $relative = $file.FullName.Substring($rootPath.Length) -replace '^[\\/]+',''
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

Write-Host ''
Write-Host "Manifest: $((Resolve-Path -LiteralPath $OutFile).Path)" -ForegroundColor Green
Write-Host "Summary : $((Resolve-Path -LiteralPath $summaryPath).Path)" -ForegroundColor Green
Write-Host 'No RDR2 game file was modified or copied.'
