param(
    [Parameter(Mandatory = $false)]
    [string]$FH6Root,

    [Parameter(Mandatory = $false)]
    [string]$GTAVRoot,

    [Parameter(Mandatory = $false)]
    [string]$OutputRoot,

    [switch]$SkipBinaryStringProbe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$ScannerVersion = "0.1.5"
$ScanStartedUtc = [DateTime]::UtcNow

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot "..\scan-output"
}

function Write-Step {
    param([string]$Message)
    Write-Host ("[ColorCoreVI] {0}" -f $Message) -ForegroundColor Cyan
}

function Get-SteamLibraries {
    $result = New-Object System.Collections.Generic.List[string]
    $steam = "C:\Program Files (x86)\Steam"
    if (Test-Path -LiteralPath $steam -PathType Container) {
        $result.Add($steam)
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path -LiteralPath $vdf -PathType Leaf) {
            try {
                $raw = Get-Content -LiteralPath $vdf -Raw
                $matches = [regex]::Matches($raw, '"path"\s+"([^"]+)"')
                foreach ($match in $matches) {
                    $path = $match.Groups[1].Value -replace '\\\\', '\'
                    if ((Test-Path -LiteralPath $path -PathType Container) -and -not $result.Contains($path)) {
                        $result.Add($path)
                    }
                }
            }
            catch {
                Write-Warning ("Could not parse Steam libraries: {0}" -f $_.Exception.Message)
            }
        }
    }
    return $result
}

function Find-GameCandidates {
    param([ValidateSet("FH6", "GTAV")][string]$Game)

    $result = New-Object System.Collections.Generic.List[string]
    $steamNames = @()
    $commonPaths = @()

    if ($Game -eq "FH6") {
        $steamNames = @("Forza Horizon 6", "ForzaHorizon6")
        $commonPaths = @(
            "XboxGames\Forza Horizon 6\Content",
            "XboxGames\Forza Horizon 6",
            "Games\Forza Horizon 6",
            "Forza Horizon 6"
        )
    }
    else {
        $steamNames = @("Grand Theft Auto V Enhanced", "Grand Theft Auto V", "GTAV Enhanced", "GTAV")
        $commonPaths = @(
            "XboxGames\Grand Theft Auto V Enhanced\Content",
            "XboxGames\Grand Theft Auto V\Content",
            "Games\Grand Theft Auto V Enhanced",
            "Games\GTAVEnhanced"
        )

        $manifestRoot = "C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests"
        if (Test-Path -LiteralPath $manifestRoot -PathType Container) {
            $manifests = Get-ChildItem -LiteralPath $manifestRoot -Filter *.item -File -ErrorAction SilentlyContinue
            foreach ($item in $manifests) {
                try {
                    $manifest = Get-Content -LiteralPath $item.FullName -Raw | ConvertFrom-Json
                    if (($manifest.DisplayName -match 'Grand Theft Auto V') -and $manifest.InstallLocation) {
                        $install = [string]$manifest.InstallLocation
                        if ((Test-Path -LiteralPath $install -PathType Container) -and -not $result.Contains($install)) {
                            $result.Add($install)
                        }
                    }
                }
                catch { }
            }
        }
    }

    foreach ($library in (Get-SteamLibraries)) {
        foreach ($name in $steamNames) {
            $candidate = Join-Path $library ("steamapps\common\{0}" -f $name)
            if ((Test-Path -LiteralPath $candidate -PathType Container) -and -not $result.Contains($candidate)) {
                $result.Add($candidate)
            }
        }
    }

    $drives = Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue
    foreach ($drive in $drives) {
        foreach ($relative in $commonPaths) {
            $candidate = Join-Path $drive.Root $relative
            if ((Test-Path -LiteralPath $candidate -PathType Container) -and -not $result.Contains($candidate)) {
                $result.Add($candidate)
            }
        }
    }

    return $result
}

