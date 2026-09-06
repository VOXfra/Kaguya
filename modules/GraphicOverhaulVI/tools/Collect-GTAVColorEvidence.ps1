param(
    [Parameter(Mandatory = $false)]
    [string]$GTAVRoot,

    [Parameter(Mandatory = $false)]
    [string]$OutputRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$BootstrapVersion = "0.1.1"
$UpstreamCommit = "c09f1f1b15f7a0ebe77f852c44f8aca3c9e358a7"
$UpstreamUrl = "https://codeload.github.com/wjy000/gtav-enhanced-rpf/zip/$UpstreamCommit"
$ExpectedMagicSha256 = "dc35981f822e892ced3aa81d31e7a96927d573ee28f67417592b5afeaf330832"
$PyCryptodomeVersion = "3.23.0"
$PythonCollector = Join-Path $PSScriptRoot "Collect-GTAVColorEvidence.py"
$ModuleRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$RuntimeRoot = Join-Path $ModuleRoot "p0004-runtime"

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $ModuleRoot "stage3-output"
}
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)

function Write-Step {
    param([string]$Message)
    Write-Host ("[ColorCoreVI/P0004] {0}" -f $Message) -ForegroundColor Cyan
}

function Invoke-NativeChecked {
    param(
        [Parameter(Mandatory = $true)][string]$Exe,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        & $Exe @Arguments
        $code = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
    if ($code -ne 0) {
        throw ("{0} Exit code: {1}" -f $FailureMessage, $code)
    }
}

function Get-PythonExecutable {
    $attempts = @(
        @{ Command = "py.exe"; Prefix = @("-3.11") },
        @{ Command = "py.exe"; Prefix = @("-3") },
        @{ Command = "python.exe"; Prefix = @() },
        @{ Command = "python"; Prefix = @() }
    )

    foreach ($attempt in $attempts) {
        $command = $attempt.Command
        $prefix = @($attempt.Prefix)
        try {
            $resolved = Get-Command $command -ErrorAction Stop
            $oldPreference = $ErrorActionPreference
            $ErrorActionPreference = "Continue"
            try {
                $result = & $resolved.Source @prefix -c "import sys; print(sys.executable); print('%d.%d' % sys.version_info[:2])" 2>$null
                $code = $LASTEXITCODE
            }
            finally {
                $ErrorActionPreference = $oldPreference
            }
            if (($code -eq 0) -and (@($result).Count -ge 2)) {
                $exe = [string]$result[0]
                $versionText = [string]$result[1]
                $parts = $versionText.Trim().Split('.')
                if (($parts.Count -ge 2) -and ([int]$parts[0] -eq 3) -and ([int]$parts[1] -ge 10)) {
                    return $exe.Trim()
                }
            }
        }
        catch { }
    }

    throw "Python 3.10+ was not found. Python 3.11 is recommended."
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
            catch { }
        }
    }
    return $result
}

function Add-GTACandidate {
    param(
        [System.Collections.Generic.List[string]]$List,
        [string]$Path
    )
    if ([string]::IsNullOrWhiteSpace($Path)) { return }
    try {
        if ((Test-Path -LiteralPath $Path -PathType Container) -and
            (Test-Path -LiteralPath (Join-Path $Path "GTA5_Enhanced.exe") -PathType Leaf)) {
            $resolved = (Resolve-Path -LiteralPath $Path).Path
            if (-not $List.Contains($resolved)) { $List.Add($resolved) }
        }
    }
    catch { }
}

function Find-GTACandidates {
    $result = New-Object System.Collections.Generic.List[string]

    $manifestRoot = "C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests"
    if (Test-Path -LiteralPath $manifestRoot -PathType Container) {
        foreach ($item in (Get-ChildItem -LiteralPath $manifestRoot -Filter *.item -File -ErrorAction SilentlyContinue)) {
            try {
                $manifest = Get-Content -LiteralPath $item.FullName -Raw | ConvertFrom-Json
                if (($manifest.DisplayName -match 'Grand Theft Auto V') -and $manifest.InstallLocation) {
                    Add-GTACandidate -List $result -Path ([string]$manifest.InstallLocation)
                }
            }
            catch { }
        }
    }

    foreach ($library in (Get-SteamLibraries)) {
        foreach ($name in @("Grand Theft Auto V Enhanced", "Grand Theft Auto V", "GTAVEnhanced")) {
            Add-GTACandidate -List $result -Path (Join-Path $library ("steamapps\common\{0}" -f $name))
        }
    }

    $commonRelativePaths = @(
        "Jeux Epic\GTAVEnhanced",
        "Jeux Epic\Grand Theft Auto V Enhanced",
        "Epic Games\GTAVEnhanced",
        "Epic Games\Grand Theft Auto V Enhanced",
        "Games\GTAVEnhanced",
        "Games\Grand Theft Auto V Enhanced",
        "SteamLibrary\steamapps\common\Grand Theft Auto V Enhanced",
        "SteamLibrary\steamapps\common\Grand Theft Auto V"
    )

    foreach ($drive in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        foreach ($relative in $commonRelativePaths) {
            Add-GTACandidate -List $result -Path (Join-Path $drive.Root $relative)
        }
    }

    return $result
}

