// ============================================================================
//  Fusion-HP · Núcleo nativo C++ (Win32/CRT, /MT, Win7 SP1 → Win11)
//  Common.h — includes y utilidades comunes del núcleo.
//  [SPEC §3.1] Todo lo que no puede fallar vive aquí.
// ============================================================================
#pragma once

#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <objbase.h>

#include <string>
#include <vector>
#include <memory>
#include <mutex>
#include <atomic>
#include <thread>
#include <chrono>
#include <functional>
#include <algorithm>

#include <cstdint>
#include <cstdio>
#include <cstring>

// JSON (nlohmann, header-only, incluido en third_party/) — [SPEC §3.5] sin dependencias externas al paquete
#include <nlohmann/json.hpp>

namespace fusion {

using Json = nlohmann::json;

// --- Versión del núcleo (version.props es la fuente en MSBuild; se refleja aquí) ---
#ifndef FUSION_VERSION
#define FUSION_VERSION "2.0.0-beta.1"
#endif
#define FUSION_IPC_PROTOCOL "ipc.v1"   // [SPEC §3.4] protocolo versionado

// --- Utilidades de cadena ---------------------------------------------------
std::wstring ToWide(const std::string& utf8);
std::string ToUtf8(const std::wstring& wide);
std::string NarrowCopy(const std::string& s);

// Color "#RRGGBB" (o "#AARRGGBB") → packed 0xAARRGGBB
uint32_t ParseColor(const std::string& hex, uint32_t fallback = 0xFF000000);

// Rutas de datos: %APPDATA%\FusionHP (instalado) o .\datos (portable). [SPEC §4.4]
// Sin Registro: detección portable = existencia de la carpeta "datos" junto al exe.
std::wstring DataDir();
std::wstring ExeDir();

// ¿Ejecución portable? (misma carpeta del exe contiene "datos" o archivo "portable.flag")
bool IsPortableMode();

} // namespace fusion
