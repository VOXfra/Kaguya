param(
    [Parameter(Mandatory = $false)]
    [string]$FH6Root,

    [Parameter(Mandatory = $false)]
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$CollectorVersion = "0.1.0"
$StartedUtc = [DateTime]::UtcNow

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $PSScriptRoot "..\stage2-output"
}

function Write-Step {
    param([string]$Message)
    Write-Host ("[ColorCoreVI/P0003] {0}" -f $Message) -ForegroundColor Cyan
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

function Find-FH6Candidates {
    $result = New-Object System.Collections.Generic.List[string]

    foreach ($library in (Get-SteamLibraries)) {
        foreach ($name in @("Forza Horizon 6", "ForzaHorizon6")) {
            $candidate = Join-Path $library ("steamapps\common\{0}" -f $name)
            if ((Test-Path -LiteralPath $candidate -PathType Container) -and -not $result.Contains($candidate)) {
                $result.Add($candidate)
            }
        }
    }

    $commonPaths = @(
        "XboxGames\Forza Horizon 6\Content",
        "XboxGames\Forza Horizon 6",
        "Games\Forza Horizon 6",
        "Forza Horizon 6"
    )

    foreach ($drive in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        foreach ($relative in $commonPaths) {
            $candidate = Join-Path $drive.Root $relative
            if ((Test-Path -LiteralPath $candidate -PathType Container) -and -not $result.Contains($candidate)) {
                $result.Add($candidate)
            }
        }
    }

    return $result
}

function Resolve-FH6Root {
    param([string]$Provided)

    if (-not [string]::IsNullOrWhiteSpace($Provided)) {
        $clean = $Provided.Trim().Trim('"')
        if (-not (Test-Path -LiteralPath $clean -PathType Container)) {
            throw ("Forza Horizon 6 root does not exist: {0}" -f $clean)
        }
        return (Resolve-Path -LiteralPath $clean).Path
    }

    $found = @(Find-FH6Candidates)
    if ($found.Count -eq 1) {
        Write-Step ("Auto-detected Forza Horizon 6: {0}" -f $found[0])
        return (Resolve-Path -LiteralPath $found[0]).Path
    }

    if ($found.Count -gt 1) {
        Write-Host "Multiple Forza Horizon 6 candidates were found:" -ForegroundColor Yellow
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
        $manual = Read-Host "Paste the Forza Horizon 6 install folder, or press Enter to cancel"
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

function Get-SafeArchiveId {
    param([string]$RelativePath)
    $id = $RelativePath -replace '[\\/:*?"<>|]', '_'
    return $id.Trim('_')
}

function Get-KeywordHits {
    param([string]$Text)

    if ([string]::IsNullOrEmpty($Text)) {
        return @()
    }

    $pattern = '(?i)(tonemap|tone[._\- ]?map|hdr10|\bhdr\b|st[._\- ]?2084|\bpq\b|bt[._\- ]?2020|rec[._\- ]?2020|gamut|scrgb|scene[._\- ]?linear|white[._\- ]?point|exposure|eye[._\- ]?adapt|adaptation|bloom|colour|color|\blut\b|gamma|srgb|display[._\- ]?map|displaymapper|post[._\- ]?effect|post[._\- ]?process|aces|\bnits?\b)'
    $matches = [regex]::Matches($Text, $pattern)
    return @($matches | ForEach-Object { $_.Value.ToLowerInvariant() } | Sort-Object -Unique)
}

function Copy-LooseEvidence {
    param(
        [string]$Root,
        [string]$RelativePath,
        [string]$DestinationRoot,
        [System.Collections.Generic.List[object]]$SourceManifest
    )

    $source = Join-Path $Root $RelativePath
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        $SourceManifest.Add([pscustomobject]@{
            Kind = "Loose"
            RelativePath = $RelativePath
            State = "MISSING"
            SizeBytes = 0
            SHA256 = ""
        })
        return
    }

    $destination = Join-Path $DestinationRoot $RelativePath
    $destinationDirectory = Split-Path -Parent $destination
    New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Force

    $file = Get-Item -LiteralPath $source
    $hash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash

    $SourceManifest.Add([pscustomobject]@{
        Kind = "Loose"
        RelativePath = $RelativePath
        State = "COPIED"
        SizeBytes = [Int64]$file.Length
        SHA256 = $hash
    })
}

Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$FH6Root = Resolve-FH6Root -Provided $FH6Root
if ([string]::IsNullOrWhiteSpace($FH6Root)) {
    Write-Host "Forza Horizon 6 is required. No source file was modified." -ForegroundColor Red
    exit 2
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $resolvedOutput -PathType Container) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $resolvedOutput | Out-Null

$looseRoot = Join-Path $resolvedOutput "loose"
$archiveCopyRoot = Join-Path $resolvedOutput "archives"
$extractedRoot = Join-Path $resolvedOutput "extracted"
New-Item -ItemType Directory -Force -Path $looseRoot, $archiveCopyRoot, $extractedRoot | Out-Null

$sourceManifest = New-Object System.Collections.Generic.List[object]
$archiveEntries = New-Object System.Collections.Generic.List[object]
$evidenceHits = New-Object System.Collections.Generic.List[object]
$errors = New-Object System.Collections.Generic.List[object]

$looseTargets = @(
    "media\timeofday\TimeOfDay.xml",
    "media\timeofday\TimeOfDayA.xml",
    "media\tracks\DefaultTrackSettings.xml",
    "media\weather\Presets.xml",
    "media\weather\Presets_Island.xml",
    "media\weather\SkyZonesEffectsDefinitions.xml",
    "media\weather\StormEffects.xml",
    "media\PhotoModeEffectPresets.xml",
    "media\THPConfig.xml"
)

Write-Step "Copying loose color/time/weather evidence..."
foreach ($relative in $looseTargets) {
    Copy-LooseEvidence -Root $FH6Root -RelativePath $relative -DestinationRoot $looseRoot -SourceManifest $sourceManifest
}

$archiveTargets = @(
    "media\colourgrades.zip",
    "media\displaymappers.zip",
    "media\postEffects.zip",
    "media\Camera.zip",
    "media\Sky.zip",
    "media\_library\Shaders.zip"
)

$textExtensions = @(".txt",".xml",".ini",".cfg",".json",".csv",".hlsl",".fx",".fxh",".glsl",".shader",".material",".meta",".yaml",".yml",".toml")
$binaryProbeExtensions = @(".bin",".dat",".cso",".dxbc",".dxil",".shaderbin",".shbin",".cache",".pc",".x64")
$maxTextExtractBytes = 4194304
$maxBinaryProbeBytes = 8388608
$maxExtractTotalBytes = 67108864
$extractedTotal = [Int64]0

foreach ($relativeArchive in $archiveTargets) {
    $archivePath = Join-Path $FH6Root $relativeArchive
    Write-Step ("Inspecting {0}" -f $relativeArchive)

    if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
        $sourceManifest.Add([pscustomobject]@{
            Kind = "Archive"
            RelativePath = $relativeArchive
            State = "MISSING"
            SizeBytes = 0
            SHA256 = ""
        })
        continue
    }

    $archiveFile = Get-Item -LiteralPath $archivePath
    $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    $archiveId = Get-SafeArchiveId -RelativePath $relativeArchive

    $sourceManifest.Add([pscustomobject]@{
        Kind = "Archive"
        RelativePath = $relativeArchive
        State = "FOUND"
        SizeBytes = [Int64]$archiveFile.Length
        SHA256 = $archiveHash
    })

    if (($relativeArchive -ne "media\_library\Shaders.zip") -and ($archiveFile.Length -le 16777216)) {
        $copyName = $archiveId + ".zip"
        Copy-Item -LiteralPath $archivePath -Destination (Join-Path $archiveCopyRoot $copyName) -Force
    }

    $zip = $null
    try {
        $zip = [System.IO.Compression.ZipFile]::OpenRead($archivePath)

        foreach ($entry in $zip.Entries) {
            if ([string]::IsNullOrEmpty($entry.Name)) {
                continue
            }

            $entryExtension = [System.IO.Path]::GetExtension($entry.Name).ToLowerInvariant()
            $nameHits = @(Get-KeywordHits -Text $entry.FullName)
            $contentHits = @()
            $probeType = "none"
            $wasExtracted = $false
            $extractedRelative = ""

            $shouldReadText = ($textExtensions -contains $entryExtension) -and ($entry.Length -le $maxTextExtractBytes)
            $shouldProbeBinary = ($binaryProbeExtensions -contains $entryExtension) -and ($entry.Length -le $maxBinaryProbeBytes)

            if ($shouldReadText) {
                try {
                    $stream = $entry.Open()
                    try {
                        $reader = New-Object System.IO.StreamReader($stream, $true)
                        try {
                            $text = $reader.ReadToEnd()
                        }
                        finally {
                            $reader.Dispose()
                        }
                    }
                    finally {
                        $stream.Dispose()
                    }

                    $contentHits = @(Get-KeywordHits -Text $text)
                    $probeType = "text"

                    $shouldExtract = ($nameHits.Count -gt 0) -or ($contentHits.Count -gt 0) -or ($relativeArchive -ne "media\_library\Shaders.zip")
                    if ($shouldExtract -and (($extractedTotal + $entry.Length) -le $maxExtractTotalBytes)) {
                        $entryDestinationRoot = Join-Path $extractedRoot $archiveId
                        $candidateDestination = Join-Path $entryDestinationRoot ($entry.FullName -replace '/', '\')
                        $fullDestination = [System.IO.Path]::GetFullPath($candidateDestination)
                        $fullRoot = [System.IO.Path]::GetFullPath($entryDestinationRoot)
                        if (-not $fullRoot.EndsWith("\")) { $fullRoot = $fullRoot + "\" }

                        if (-not $fullDestination.StartsWith($fullRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
                            throw ("Unsafe archive entry path: {0}" -f $entry.FullName)
                        }

                        $destinationDirectory = Split-Path -Parent $fullDestination
                        New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
                        Set-Content -LiteralPath $fullDestination -Value $text -Encoding UTF8
                        $wasExtracted = $true
                        $extractedTotal += [Int64]$entry.Length
                        $extractedRelative = $fullDestination.Substring($resolvedOutput.Length).TrimStart('\')
                    }
                }
                catch {
                    $errors.Add([pscustomobject]@{
                        Source = $relativeArchive
                        Entry = $entry.FullName
                        Error = $_.Exception.Message
                    })
                }
            }
            elseif ($shouldProbeBinary) {
                try {
                    $stream = $entry.Open()
                    try {
                        $limit = [int][Math]::Min([Int64]$entry.Length, [Int64]$maxBinaryProbeBytes)
                        $buffer = New-Object byte[] $limit
                        $offset = 0
                        while ($offset -lt $limit) {
                            $read = $stream.Read($buffer, $offset, $limit - $offset)
                            if ($read -le 0) { break }
                            $offset += $read
                        }
                    }
                    finally {
                        $stream.Dispose()
                    }

                    if ($offset -gt 0) {
                        if ($offset -lt $buffer.Length) {
                            $trimmed = New-Object byte[] $offset
                            [Array]::Copy($buffer, $trimmed, $offset)
                            $buffer = $trimmed
                        }

                        $ascii = [System.Text.Encoding]::ASCII.GetString($buffer)
                        $unicode = [System.Text.Encoding]::Unicode.GetString($buffer)
                        $contentHits = @((Get-KeywordHits -Text ($ascii + "`n" + $unicode)) | Sort-Object -Unique)
                        $probeType = "binary-prefix"
                    }
                }
                catch {
                    $errors.Add([pscustomobject]@{
                        Source = $relativeArchive
                        Entry = $entry.FullName
                        Error = $_.Exception.Message
                    })
                }
            }

            $archiveEntries.Add([pscustomobject]@{
                Archive = $relativeArchive
                Entry = $entry.FullName
                Extension = $entryExtension
                Length = [Int64]$entry.Length
                CompressedLength = [Int64]$entry.CompressedLength
                NameKeywordHits = ($nameHits -join ';')
                ContentKeywordHits = ($contentHits -join ';')
                ProbeType = $probeType
                Extracted = [bool]$wasExtracted
                ExtractedRelativePath = $extractedRelative
            })

            if (($nameHits.Count -gt 0) -or ($contentHits.Count -gt 0)) {
                $evidenceHits.Add([pscustomobject]@{
                    Archive = $relativeArchive
                    Entry = $entry.FullName
                    NameKeywordHits = ($nameHits -join ';')
                    ContentKeywordHits = ($contentHits -join ';')
                    ProbeType = $probeType
                    Extracted = [bool]$wasExtracted
                    ExtractedRelativePath = $extractedRelative
                })
            }
        }
    }
    catch {
        $errors.Add([pscustomobject]@{
            Source = $relativeArchive
            Entry = ""
            Error = $_.Exception.Message
        })
    }
    finally {
        if ($null -ne $zip) {
            $zip.Dispose()
        }
    }
}

$sourceManifestPath = Join-Path $resolvedOutput "source_manifest.csv"
$archiveEntriesPath = Join-Path $resolvedOutput "archive_entries.csv"
$evidenceHitsPath = Join-Path $resolvedOutput "evidence_hits.csv"
$errorsPath = Join-Path $resolvedOutput "collector_errors.csv"
$summaryPath = Join-Path $resolvedOutput "stage2_summary.json"
$readmePath = Join-Path $resolvedOutput "README.txt"

$sourceManifest | Export-Csv -LiteralPath $sourceManifestPath -NoTypeInformation -Encoding UTF8
$archiveEntries | Sort-Object Archive, Entry | Export-Csv -LiteralPath $archiveEntriesPath -NoTypeInformation -Encoding UTF8

if ($evidenceHits.Count -gt 0) {
    $evidenceHits | Sort-Object Archive, Entry | Export-Csv -LiteralPath $evidenceHitsPath -NoTypeInformation -Encoding UTF8
}
else {
    Set-Content -LiteralPath $evidenceHitsPath -Value '"Archive","Entry","NameKeywordHits","ContentKeywordHits","ProbeType","Extracted","ExtractedRelativePath"' -Encoding UTF8
}

if ($errors.Count -gt 0) {
    $errors | Export-Csv -LiteralPath $errorsPath -NoTypeInformation -Encoding UTF8
}
else {
    Set-Content -LiteralPath $errorsPath -Value '"Source","Entry","Error"' -Encoding UTF8
}

$foundSources = @($sourceManifest | Where-Object { $_.State -ne "MISSING" }).Count
$missingSources = @($sourceManifest | Where-Object { $_.State -eq "MISSING" }).Count
$copiedArchives = @(Get-ChildItem -LiteralPath $archiveCopyRoot -File -ErrorAction SilentlyContinue).Count
$extractedFiles = @(Get-ChildItem -LiteralPath $extractedRoot -Recurse -File -ErrorAction SilentlyContinue).Count

$summary = [ordered]@{
    collectorVersion = $CollectorVersion
    startedUtc = $StartedUtc.ToString("o")
    finishedUtc = [DateTime]::UtcNow.ToString("o")
    fh6Root = $FH6Root
    foundTargetCount = $foundSources
    missingTargetCount = $missingSources
    archiveEntryCount = $archiveEntries.Count
    evidenceHitCount = $evidenceHits.Count
    copiedSmallArchiveCount = $copiedArchives
    extractedFileCount = $extractedFiles
    extractedOriginalBytes = $extractedTotal
    collectorErrorCount = $errors.Count
    sourceFilesModified = $false
}
$summary | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $summaryPath -Encoding UTF8

$readme = @"
GraphicOverhaulVI - ColorCoreVI P0003 FH6 Evidence Collector v$CollectorVersion

This folder is intentionally read-only evidence collected from the local FH6 installation.
The source game is never modified.

Send the entire stage2-output folder back to ChatGPT as a ZIP.

Important files:
- stage2_summary.json      overall status
- source_manifest.csv     exact source paths, sizes and SHA-256
- archive_entries.csv     complete entry listing for selected FH6 archives
- evidence_hits.csv       entries matching color/HDR/tonemap/display keywords
- collector_errors.csv    any read/extract errors
- loose\                  copied time-of-day/weather/config evidence
- archives\               small high-value FH6 archives copied intact
- extracted\              selected small text/shader evidence from archives

Large Shaders.zip is NOT copied whole; it is indexed and only selected evidence is extracted.
"@
Set-Content -LiteralPath $readmePath -Value $readme -Encoding UTF8

Write-Host ""
Write-Host "=== P0003 FH6 color evidence collection complete ===" -ForegroundColor Green
Write-Host ("FH6 root: {0}" -f $FH6Root)
Write-Host ("Targets found: {0}" -f $foundSources)
Write-Host ("Targets missing: {0}" -f $missingSources)
Write-Host ("Archive entries indexed: {0}" -f $archiveEntries.Count)
Write-Host ("Color/HDR evidence hits: {0}" -f $evidenceHits.Count)
Write-Host ("Extracted files: {0}" -f $extractedFiles)
Write-Host ("Collector errors: {0}" -f $errors.Count)
Write-Host ("Output: {0}" -f $resolvedOutput)
Write-Host ""
Write-Host "ZIP the stage2-output folder and send it back in ChatGPT."
exit 0
