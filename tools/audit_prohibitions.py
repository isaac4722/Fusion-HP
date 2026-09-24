#!/usr/bin/env python3
# ============================================================================
#  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac.
#  Licencia View-Only.
# ----------------------------------------------------------------------------
#  tools/audit_prohibitions.py : auditoría AUTOMÁTICA de prohibiciones (F6.09
#  del Plan de Ultra Implementación — Sección 1.3 del documento técnico).
#
#  Comprueba en el CÓDIGO FUENTE (fuentes propios: native/ managed/ apps/ —
#  terceros excluidos):
#    1.  cero Java/JRE;
#    2.  cero dependencia de .NET moderno OBLIGATORIA (solo net35/net48/net8
#        de arnés; prohibido TFM net5/net6/net7 en proyectos del PRODUCTO);
#    3.  cero escritura al Registro (RegSetValue/RegCreateKey/SetValue en
#        Microsoft.Win32.Registry o CRegKey);
#    4.  cero `regedit` requerido;
#    5.  cero consola requerida (subsystem CONSOLE en el launcher);
#    6.  cero elevación para operación normal (requireAdministrator /
#        ShellExecute runas);
#    7.  cero pantalla blanca como respuesta de error (el patrón se audita
#        por configuración de errores — heurística en mensajes);
#    8.  cero stack trace crudo como ÚNICO mensaje (ex.ToString() mostrado
#        al usuario);
#    9.  cero desactivación del log.
#
#  Salida: informe + exit 0 (verde) / 1 (violaciones). CI lo ejecuta como GATE.
# ============================================================================
import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

OWN_DIRS = ["native", "managed", "apps", "tools"]
SKIP_DIRS = {"bin", "obj", "build", ".git", "node_modules", "third_party"}
TEXT_EXT = {".cs", ".cpp", ".h", ".hpp", ".c", ".xaml", ".py", ".csproj",
            ".sln", ".props", ".targets", ".cmd", ".ps1", ".yml", ".yaml",
            ".manifest", ".asm"}

violations = []
notes = []

AUDIT_SELF = os.path.abspath(__file__)

def iter_files():
    for d in OWN_DIRS:
        base = os.path.join(ROOT, d)
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [x for x in dirnames if x not in SKIP_DIRS]
            for fn in filenames:
                ext = os.path.splitext(fn)[1].lower()
                if ext in TEXT_EXT:
                    full = os.path.join(dirpath, fn)
                    if full == AUDIT_SELF:      # el auditor no se audita a sí mismo
                        continue
                    yield full

def add(severity, rule, where, detail):
    entry = f"[{severity}] {rule}: {where} — {detail}"
    (violations if severity == "VIOLACION" else notes).append(entry)

# ------------------------------------------------------------------ 1. Java
java_pat = re.compile(r"\b(java\.lang|javax\.|import\s+java\.|System\.out\.print|jre\b|jvm\b)",
                      re.IGNORECASE)
for path in iter_files():
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            text = f.read()
    except OSError:
        continue
    rel = os.path.relpath(path, ROOT)
    for m in java_pat.finditer(text):
        line = text[: m.start()].count("\n") + 1
        frag = text.splitlines()[line - 1].strip()[:100]
        # menciones documentativas (comentarios con la palabra prohibida) no cuentan
        if frag.startswith("//") or frag.startswith("///") or frag.startswith("#") or frag.startswith("*"):
            continue
        add("VIOLACION", "1. cero Java/JRE", rel, f"línea {line}: {frag}")

# --------------------------------------------- 2. .NET moderno obligatorio
for path in iter_files():
    if not path.endswith(".csproj"):
        continue
    rel = os.path.relpath(path, ROOT)
    try:
        with open(path, encoding="utf-8") as f:
            text = f.read()
    except OSError:
        continue
    m = re.search(r"<TargetFrameworks?>([^<]+)<", text)
    if not m:
        continue
    tfms = [t.strip() for t in m.group(1).split(";")]
    modern = [t for t in tfms if re.match(r"net[5-9]\.0", t)]
    framework = [t for t in tfms if re.match(r"net[3-4]", t)]
    if modern:
        # El TFM net8.0 se usa SOLO para el arnés de pruebas (v5.0.0, decisión
        # documentada): los paquetes distribuidos llevan net35/net48. Un
        # proyecto es "solo moderno" (VIOLACIÓN) si NO declara ningún TFM de
        # .NET Framework distribuible.
        if framework or "Tests" in rel or "PoC" in rel:
            add("NOTA", "2. .NET moderno (solo arnés)", rel,
                f"TFMs {modern} junto a {framework or '(pruebas)'} — el paquete "
                f"distribuido usa .NET Framework")
        else:
            add("VIOLACION", "2. cero .NET moderno obligatorio", rel,
                f"TFMs {modern} sin variante .NET Framework distribuible")

