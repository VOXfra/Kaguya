@echo off
setlocal
cd /d "%~dp0"
set "COLORCORE_SCRIPT=%~dp0tools\Scan-ColorCoreVI.ps1"
set "COLORCORE_PREFLIGHT=%~dp0tools\Test-ColorCoreVI.ps1"
set "COLORCORE_OUTPUT=%~dp0scan-output"

echo.
echo ==========================================
echo   GraphicOverhaulVI - ColorCoreVI Scan
echo              Scanner v0.1.5
echo ==========================================
echo.
echo [1/2] Checking PowerShell syntax...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%COLORCORE_PREFLIGHT%" -TargetScript "%COLORCORE_SCRIPT%"
if errorlevel 1 goto :preflight_fail

echo.
echo [2/2] Starting read-only scan...
if exist "%COLORCORE_OUTPUT%" rmdir /s /q "%COLORCORE_OUTPUT%"

if "%~1"=="" goto :run_auto
if "%~2"=="" goto :run_auto

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%COLORCORE_SCRIPT%" -FH6Root "%~1" -GTAVRoot "%~2" -OutputRoot "%COLORCORE_OUTPUT%"
set "SCAN_EXIT=%ERRORLEVEL%"
goto :report

:run_auto
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%COLORCORE_SCRIPT%" -OutputRoot "%COLORCORE_OUTPUT%"
set "SCAN_EXIT=%ERRORLEVEL%"
goto :report

:report
echo.
if not "%SCAN_EXIT%"=="0" goto :scan_fail
if not exist "%COLORCORE_OUTPUT%\scan_summary.json" (
  echo ERROR: scanner returned success but no fresh scan_summary.json was created.
  set "SCAN_EXIT=97"
  goto :scan_fail
)

echo Scan finished successfully.
echo Results: %COLORCORE_OUTPUT%
echo.
if /I not "%COLORCORE_NO_PAUSE%"=="1" pause
endlocal & exit /b 0

:preflight_fail
echo.
echo Preflight failed. The scanner was NOT started.
echo Please send the full message above back in ChatGPT.
echo.
if /I not "%COLORCORE_NO_PAUSE%"=="1" pause
endlocal & exit /b 1

:scan_fail
echo Scan stopped with an error. Read the message above.
echo.
if /I not "%COLORCORE_NO_PAUSE%"=="1" pause
endlocal & exit /b %SCAN_EXIT%
