@echo off
setlocal
cd /d "%~dp0"
title VOX RDR2 RPF8 Cataloger v0.3.0 - DEEP

echo ============================================================
echo   VOX RDR2 RPF8 Cataloger v0.3.0 - DEEP
echo ============================================================
echo.
echo Mode DEEP : scan integral des archives CIBLES pour offsets RPF8/RSC8.
echo Aucun asset n'est extrait.
echo.
set "ROOT="
if not "%~1"=="" set "ROOT=%~1"
if "%ROOT%"=="" set /p "ROOT=Colle le dossier qui contient RDR2.exe : "

if exist "%~dp0VOX-RDR2-RPF8-Cataloger.exe" (
  "%~dp0VOX-RDR2-RPF8-Cataloger.exe" --root "%ROOT%" --out "%~dp0VOX-RDR2-RPF8-Catalog" --deep-signatures
) else (
  set "PYEXE="
  where py >nul 2>nul && set "PYEXE=py -3"
  if not defined PYEXE where python >nul 2>nul && set "PYEXE=python"
  if not defined PYEXE (
    echo [ERREUR] EXE et Python 3 introuvables.
    pause
    exit /b 1
  )
  %PYEXE% "%~dp0vox_rdr2_rpf8_catalog.py" --root "%ROOT%" --out "%~dp0VOX-RDR2-RPF8-Catalog" --deep-signatures
)
set "EC=%ERRORLEVEL%"
echo.
if "%EC%"=="0" (echo [OK] Scan DEEP termine.) else (echo [ERREUR] Code %EC%.)
echo.
pause
exit /b %EC%