function Resolve-GTARoot {
    param([string]$Provided)

    if (-not [string]::IsNullOrWhiteSpace($Provided)) {
        $clean = $Provided.Trim().Trim('"')
        if (-not (Test-Path -LiteralPath (Join-Path $clean "GTA5_Enhanced.exe") -PathType Leaf)) {
            throw "GTA5_Enhanced.exe was not found in: $clean"
        }
        return (Resolve-Path -LiteralPath $clean).Path
    }

    $found = @(Find-GTACandidates)
    if ($found.Count -eq 1) {
        Write-Step ("Auto-detected GTA V Enhanced: {0}" -f $found[0])
        return $found[0]
    }

    if ($found.Count -gt 1) {
        Write-Host "Multiple GTA V Enhanced candidates were found:" -ForegroundColor Yellow
        for ($i = 0; $i -lt $found.Count; $i++) {
            Write-Host ("  [{0}] {1}" -f ($i + 1), $found[$i])
        }
        $choice = Read-Host "Enter a number, or press Enter to paste another path"
        if ($choice -match '^\d+$') {
            $index = [int]$choice - 1
            if (($index -ge 0) -and ($index -lt $found.Count)) {
                return $found[$index]
            }
        }
    }

    while ($true) {
        $manual = Read-Host "Paste the GTA V Enhanced install folder, or press Enter to cancel"
        if ([string]::IsNullOrWhiteSpace($manual)) { return $null }
        $manual = $manual.Trim().Trim('"')
        if (Test-Path -LiteralPath (Join-Path $manual "GTA5_Enhanced.exe") -PathType Leaf) {
            return (Resolve-Path -LiteralPath $manual).Path
        }
        Write-Host "That folder does not contain GTA5_Enhanced.exe. Try again." -ForegroundColor Red
    }
}

function Ensure-UpstreamRuntime {
    param([string]$PythonExe)

    New-Item -ItemType Directory -Force -Path $RuntimeRoot | Out-Null
    $marker = Join-Path $RuntimeRoot "upstream.commit"
    $extractRoot = Join-Path $RuntimeRoot "upstream"
    $needsRefresh = $true

    if ((Test-Path -LiteralPath $marker -PathType Leaf) -and (Test-Path -LiteralPath $extractRoot -PathType Container)) {
        $current = (Get-Content -LiteralPath $marker -Raw).Trim()
        if ($current -eq $UpstreamCommit) { $needsRefresh = $false }
    }

    if ($needsRefresh) {
        Write-Step "Downloading pinned Enhanced RPF reader..."
        if (Test-Path -LiteralPath $extractRoot) { Remove-Item -LiteralPath $extractRoot -Recurse -Force }
        $zipPath = Join-Path $RuntimeRoot "gtav-enhanced-rpf.zip"
        if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $UpstreamUrl -OutFile $zipPath
        New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
        Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force
        Remove-Item -LiteralPath $zipPath -Force
        Set-Content -LiteralPath $marker -Value $UpstreamCommit -Encoding ASCII
    }

    $packageRoot = $null
    foreach ($dir in (Get-ChildItem -LiteralPath $extractRoot -Directory -ErrorAction SilentlyContinue)) {
        if (Test-Path -LiteralPath (Join-Path $dir.FullName "rpf_enhanced\magic.dat") -PathType Leaf) {
            $packageRoot = $dir.FullName
            break
        }
    }
    if ([string]::IsNullOrWhiteSpace($packageRoot)) {
        throw "Pinned Enhanced RPF reader was downloaded but rpf_enhanced/magic.dat is missing."
    }

    $magic = Join-Path $packageRoot "rpf_enhanced\magic.dat"
    $magicHash = (Get-FileHash -LiteralPath $magic -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($magicHash -ne $ExpectedMagicSha256) {
        throw "Pinned reader integrity check failed for magic.dat. Expected $ExpectedMagicSha256, got $magicHash"
    }
    if (-not (Test-Path -LiteralPath (Join-Path $packageRoot "LICENSE") -PathType Leaf)) {
        throw "Pinned reader LICENSE file is missing."
    }

    $venvRoot = Join-Path $RuntimeRoot "venv"
    $venvPython = Join-Path $venvRoot "Scripts\python.exe"
    if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
        Write-Step "Creating isolated Python runtime..."
        Invoke-NativeChecked -Exe $PythonExe -Arguments @("-m", "venv", $venvRoot) -FailureMessage "Failed to create Python virtual environment."
    }

    $cryptoMarker = Join-Path $venvRoot "pycryptodome.version"
    $installCrypto = $true
    if (Test-Path -LiteralPath $cryptoMarker -PathType Leaf) {
        if ((Get-Content -LiteralPath $cryptoMarker -Raw).Trim() -eq $PyCryptodomeVersion) {
            $installCrypto = $false
        }
    }
    if ($installCrypto) {
        Write-Step ("Installing pinned pycryptodome {0}..." -f $PyCryptodomeVersion)
        Invoke-NativeChecked -Exe $venvPython -Arguments @(
            "-m", "pip", "install", "--disable-pip-version-check", "--quiet",
            ("pycryptodome=={0}" -f $PyCryptodomeVersion)
        ) -FailureMessage ("Failed to install pycryptodome {0}." -f $PyCryptodomeVersion)
        Set-Content -LiteralPath $cryptoMarker -Value $PyCryptodomeVersion -Encoding ASCII
    }

    Invoke-NativeChecked -Exe $venvPython -Arguments @("-c", "import Crypto") -FailureMessage "pycryptodome import validation failed."
    Invoke-NativeChecked -Exe $venvPython -Arguments @("-m", "py_compile", $PythonCollector) -FailureMessage "Python collector syntax validation failed."

    return [pscustomobject]@{
        PackageRoot = $packageRoot
        Python = $venvPython
    }
}

