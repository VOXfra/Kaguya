@echo off
setlocal EnableExtensions EnableDelayedExpansion
title VOX RDR2 TFIT2 Metadata Bridge v0.4.1
cd /d "%~dp0"

echo ============================================================
echo   VOX RDR2 TFIT2 Metadata Bridge v0.4.1
echo ============================================================
echo.
echo READ-ONLY: no RDR2 archive is modified.
echo No key material or process-memory dump is written to disk.
echo.
echo The launcher can start RDR2 automatically if needed.
echo Main menu or Story Mode is recommended.
echo.

set "ROOT=%~1"
set "ELEVATED=0"
if /I "%~2"=="--elevated" set "ELEVATED=1"
if not defined ROOT (
  set /p "ROOT=RDR2 folder containing RDR2.exe: "
)
set "ROOT=%ROOT:"=%"
if not exist "%ROOT%\RDR2.exe" (
  echo.
  echo [ERROR] RDR2.exe was not found in:
  echo %ROOT%
  echo.
  pause
  exit /b 1
)

call :EnsureRDR2
if errorlevel 1 (
  echo.
  echo [ERROR] RDR2.exe could not be detected after the automatic launch attempt.
  echo Start the game normally with Rockstar Games Launcher, then run this BAT again.
  echo.
  pause
  exit /b 5
)

set "TMPDIR=%TEMP%\VOX_RDR2_TFIT2_%RANDOM%_%RANDOM%"
mkdir "%TMPDIR%" >nul 2>&1
set "HASHES=%TMPDIR%\RDR2.h"
set "RPF8SRC=%TMPDIR%\rpf8.cpp"

echo [1/3] Fetching pinned public fingerprint definitions...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue';" ^
  "Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='VOX-RDR2-TFIT2-Bridge'} -Uri 'https://raw.githubusercontent.com/0x1F9F1/Swage/201320d61ae04909f01af247c41f864a9174b691/src/games/rage/hashes/RDR2.h' -OutFile '%HASHES%';" ^
  "Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='VOX-RDR2-TFIT2-Bridge'} -Uri 'https://raw.githubusercontent.com/0x1F9F1/Swage/201320d61ae04909f01af247c41f864a9174b691/src/games/rage/rpf8.cpp' -OutFile '%RPF8SRC%'"
if errorlevel 1 (
  echo.
  echo [ERROR] Could not fetch the pinned public fingerprint definitions.
  rd /s /q "%TMPDIR%" >nul 2>&1
  pause
  exit /b 2
)

echo [2/3] Validating fingerprint schema...
"VOX-RDR2-TFIT2-Bridge.exe" --fingerprint-self-test --fingerprints "%HASHES%" --rpf8-source "%RPF8SRC%"
if errorlevel 1 (
  echo.
  echo [ERROR] Public fingerprint schema validation failed.
  rd /s /q "%TMPDIR%" >nul 2>&1
  pause
  exit /b 3
)

set /a SCANTRY=1
:RunBridge
echo [3/3] Discovering local TFIT2 data and cataloguing RPF8 metadata... attempt !SCANTRY!/3
"VOX-RDR2-TFIT2-Bridge.exe" --root "%ROOT%" --fingerprints "%HASHES%" --rpf8-source "%RPF8SRC%" --out "%~dp0VOX-RDR2-TFIT2-Catalog"
set "RC=!ERRORLEVEL!"

if "!RC!"=="3" if !SCANTRY! LSS 3 (
  echo.
  echo [INFO] TFIT2 data is not fully resident yet. Keeping RDR2 open and retrying automatically...
  timeout /t 15 /nobreak >nul
  set /a SCANTRY+=1
  goto RunBridge
)

if "!RC!"=="1" if "!ELEVATED!"=="0" (
  tasklist /FI "IMAGENAME eq RDR2.exe" /NH 2>nul | findstr /I /C:"RDR2.exe" >nul
  if not errorlevel 1 (
    echo.
    echo [INFO] RDR2.exe is visible to Windows but the bridge could not access it.
    echo [INFO] Retrying the same read-only scan with administrator rights...
    set "VOX_BAT=%~f0"
    set "VOX_ROOT=%ROOT%"
    powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath $env:VOX_BAT -ArgumentList @(('\"' + $env:VOX_ROOT + '\"'),'--elevated') -Verb RunAs"
    rd /s /q "%TMPDIR%" >nul 2>&1
    exit /b 0
  )
)

rd /s /q "%TMPDIR%" >nul 2>&1

echo.
if "!RC!"=="0" (
  echo [OK] Catalogue completed.
) else if "!RC!"=="3" (
  echo [INFO] Memory discovery remained incomplete after automatic retries.
  echo Send the generated reports; the launcher has already exhausted the safe retries.
) else if "!RC!"=="4" (
  echo [WARNING] TOCs were decrypted but some entry bounds failed validation.
  echo Send the generated reports before using the metadata.
) else (
  echo [ERROR] Bridge returned exit code !RC!.
)
echo Output: %~dp0VOX-RDR2-TFIT2-Catalog
echo.
pause
exit /b !RC!

:EnsureRDR2
tasklist /FI "IMAGENAME eq RDR2.exe" /NH 2>nul | findstr /I /C:"RDR2.exe" >nul
if not errorlevel 1 (
  echo [PROC] RDR2.exe is already running.
  exit /b 0
)

echo [PROC] RDR2.exe is not running. Starting the selected local installation...
start "" /D "%ROOT%" "%ROOT%\RDR2.exe"
set /a WAITTRY=0
:WaitForRDR2
timeout /t 2 /nobreak >nul
tasklist /FI "IMAGENAME eq RDR2.exe" /NH 2>nul | findstr /I /C:"RDR2.exe" >nul
if not errorlevel 1 (
  echo [PROC] RDR2.exe detected. Allowing the game runtime to initialize...
  timeout /t 20 /nobreak >nul
  tasklist /FI "IMAGENAME eq RDR2.exe" /NH 2>nul | findstr /I /C:"RDR2.exe" >nul
  if not errorlevel 1 exit /b 0
)
set /a WAITTRY+=1
if !WAITTRY! LSS 90 goto WaitForRDR2
exit /b 1
