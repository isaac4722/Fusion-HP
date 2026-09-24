# Verificación local — Empaquetado portable (F6.06.6)

**Tarea:** 6 · **Agente:** general-purpose (packaging) · **Fecha:** 2026-09-24
**Entorno:** Linux (sandbox), bash, `zip` 3.0, `python3`, sin `iscc` (no hay
Inno Setup en Linux) y **sin toolchain MSVC/CMake nativo** → el núcleo
`LuminaCore.dll` no existe en local.

---

## 1. Comando ejecutado

```bash
cd /home/z/my-project/Fusion-HP
rm -rf dist
bash scripts/package_portable.sh          # modo AUTO-DETECCIÓN (builds locales)
echo "REAL EXIT=$?"                        # → 0
```

Modo auto-detección: sin `--staging`, el script localiza los binarios en
`src/managed/*/bin/Release/**` (builds net35/net48 producidas por
`dotnet build src/managed/Lumina.sln -c Release` en pasos anteriores).

## 2. Salida real (completa)

```text
== Empaquetado portable LuminaPresentation Suite v1.0.0-beta.1 ==
[info] modo AUTO-DETECCIÓN (builds locales)

== Inventario de fuentes ==

== Variante x86 → dist/portable/x86 ==
[OK] LuminaPresentation.exe             ← src/managed/Lumina.WPF/bin/Release/net48/LuminaPresentation.exe
[OK] LuminaPresentation.exe.config      ← src/managed/Lumina.WPF/bin/Release/net48/LuminaPresentation.exe.config
[OK] LuminaPresentation35.exe           ← src/managed/Lumina.UI/bin/Release/net35/LuminaPresentation35.exe
[OK] LuminaPresentation35.exe.config    ← src/managed/Lumina.UI/bin/Release/net35/LuminaPresentation35.exe.config
[OK] Lumina.Core.dll                    ← src/managed/Lumina.Core/bin/Release/net35/Lumina.Core.dll
[OK] Lumina.Api.dll                     ← src/managed/Lumina.Api/bin/Release/net35/Lumina.Api.dll
[OK] Lumina.Bridge.dll                  ← src/managed/Lumina.Bridge/bin/Release/net35/Lumina.Bridge.dll
[OK] app.net48.config                   ← src/managed/Lumina.WPF/app.net48.config
[OK] app.net35.config                   ← src/managed/Lumina.UI/app.net35.config
[info] Lumina.WPF.dll — omitido: la build actual fusiona el shell WPF DENTRO de LuminaPresentation.exe (v5.4.0)
[info] Lumina.PocFacade.dll — omitido por diseño (artefacto PoC; solo entra vía --staging)
[FALTA] LuminaCore.dll (x86) AUSENTE — núcleo nativo C++ no compilado en este entorno.
       Se empaqueta la parte gestionada con aviso (el paquete queda INCOMPLETO).
       El CI lo produce así (.github/workflows/ci.yml — job native-windows):
         cmake -S . -B build -A Win32 -DCMAKE_BUILD_TYPE=Release
         cmake --build build --config Release
         #  ->  build\native\Release\LuminaCore.dll
[info] LuminaLauncher.exe — omitido (opcional; el CI windows-latest lo compila)
[OK] LICENSE.md                         ← LICENSE.md
[OK] README.txt                         ← generado (es-VE)
[OK] data/sample/*                      ← resources/data/sample/
[OK] verificación de contenido: todos los archivos presentes con tamaño > 0
[OK] SHA256SUMS generado y verificado con `sha256sum -c` (12 entradas)
[OK] ZIP: /home/z/my-project/Fusion-HP/dist/LuminaPresentation-1.0.0-beta.1-portable-x86.zip
[OK] integridad del ZIP: `unzip -t` OK
       SHA256(LuminaPresentation-1.0.0-beta.1-portable-x86.zip) = 306e602fb84e4f39d9c232b5d24bec6b2f3d16b1b405c72cc1b2b87c7b4715b0
       variante x86: 7 binarios/datos esenciales empaquetados

== Variante x64 → dist/portable/x64 ==
[OK] LuminaPresentation.exe             ← src/managed/Lumina.WPF/bin/Release/net48/LuminaPresentation.exe
[OK] LuminaPresentation.exe.config      ← src/managed/Lumina.WPF/bin/Release/net48/LuminaPresentation.exe.config
[OK] LuminaPresentation35.exe           ← src/managed/Lumina.UI/bin/Release/net35/LuminaPresentation35.exe
[OK] LuminaPresentation35.exe.config    ← src/managed/Lumina.UI/bin/Release/net35/LuminaPresentation35.exe.config
[OK] Lumina.Core.dll                    ← src/managed/Lumina.Core/bin/Release/net35/Lumina.Core.dll
[OK] Lumina.Api.dll                     ← src/managed/Lumina.Api/bin/Release/net35/Lumina.Api.dll
[OK] Lumina.Bridge.dll                  ← src/managed/Lumina.Bridge/bin/Release/net35/Lumina.Bridge.dll
[OK] app.net48.config                   ← src/managed/Lumina.WPF/app.net48.config
[OK] app.net35.config                   ← src/managed/Lumina.UI/app.net35.config
[info] Lumina.WPF.dll — omitido: la build actual fusiona el shell WPF DENTRO de LuminaPresentation.exe (v5.4.0)
[info] Lumina.PocFacade.dll — omitido por diseño (artefacto PoC; solo entra vía --staging)
[FALTA] LuminaCore.dll (x64) AUSENTE — núcleo nativo C++ no compilado en este entorno.
       Se empaqueta la parte gestionada con aviso (el paquete queda INCOMPLETO).
       El CI lo produce así (.github/workflows/ci.yml — job native-windows):
         cmake -S . -B build -A x64   -DCMAKE_BUILD_TYPE=Release
         cmake --build build --config Release
         #  ->  build\native\Release\LuminaCore.dll
[info] LuminaLauncher.exe — omitido (opcional; el CI windows-latest lo compila)
[OK] LICENSE.md                         ← LICENSE.md
[OK] README.txt                         ← generado (es-VE)
[OK] data/sample/*                      ← resources/data/sample/
[OK] verificación de contenido: todos los archivos presentes con tamaño > 0
[OK] SHA256SUMS generado y verificado con `sha256sum -c` (12 entradas)
[OK] ZIP: /home/z/my-project/Fusion-HP/dist/LuminaPresentation-1.0.0-beta.1-portable-x64.zip
[OK] integridad del ZIP: `unzip -t` OK
       SHA256(LuminaPresentation-1.0.0-beta.1-portable-x64.zip) = 14dd7b080305c1a8c0f2a55c465a6d544a4927c16b649e3313bcb9a61bc2f5b3
       variante x64: 7 binarios/datos esenciales empaquetados

== Resumen ==
[AVISO] PAQUETE INCOMPLETO: sin LuminaCore.dll nativo (ver comandos CI impresos arriba). Use --allow-missing-native para silenciar el veredicto en ensayos locales.
       ZIPS y SHA256SUMS en: dist/portable/{x86,x64}/ y dist/*.zip
```

