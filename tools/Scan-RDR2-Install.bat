@echo off
setlocal
cd /d "%~dp0"
title VOX - Scan RDR2 Install

echo ============================================================
echo   VOX RDR2 Reference Scanner
echo ============================================================
echo.
echo Double-clic : detection automatique puis demande du chemin si besoin.
echo Tu peux aussi glisser le dossier de RDR2 directement sur ce .bat.
echo La fenetre restera ouverte jusqu'a ce que tu appuies sur une touche.
echo.

if not "%~1"=="" (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scan-RDR2-Install.ps1" -Root "%~1"
) else (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scan-RDR2-Install.ps1"
)
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
    echo [OK] Scan termine.
    echo Les rapports sont dans le dossier TOOLS.
) else (
    echo [ERREUR] Le scanner s'est termine avec le code %EXITCODE%.
)
echo.
echo Tu peux maintenant m'envoyer les fichiers CSV generes.
echo.
pause
exit /b %EXITCODE%
