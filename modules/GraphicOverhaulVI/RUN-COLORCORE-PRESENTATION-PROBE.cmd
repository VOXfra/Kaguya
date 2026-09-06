@echo off
setlocal
cd /d "%~dp0"

echo.
echo ==========================================
echo   GraphicOverhaulVI - ColorCoreVI P0005
echo      External Presentation Probe v0.1.4
echo ==========================================
echo.
echo This tool is EXTERNAL and read-only.
echo It does NOT load an ASI into GTA and does NOT hook DXGI/D3D12.
echo.
echo Leave this window open, then launch GTA V Enhanced normally.
echo.

if not exist "%~dp0ColorCoreVIExternalProbe.exe" (
  echo ERROR: ColorCoreVIExternalProbe.exe is missing.
  pause
  exit /b 1
)

del /q "%~dp0ColorCoreVI_ExternalProbe.log" 2>nul
"%~dp0ColorCoreVIExternalProbe.exe" --wait 180 --duration 60
set "PROBE_EXIT=%ERRORLEVEL%"

echo.
if "%PROBE_EXIT%"=="0" (
  echo Probe finished successfully.
  echo Send ColorCoreVI_ExternalProbe.log back in ChatGPT.
) else (
  echo Probe stopped with exit code %PROBE_EXIT%.
  echo Send ColorCoreVI_ExternalProbe.log back in ChatGPT anyway.
)
echo.
pause
endlocal & exit /b %PROBE_EXIT%