`REAL EXIT=0` — el nativo ausente se reporta con claridad (con el comando
exacto del CI que lo produce) y el empaquetado de la parte gestionada
continúa, tal como exige el fixture.

## 3. Artefactos producidos

| Archivo | Tamaño | SHA256 |
|---|---|---|
| `dist/LuminaPresentation-1.0.0-beta.1-portable-x86.zip` | 1 653 233 B | `306e602fb84e4f39d9c232b5d24bec6b2f3d16b1b405c72cc1b2b87c7b4715b0` |
| `dist/LuminaPresentation-1.0.0-beta.1-portable-x64.zip` | 1 653 235 B | `14dd7b080305c1a8c0f2a55c465a6d544a4927c16b649e3313bcb9a61bc2f5b3` |
| `dist/portable/x86/SHA256SUMS` (12 entradas) | — | verificado abajo |
| `dist/portable/x64/SHA256SUMS` (12 entradas) | — | verificado abajo |

## 4. Contenido del ZIP (x86 — la variante x64 es idéntica salvo el README)

```text
Archive:  dist/LuminaPresentation-1.0.0-beta.1-portable-x86.zip
  Length      Date    Time    Name
---------  ---------- -----   ----
   155136  2026-09-24 15:50   Lumina.Core.dll
   187904  2026-09-24 15:50   LuminaPresentation35.exe
      460  2026-09-24 15:50   LuminaPresentation.exe.config
      692  2026-09-24 15:50   app.net35.config
     3580  2026-09-24 15:50   LICENSE.md
   310272  2026-09-24 15:50   LuminaPresentation.exe
      460  2026-09-24 15:50   app.net48.config
        0  2026-09-24 15:50   data/
        0  2026-09-24 15:50   data/sample/
  4367827  2026-09-24 15:50   data/sample/rvr1909.bib
    27648  2026-09-24 15:50   Lumina.Bridge.dll
     2253  2026-09-24 15:50   README.txt
     1031  2026-09-24 15:50   SHA256SUMS
    62976  2026-09-24 15:50   Lumina.Api.dll
      693  2026-09-24 15:50   LuminaPresentation35.exe.config
---------                       -------
  5120932                     15 files
```

