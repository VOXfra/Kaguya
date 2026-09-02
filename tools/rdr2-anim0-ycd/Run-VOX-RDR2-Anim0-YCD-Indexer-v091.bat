@echo off
setlocal EnableExtensions EnableDelayedExpansion
title VOX RDR2 anim_0 Targeted YCD String Indexer v0.9.1
cd /d "%~dp0"

echo ============================================================
echo   VOX RDR2 anim_0 Targeted YCD String Indexer v0.9.1
echo ============================================================
echo.
echo READ-ONLY: RDR2 files are never modified.
echo No Rockstar asset, key bytes or process-memory dump is written.
echo Only metadata and printable diagnostic strings are exported.
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

set "STARTED=0"
tasklist /FI "IMAGENAME eq RDR2.exe" /NH 2>nul | findstr /I /C:"RDR2.exe" >nul
if errorlevel 1 (
  set "STARTED=1"
  echo [PROC] Starting RDR2 for local TFIT2/RPF8 discovery...
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
if "%STARTED%"=="1" (
  echo [PROC] RDR2.exe detected. Waiting 20 seconds for runtime data...
  timeout /t 20 /nobreak >nul
) else (
  echo [PROC] RDR2.exe already running. Continuing in 3 seconds...
  timeout /t 3 /nobreak >nul
)

set "TMPDIR=%TEMP%\VOX_RDR2_ANIM0_YCD091_%RANDOM%_%RANDOM%"
mkdir "%TMPDIR%" >nul 2>&1
set "HASHES=%TMPDIR%\RDR2.h"
set "RPF8SRC=%TMPDIR%\rpf8.cpp"
set "OUT=%~dp0VOX-RDR2-ANIM0-YCD-Index-v091"

echo [1/3] Fetching pinned public TFIT2/RPF8 definitions...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue';" ^
  "Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='VOX-RDR2-Anim0-YCD091'} -Uri 'https://raw.githubusercontent.com/0x1F9F1/Swage/201320d61ae04909f01af247c41f864a9174b691/src/games/rage/hashes/RDR2.h' -OutFile '%HASHES%';" ^
  "Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='VOX-RDR2-Anim0-YCD091'} -Uri 'https://raw.githubusercontent.com/0x1F9F1/Swage/201320d61ae04909f01af247c41f864a9174b691/src/games/rage/rpf8.cpp' -OutFile '%RPF8SRC%'"
if errorlevel 1 goto FETCHFAIL

echo [2/3] Validating format definitions and corrected indexer...
"VOX-RDR2-Anim0-YCD-Indexer-v091.exe" --self-test
if errorlevel 1 goto TESTFAIL
"VOX-RDR2-Anim0-YCD-Indexer-v091.exe" --fingerprint-self-test --fingerprints "%HASHES%" --rpf8-source "%RPF8SRC%"
if errorlevel 1 goto TESTFAIL

echo [3/3] Fully decoding selected exact anim_0 YCD packs in memory...
"VOX-RDR2-Anim0-YCD-Indexer-v091.exe" --root "%ROOT%" --fingerprints "%HASHES%" --rpf8-source "%RPF8SRC%" --out "%OUT%"
set "RC=!ERRORLEVEL!"
rd /s /q "%TMPDIR%" >nul 2>&1
if not "!RC!"=="0" goto PROBEFAIL

echo.
echo [OK] Corrected YCD indexing completed.
echo Output: %OUT%
echo Send the whole output folder as a ZIP.
pause
exit /b 0

:PROBEFAIL
echo [ERROR] YCD indexer returned exit code !RC!.
echo Keep RDR2 open and run this BAT as administrator if Windows denied process-memory read access.
pause
exit /b !RC!
:FETCHFAIL
rd /s /q "%TMPDIR%" >nul 2>&1
echo [ERROR] Could not fetch pinned public definitions.
pause
exit /b 2
:TESTFAIL
rd /s /q "%TMPDIR%" >nul 2>&1
echo [ERROR] Self-test or format/fingerprint validation failed.
pause
exit /b 3
