@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0RUN_DIRECT_GTA_BRIDGE.ps1"
if errorlevel 1 (
  echo.
  echo [ERROR] Direct bridge stopped before producing the GTA V package.
  pause
)
endlocal
