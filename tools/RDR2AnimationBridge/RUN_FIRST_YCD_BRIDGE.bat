@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0RUN_FIRST_YCD_BRIDGE.ps1"
if errorlevel 1 (
  echo.
  echo [ERROR] The bridge runner stopped with an error.
  pause
)
endlocal
