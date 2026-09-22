#!/usr/bin/env python3
# ============================================================================
#  LuminaPresentation Suite v5.2.0 «MOTOR» — tools/verify_win7_imports.py
#  Gate de compatibilidad Windows 7 SP1 para binarios nativos (x86/x64).
#
#  QUÉ HACE
#  --------
#  Parsea la tabla de IMPORTACIONES ESTÁTICAS de un PE (DLL/EXE) y falla si:
#    1) Aparece cualquier API que NO existe en Windows 7 SP1 (Win8+/Win10+):
#       p. ej. GetSystemTimePreciseAsFileTime — la que, como import estático,
#       hizo que LuminaCore.dll v5.1.1 NO CARGARA en Win7 SP1 x86
#       (LoadLibrary → ERROR_PROCEDURE_NOT_FOUND → app en «modo limitado»:
#       «carga la GUI pero más nada»).
#    2) Aparece runtime DINÁMICO prohibido en un binario /MT: api-ms-win-crt-*,
#       ucrtbase.dll, vcruntime*.dll, msvcp140.dll…
#    3) (opcional) El bitness del PE no coincide con --expect-bits.
#
#  Las tablas DELAY-LOAD (directorio 13) NO cuentan: una API delay-loaded se
#  resuelve al primer uso y un hook puede sustituirla — esa ES la vía válida
#  de convivencia con APIs Win8+ (ver native/core/src/Win7Compat.cpp).
#
#  USO (CI y local)
#  ----------------
#    python tools/verify_win7_imports.py bin/LuminaCore.dll [otros.bin...]
#    python tools/verify_win7_imports.py --expect-bits 32 bin/LuminaCore.dll
#
#  Exit code: 0 = PASS · 1 = FAIL (con detalle por archivo y API).
#  Requisito: Python 3.7+ (solo stdlib) — corre en windows-latest y Linux.
# ============================================================================
import argparse
import struct
import sys

# ---------------------------------------------------------------------------
# APIs que NO existen en Windows 7 SP1 (aparecen en Win8 o Win10).
# Mantener ALFABÉTICO. Antes de añadir una, verificar contra la doc del SDK
# («Minimum supported client») — incluir solo las que el cargador resuelve.
# ---------------------------------------------------------------------------
WIN8_PLUS_APIS = {
    "CreateFile2",
    "GetDpiForMonitor",
    "GetDpiForSystem",
    "GetDpiForWindow",
    "GetProcessMitigationPolicy",
    "GetSystemMetricsForDpi",
    "GetSystemTimePreciseAsFileTime",
    "IsImmersiveProcess",
    "PathCchAppend",
    "PathCchAppendEx",
    "PathCchCanonicalize",
    "PathCchCanonicalizeEx",
    "PathCchCombine",
    "PathCchCombineEx",
    "PathCchStripPrefix",
    "PowerSettingRegisterNotification",
    "PowerSettingUnregisterNotification",
    "RoInitialize",
    "RoUninitialize",
    "SetProcessDpiAwarenessContext",
    "SetProcessMitigationPolicy",
    "SetThreadDpiAwarenessContext",
    "WakeByAddressAll",
    "WakeByAddressSingle",
    "WaitOnAddress",
    "WindowsCreateString",
}

# Prefijos/nombres de DLL de runtime DINÁMICO — prohibidos en binarios /MT
# (el paquete debe correr sin instalar UCRT ni VC++ Redist).
FORBIDDEN_DLLS = (
    "api-ms-win-crt-",
    "api-ms-win-core-file-l2",
    "ucrtbase",
    "vcruntime",
    "msvcp140",
    "concrt140",
    "vcomp140",
)

DELAY_IMPORT_DIR = 13
IMPORT_DIR = 1


def _fail(msg):
    print("WIN7-IMPORTS FAIL: %s" % msg)
    return 1