function Resolve-GameRoot {
    param(
        [string]$Provided,
        [ValidateSet("FH6", "GTAV")][string]$Game,
        [string]$FriendlyName
    )

    if (-not [string]::IsNullOrWhiteSpace($Provided)) {
        $clean = $Provided.Trim().Trim('"')
        if (-not (Test-Path -LiteralPath $clean -PathType Container)) {
            throw ("{0} root does not exist: {1}" -f $FriendlyName, $clean)
        }
        return (Resolve-Path -LiteralPath $clean).Path
    }

    $found = @(Find-GameCandidates -Game $Game)
    if ($found.Count -eq 1) {
        Write-Step ("Auto-detected {0}: {1}" -f $FriendlyName, $found[0])
        return (Resolve-Path -LiteralPath $found[0]).Path
    }

    if ($found.Count -gt 1) {
        Write-Host ("Multiple {0} candidates were found:" -f $FriendlyName) -ForegroundColor Yellow
        for ($i = 0; $i -lt $found.Count; $i++) {
            Write-Host ("  [{0}] {1}" -f ($i + 1), $found[$i])
        }
        $choice = Read-Host "Enter a number, or press Enter to paste another path"
        if ($choice -match '^\d+$') {
            $index = [int]$choice - 1
            if (($index -ge 0) -and ($index -lt $found.Count)) {
                return (Resolve-Path -LiteralPath $found[$index]).Path
            }
        }
    }

    while ($true) {
        $manual = Read-Host ("Paste the {0} install folder, or press Enter to cancel" -f $FriendlyName)
        if ([string]::IsNullOrWhiteSpace($manual)) {
            return $null
        }
        $manual = $manual.Trim().Trim('"')
        if (Test-Path -LiteralPath $manual -PathType Container) {
            return (Resolve-Path -LiteralPath $manual).Path
        }
        Write-Host "That folder does not exist. Try again." -ForegroundColor Red
    }
}

