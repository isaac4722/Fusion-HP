#!/usr/bin/env python3
# ============================================================================
#  LuminaPresentation Suite - tools/verify_portable.py
#  Copyright (c) 2026 Isaac. Licencia View-Only.
# ----------------------------------------------------------------------------
#  GATE de empaquetado portable: audita las tablas de imports PE de TODOS los
#  binarios del paquete (exe + dlls, recursivo) y garantiza que cada DLL
#  requerida esta: (a) presente en el paquete, o (b) es una DLL de sistema de
#  Windows. Falla (exit 1) listando las faltantes.
#
#  Motivo: la Release v1.0.1 publicaba paquetes sin el runtime de MinGW
#  (libgcc_s_dw2-1/libgcc_s_seh-1, libstdc++-6, libwinpthread-1) que las
#  Qt5*.dll precompiladas importan de forma dinamica -> el exe moria al
#  arrancar en cualquier Windows limpio con "no se encontro
#  libwinpthread-1.dll". Este gate se ejecuta en CI ANTES de empaquetar.
#
#  Uso: python tools/verify_portable.py <carpeta_staging> [<arquitectura>]
#  Arquitectura opcional (x86|x64) para verificar que TODOS los PE coinciden.
# ============================================================================
import os
import struct
import sys
import glob

# DLLs que garantiza el propio sistema operativo Windows (Win7 SP1+).
# Cualquier otra DLL requerida debe viajar dentro del paquete.
WINDOWS_SYSTEM_DLLS = {
    'kernel32.dll', 'user32.dll', 'gdi32.dll', 'shell32.dll', 'ole32.dll',
    'oleaut32.dll', 'advapi32.dll', 'ws2_32.dll', 'winmm.dll', 'msvcrt.dll',
    'mpr.dll', 'netapi32.dll', 'userenv.dll', 'version.dll', 'setupapi.dll',
    'bcrypt.dll', 'crypt32.dll', 'imm32.dll', 'dwmapi.dll', 'uxtheme.dll',
    'd3d9.dll', 'dxva2.dll', 'shlwapi.dll', 'secur32.dll', 'iphlpapi.dll',
    'wininet.dll', 'urlmon.dll', 'comctl32.dll', 'comdlg32.dll',
    'rpcrt4.dll', 'ntdll.dll', 'dwrite.dll', 'dnsapi.dll', 'mswsock.dll',
    'shcore.dll', 'gdiplus.dll', 'winspool.drv', 'psapi.dll', 'dbghelp.dll',
    'powrprof.dll', 'usp10.dll', 'wintrust.dll', 'msimg32.dll',
    'wsock32.dll', 'opengl32.dll', 'glu32.dll', 'dsound.dll', 'ddraw.dll',
    'imagehlp.dll', 'winscard.dll', 'msacm32.dll', 'amstream.dll',
    'devenum.dll', 'dxgi.dll', 'd3d11.dll', 'evr.dll', 'mf.dll',
    'mfplat.dll', 'mfuuid.dll', 'quartz.dll', 'qedit.dll',
    'strmiids.dll', 'wtsapi32.dll', 'sensorsapi.dll', 'cabinet.dll',
    'winhttp.dll', 'webio.dll', 'combase.dll', 'propsys.dll', 'wer.dll',
    'shfolder.dll', 'apphelp.dll', 'schannel.dll', 'sspicli.dll',
    'fwpuclnt.dll', 'wlanapi.dll', 'rasapi32.dll', 'credui.dll',
    'dhcpcsvc.dll', 'dhcpcsvc6.dll', 'winnsi.dll', 'ntmarta.dll',
    'synchronization.dll', 'api-ms-win-crt-runtime-l1-1-0.dll',
    'api-ms-win-crt-heap-l1-1-0.dll', 'api-ms-win-crt-math-l1-1-0.dll',
    'api-ms-win-crt-stdio-l1-1-0.dll', 'api-ms-win-crt-string-l1-1-0.dll',
    'api-ms-win-crt-convert-l1-1-0.dll', 'api-ms-win-crt-environment-l1-1-0.dll',
    'api-ms-win-crt-locale-l1-1-0.dll', 'api-ms-win-crt-time-l1-1-0.dll',
    'api-ms-win-crt-filesystem-l1-1-0.dll', 'api-ms-win-crt-utility-l1-1-0.dll',
    'api-ms-win-crt-multibyte-l1-1-0.dll', 'api-ms-win-crt-private-l1-1-0.dll',
    'api-ms-win-core-synch-l1-2-0.dll', 'api-ms-win-core-profile-l1-1-0.dll',
    'api-ms-win-security-base-l1-1-0.dll', 'ucrtbase.dll',
    'msvcp140.dll', 'vcruntime140.dll', 'msvcr90.dll', 'msvcr100.dll',
    'msvcr110.dll', 'msvcr120.dll',
}

PE_MACHINE = {0x14C: 'x86', 0x8664: 'x64', 0xAA64: 'ARM64'}


