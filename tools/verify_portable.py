#!/usr/bin/env python3
# ============================================================================
#  Fusion-HP / LuminaPresentation Suite - tools/verify_portable.py
#  Copyright (c) 2026 Isaac. Licencia View-Only.
# ----------------------------------------------------------------------------
#  GATE de empaquetado portable de la v3.0.0 «HÍBRIDA» (núcleo C++ + capa C#).
#  Audita el layout portable (por arquitectura) en 4 niveles:
#
#    1. FICHEROS MÍNIMOS del contrato de empaquetado presentes:
#         FusionLauncher.exe, FusionHP.exe, FusionCore.dll y
#         resources/data/bible_rvr1909.json (FusionHP35.exe es OPCIONAL:
#         solo se espera si FusionHP.UI multi-apunta net35).
#    2. IMPORTS PE de TODOS los binarios (exe + dll, recursivo): cada DLL
#       requerida debe estar (a) en el paquete o (b) ser DLL de sistema de
#       Windows. Falla listando las faltantes. (Herencia v1.0.1: el paquete
#       salió una vez sin el runtime de MinGW y el exe moría al arrancar.)
#    3. EXPORTS de FusionCore.dll: la ABI contractual exige el export C
#       "fusion_create" (docs/architecture-hybrid.md §4). Se parsea la tabla
#       de exports PE con struct de python puro (sin pefile, sin pip).
#    4. VS_FIXEDFILEINFO de FusionHP.exe (INFORMATIVO): si el exe lleva
#       recurso de versión se imprime la versión 1.2.3.4 parseada del
#       VS_VERSIONINFO; si no lo lleva, se anota y NO falla.
#
#  Todo el parseo PE es python puro (struct): cero dependencias pip.
#
#  Uso: python tools/verify_portable.py <carpeta_staging> [x86|x64]
#  Salida: exit 0 = paquete válido; exit 1 = paquete inválido (detallado).
# ============================================================================
import os
import struct
import sys
import glob

# ---------------------------------------------------------------------------
# Contrato de empaquetado (job «package» de .github/workflows/ci.yml)
# ---------------------------------------------------------------------------
REQUIRED_FILES = [
    'FusionLauncher.exe',                    # nativo: detección de runtime
    'FusionHP.exe',                          # interfaz gestionada (net48)
    'FusionCore.dll',                        # motor C++ (/MT)
    'resources/data/bible_rvr1909.json',     # biblia de fábrica
]
REQUIRED_EXPORTS = {
    'FusionCore.dll': ['fusion_create'],     # ABI contractual (API C plana)
}

# DLLs que garantiza el propio sistema operativo Windows (Win7 SP1+).
# Cualquier otra DLL requerida debe viajar dentro del paquete.
# (mscoree.dll: shim de .NET Framework — presente en todo Windows con .NET;
#  es el import normal de un exe gestionado / apphost .NET Framework.)
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
    'mscoree.dll',   # shim de .NET Framework (exe gestionado net48)
}

PE_MACHINE = {0x14C: 'x86', 0x8664: 'x64', 0xAA64: 'ARM64'}

# Firma del VS_FIXEDFILEINFO dentro del recurso VS_VERSIONINFO.
VS_FIXEDFILEINFO_SIG = 0xFEEF04BD


# ---------------------------------------------------------------------------
# Parseo PE (python puro)
# ---------------------------------------------------------------------------
class PeInfo:
    """Resultado del parseo mínimo de un fichero PE."""
    __slots__ = ('valid', 'machine', 'imports', 'exports', 'data')

    def __init__(self):
        self.valid = False    # MZ + 'PE\0\0' + cabeceras legibles
        self.machine = None   # 'x86' | 'x64' | 'ARM64' | None
        self.imports = []     # nombres de DLL de la tabla de imports
        self.exports = []     # nombres de la tabla de exports
        self.data = None      # bytes crudos (para buscar FIXEDFILEINFO)


