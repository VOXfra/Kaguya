@echo off
setlocal
cd /d "%~dp0"
set "COLORCORE_SCRIPT=%~dp0tools\Scan-ColorCoreVI.ps1"
echo.
echo ==========================================
echo   GraphicOverhaulVI - ColorCoreVI Scan
echo              Scanner v0.1.3
echo ==========================================
echo.
echo [1/2] Checking PowerShell syntax...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -Command "$tokens=$null; $parseErrors=$null; [void][System.Management.Automation.Language.Parser]::ParseFile($env:COLORCORE_SCRIPT,[ref]$tokens,[ref]$parseErrors); if ($parseErrors.Count -gt 0) { Write-Host 'PowerShell parser found errors:' -ForegroundColor Red; $parseErrors ^| ForEach-Object { Write-Host (' - ' + $_.Message) -ForegroundColor Red }; exit 90 } else { Write-Host 'Syntax OK.' -ForegroundColor Green }"
if errorlevel 1 (
  echo.
  echo Preflight failed. The scanner was NOT started.
  echo Please send the full message above back in ChatGPT.
  echo.
  pause
  exit /b 1
)
echo.
echo [2/2] Starting read-only scan...
powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%COLORCORE_SCRIPT%"
echo.
if errorlevel 1 (
  echo Scan stopped with an error. Read the message above.
) else (
  echo Scan finished. The results are in the scan-output folder.
)
echo.
pause
endlocal