function Get-RelativePathCompat {
    param([string]$Root, [string]$FullPath)
    $rootUri = New-Object System.Uri(($Root.TrimEnd('\') + '\'))
    $fileUri = New-Object System.Uri($FullPath)
    return [Uri]::UnescapeDataString($rootUri.MakeRelativeUri($fileUri).ToString()).Replace('/', '\')
}

$KeywordPattern = '(?i)(tone[._\- ]?map|tonemap|hdr10|hdr|pq|st[._\- ]?2084|rec[._\- ]?2020|bt[._\- ]?2020|gamut|scrgb|scene[._\- ]?linear|linear|white[._\- ]?point|exposure|bloom|colour|color|lut|gamma|srgb|display|post[._\- ]?process|aces|nit|nits)'
$TextExtensions = @('.txt','.ini','.cfg','.json','.xml','.yaml','.yml','.toml','.csv','.hlsl','.fx','.fxh','.glsl','.shader','.material','.meta')
$ShaderExtensions = @('.cso','.dxbc','.dxil','.spv','.shaderbin','.shbin','.cache')
$BinaryExtensions = @('.exe','.dll','.cso','.dxbc','.dxil','.spv','.shaderbin','.shbin')
$NamedBinaryExtensions = @('.bin','.dat','.cache')
$DirectAssetExtensions = @('.cube','.lut','.hdr','.exr')
$InventoryOnlyExtensions = @('.dds','.rpf')

function Get-KeywordHits {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) { return @() }
    $matches = [regex]::Matches($Text, $KeywordPattern)
    return @($matches | ForEach-Object { $_.Value.ToLowerInvariant() } | Sort-Object -Unique)
}

function Probe-File {
    param([System.IO.FileInfo]$File)

    $hits = New-Object System.Collections.Generic.List[string]
    $probeType = 'none'
    $probeError = $null
    $extension = $File.Extension.ToLowerInvariant()

    try {
        if (($TextExtensions -contains $extension) -and ($File.Length -le 16777216)) {
            $probeType = 'text'
            $text = Get-Content -LiteralPath $File.FullName -Raw -ErrorAction Stop
            foreach ($hit in (Get-KeywordHits -Text $text)) {
                if (-not $hits.Contains($hit)) { $hits.Add($hit) }
            }
        }
        elseif ((-not $SkipBinaryStringProbe.IsPresent) -and ($BinaryExtensions -contains $extension) -and ($File.Length -le 67108864)) {
            $probeType = 'binary-ascii'
            $bytes = [System.IO.File]::ReadAllBytes($File.FullName)
            $text = [System.Text.Encoding]::ASCII.GetString($bytes)
            foreach ($hit in (Get-KeywordHits -Text $text)) {
                if (-not $hits.Contains($hit)) { $hits.Add($hit) }
            }
        }
    }
    catch {
        $probeError = $_.Exception.Message
    }

    return [pscustomobject]@{
        ProbeType = $probeType
        Hits = (($hits | Sort-Object -Unique) -join ';')
        Error = $probeError
    }
}

$FH6Root = Resolve-GameRoot -Provided $FH6Root -Game 'FH6' -FriendlyName 'Forza Horizon 6'
$GTAVRoot = Resolve-GameRoot -Provided $GTAVRoot -Game 'GTAV' -FriendlyName 'GTA V Enhanced'

if ([string]::IsNullOrWhiteSpace($FH6Root) -or [string]::IsNullOrWhiteSpace($GTAVRoot)) {
    Write-Host "Both game folders are required. No game file was modified." -ForegroundColor Red
    exit 2
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputRoot)
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

$inventory = New-Object System.Collections.Generic.List[object]
$candidates = New-Object System.Collections.Generic.List[object]
$scanErrors = New-Object System.Collections.Generic.List[object]

function Scan-Game {
    param([string]$Game, [string]$Root)

    Write-Step ("Scanning {0}: {1}" -f $Game, $Root)
    $count = 0
    $files = Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction SilentlyContinue

    foreach ($file in $files) {
        $count++
        if (($count % 5000) -eq 0) {
            Write-Step ("{0}: indexed {1} files..." -f $Game, $count)
        }

        try {
            $extension = $file.Extension.ToLowerInvariant()
            $relative = Get-RelativePathCompat -Root $Root -FullPath $file.FullName
            $nameHits = @(Get-KeywordHits -Text ($file.Name + ' ' + $relative))
            $interestingExtension = ($TextExtensions -contains $extension) -or ($ShaderExtensions -contains $extension) -or ($DirectAssetExtensions -contains $extension) -or ($InventoryOnlyExtensions -contains $extension)

            $inventory.Add([pscustomobject]@{
                Game = $Game
                RelativePath = $relative
                Extension = $extension
                SizeBytes = [Int64]$file.Length
                LastWriteTimeUtc = $file.LastWriteTimeUtc.ToString('o')
                NameKeywordHits = ($nameHits -join ';')
                InterestingExtension = [bool]$interestingExtension
            })

            $binaryNamed = ($NamedBinaryExtensions -contains $extension) -and ($nameHits.Count -gt 0) -and ($file.Length -le 67108864)
            $candidate = ($nameHits.Count -gt 0) -or ($TextExtensions -contains $extension) -or ($ShaderExtensions -contains $extension) -or ($DirectAssetExtensions -contains $extension) -or $binaryNamed

            if ($candidate) {
                $probe = Probe-File -File $file
                $hashState = 'SKIPPED'
                $sha256 = $null
                if ($file.Length -le 268435456) {
                    try {
                        $sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256 -ErrorAction Stop).Hash
                        $hashState = 'SHA256'
                    }
                    catch {
                        $hashState = 'ERROR'
                    }
                }
                else {
                    $hashState = 'SKIPPED_GT_256MB'
                }

                $priority = 'LOW'
                if (($nameHits.Count -gt 0) -and (-not [string]::IsNullOrWhiteSpace($probe.Hits))) { $priority = 'HIGH' }
                elseif (($nameHits.Count -gt 0) -or (-not [string]::IsNullOrWhiteSpace($probe.Hits))) { $priority = 'MEDIUM' }
                elseif (($ShaderExtensions -contains $extension) -or ($DirectAssetExtensions -contains $extension)) { $priority = 'MEDIUM' }

                $candidates.Add([pscustomobject]@{
                    Game = $Game
                    RelativePath = $relative
                    Extension = $extension
                    SizeBytes = [Int64]$file.Length
                    Priority = $priority
                    NameKeywordHits = ($nameHits -join ';')
                    ContentHits = $probe.Hits
                    ProbeType = $probe.ProbeType
                    ProbeError = $probe.Error
                    HashState = $hashState
                    SHA256 = $sha256
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

    Write-Step ("{0}: indexed {1} files." -f $Game, $count)
}

Scan-Game -Game 'FH6' -Root $FH6Root
Scan-Game -Game 'GTAVEnhanced' -Root $GTAVRoot
Write-Step 'Writing reports...'

$inventoryPath = Join-Path $resolvedOutput 'file_inventory.csv'
$candidatePath = Join-Path $resolvedOutput 'color_candidates.csv'
$extensionPath = Join-Path $resolvedOutput 'extension_summary.csv'
$errorPath = Join-Path $resolvedOutput 'scan_errors.csv'
$summaryPath = Join-Path $resolvedOutput 'scan_summary.json'
$readmePath = Join-Path $resolvedOutput 'README.txt'

$inventory | Sort-Object Game, RelativePath | Export-Csv -LiteralPath $inventoryPath -NoTypeInformation -Encoding UTF8
$candidates | Sort-Object Game, Priority, RelativePath | Export-Csv -LiteralPath $candidatePath -NoTypeInformation -Encoding UTF8

$extensionSummary = $inventory | Group-Object Game, Extension | ForEach-Object {
    $parts = $_.Name -split ', ', 2
    $extensionName = ''
    if ($parts.Count -gt 1) { $extensionName = $parts[1] }
    [pscustomobject]@{
        Game = $parts[0]
        Extension = $extensionName
        FileCount = $_.Count
        TotalBytes = [Int64](($_.Group | Measure-Object SizeBytes -Sum).Sum)
    }
} | Sort-Object Game, FileCount -Descending
$extensionSummary | Export-Csv -LiteralPath $extensionPath -NoTypeInformation -Encoding UTF8

if ($scanErrors.Count -gt 0) {
    $scanErrors | Export-Csv -LiteralPath $errorPath -NoTypeInformation -Encoding UTF8
}
else {
    Set-Content -LiteralPath $errorPath -Value '"Game","Path","Error"' -Encoding UTF8
}

$summary = [ordered]@{
    scannerVersion = $ScannerVersion
    scanStartedUtc = $ScanStartedUtc.ToString('o')
    scanFinishedUtc = [DateTime]::UtcNow.ToString('o')
    fh6Root = $FH6Root
    gtavRoot = $GTAVRoot
    inventoryCount = $inventory.Count
    candidateCount = $candidates.Count
    highPriorityCandidateCount = @($candidates | Where-Object { $_.Priority -eq 'HIGH' }).Count
    mediumPriorityCandidateCount = @($candidates | Where-Object { $_.Priority -eq 'MEDIUM' }).Count
    scanErrorCount = $scanErrors.Count
    binaryStringProbeEnabled = (-not $SkipBinaryStringProbe.IsPresent)
    largeCandidateHashLimitBytes = 268435456
    keywordFamilies = @('tonemap','HDR/HDR10','PQ/ST.2084','BT/Rec.2020','gamut','scRGB','scene-linear','white point','exposure','bloom','color/colour','LUT','gamma/sRGB','display','post-process','ACES','nits')
    outputFiles = @('file_inventory.csv','color_candidates.csv','extension_summary.csv','scan_errors.csv','scan_summary.json','README.txt')
}
$summary | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$readme = @(
    'GraphicOverhaulVI / ColorCoreVI local scan',
    ("Scanner version: {0}" -f $ScannerVersion),
    '',
    'READ-ONLY. No FH6 or GTA V Enhanced file is modified.',
    'ZIP the scan-output folder and send it back in ChatGPT.'
)
$readme | Set-Content -LiteralPath $readmePath -Encoding UTF8

Write-Host ''
Write-Host '=== ColorCoreVI scan complete ===' -ForegroundColor Green
Write-Host ("Output: {0}" -f $resolvedOutput)
Write-Host ("Files indexed: {0}" -f $inventory.Count)
Write-Host ("Candidates: {0}" -f $candidates.Count)
Write-Host ("High-priority candidates: {0}" -f @($candidates | Where-Object { $_.Priority -eq 'HIGH' }).Count)
Write-Host ("Scan errors: {0}" -f $scanErrors.Count)
Write-Host ''
Write-Host 'ZIP the scan-output folder and send it back in ChatGPT.' -ForegroundColor Yellow
