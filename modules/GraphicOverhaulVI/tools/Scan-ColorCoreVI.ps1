param(
    [Parameter(Mandatory = $false)]
    [string]$FH6Root,

    [Parameter(Mandatory = $false)]
    [string]$GTAVRoot,

    [Parameter(Mandatory = $false)]
    [string]$OutputRoot = (Join-Path $PSScriptRoot "..\scan-output"),

    [switch]$SkipBinaryStringProbe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScannerVersion = "0.1.0"
$ScanStartedUtc = [DateTime]::UtcNow

function Write-Step {
    param([string]$Message)
    Write-Host "[ColorCoreVI] $Message" -ForegroundColor Cyan
}

function Get-SteamLibraries {
    $libraries = New-Object System.Collections.Generic.List[string]
    $defaultSteam = "C:\Program Files (x86)\Steam"
    if (Test-Path $defaultSteam) {
        $libraries.Add($defaultSteam)
    }

    $vdf = Join-Path $defaultSteam "steamapps\libraryfolders.vdf"
    if (Test-Path $vdf) {
        try {
            $raw = Get-Content -LiteralPath $vdf -Raw
            foreach ($m in [regex]::Matches($raw, '"path"\s+"([^"]+)"')) {
                $p = $m.Groups[1].Value -replace '\\\\', '\'
                if ((Test-Path $p) -and -not $libraries.Contains($p)) {
                    $libraries.Add($p)
                }
            }
        }
        catch {
            Write-Warning "Could not parse Steam libraryfolders.vdf: $($_.Exception.Message)"
        }
    }

    return $libraries
}

function Find-FH6Install {
    $candidates = New-Object System.Collections.Generic.List[string]

    foreach ($steam in (Get-SteamLibraries)) {
        foreach ($name in @("Forza Horizon 6", "ForzaHorizon6")) {
            $p = Join-Path $steam "steamapps\common\$name"
            if (Test-Path $p) { $candidates.Add($p) }
        }
    }

    foreach ($drive in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        foreach ($rel in @(
            "XboxGames\Forza Horizon 6\Content",
            "XboxGames\Forza Horizon 6",
            "Games\Forza Horizon 6",
            "Forza Horizon 6"
        )) {
            $p = Join-Path $drive.Root $rel
            if (Test-Path $p) { $candidates.Add($p) }
        }
    }

    return $candidates | Select-Object -Unique
}

function Find-GTAVInstall {
    $candidates = New-Object System.Collections.Generic.List[string]

    $manifestRoot = "C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests"
    if (Test-Path $manifestRoot) {
        Get-ChildItem -LiteralPath $manifestRoot -Filter *.item -File -ErrorAction SilentlyContinue | ForEach-Object {
            try {
                $manifest = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
                if ($manifest.DisplayName -match 'Grand Theft Auto V' -and $manifest.InstallLocation -and (Test-Path $manifest.InstallLocation)) {
                    $candidates.Add([string]$manifest.InstallLocation)
                }
            }
            catch { }
        }
    }

    foreach ($steam in (Get-SteamLibraries)) {
        foreach ($name in @("Grand Theft Auto V Enhanced", "Grand Theft Auto V", "GTAV Enhanced", "GTAV")) {
            $p = Join-Path $steam "steamapps\common\$name"
            if (Test-Path $p) { $candidates.Add($p) }
        }
    }

    foreach ($drive in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        foreach ($rel in @(
            "XboxGames\Grand Theft Auto V\Content",
            "XboxGames\Grand Theft Auto V Enhanced\Content",
            "Games\Grand Theft Auto V Enhanced",
            "Games\GTAVEnhanced"
        )) {
            $p = Join-Path $drive.Root $rel
            if (Test-Path $p) { $candidates.Add($p) }
        }
    }

    return $candidates | Select-Object -Unique
}

function Resolve-GameRoot {
    param(
        [string]$Provided,
        [string]$GameName,
        [scriptblock]$Finder
    )

    if ($Provided) {
        if (-not (Test-Path -LiteralPath $Provided -PathType Container)) {
            throw "$GameName root does not exist: $Provided"
        }
        return (Resolve-Path -LiteralPath $Provided).Path
    }

    $found = @(& $Finder)
    if ($found.Count -eq 1) {
        Write-Step "Auto-detected $GameName at: $($found[0])"
        return (Resolve-Path -LiteralPath $found[0]).Path
    }

    if ($found.Count -gt 1) {
        Write-Host "Multiple $GameName candidates were found:" -ForegroundColor Yellow
        $found | ForEach-Object { Write-Host "  - $_" }
        throw "Re-run with the explicit -${GameName}Root path."
    }

    return $null
}

$FH6Root = Resolve-GameRoot -Provided $FH6Root -GameName "FH6" -Finder { Find-FH6Install }
$GTAVRoot = Resolve-GameRoot -Provided $GTAVRoot -GameName "GTAV" -Finder { Find-GTAVInstall }

if (-not $FH6Root -and -not $GTAVRoot) {
    Write-Host "Neither game could be auto-detected." -ForegroundColor Red
    Write-Host "Example:" -ForegroundColor Yellow
    Write-Host '.\Scan-ColorCoreVI.ps1 -FH6Root "D:\...\Forza Horizon 6" -GTAVRoot "E:\...\GTAVEnhanced"'
    exit 2
}

$resolvedOutput = [IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

$textExtensions = @(
    ".txt", ".ini", ".cfg", ".json", ".xml", ".yaml", ".yml", ".toml", ".csv",
    ".hlsl", ".fx", ".fxh", ".glsl", ".shader", ".material", ".meta"
)

$shaderBinaryExtensions = @(
    ".cso", ".dxbc", ".dxil", ".spv", ".shaderbin", ".shbin", ".cache"
)

$binaryProbeExtensions = @(
    ".exe", ".dll", ".cso", ".dxbc", ".dxil", ".spv", ".shaderbin", ".shbin", ".bin", ".dat", ".cache"
)

$assetExtensions = @(
    ".cube", ".lut", ".dds", ".hdr", ".exr", ".rpf"
)

$keywordRegex = [regex]::new(
    '(?i)(tone[._\- ]?map|tonemap|hdr10|hdr|pq|st[._\- ]?2084|rec[._\- ]?2020|bt[._\- ]?2020|gamut|scrgb|scene[._\- ]?linear|linear|white[._\- ]?point|exposure|bloom|colour|color|lut|gamma|srgb|display|post[._\- ]?process|aces|nit|nits)',
    [System.Text.RegularExpressions.RegexOptions]::Compiled
)

function Get-RelativePathCompat {
    param([string]$Root, [string]$FullPath)

    $rootUri = New-Object System.Uri(($Root.TrimEnd('\') + '\'))
    $fileUri = New-Object System.Uri($FullPath)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($fileUri).ToString()).Replace('/', '\')
}

function Get-KeywordHits {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) { return @() }
    return @($keywordRegex.Matches($Text) | ForEach-Object { $_.Value.ToLowerInvariant() } | Sort-Object -Unique)
}

function Probe-CandidateContent {
    param([IO.FileInfo]$File)

    $ext = $File.Extension.ToLowerInvariant()
    $hits = New-Object System.Collections.Generic.List[string]
    $probeType = "none"
    $probeError = $null

    try {
        if ($textExtensions -contains $ext -and $File.Length -le 16777216) {
            $probeType = "text"
            $text = Get-Content -LiteralPath $File.FullName -Raw -ErrorAction Stop
            foreach ($hit in (Get-KeywordHits $text)) { $hits.Add($hit) }
        }
        elseif (-not $SkipBinaryStringProbe -and ($binaryProbeExtensions -contains $ext) -and $File.Length -le 67108864) {
            $probeType = "binary-ascii+utf16"
            $bytes = [IO.File]::ReadAllBytes($File.FullName)
            $ascii = [Text.Encoding]::ASCII.GetString($bytes)
            foreach ($hit in (Get-KeywordHits $ascii)) { if (-not $hits.Contains($hit)) { $hits.Add($hit) } }

            $unicode = [Text.Encoding]::Unicode.GetString($bytes)
            foreach ($hit in (Get-KeywordHits $unicode)) { if (-not $hits.Contains($hit)) { $hits.Add($hit) } }
        }
    }
    catch {
        $probeError = $_.Exception.Message
    }

    return [pscustomobject]@{
        ProbeType  = $probeType
        Hits       = ($hits | Sort-Object -Unique) -join ';'
        ProbeError = $probeError
    }
}

$inventory = New-Object System.Collections.Generic.List[object]
$candidates = New-Object System.Collections.Generic.List[object]
$scanErrors = New-Object System.Collections.Generic.List[object]

function Scan-Game {
    param([string]$Game, [string]$Root)

    if (-not $Root) {
        Write-Step "$Game was not supplied/detected; skipping."
        return
    }

    Write-Step "Scanning $Game metadata: $Root"

    $files = Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction SilentlyContinue
    $count = 0

    foreach ($file in $files) {
        $count++
        if (($count % 5000) -eq 0) {
            Write-Step "$Game: indexed $count files..."
        }

        try {
            $ext = $file.Extension.ToLowerInvariant()
            $relative = Get-RelativePathCompat -Root $Root -FullPath $file.FullName
            $nameHits = @(Get-KeywordHits ($file.Name + ' ' + $relative))
            $interestingExt = ($textExtensions -contains $ext) -or ($shaderBinaryExtensions -contains $ext) -or ($assetExtensions -contains $ext)
            $isCandidate = ($nameHits.Count -gt 0) -or $interestingExt -or (($binaryProbeExtensions -contains $ext) -and $file.Length -le 67108864)

            $row = [pscustomobject]@{
                Game                 = $Game
                RelativePath         = $relative
                Extension            = $ext
                SizeBytes            = [Int64]$file.Length
                LastWriteTimeUtc     = $file.LastWriteTimeUtc.ToString('o')
                NameKeywordHits      = ($nameHits -join ';')
                InterestingExtension = [bool]$interestingExt
            }
            $inventory.Add($row)

            if ($isCandidate) {
                $probe = Probe-CandidateContent -File $file
                $sha256 = $null
                try {
                    $sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256 -ErrorAction Stop).Hash
                }
                catch { }

                $priority = "LOW"
                if ($nameHits.Count -gt 0 -and $probe.Hits) { $priority = "HIGH" }
                elseif ($nameHits.Count -gt 0 -or $probe.Hits) { $priority = "MEDIUM" }
                elseif ($shaderBinaryExtensions -contains $ext -or $ext -in @('.cube', '.lut')) { $priority = "MEDIUM" }

                $candidates.Add([pscustomobject]@{
                    Game             = $Game
                    RelativePath     = $relative
                    Extension        = $ext
                    SizeBytes        = [Int64]$file.Length
                    Priority         = $priority
                    NameKeywordHits  = ($nameHits -join ';')
                    ContentHits      = $probe.Hits
                    ProbeType        = $probe.ProbeType
                    ProbeError       = $probe.ProbeError
                    SHA256           = $sha256
                })
            }
        }
        catch {
            $scanErrors.Add([pscustomobject]@{
                Game = $Game
                Path = $file.FullName
                Error = $_.Exception.Message
            })
        }
    }

    Write-Step "$Game: indexed $count files."
}

Scan-Game -Game "FH6" -Root $FH6Root
Scan-Game -Game "GTAVEnhanced" -Root $GTAVRoot

Write-Step "Writing reports..."

$inventoryPath = Join-Path $resolvedOutput "file_inventory.csv"
$candidatePath = Join-Path $resolvedOutput "color_candidates.csv"
$extensionPath = Join-Path $resolvedOutput "extension_summary.csv"
$errorPath = Join-Path $resolvedOutput "scan_errors.csv"
$summaryPath = Join-Path $resolvedOutput "scan_summary.json"
$readmePath = Join-Path $resolvedOutput "README.txt"

$inventory | Sort-Object Game, RelativePath | Export-Csv -LiteralPath $inventoryPath -NoTypeInformation -Encoding UTF8
$candidates | Sort-Object Priority, Game, RelativePath | Export-Csv -LiteralPath $candidatePath -NoTypeInformation -Encoding UTF8

$extensionSummary = $inventory |
    Group-Object Game, Extension |
    ForEach-Object {
        $parts = $_.Name -split ', ', 2
        [pscustomobject]@{
            Game = $parts[0]
            Extension = if ($parts.Count -gt 1) { $parts[1] } else { '' }
            FileCount = $_.Count
            TotalBytes = [Int64](($_.Group | Measure-Object SizeBytes -Sum).Sum)
        }
    } |
    Sort-Object Game, FileCount -Descending

$extensionSummary | Export-Csv -LiteralPath $extensionPath -NoTypeInformation -Encoding UTF8
$scanErrors | Export-Csv -LiteralPath $errorPath -NoTypeInformation -Encoding UTF8

$summary = [ordered]@{
    scannerVersion = $ScannerVersion
    scanStartedUtc = $ScanStartedUtc.ToString('o')
    scanFinishedUtc = [DateTime]::UtcNow.ToString('o')
    fh6Root = $FH6Root
    gtavRoot = $GTAVRoot
    inventoryCount = $inventory.Count
    candidateCount = $candidates.Count
    highPriorityCandidateCount = @($candidates | Where-Object Priority -eq 'HIGH').Count
    mediumPriorityCandidateCount = @($candidates | Where-Object Priority -eq 'MEDIUM').Count
    scanErrorCount = $scanErrors.Count
    binaryStringProbeEnabled = (-not $SkipBinaryStringProbe.IsPresent)
    keywordFamilies = @(
        'tonemap', 'HDR/HDR10', 'PQ/ST.2084', 'BT/Rec.2020', 'gamut', 'scRGB', 'scene-linear',
        'white point', 'exposure', 'bloom', 'color/colour', 'LUT', 'gamma/sRGB', 'display',
        'post-process', 'ACES', 'nits'
    )
    outputFiles = @(
        'file_inventory.csv',
        'color_candidates.csv',
        'extension_summary.csv',
        'scan_errors.csv',
        'scan_summary.json',
        'README.txt'
    )
}

$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

@"
GraphicOverhaulVI / ColorCoreVI local scan
Scanner version: $ScannerVersion

This scan is READ-ONLY. It does not patch or modify FH6 or GTA V Enhanced.

Most useful files to return for analysis:
  1. scan_summary.json
  2. color_candidates.csv
  3. extension_summary.csv
  4. scan_errors.csv

file_inventory.csv can be large; include it when practical because it gives us the complete file map.

The scanner identifies candidates. A keyword hit is evidence that a file deserves inspection, NOT proof of the exact rendering implementation.
"@ | Set-Content -LiteralPath $readmePath -Encoding UTF8

Write-Host ""
Write-Host "=== ColorCoreVI scan complete ===" -ForegroundColor Green
Write-Host "Output: $resolvedOutput"
Write-Host "Files indexed: $($inventory.Count)"
Write-Host "Candidates: $($candidates.Count)"
Write-Host "High-priority candidates: $(@($candidates | Where-Object Priority -eq 'HIGH').Count)"
Write-Host "Scan errors: $($scanErrors.Count)"
Write-Host ""
Write-Host "Return the scan-output folder (ZIP is fine) for the next ColorCoreVI analysis pass." -ForegroundColor Yellow
