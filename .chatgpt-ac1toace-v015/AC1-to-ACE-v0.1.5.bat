@echo off
setlocal
cd /d "%~dp0"
where py >nul 2>nul
if %errorlevel%==0 (
  py -3 "AC1-to-ACE-v0.1.5.py"
) else (
  python "AC1-to-ACE-v0.1.5.py"
)
if errorlevel 1 pause
