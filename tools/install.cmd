@echo off
REM ============================================================================
REM  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac.
REM  Licencia View-Only.
REM ----------------------------------------------------------------------------
REM  tools\install.cmd : INSTALADOR DUAL (F6.06 del Plan de Ultra Implementación).
REM
REM  1. Detecta la arquitectura (x86/x64 del PROCESSOR_ARCHITECTURE y WOW64).
REM  2. Instala SOLO la variante adecuada en %LOCALAPPDATA%\LuminaPresentation
REM     (sin elevación: F6.06.5 — cero UAC, cero registro, cero Admin).
REM  3. Verifica el runtime .NET disponible y avisa si falta (OFRECE abrir la
REM     página del instalador OFFLINE oficial de .NET 4.8 — jamás descarga ni
REM     instala por sí solo: F0.03.8).
REM  4. Conserva el modo PORTABLE equivalente: copiar la carpeta y ejecutar
REM     LuminaLauncher.exe (F6.06.6 — el ZIP portable sigue siendo válido).
REM  5. Crea el marcador «installed.marker» → el launcher enruta los logs a
REM     %APPDATA%\AppHibrida\logs (F6.05.1).
REM  6. Crea el acceso directo del menú Inicio (sin registro: carpeta
REM     %APPDATA%\Microsoft\Windows\Start Menu\Programs).
REM ============================================================================
setlocal enabledelayedexpansion
title Instalador de LuminaPresentation

set "SRC=%~dp0.."
if not exist "%SRC%\LuminaLauncher.exe" set "SRC=%~dp0"

echo === Instalador de LuminaPresentation (modo usuario, sin Administrador) ===
echo.

REM ---- 1) arquitectura (F6.06.1) ------------------------------------------
set "ARCH=x86"
if /i "%PROCESSOR_ARCHITECTURE%"=="AMD64" set "ARCH=x64"
if /i "%PROCESSOR_ARCHITEW6432%"=="AMD64" set "ARCH=x64"
echo Arquitectura detectada: %ARCH%

REM ---- 2) destino (F6.06.5: sin elevación) --------------------------------
set "DEST=%LOCALAPPDATA%\LuminaPresentation"
echo Destino: %DEST%
echo.

if not exist "%DEST%" mkdir "%DEST%"

REM El paquete es único y el launcher ELIGE la variante: se copia completo
REM (F6.06.2: la variante instalada es la detectada por el launcher al
REM arrancar — net48/net35 según el runtime del equipo).
robocopy "%SRC%" "%DEST%" /E /NFL /NDL /NJH /NP /R:2 /W:1 ^
    /XD build bin obj .git /XF CMakeCache.txt >nul
if errorlevel 8 (
    echo ERROR: no se pudo copiar el programa a %DEST%.
    pause
    exit /b 1
)
echo Copia completada.

REM ---- 5) marcador de modo instalado (F6.05.1) -----------------------------
echo.> "%DEST%\installed.marker"
echo Modo INSTALADO activado (logs en %%APPDATA%%\AppHibrida\logs).

REM ---- 6) acceso directo del menú Inicio (carpetas de usuario) -------------
set "SHORTCUTDIR=%APPDATA%\Microsoft\Windows\Start Menu\Programs"
if not exist "%SHORTCUTDIR%" mkdir "%SHORTCUTDIR%"
set "VBS=%TEMP%\lumina-shortcut.vbs"
> "%VBS%" echo Set s = WScript.CreateObject("WScript.Shell")
>> "%VBS%" echo Set sc = s.CreateShortcut("%SHORTCUTDIR%\LuminaPresentation.lnk")
>> "%VBS%" echo sc.TargetPath = "%DEST%\LuminaLauncher.exe"
>> "%VBS%" echo sc.WorkingDirectory = "%DEST%"
>> "%VBS%" echo sc.Description = "LuminaPresentation Suite"
>> "%VBS%" echo sc.Save
cscript //nologo "%VBS%" >nul 2>&1
del "%VBS%" >nul 2>&1
echo Acceso directo creado en el menú Inicio.

REM ---- 3) verificación de .NET (F6.06.3) -----------------------------------
reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release >nul 2>&1
if errorlevel 1 (
    echo.
    echo AVISO: no se detectó .NET Framework 4.x.
    echo El programa puede ejecutarse en MODO EMERGENCIA (perfil C: proyección
    echo nativa de texto/imagen/video sin .NET). Para TODAS las funciones
    echo instale .NET Framework 4.8 OFFLINE:
    echo   https://dotnet.microsoft.com/download/dotnet-framework/net48
    echo.
    set /p OPENNET="¿Abrir ahora la página de descarga? (S/N): "
    if /i "!OPENNET!"=="S" start "" https://dotnet.microsoft.com/download/dotnet-framework/net48
) else (
    echo Runtime .NET 4.x detectado: se usará la variante completa.
)

echo.
echo Instalación terminada. Ejecute «LuminaPresentation» desde el menú Inicio.
echo (El modo PORTABLE sigue disponible: copie la carpeta y ejecute
echo  LuminaLauncher.exe directamente — sin instalar nada.)
pause
exit /b 0
