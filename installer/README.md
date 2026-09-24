# Instalador dual — `installer/AppHibrida.iss`

Fixture **F6.06** (Instalador dual) de la reestructuración v1.0.0-beta.1.
Fuente normativa: `spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md`
(§4.4 Distribución) y `spec/plan-ultra-implementacion.md` (F6.05, F6.06).

Script de **Inno Setup 6.3+** (requerido: usa `x86compatible`/`x64compatible`
y `{autopf}`, introducidos en 6.3). Comentarios del script en inglés; todo
texto visible del asistente en **español (es-VE)**.

---

## 1. Qué instala cada variante

El instalador es **dual**: un único `setup.exe` sirve para Windows 7 SP1 x86
hasta Windows 11 x64 y decide en runtime qué instalar.

| Máquina detectada | Modo de instalación | Lo que se copia a `{app}` |
|---|---|---|
| x64 | 64-bit (`ArchitecturesInstallIn64BitMode=x64compatible`) | `LuminaCore.dll` **x64** (nativo) + capa gestionada completa |
| x86 | 32-bit | `LuminaCore.dll` **x86** (nativo) + capa gestionada completa |

La **capa gestionada** (componente `managed`, obligatorio) lleva:
`LuminaPresentation.exe` (WPF, .NET 4.8) + su `.config`,
`LuminaPresentation35.exe` (WinForms baseline, .NET 3.5) + su `.config`,
`Lumina.Core.dll`, `Lumina.Api.dll`, `Lumina.Bridge.dll`, y opcionales
(`Lumina.WPF.dll`, `Lumina.PocFacade.dll`, `LuminaLauncher.exe`,
`app.net35.config`, `app.net48.config`, `README.txt`, `LICENSE.md`, `data\`).

La detección usa el patrón del ejemplo oficial **`64BitTwoArch.iss`**:
entradas `[Files]` por arquitectura con `Check: Is64BitInstallMode` /
`Check: not Is64BitInstallMode`. Si falta un binario del staging, **ISCC
falla en compilación** a propósito (nunca un instalador a medias); solo los
archivos genuinamente opcionales llevan `skipifsourcedoesntexist`.

**Componentes** (página de componentes):

1. `managed` — capa gestionada (fijo, no deseleccionable).
2. `native_x64` / `native_x86` — núcleo nativo C++; solo el adecuado a la
   arquitectura es visible (el otro se oculta con `Check:`).
3. `redist` — instalador offline oficial de .NET Framework 4.8
   (`ndp48-x86-x64-allos-enu.exe`, ~111 MB). **Opcional**, solo aparece
   cuando falta .NET 4.7.2+, y va **marcado por defecto** en ese caso.

El instalador crea accesos directos en el menú Inicio (variante WPF, baseline
35 y «Desinstalar»), en la zona común o por usuario según el modo final de
instalación (`IsAdminInstallMode`).

## 2. Verificación de .NET (F6.06.3 y F6.06.4)

- **Lectura SOLO** de la clave estándar NDP, en la vista de Registro que
  corresponda (se prueba `HKLM`, luego la vista alternativa `HKLM32`/`HKLM64`
  bajo WoW64):

  ```
  HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full
    Release  >=  461808        (== .NET Framework 4.7.2, mínimo normativo)
  ```

  (`528040` sería el valor exacto de 4.8; 461808 es el mínimo fijado por el
  fixture. La comparación y el valor se escriben al log del instalador con
  `/LOG`.)

- **Si falta .NET**: la aplicación puede instalarse igualmente (degrada a
  perfiles B/C según §4.2 — nunca se bloquea la instalación):
  - Aviso informativo no bloqueante al salir de la página de componentes.
  - Componente opcional `redist`, **marcado por defecto**, que instala
    `{app}\redist\ndp48-x86-x64-allos-enu.exe` y, en post-instalación, lo
    ejecuta con `ShellExec('runas', ..., '/passive /norestart /showrmui')`.
    La única elevación del flujo es el UAC que el propio instalador de
    Microsoft exige; códigos `0/3010/1641` se consideran éxito (3010/1641
    piden reinicio).
  - **Nunca hay descarga automática.** Si el redist no viaja con el paquete
    (o el usuario cancela), se muestra un mensaje claro con la URL oficial
    `https://go.microsoft.com/fwlink/?linkid=2088631` y se continúa.
- **El redist es opcional en compilación**: si `dist\redist\` no contiene el
  exe al compilar, el componente ni siquiera se compila en el instalador
  (sondeo ISPP `#ifexist`). Compilarlo sin redist es válido y esperado.
- Nombre de archivo: el fixture normaliza `ndp48-x86-x64-**allos**-enu.exe`
  (doble «l»); el nombre oficial de Microsoft es `ndp48-x86-x64-alos-enu.exe`
  — renombre el archivo descargado al colocarlo en `dist\redist\`.

## 3. Modelo de privilegios y Registro (decisiones documentadas)

- **Privilegios**: `PrivilegesRequired=lowest` +
  `PrivilegesRequiredOverridesAllowed=dialog commandline` con
  `DefaultDirName={autopf}\AppHibrida`. Por defecto (uso normal) **no se
  exige elevación**: instalación por usuario en
  `%LOCALAPPDATA%\Programs\AppHibrida` (así resuelve `{autopf}` sin admin).
  El asistente ofrece el diálogo «solo para mí / para todos» y `/ALLUSERS`
  funciona por línea de comandos: eligiendo «todos», `{autopf}` resuelve a
  `Program Files` con el UAC estándar. Así se cumple «no exigir elevación
  para uso normal» sin romper la instalación en `Program Files`.
- **Registro**: la sección `[Registry]` **no existe**. La aplicación NUNCA
  usa el Registro (F6.05.5); el único acceso al Registro en todo el ciclo de
  vida es (a) la lectura del NDP descrita arriba y (b) las entradas estándar
  de desinstalación que Inno Setup escribe por diseño
  (`UninstallString`, `DisplayName`, etc., en
  `HKCU\...\Uninstall\{AppId}_is1` para instalaciones por usuario o `HKLM`
  para todos los usuarios). Nada más, y ninguna escritura de configuración.
- **Desinstalador**: borra los binarios de `{app}` (incluido el redist si se
  instaló). Los datos de usuario (`%APPDATA%\AppHibrida`: configuración JSON,
  proyectos, biblias, `logs/`) se **conservan por defecto**; se ofrece una
  pregunta Sí/No explícita en la desinstalación para eliminarlos (Inno no
  permite casillas reales en el desinstalador; la pregunta confirmada es el
  equivalente documentado).

## 4. Relación con el modo portable

- El **portable NO va dentro del instalador** (F6.06.6): se distribuye como
  ZIP por arquitectura que produce `scripts/package_portable.sh`
  (`dist/LuminaPresentation-<versión>-portable-<arch>.zip`).
- Ambos usan el **mismo layout plano** de archivos (por eso el `.iss` toma su
  staging de `dist\portable\{x86,x64}` por defecto).
- Diferencia de rutas (F6.05): **instalado** → configuración y logs en
  `%APPDATA%\AppHibrida`; **portable** → todo junto a la carpeta del programa.
  En ambos casos: **nunca el Registro**.

## 5. Cómo compilar

```bat
:: Desde la raíz del repositorio, con Inno Setup 6.3+ en el PATH:
iscc installer\AppHibrida.iss
::  -> dist\installer\LuminaPresentation-1.0.0-beta.1-setup.exe

:: Staging alternativo (p. ej. artefactos del CI):
iscc /DStagingX86="C:\ci\stage\x86" /DStagingX64="C:\ci\stage\x64" installer\AppHibrida.iss
```

Requisitos del staging por defecto (`dist\portable\{x86,x64}\`, que es lo que
produce `scripts/package_portable.sh`): `LuminaPresentation.exe`(+`.config`),
`LuminaPresentation35.exe`(+`.config`), `Lumina.Core.dll`, `Lumina.Api.dll`,
`Lumina.Bridge.dll`, `LuminaCore.dll`, y opcionalmente `Lumina.WPF.dll`,
`Lumina.PocFacade.dll`, `LuminaLauncher.exe`, `app.net35.config`,
`app.net48.config`, `README.txt`, `LICENSE.md`, `data\*`.
Opcional: `dist\redist\ndp48-x86-x64-allos-enu.exe` (redist .NET 4.8).

## 6. Cómo validar

**No se puede compilar el `.iss` en Linux** (no existe `iscc` aquí): la
sintaxis se revisó contra el ejemplo oficial `64BitTwoArch.iss` y la
documentación de Inno Setup 6.3. Validación real en Windows:

1. **Compilar** (debe terminar sin errores ni warnings de parámetros):

   ```bat
   iscc /O- installer\AppHibrida.iss
   ```

   Qué mirar si falla:
   - `Unknown directive or invalid expression` en `#ifexist RedistSourcePath`
     → sustituya el identificador por la ruta literal entre comillas:
     `#ifexist "..\dist\redist\ndp48-x86-x64-allos-enu.exe"`.
   - `Unknown [Messages] entry` → algún override de `[Messages]` no coincide
     con su versión de `Default.isl`; elimine esa línea (solo pierde esa
     etiqueta en español).
   - `Source file ... does not exist` → el staging por arquitectura está
     incompleto (comportamiento intencional: fallo ruidoso, no silencioso).
     Ejecute `scripts/package_portable.sh` en el CI o pase `/DStagingX86` y
     `/DStagingX64`.

2. **Probar ambos caminos de redist**: (a) con
   `dist\redist\ndp48-x86-x64-allos-enu.exe` presente al compilar → el
   instalador debe ser ~111 MB más grande y ofrecer el componente si falta
   .NET; (b) sin él → el instalador compila y el mensaje de fallback con la
   URL aparece cuando falta .NET.

3. **Probar en VMs** (F6.07): Win7 SP1 x86 (32-bit mode, LuminaCore x86),
   Win7 SP1 x64, Win11 x64 (64-bit mode, `{autopf}` → Program Files si se
   elige «todos», o `%LOCALAPPDATA%\Programs\AppHibrida` por defecto).
   Verificar con `/LOG` la línea
   `.NET Framework NDP v4 Release detected: <n> (minimum 461808)`.

4. **Auditoría de Registro** (F6.09): con Process Monitor filtrando por el
   proceso de instalación, confirmar que las únicas claves escritas son las
   estándar `Uninstall\{AppId}_is1` (HKCU en modo por usuario) y que la app
   instalada no toca el Registro en ejecución (config en
   `%APPDATA%\AppHibrida`).

5. **Desinstalación**: binarios fuera; datos de usuario conservados al
   responder «No», eliminados al responder «Sí».
