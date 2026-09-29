@echo off
setlocal
cd /d "%~dp0"
echo ============================================================
echo  AC1 to ACE Converter - UPGRADE v0.1.5
echo ============================================================
where py >nul 2>nul
if %errorlevel%==0 (
  py -3 "upgrade_v015.py"
) else (
  python "upgrade_v015.py"
)
if errorlevel 1 pause
