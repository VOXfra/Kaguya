@echo off
setlocal
cd /d "%~dp0"
set "COLORCORE_SCRIPT=%~dp0tools\Scan-ColorCoreVI.ps1"
set "COLORCORE_PREFLIGHT=%~dp0tools\Test-ColorCoreVI.ps1"

echo.
echo ==========================================
echo   GraphicOverhaulVI - ColorCoreVI Scan
echo              Scanner v0.1.4
echo ==========================================
echo.
echo [1/2] Checking PowerShell syntax...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%COLORCORE_PREFLIGHT%" -TargetScript "%COLORCORE_SCRIPT%"
if errorlevel 1 (
  echo.
  echo Preflight failed. The scanner was NOT started.
  echo Please send the full message above back in ChatGPT.
  echo.
  if /I not "%COLORCORE_NO_PAUSE%"=="1" pause
  endlocal
  exit /b 1
)

echo.
echo [2/2] Starting read-only scan...
if not "%~1"=="" if not "%~2"=="" (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%COLORCORE_SCRIPT%" -FH6Root "%~1" -GTAVRoot "%~2"
) else (
  powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%COLORCORE_SCRIPT%"
)
set "SCAN_EXIT=%ERRORLEVEL%"

echo.
if not "%SCAN_EXIT%"=="0" (
  echo Scan stopped with an error. Read the message above.
) else (
  echo Scan finished. The results are in the scan-output folder.
)
echo.
if /I not "%COLORCORE_NO_PAUSE%"=="1" pause
endlocal & exit /b %SCAN_EXIT%
