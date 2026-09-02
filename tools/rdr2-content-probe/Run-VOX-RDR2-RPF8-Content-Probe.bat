@echo off
setlocal EnableExtensions EnableDelayedExpansion
title VOX RDR2 RPF8 Recursive Content Probe v0.6.0
cd /d "%~dp0"

echo ============================================================
echo   VOX RDR2 RPF8 Recursive Content Probe v0.6.0
echo ============================================================
echo.
echo READ-ONLY: no RDR2 archive is modified.
echo No raw asset, key material or memory dump is written to disk.
echo The probe opens entry prefixes and nested RPF8 TOCs in memory only.
echo.
set "ROOT=%~1"
if not defined ROOT set /p "ROOT=RDR2 folder containing RDR2.exe: "
set "ROOT=%ROOT:"=%"
if not exist "%ROOT%\RDR2.exe" (
  echo [ERROR] RDR2.exe not found in: %ROOT%
  pause
  exit /b 1
)

tasklist /FI "IMAGENAME eq RDR2.exe" /NH 2>nul | findstr /I /C:"RDR2.exe" >nul
if errorlevel 1 (
  echo [PROC] Starting RDR2...
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

set "TMPDIR=%TEMP%\VOX_RDR2_CONTENT_%RANDOM%_%RANDOM%"
mkdir "%TMPDIR%" >nul 2>&1
set "HASHES=%TMPDIR%\RDR2.h"
set "RPF8SRC=%TMPDIR%\rpf8.cpp"
echo [1/3] Fetching pinned public fingerprint definitions...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop'; $ProgressPreference='SilentlyContinue';" ^
  "Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='VOX-RDR2-Content-Probe'} -Uri 'https://raw.githubusercontent.com/0x1F9F1/Swage/201320d61ae04909f01af247c41f864a9174b691/src/games/rage/hashes/RDR2.h' -OutFile '%HASHES%';" ^
  "Invoke-WebRequest -UseBasicParsing -Headers @{'User-Agent'='VOX-RDR2-Content-Probe'} -Uri 'https://raw.githubusercontent.com/0x1F9F1/Swage/201320d61ae04909f01af247c41f864a9174b691/src/games/rage/rpf8.cpp' -OutFile '%RPF8SRC%'"
if errorlevel 1 goto FETCHFAIL

echo [2/3] Validating format fingerprints and probe core...
"VOX-RDR2-RPF8-Content-Probe.exe" --fingerprint-self-test --fingerprints "%HASHES%" --rpf8-source "%RPF8SRC%"
if errorlevel 1 goto TESTFAIL

echo [3/3] Discovering all local RPF8 keys, decoding prefixes and opening nested TOCs...
"VOX-RDR2-RPF8-Content-Probe.exe" --root "%ROOT%" --fingerprints "%HASHES%" --rpf8-source "%RPF8SRC%" --out "%~dp0VOX-RDR2-RPF8-Content-Probe"
set "RC=!ERRORLEVEL!"
rd /s /q "%TMPDIR%" >nul 2>&1
if "!RC!"=="0" (
  echo.
  echo [OK] Recursive content probe completed.
  echo Output: %~dp0VOX-RDR2-RPF8-Content-Probe
) else (
  echo.
  echo [ERROR] Probe returned exit code !RC!.
  echo If access was denied, run this BAT as administrator while RDR2 stays open.
)
pause
exit /b !RC!

:FETCHFAIL
rd /s /q "%TMPDIR%" >nul 2>&1
echo [ERROR] Could not fetch pinned public fingerprint definitions.
pause
exit /b 2
:TESTFAIL
rd /s /q "%TMPDIR%" >nul 2>&1
echo [ERROR] Fingerprint validation failed.
pause
exit /b 3
