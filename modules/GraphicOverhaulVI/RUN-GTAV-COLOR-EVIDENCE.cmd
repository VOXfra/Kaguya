@echo off
setlocal
cd /d "%~dp0"

set "BOOTSTRAP=%~dp0tools\Collect-GTAVColorEvidence.ps1"
set "PREFLIGHT=%~dp0tools\Test-GTAVColorEvidence.ps1"
set "OUTPUT=%~dp0stage3-output"

echo.
echo ==========================================
echo   GraphicOverhaulVI - ColorCoreVI P0004
echo       GTA Enhanced Evidence v0.1.0
echo ==========================================
echo.
echo [1/2] Checking PowerShell syntax...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%PREFLIGHT%" -TargetScript "%BOOTSTRAP%"
if errorlevel 1 goto :preflight_failed

if exist "%OUTPUT%" rmdir /s /q "%OUTPUT%"

echo.
echo [2/2] Collecting GTA V Enhanced ColorCore evidence...
if "%~1"=="" goto :run_auto
goto :run_with_path

:run_auto
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%BOOTSTRAP%"
goto :after_run

:run_with_path
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%BOOTSTRAP%" -GTAVRoot "%~1"

:after_run
set "COLLECT_EXIT=%ERRORLEVEL%"
if not "%COLLECT_EXIT%"=="0" goto :collector_failed
if not exist "%OUTPUT%\stage3_summary.json" goto :missing_output

echo.
echo P0004 finished successfully.
echo Results: %OUTPUT%
echo.
if /I not "%COLORCORE_NO_PAUSE%"=="1" pause
endlocal & exit /b 0

:preflight_failed
echo.
echo Preflight failed. Collector was NOT started.
echo.
if /I not "%COLORCORE_NO_PAUSE%"=="1" pause
endlocal & exit /b 1

:collector_failed
echo.
echo Collector stopped with exit code %COLLECT_EXIT%.
echo Read the message above.
echo.
if /I not "%COLORCORE_NO_PAUSE%"=="1" pause
endlocal & exit /b %COLLECT_EXIT%

:missing_output
echo.
echo ERROR: Collector returned success but stage3_summary.json is missing.
echo This is treated as a hard failure.
echo.
if /I not "%COLORCORE_NO_PAUSE%"=="1" pause
endlocal & exit /b 92