def pe_imports_and_machine(path):
    """Devuelve (lista_de_imports, maquina) o (None, None) si no es PE valido."""
    try:
        with open(path, 'rb') as f:
            data = f.read()
    except OSError:
        return None, None
    if len(data) < 64 or data[:2] != b'MZ':
        return None, None
    pe_off = struct.unpack_from('<I', data, 0x3C)[0]
    if pe_off + 24 > len(data) or data[pe_off:pe_off + 4] != b'PE\x00\x00':
        return None, None
    machine = PE_MACHINE.get(struct.unpack_from('<H', data, pe_off + 4)[0])
    opt_off = pe_off + 24
    if opt_off + 2 > len(data):
        return None, None
    magic = struct.unpack_from('<H', data, opt_off)[0]
    if magic == 0x10B:      # PE32
        dd_off = opt_off + 96
    elif magic == 0x20B:    # PE32+
        dd_off = opt_off + 112
    else:
        return [], machine
    if dd_off + 16 > len(data):
        return [], machine
    imp_rva, _ = struct.unpack_from('<II', data, dd_off + 8)
    if imp_rva == 0:
        return [], machine
    nsec = struct.unpack_from('<H', data, pe_off + 6)[0]
    opt_size = struct.unpack_from('<H', data, pe_off + 20)[0]
    sec_off = opt_off + opt_size
    sections = []
    for i in range(nsec):
        s = sec_off + 40 * i
        if s + 40 > len(data):
            break
        vsize = struct.unpack_from('<I', data, s + 8)[0]
        vaddr = struct.unpack_from('<I', data, s + 12)[0]
        roff = struct.unpack_from('<I', data, s + 20)[0]
        sections.append((vaddr, vsize, roff))

    def rva_to_offset(rva):
        for vaddr, vsize, roff in sections:
            if vaddr <= rva < vaddr + max(vsize, 0x1000):
                return roff + (rva - vaddr)
        return None

    imports = []
    off = rva_to_offset(imp_rva)
    if off is None:
        return [], machine
    while off + 20 <= len(data):
        _, _, _, name_rva, _ = struct.unpack_from('<IIIII', data, off)
        if name_rva == 0:
            break
        no = rva_to_offset(name_rva)
        if no is None:
            break
        end = data.find(b'\x00', no)
        if end < 0:
            break
        imports.append(data[no:end].decode('ascii', 'replace').lower())
        off += 20
    return imports, machine


def main():
    if len(sys.argv) < 2:
        print('Uso: python tools/verify_portable.py <carpeta_staging> [x86|x64]')
        return 2
    root = os.path.abspath(sys.argv[1])
    expected_arch = sys.argv[2].lower() if len(sys.argv) > 2 else None
    if not os.path.isdir(root):
        print(f'ERROR: no existe la carpeta {root}')
        return 2

    binaries = []
    for pattern in ('*.exe', '*.dll'):
        binaries.extend(glob.glob(os.path.join(root, '**', pattern), recursive=True))
    if not binaries:
        print('ERROR: no se encontraron binarios PE en el paquete')
        return 2

    # DLLs disponibles: cualquier DLL del paquete (la resolucion de Windows
    # para un binario en subcarpeta mira: su propia carpeta, la carpeta del
    # exe y las carpetas de sistema; aqui normalizamos por nombre).
    present = {os.path.basename(b).lower() for b in binaries}

    missing = {}       # dll -> [binarios que la requieren]
    arch_mismatch = [] # (binario, maquina)
    checked = 0
    for b in sorted(binaries):
        imports, machine = pe_imports_and_machine(b)
        if imports is None:
            continue
        checked += 1
        if expected_arch and machine and machine != expected_arch:
            arch_mismatch.append((os.path.relpath(b, root), machine))
        for imp in imports:
            base = os.path.basename(imp)
            if base in WINDOWS_SYSTEM_DLLS or base in present:
                continue
            # Resolucion adicional: la DLL puede vivir en la misma carpeta
            # del binario importador o en la raiz del paquete.
            own_dir = os.path.dirname(b)
            if os.path.exists(os.path.join(own_dir, base)):
                continue
            if os.path.exists(os.path.join(root, base)):
                continue
            missing.setdefault(base, []).append(os.path.relpath(b, root))

    print(f'[verify_portable] Binarios PE auditados: {checked}')
    print(f'[verify_portable] DLLs presentes en el paquete: {len(present)}')

    ok = True
    if arch_mismatch:
        ok = False
        print('\n[FAIL] Arquitectura PE inconsistente con ' + expected_arch + ':')
        for rel, m in arch_mismatch:
            print(f'  - {rel} es {m}')
    if missing:
        ok = False
        print('\n[FAIL] DLLs requeridas AUSENTES en el paquete '
              '(romperian el arranque en Windows limpio):')
        for dll, users in sorted(missing.items()):
            print(f'  - {dll}  (requerida por {len(users)} binarios; p.ej. {users[:3]})')
    if ok:
        print('[verify_portable] OK: todas las dependencias externas estan '
              'satisfechas dentro del paquete (o son DLLs de sistema Windows).')
    return 0 if ok else 1


if __name__ == '__main__':
    sys.exit(main())
