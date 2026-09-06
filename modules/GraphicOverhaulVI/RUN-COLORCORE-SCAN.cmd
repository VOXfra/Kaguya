@echo off
setlocal
cd /d "%~dp0"
echo.
echo ==========================================
echo   GraphicOverhaulVI - ColorCoreVI Scan
echo ==========================================
echo.
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\Scan-ColorCoreVI.ps1"
echo.
if errorlevel 1 (
  echo Scan stopped with an error. Read the message above.
) else (
  echo Scan finished. The results are in the scan-output folder.
)
echo.
pause
endlocal