def parse_pe(path):
    """Devuelve (machine, bits, imports_estáticos, delay_imports, error)."""
    try:
        with open(path, "rb") as f:
            data = f.read()
    except OSError as e:
        return None, None, {}, {}, "no se pudo leer: %s" % e

    if len(data) < 0x40 or struct.unpack_from("<H", data, 0)[0] != 0x5A4D:
        return None, None, {}, {}, "no es un PE (falta firma MZ)"
    pe_off = struct.unpack_from("<I", data, 0x3C)[0]
    if data[pe_off:pe_off + 4] != b"PE\x00\x00":
        return None, None, {}, {}, "no es un PE (firma PE ausente)"

    machine = struct.unpack_from("<H", data, pe_off + 4)[0]
    num_sections = struct.unpack_from("<H", data, pe_off + 6)[0]
    opt_size = struct.unpack_from("<H", data, pe_off + 20)[0]
    opt_off = pe_off + 24
    magic = struct.unpack_from("<H", data, opt_off)[0]
    if magic == 0x20B:
        bits, dd_off, word = 64, opt_off + 112, 8
    elif magic == 0x10B:
        bits, dd_off, word = 32, opt_off + 96, 4
    else:
        return None, None, {}, {}, "opcional header mágico 0x%X desconocido" % magic

    sections = []
    sec_off = opt_off + opt_size
    for i in range(num_sections):
        o = sec_off + i * 40
        vsize, va, rawsize, raw_ptr = struct.unpack_from("<IIII", data, o + 8)
        sections.append((va, raw_ptr, max(vsize, rawsize)))

    def rva2off(rva):
        for va, raw_ptr, span in sections:
            if va <= rva < va + span:
                return raw_ptr + (rva - va)
        return None

    def read_imports(dir_index):
        rva = struct.unpack_from("<I", data, dd_off + dir_index * 8)[0]
        imports = {}
        if rva == 0:
            return imports
        off = rva2off(rva)
        if off is None:
            return imports
        i = 0
        while True:
            ent = off + i * 20
            if ent + 20 > len(data):
                break
            ilt, _ts, _fwd, name_rva, iat = struct.unpack_from("<IIIII", data, ent)
            if name_rva == 0:
                break
            noff = rva2off(name_rva)
            if noff is None:
                i += 1
                continue
            dll = data[noff:data.index(b"\x00", noff)].decode("ascii", "replace")
            funcs = []
            walk = rva2off(ilt if ilt != 0 else iat)
            if walk is not None:
                j = 0
                while True:
                    pos = walk + j * word
                    if pos + word > len(data):
                        break
                    val = struct.unpack_from("<Q" if word == 8 else "<I", data, pos)[0]
                    if val == 0:
                        break
                    if val & (1 << 63 if word == 8 else 1 << 31):
                        funcs.append("#ordinal%d" % (val & 0xFFFF))
                    else:
                        ho = rva2off(val & 0x7FFFFFFF)
                        if ho is None:
                            j += 1
                            continue
                        end = data.index(b"\x00", ho + 2)
                        funcs.append(data[ho + 2:end].decode("ascii", "replace"))
                    j += 1
            imports[dll] = funcs
            i += 1
        return imports

    return machine, bits, read_imports(IMPORT_DIR), read_imports(DELAY_IMPORT_DIR), None


def check_file(path, expect_bits=None):
    print("== %s" % path)
    machine, bits, imports, delay, err = parse_pe(path)
    if err:
        return _fail(err)

    arch = {0x14C: "x86", 0x8664: "x64", 0x1C0: "ARM", 0xAA64: "ARM64"}.get(machine, "?")
    print("   PE %d bits (machine 0x%X/%s)" % (bits, machine, arch))

    if expect_bits and bits != expect_bits:
        return _fail("bitness %d != esperado %d" % (bits, expect_bits))

    failures = 0

    # DLLs delay-loaded: sus APIs NO fallan la carga (se resuelven al uso).
    delay_api_apis = set()
    for dll, funcs in delay.items():
        delay_api_apis.update(f for f in funcs if not f.startswith("#ordinal"))
    if delay:
        print("   delay-load (no bloquean la carga): %s"
              % ", ".join("%s(%d)" % (d, len(f)) for d, f in sorted(delay.items())))

    for dll in sorted(imports):
        funcs = imports[dll]
        low = dll.lower()
        if any(low.startswith(p) for p in FORBIDDEN_DLLS):
            print("   !! DLL PROHIBIDA (runtime dinámico en binario /MT): %s" % dll)
            failures += 1
            continue
        bad = [f for f in funcs
               if f in WIN8_PLUS_APIS and f not in delay_api_apis]
        # Una API listada como delay-load en OTRA descriptor del mismo binario
        # ya no es import estático — este caso no debería darse (el linker la
        # saca de la tabla estática), pero por si acaso lo toleramos arriba.
        if bad:
            for f in bad:
                print("   !! %s ! %s  ← NO existe en Win7 SP1 (Win8+/Win10+)"
                      % (dll, f))
            failures += 1
        else:
            print("   ok  %-20s %d import(s)" % (dll, len(funcs)))

    return failures


def main():
    ap = argparse.ArgumentParser(description="Gate Win7 SP1 de imports de PE")
    ap.add_argument("files", nargs="+", help="binarios PE a auditar")
    ap.add_argument("--expect-bits", type=int, choices=(32, 64), default=None,
                    help="exigir este bitness de PE")
    args = ap.parse_args()

    total = 0
    for path in args.files:
        total += check_file(path, args.expect_bits)

    if total:
        print("\nWIN7-IMPORTS FAIL (%d problema/s) — el binario NO cargaría en "
              "Windows 7 SP1 o exigiría runtime dinámico." % total)
        sys.exit(1)
    print("\nWIN7-IMPORTS PASS — sin imports estáticos incompatibles con Win7 SP1")
    sys.exit(0)


if __name__ == "__main__":
    main()
