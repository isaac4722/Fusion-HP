---
name: win7-compat
description: Compatibilidad Windows 7 SP1 → Windows 11 para el núcleo nativo C++. Verifica _WIN32_WINNT=0x0601, CRT estático /MT, ausencia de imports Win8+ y detección por RtlGetVersion. Usar siempre que se toque el bootstrap o se añada una API de Windows.
---

# win7-compat — Contrato de compatibilidad Win7 x86 → Win11 x64

## Reglas vinculantes [SPEC §3.1, §4]

1. `_WIN32_WINNT=0x0601` y `WINVER=0x0601` en TODA unidad de compilación del
   núcleo. Sin esto el SDK moderno asume 0x0A00 y expone APIs inexistentes en Win7.
2. Detección de SO SOLO con `RtlGetVersion` / `VerifyVersionInfo`.
   `GetVersion` está prohibida (obsoleta y minted por el manifiesto).
3. Arquitectura con `IsWow64Process2` (o fallback `IsWow64Process`).
4. CRT enlazado estáticamente `/MT` — cero dependencias redistribuibles.
5. En x86: ningún import de APIs Win8+ (`GetSystemTimePreciseAsFileTime`,
   `std::thread` del STL que arrastra imports nuevos). Usar
   `CreateThread` Win32 puro y `CRITICAL_SECTION` si hace falta.
6. Lectura del Registro SOLO de lectura (claves NDP de .NET) — jamás escritura.
7. Ningún binario del núcleo puede requerir permisos de elevación.

## Lista de verificación por pieza

- [ ] ¿La pieza compila con `_WIN32_WINNT=0x0601`?
- [ ] ¿Los imports del DLL/linker solo incluyen kernel32/user32/gdi32/d2d1/
      gdiplus/ole32/oleaut32 de Win7 RTM?
- [ ] ¿Hay camino de degradación si la API falta (GetProcAddress + guard)?
- [ ] ¿El log registra SO/arquitectura/perfil detectado?

## Fallos históricos del repo (no repetir)

- `std::thread`/`std::mutex` del STL en el launcher x86 → imports Win8+ →
  el DLL no cargaba en Win7 SP1 (ciclos v6/v7 del repo).
- `GetSystemTimePreciseAsFileTime` estático → mismo problema; shim MASM
  `Win7CompatX86.asm` como resolución.
- Render compuesto sin `GdiplusStartup` previo → crash latente (v7.1.0).
