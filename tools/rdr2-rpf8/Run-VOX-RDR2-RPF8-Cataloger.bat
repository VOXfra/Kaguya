@echo off
setlocal
cd /d "%~dp0"
title VOX RDR2 RPF8 Cataloger v0.3.0

echo ============================================================
echo   VOX RDR2 RPF8 Cataloger v0.3.0
echo ============================================================
echo.
echo Lecture seule. Aucun fichier du jeu ne sera modifie ou extrait.
echo.
set "ROOT="
if not "%~1"=="" set "ROOT=%~1"
if "%ROOT%"=="" set /p "ROOT=Colle le dossier qui contient RDR2.exe : "

if exist "%~dp0VOX-RDR2-RPF8-Cataloger.exe" (
  "%~dp0VOX-RDR2-RPF8-Cataloger.exe" --root "%ROOT%" --out "%~dp0VOX-RDR2-RPF8-Catalog"
) else (
  set "PYEXE="
  where py >nul 2>nul && set "PYEXE=py -3"
  if not defined PYEXE where python >nul 2>nul && set "PYEXE=python"
  if not defined PYEXE (
    echo [ERREUR] EXE et Python 3 introuvables.
    pause
    exit /b 1
  )
  %PYEXE% "%~dp0vox_rdr2_rpf8_catalog.py" --root "%ROOT%" --out "%~dp0VOX-RDR2-RPF8-Catalog"
)
set "EC=%ERRORLEVEL%"
echo.
if "%EC%"=="0" (echo [OK] Catalogue genere.) else (echo [ERREUR] Code %EC%.)
echo.
pause
exit /b %EC%
