@echo off
setlocal
cd /d "%~dp0"
title VOX - Scan RDR2 Install

echo ============================================================
echo   VOX RDR2 Reference Scanner
echo ============================================================
echo.
echo Le scanner va lancer le script PowerShell et conserver cette
echo fenetre ouverte pour afficher le resultat ou une erreur.
echo.

powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scan-RDR2-Install.ps1" %*
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
    echo [OK] Scan termine.
) else (
    echo [ERREUR] Le scanner s'est termine avec le code %EXITCODE%.
)
echo.
echo Tu peux maintenant m'envoyer le rapport genere.
echo.
pause
exit /b %EXITCODE%
