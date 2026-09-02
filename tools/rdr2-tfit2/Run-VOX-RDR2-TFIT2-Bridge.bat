@echo off
setlocal EnableExtensions
title VOX RDR2 TFIT2 Metadata Bridge v0.4.0
cd /d "%~dp0"

echo ============================================================
echo   VOX RDR2 TFIT2 Metadata Bridge v0.4.0
echo ============================================================
echo.
echo READ-ONLY: no RDR2 archive is modified.
echo No key material or process-memory dump is written to disk.
echo.
echo IMPORTANT: RDR2.exe must already be running.
echo Main menu or Story Mode is recommended.
echo.

set "ROOT=%~1"
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

echo [3/3] Discovering local TFIT2 data and cataloguing RPF8 metadata...
"VOX-RDR2-TFIT2-Bridge.exe" --root "%ROOT%" --fingerprints "%HASHES%" --rpf8-source "%RPF8SRC%" --out "%~dp0VOX-RDR2-TFIT2-Catalog"
set "RC=%ERRORLEVEL%"

rd /s /q "%TMPDIR%" >nul 2>&1

echo.
if "%RC%"=="0" (
  echo [OK] Catalogue completed.
) else if "%RC%"=="3" (
  echo [INFO] Memory discovery was incomplete. Send the generated reports.
) else if "%RC%"=="4" (
  echo [WARNING] TOCs were decrypted but some entry bounds failed validation.
  echo Send the generated reports before using the metadata.
) else (
  echo [ERROR] Bridge returned exit code %RC%.
)
echo Output: %~dp0VOX-RDR2-TFIT2-Catalog
echo.
pause
exit /b %RC%