def parse_pe(path):
    """Parsea imports+exports+machine de un PE. Devuelve PeInfo."""
    info = PeInfo()
    try:
        with open(path, 'rb') as f:
            data = f.read()
    except OSError:
        return info
    info.data = data
    if len(data) < 64 or data[:2] != b'MZ':
        return info
    try:
        pe_off = struct.unpack_from('<I', data, 0x3C)[0]
        if pe_off + 24 > len(data) or data[pe_off:pe_off + 4] != b'PE\x00\x00':
            return info
        info.machine = PE_MACHINE.get(struct.unpack_from('<H', data, pe_off + 4)[0])
        opt_off = pe_off + 24
        if opt_off + 2 > len(data):
            return info
        magic = struct.unpack_from('<H', data, opt_off)[0]
        if magic == 0x10B:      # PE32
            dd_off = opt_off + 96
            n_dd = 16
        elif magic == 0x20B:    # PE32+
            dd_off = opt_off + 112
            n_dd = 16
        else:
            return info
        if dd_off + 8 * n_dd > len(data):
            return info
        data_dirs = [struct.unpack_from('<II', data, dd_off + 8 * i)
                     for i in range(n_dd)]
        info.valid = True

        # --- Secciones (para traducir RVA -> offset de fichero) -------------
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

        def read_cstr(off, limit=256):
            if off is None or off >= len(data):
                return None
            end = data.find(b'\x00', off, off + limit)
            if end < 0:
                return None
            return data[off:end].decode('ascii', 'replace')

        # --- Imports (data directory 1) --------------------------------------
        imp_rva, _ = data_dirs[1]
        if imp_rva:
            off = rva_to_offset(imp_rva)
            while off is not None and off + 20 <= len(data):
                _, _, _, name_rva, _ = struct.unpack_from('<IIIII', data, off)
                if name_rva == 0:
                    break
                name = read_cstr(rva_to_offset(name_rva))
                if name is None:
                    break
                info.imports.append(name.lower())
                off += 20

        # --- Exports (data directory 0) --------------------------------------
        exp_rva, _ = data_dirs[0]
        if exp_rva:
            off = rva_to_offset(exp_rva)
            if off is not None and off + 40 <= len(data):
                n_funcs = struct.unpack_from('<I', data, off + 20)[0]
                n_names = struct.unpack_from('<I', data, off + 24)[0]
                names_rva = struct.unpack_from('<I', data, off + 32)[0]
                names_off = rva_to_offset(names_rva)
                if names_off is not None:
                    for i in range(min(n_names, 65535)):
                        nrva = struct.unpack_from('<I', data,
                                                  names_off + 4 * i)[0]
                        name = read_cstr(rva_to_offset(nrva))
                        if name:
                            info.exports.append(name)
    except struct.error:
        # PE truncado/corrupto: queda info.valid=False (o parcial) y fallará.
        pass
    return info


def fixed_file_info_version(pe):
    """(INFORMATIVO) Busca el recurso de versión VS_VERSIONINFO y devuelve
    'mayor.menor.build.revisión' del VS_FIXEDFILEINFO, o None si no existe.

    Estrategia sin parser de recursos completo: la firma 0xFEEF04BD es única
    en el binario normal (aparece dentro del blob VS_VERSIONINFO del .rsrc);
    se valida que el campo strcutLength contiguo sea coherente."""
    data = pe.data
    if not data:
        return None
    sig = struct.pack('<I', VS_FIXEDFILEINFO_SIG)
    pos = data.find(sig)
    while pos >= 0 and pos + 52 <= len(data):
        (strut_length, file_version_ms, file_version_ls,
         product_version_ms, product_version_ls) = struct.unpack_from(
            '<IIIII', data, pos + 4)
        # Filtro de plausibilidad: el bloque FIXEDFILEINFO mide 52 bytes.
        if strut_length == 52:
            major = (file_version_ms >> 16) & 0xFFFF
            minor = file_version_ms & 0xFFFF
            build = (file_version_ls >> 16) & 0xFFFF
            revision = file_version_ls & 0xFFFF
            return f'{major}.{minor}.{build}.{revision}'
        pos = data.find(sig, pos + 1)
    return None