Write-Step ("Bootstrap v{0}" -f $BootstrapVersion)
$python = Get-PythonExecutable
Write-Step ("Python: {0}" -f $python)
$runtime = Ensure-UpstreamRuntime -PythonExe $python

if (Test-Path -LiteralPath $OutputRoot -PathType Container) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}

$oldPythonPath = $env:PYTHONPATH
try {
    if ([string]::IsNullOrWhiteSpace($oldPythonPath)) {
        $env:PYTHONPATH = $runtime.PackageRoot
    }
    else {
        $env:PYTHONPATH = $runtime.PackageRoot + ";" + $oldPythonPath
    }

    if ($env:COLORCORE_P0004_SELFTEST -eq "1") {
        Write-Step "Running P0004 runtime self-test..."
        Invoke-NativeChecked -Exe $runtime.Python -Arguments @(
            $PythonCollector, "--self-test", "--output", $OutputRoot
        ) -FailureMessage "P0004 Python self-test failed."
    }
    else {
        $GTAVRoot = Resolve-GTARoot -Provided $GTAVRoot
        if ([string]::IsNullOrWhiteSpace($GTAVRoot)) {
            Write-Host "GTA V Enhanced is required. No game file was modified." -ForegroundColor Red
            exit 2
        }
        Write-Step ("Collecting from: {0}" -f $GTAVRoot)
        Invoke-NativeChecked -Exe $runtime.Python -Arguments @(
            $PythonCollector, "--game-root", $GTAVRoot, "--output", $OutputRoot
        ) -FailureMessage "P0004 GTA RPF collector failed."
    }
}
finally {
    $env:PYTHONPATH = $oldPythonPath
}

$summaryPath = Join-Path $OutputRoot "stage3_summary.json"
if (-not (Test-Path -LiteralPath $summaryPath -PathType Leaf)) {
    throw "Collector returned success but stage3_summary.json is missing."
}

$forbiddenNames = @("gtav_aes_key.dat", "gtav_ng_key.dat", "gtav_ng_decrypt_tables.dat")
$forbiddenFound = @()
foreach ($file in (Get-ChildItem -LiteralPath $OutputRoot -Recurse -File -ErrorAction SilentlyContinue)) {
    if ($forbiddenNames -contains $file.Name.ToLowerInvariant()) {
        $forbiddenFound += $file.FullName
    }
}
if ($forbiddenFound.Count -gt 0) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force -ErrorAction SilentlyContinue
    throw "SECURITY GUARD: a GTA key artifact reached stage3-output. Output was deleted."
}

Write-Host ""
Write-Host "P0004 completed successfully." -ForegroundColor Green
Write-Host ("Output: {0}" -f $OutputRoot)
Write-Host "No GTA encryption key file was written to stage3-output." -ForegroundColor Green