## 5. `sha256sum -c` (evidencia)

```text
$ (cd dist/portable/x86 && sha256sum -c SHA256SUMS)
LICENSE.md: OK
Lumina.Api.dll: OK
Lumina.Bridge.dll: OK
Lumina.Core.dll: OK
LuminaPresentation.exe: OK
LuminaPresentation.exe.config: OK
LuminaPresentation35.exe: OK
LuminaPresentation35.exe.config: OK
README.txt: OK
app.net35.config: OK
app.net48.config: OK
data/sample/rvr1909.bib: OK
```

La variante x64 pasa igual (12/12 `OK`). El propio script ejecuta
`sha256sum -c --quiet` como parte de su verificación post-empaquetado.

## 6. Pruebas adicionales del script (mismo día)

| Prueba | Resultado |
|---|---|
| `--allow-missing-native` (ensayo local) | exit 0; veredicto: «paquete(s) SIN núcleo nativo (—allow-missing-native). El paquete final de CI (windows-latest) SÍ incluye LuminaCore.dll.» |
| Nativo presente (dummy en `artifacts/native-{arch}/LuminaCore.dll`, directorio temporal de prueba, eliminado después) | exit 0; empaqueta nativo + gestionado; «paquete(s) completo(s): nativo + gestionado por arquitectura» |
| `--staging` vacío | **exit 1** con «no se empaquetó NADA — sin builds locales ni staging utilizable» + comando de build sugerido (cumple: falla solo si no hay nada que empaquetar) |

## 7. Limitaciones (transparentes)

1. **`LuminaCore.dll` nativo ausente en local** — en este sandbox Linux no hay
   MSVC/CMake ni Windows SDK. El ZIP de ensayo lleva `*** AVISO DE EMPAQUETADO
   ***` impreso dentro de su `README.txt` para que nadie lo confunda con el
   paquete final. La variante completa (nativo x86/x64 `/MT` + imports Win7
   verificados con `tools/verify_win7_imports.py`) se produce y se verifica en
   **CI windows-latest** (jobs `native-windows` + `package`).
2. **Exes gestionados AnyCPU en local** — `dotnet build` en Linux produce
   `LuminaPresentation.exe`/`LuminaPresentation35.exe` como AnyCPU; el CI
   final decide `PlatformTarget` por arquitectura (x86 en la variante x86)
   antes del empaquetado de distribución. El gate de bitness lo aplica
   `tools/verify_portable.py` en CI.
3. **`Lumina.WPF.dll` no viaja como DLL** — la build v5.4.0 compila el shell
   WPF *dentro* de `LuminaPresentation.exe`; el script lo reporta como
   omitido-con-motivo y el contrato lo contempla como opcional (si un build
   futuro lo separa, entra automáticamente desde el staging).
4. **`Lumina.PocFacade.dll` solo vía `--staging`** — artefacto del PoC, no del
   producto; se omite por diseño en auto-detección y se reporta.
5. **DLLs gestionadas net35 en el layout plano** — decisión documentada en el
   script: la API pública de `Lumina.Core/Api/Bridge` es idéntica en net35 y
   net48 (los `#if LUMINA_NET35` son internos); un ensamblado CLR2 carga sobre
   CLR 4.x (compatibilidad in-place) y es además el único que arranca en Win7
   SP1 con solo .NET 3.5. El exe WPF (net48) requiere .NET 4.x, igual que en
   el instalador (que verifica ≥ 4.7.2 y ofrece el redist offline).

## 8. Cómo reproducir

```bash
bash scripts/package_portable.sh --help
bash scripts/package_portable.sh                          # auto-detección
bash scripts/package_portable.sh --staging <dir-del-CI>   # layout CI
bash scripts/package_portable.sh --allow-missing-native   # ensayo sin nativo
(cd dist/portable/x86 && sha256sum -c SHA256SUMS)
unzip -t dist/LuminaPresentation-1.0.0-beta.1-portable-x86.zip
```