# ---------------------------------------------------------------------------
# Gate principal
# ---------------------------------------------------------------------------
def main():
    if len(sys.argv) < 2:
        print('Uso: python tools/verify_portable.py <carpeta_staging> [x86|x64]')
        return 2
    root = os.path.abspath(sys.argv[1])
    expected_arch = sys.argv[2].lower() if len(sys.argv) > 2 else None
    if not os.path.isdir(root):
        print(f'ERROR: no existe la carpeta {root}')
        return 2

    failures = []

    # ---- 1) Ficheros mínimos del contrato ---------------------------------
    print('[verify_portable] Ficheros mínimos del paquete híbrido:')
    present_lower = {}
    for dirpath, _dirnames, filenames in os.walk(root):
        for fn in filenames:
            rel = os.path.relpath(os.path.join(dirpath, fn), root)
            present_lower[rel.replace('\\', '/').lower()] = os.path.join(dirpath, fn)
    for rel in REQUIRED_FILES:
        if rel.lower() in present_lower:
            print(f'  [ok]   {rel}')
        else:
            print(f'  [FALTA] {rel}')
            failures.append(f'falta el fichero obligatorio {rel}')
    # FusionHP35.exe es OPCIONAL (solo si la UI multi-apunta net35).
    has35 = 'fusionhp35.exe' in present_lower
    print(f'  [info] FusionHP35.exe: '
          f'{"presente (variante net35)" if has35 else "ausente (UI net48 exclusiva; documentado)"}')

    # ---- 2) Auditoría de imports PE + arquitectura -------------------------
    binaries = []
    for pattern in ('*.exe', '*.dll'):
        binaries.extend(glob.glob(os.path.join(root, '**', pattern),
                                  recursive=True))
    if not binaries:
        print('ERROR: no se encontraron binarios PE en el paquete')
        return 1

    # DLLs disponibles: cualquier DLL del paquete (la resolución de Windows
    # mira la carpeta del binario, la del exe y las de sistema; aquí
    # normalizamos por nombre).
    present = {os.path.basename(b).lower() for b in binaries}

    missing = {}        # dll -> [binarios que la requieren]
    arch_mismatch = []  # (binario, máquina)
    invalid_pe = []     # binarios con cabecera PE ilegible
    checked = 0
    parsed = {}
    for b in sorted(binaries):
        pe = parse_pe(b)
        parsed[b] = pe
        if not pe.valid:
            invalid_pe.append(os.path.relpath(b, root))
            continue
        checked += 1
        if expected_arch and pe.machine and pe.machine != expected_arch:
            arch_mismatch.append((os.path.relpath(b, root), pe.machine))
        for imp in pe.imports:
            base = os.path.basename(imp)
            if base in WINDOWS_SYSTEM_DLLS or base in present:
                continue
            own_dir = os.path.dirname(b)
            if os.path.exists(os.path.join(own_dir, base)):
                continue
            if os.path.exists(os.path.join(root, base)):
                continue
            missing.setdefault(base, []).append(os.path.relpath(b, root))

    print(f'\n[verify_portable] Binarios PE auditados: {checked} '
          f'(inválidos/ilegibles: {len(invalid_pe)})')
    print(f'[verify_portable] DLLs presentes en el paquete: {len(present)}')

    if invalid_pe:
        failures.append('binarios con cabecera PE inválida: '
                        + ', '.join(invalid_pe))

    if arch_mismatch:
        failures.append('arquitectura PE inconsistente con ' + expected_arch
                        + ': ' + '; '.join(f'{rel} es {m}' for rel, m in arch_mismatch))

    if missing:
        det = '; '.join(f'{dll} (requerida por {users[0]}'
                        + (f' y {len(users)-1} más)' if len(users) > 1 else ')')
                        for dll, users in sorted(missing.items()))
        failures.append('DLLs requeridas AUSENTES del paquete: ' + det)

    # ---- 3) Exports contractuales de FusionCore.dll ------------------------
    core_path = None
    for b in binaries:
        if os.path.basename(b).lower() == 'fusioncore.dll':
            core_path = b
            break
    if core_path is None:
        failures.append('no se encontró FusionCore.dll en el paquete')
    else:
        pe = parsed.get(core_path) or parse_pe(core_path)
        needed = REQUIRED_EXPORTS['FusionCore.dll']
        print(f'\n[verify_portable] Exports de FusionCore.dll '
              f'({len(pe.exports)} símbolos): '
              + (', '.join(pe.exports[:12]) + ('…' if len(pe.exports) > 12 else '')))
        for sym in needed:
            if sym in pe.exports:
                print(f'  [ok]   export "{sym}" presente')
            else:
                print(f'  [FALTA] export "{sym}"')
                failures.append(f'FusionCore.dll no exporta "{sym}" '
                                '(ABI contractual rota)')

    # ---- 4) VS_FIXEDFILEINFO de FusionHP.exe (informativo) -----------------
    hp_path = None
    for b in binaries:
        if os.path.basename(b).lower() == 'fusionhp.exe':
            hp_path = b
            break
    if hp_path is not None:
        ver = fixed_file_info_version(parsed.get(hp_path) or parse_pe(hp_path))
        if ver:
            print(f'\n[verify_portable] FusionHP.exe VS_FIXEDFILEINFO: {ver} (informativo)')
        else:
            print('\n[verify_portable] FusionHP.exe sin recurso de versión '
                  'VS_VERSIONINFO detectable (informativo; no falla el gate)')

    # ---- Veredicto ----------------------------------------------------------
    print()
    if failures:
        print('[verify_portable] FAIL — paquete portable inválido:')
        for i, f in enumerate(failures, 1):
            print(f'  {i}. {f}')
        return 1
    print('[verify_portable] OK: layout completo, arquitectura coherente, '
          'todas las dependencias externas dentro del paquete (o de sistema) '
          'y exports contractuales presentes.')
    return 0


if __name__ == '__main__':
    sys.exit(main())
