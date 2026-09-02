@echo off
setlocal EnableExtensions EnableDelayedExpansion
title VOX RDR2 anim_0 Research v0.8.1
cd /d "%~dp0"

echo ============================================================
echo   VOX RDR2 anim_0 Research v0.8.1
echo ============================================================
echo.
echo READ-ONLY: RDR2 files are never modified.
echo No Rockstar asset, key bytes or memory dump is written to disk.
echo Only RPF8 metadata, RAW and decrypted TOC SHA-256 hashes and public-name matches are exported.
echo.
set "ROOT=%~1"
if not defined ROOT set /p "ROOT=RDR2 folder containing RDR2.exe: "
set "ROOT=%ROOT:"=%"
if not exist "%ROOT%\RDR2.exe" (
  echo [ERROR] RDR2.exe not found in: %ROOT%
  pause
  exit /b 1
)
if not exist "%ROOT%\anim_0.rpf" (
  echo [ERROR] anim_0.rpf not found in: %ROOT%
  pause
  exit /b 1
)

tasklist /FI "IMAGENAME eq RDR2.exe" /NH 2>nul | findstr /I /C:"RDR2.exe" >nul
if errorlevel 1 (
  echo [PROC] Starting RDR2 so all local TFIT2/RPF8 key material can be discovered...
  start "" /D "%ROOT%" "%ROOT%\RDR2.exe"
  set /a W=0
  :WAITGAME
  timeout /t 2 /nobreak >nul
  tasklist /FI "IMAGENAME eq RDR2.exe" /NH 2>nul | findstr /I /C:"RDR2.exe" >nul
  if not errorlevel 1 goto GAMEFOUND
  set /a W+=1
  if !W! LSS 90 goto WAITGAME
  echo [ERROR] RDR2.exe was not detected.
  pause
  exit /b 5
)
:GAMEFOUND
echo [PROC] RDR2.exe detected. Allowing runtime data to initialize...
timeout /t 20 /nobreak >nul

set "TMPDIR=%TEMP%\VOX_RDR2_ANIM0_%RANDOM%_%RANDOM%"
mkdir "%TMPDIR%" >nul 2>&1
set "HASHES=%TMPDIR%\RDR2.h"
set "RPF8SRC=%TMPDIR%\rpf8.cpp"
set "CFX=%TMPDIR%\BaseGameRpfHeaderHashes_RDR3.h"
set "OUT=%~dp0VOX-RDR2-ANIM0-Research"

echo [1/4] Fetching pinned public format fingerprints...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue';" ^
  "Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='VOX-RDR2-Anim0'} -Uri 'https://raw.githubusercontent.com/0x1F9F1/Swage/201320d61ae04909f01af247c41f864a9174b691/src/games/rage/hashes/RDR2.h' -OutFile '%HASHES%';" ^
  "Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='VOX-RDR2-Anim0'} -Uri 'https://raw.githubusercontent.com/0x1F9F1/Swage/201320d61ae04909f01af247c41f864a9174b691/src/games/rage/rpf8.cpp' -OutFile '%RPF8SRC%';" ^
  "Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='VOX-RDR2-Anim0'} -Uri 'https://raw.githubusercontent.com/citizenfx/fivem/03dcc562ca175e24eb018569ecb919b4b7a56824/code/components/glue/include/BaseGameRpfHeaderHashes_RDR3.h' -OutFile '%CFX%'"
if errorlevel 1 goto FETCHFAIL

echo [2/4] Validating TFIT2/RPF8 format definitions...
"VOX-RDR2-Anim0-Probe.exe" --fingerprint-self-test --fingerprints "%HASHES%" --rpf8-source "%RPF8SRC%"
if errorlevel 1 goto TESTFAIL

echo [3/4] Opening anim_0.rpf and hashing RAW + decrypted nested RPF8 TOCs in memory...
"VOX-RDR2-Anim0-Probe.exe" --root "%ROOT%" --fingerprints "%HASHES%" --rpf8-source "%RPF8SRC%" --out "%OUT%"
set "RC=!ERRORLEVEL!"
if not "!RC!"=="0" goto PROBEFAIL
if not exist "%OUT%\CONTENT-nested-archives.csv" (
  echo [ERROR] Dual nested archive TOC hash report is missing.
  pause
  exit /b 6
)

echo [4/4] Comparing both TOC representations against CitizenFX SHA-256...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0Map-VOX-RDR2-Anim0-v081.ps1" -NestedCsv "%OUT%\CONTENT-nested-entries.csv" -NestedArchivesCsv "%OUT%\CONTENT-nested-archives.csv" -CitizenFxHeader "%CFX%" -OutDir "%OUT%"
set "MAPRC=!ERRORLEVEL!"
rd /s /q "%TMPDIR%" >nul 2>&1
if not "!MAPRC!"=="0" (
  echo [WARNING] anim_0 metadata succeeded, but the dual fingerprint mapper returned !MAPRC!.
  echo Output metadata is still available in: %OUT%
  pause
  exit /b !MAPRC!
)
echo.
echo [OK] anim_0 dual fingerprint research pass completed.
echo Output: %OUT%
echo Send ANIM0-dual-report.txt, ANIM0-dual-summary.json, ANIM0-dual-archive-map.csv and ANIM0-order-audit.csv.
pause
exit /b 0

:PROBEFAIL
rd /s /q "%TMPDIR%" >nul 2>&1
echo [ERROR] anim_0 probe returned exit code !RC!.
echo If Windows denied process-memory read access, keep RDR2 open and run this BAT as administrator.
pause
exit /b !RC!
:FETCHFAIL
rd /s /q "%TMPDIR%" >nul 2>&1
echo [ERROR] Could not fetch pinned public definitions.
pause
exit /b 2
:TESTFAIL
rd /s /q "%TMPDIR%" >nul 2>&1
echo [ERROR] Format/fingerprint validation failed.
pause
exit /b 3
