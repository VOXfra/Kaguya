@echo off
setlocal
cd /d "%~dp0"
title VOX - RDR2 Targeted Inventory

echo ============================================================
echo   VOX RDR2 Targeted Inventory v0.2
echo ============================================================
echo.
echo Lecture seule : aucun fichier du jeu ne sera modifie ou extrait.
echo Le scanner inventorie les archives RPF8 et cible les plus utiles
echo pour interactions, melee/animations, IA, interieurs et audio.
echo.

set "RDR2ROOT=%~1"
if "%RDR2ROOT%"=="" (
    set /p "RDR2ROOT=Colle le dossier contenant RDR2.exe : "
)

if "%RDR2ROOT%"=="" (
    echo [ERREUR] Aucun dossier indique.
    goto :fail
)

where py.exe >nul 2>nul
if %errorlevel%==0 (
    py -3 "%~dp0Scan-RDR2-Targets.py" "%RDR2ROOT%"
) else (
    where python.exe >nul 2>nul
    if not %errorlevel%==0 (
        echo [ERREUR] Python 3 est introuvable dans le PATH.
        goto :fail
    )
    python "%~dp0Scan-RDR2-Targets.py" "%RDR2ROOT%"
)

set "EXITCODE=%ERRORLEVEL%"
if not "%EXITCODE%"=="0" goto :failcode

echo.
echo [OK] Analyse terminee.
echo Envoie les trois fichiers suivants depuis TOOLS\VOX-RDR2-Targets :
echo   - RDR2-targets.csv
echo   - RDR2-target-summary.json
echo   - RDR2-targets.txt
echo.
pause
exit /b 0

:fail
set "EXITCODE=1"
:failcode
echo.
echo [ERREUR] Le scanner s'est termine avec le code %EXITCODE%.
echo.
pause
exit /b %EXITCODE%
