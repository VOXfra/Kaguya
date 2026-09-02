@echo off
setlocal
cd /d "%~dp0"
title VOX - Scan Sound Pack

echo ============================================================
echo   VOX Sound Pack Reference Scanner
echo ============================================================
echo.
echo Double-clic : le scanner te demandera le dossier du pack / SFX.
echo Tu peux aussi glisser le dossier SFX directement sur ce .bat.
echo La fenetre restera ouverte jusqu'a ce que tu appuies sur une touche.
echo.

if not "%~1"=="" (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scan-SoundPack.ps1" -SoundPackSfxPath "%~1"
) else (
    powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File "%~dp0Scan-SoundPack.ps1"
)
set "EXITCODE=%ERRORLEVEL%"

echo.
if "%EXITCODE%"=="0" (
    echo [OK] Scan termine.
    echo Les rapports sont dans TOOLS\VOX-SoundPack-Manifest.
) else (
    echo [ERREUR] Le scanner s'est termine avec le code %EXITCODE%.
)
echo.
echo Tu peux maintenant m'envoyer SoundPackManifest.csv ou .txt.
echo.
pause
exit /b %EXITCODE%