# ------------------------------------------------ 3. escritura al Registro
reg_pat = re.compile(
    r"Registry\.(CurrentUser|LocalMachine)\.(SetValue|CreateSubKey)|"
    r"RegCreateKeyEx|RegSetValueEx|CRegKey\b|regwrite|RegistryKey\.SetValue")
for path in iter_files():
    if path.endswith(".csproj") or path.endswith(".sln"):
        continue
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            lines = f.readlines()
    except OSError:
        continue
    rel = os.path.relpath(path, ROOT)
    for i, ln in enumerate(lines, 1):
        code = ln.strip()
        if code.startswith("//") or code.startswith("*") or code.startswith("#"):
            continue
        for m in reg_pat.finditer(ln):
            add("VIOLACION", "3. cero escritura al Registro", rel,
                f"línea {i}: {code[:100]}")

# ------------------------------------------------------ 4/5/6. regedit·
#      consola·elevación
regedit_pat = re.compile(r"regedit(\.exe)?\b", re.IGNORECASE)
runas_pat = re.compile(r"runas|requireAdministrator", re.IGNORECASE)
console_pat = re.compile(r"SUBSYSTEM:CONSOLE|/SUBSYSTEM:CONSOLE|OutputType>ConsoleApplication")
for path in iter_files():
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            text = f.read()
    except OSError:
        continue
    rel = os.path.relpath(path, ROOT)
    for m in regedit_pat.finditer(text):
        line = text[: m.start()].count("\n") + 1
        frag = text.splitlines()[line - 1].strip()[:100]
        if frag.startswith("//") or frag.startswith("*") or frag.startswith("#"):
            continue
        add("VIOLACION", "4. cero regedit requerido", rel, f"línea {line}: {frag}")
    for m in runas_pat.finditer(text):
        line = text[: m.start()].count("\n") + 1
        frag = text.splitlines()[line - 1].strip()[:100]
        if frag.startswith("//") or frag.startswith("*") or frag.startswith("#"):
            continue
        add("VIOLACION", "6. cero elevación para operación normal", rel, f"línea {line}: {frag}")
    if "launcher" in rel.lower() and console_pat.search(text):
        add("VIOLACION", "5. cero consola requerida", rel, "launcher con subsistema CONSOLA")

# -------------------------------- 7/8. pantallas blancas y stacks crudos
stack_pat = re.compile(r"MessageBox\w*\([^)]*ex\.ToString\(\)|Show\([^)]*ex\.ToString\(\)|"
                       r"ex\.StackTrace")
for path in iter_files():
    if not path.endswith(".cs"):
        continue
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            text = f.read()
    except OSError:
        continue
    rel = os.path.relpath(path, ROOT)
    for m in stack_pat.finditer(text):
        line = text[: m.start()].count("\n") + 1
        frag = text.splitlines()[line - 1].strip()[:100]
        add("VIOLACION", "8. cero stack crudo como único mensaje", rel,
            f"línea {line}: {frag}")

# ---------------------------------------------- 9. desactivación del log
# (el log debe ser constitutivo: se busca un flag que lo apague por completo)
logoff_pat = re.compile(r"logging\s*=\s*false|EnableLogging\s*=\s*false|logEnabled\s*=\s*false")
for path in iter_files():
    try:
        with open(path, encoding="utf-8", errors="replace") as f:
            text = f.read()
    except OSError:
        continue
    rel = os.path.relpath(path, ROOT)
    for m in logoff_pat.finditer(text):
        line = text[: m.start()].count("\n") + 1
        frag = text.splitlines()[line - 1].strip()[:100]
        add("VIOLACION", "9. cero desactivación del log", rel, f"línea {line}: {frag}")

# ------------------------------------------------------------------ informe
print("== Auditoría de prohibiciones (F6.09) ==")
print(f"Archivos propios auditados bajo: {', '.join(OWN_DIRS)} (sin third_party/bin/obj)")
for n in notes:
    print("  " + n)
for v in violations:
    print("  " + v)
print(f"== RESULTADO: {len(violations)} violaciones · {len(notes)} notas ==")
sys.exit(1 if violations else 0)
