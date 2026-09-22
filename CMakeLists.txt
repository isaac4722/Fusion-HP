# ============================================================================
#  LuminaPresentation Suite - CMakeLists.txt (edición wxWidgets)
#  Copyright (c) 2026 Isaac. Licencia View-Only - ver LICENSE.md
# ----------------------------------------------------------------------------
#  Programa de escritorio NATIVO (C++17 / wxWidgets 3.2) para Windows 7 SP1
#  x32 … Windows 11+, sin dependencias de instalación para el usuario final:
#  wxWidgets se enlaza ESTÁTICO y con runtime estático (/MT) en el CI, de modo
#  que el paquete portable solo contiene el exe + recursos de datos.
#
#  Consumo de wxWidgets (en orden de preferencia):
#   1) CONFIG  : wxWidgets compilado/instalado con CMake (targets wx::*)
#   2) Módulo  : FindwxWidgets clásico (binarios precompilados de wxwidgets.org
#                con wxWidgets_ROOT_DIR/wxWidgets_LIB_DIR, o wx-config en Unix).
#  Variables útiles:
#   -DwxWidgets_ROOT_DIR=<prefix>   (raíz de wx)
#   -DwxWidgets_LIB_DIR=<prefix>/lib/vc_lib   (libs estáticas MSVC)
#   env WX_CONFIG=/ruta/a/wx-config           (Unix)
# ============================================================================
cmake_minimum_required(VERSION 3.16)
project(LuminaPresentationSuite VERSION 2.0.0 LANGUAGES C CXX)

set(APP_NAME  "LuminaPresentationSuite")
set(APP_VER   "${PROJECT_VERSION}")
message(STATUS "LuminaPresentation Suite v${APP_VER} (wxWidgets)")

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

# ---------------------------------------------------------------------------
# wxWidgets 3.2 (core, base, net, media)
# ---------------------------------------------------------------------------
set(WX_COMPONENTS core base net media)

find_package(wxWidgets CONFIG QUIET COMPONENTS ${WX_COMPONENTS})
if(TARGET wx::core)
    message(STATUS "wxWidgets: modo CONFIG (targets CMake)")
    set(WX_LIBS wx::core wx::base wx::net wx::media)
else()
    message(STATUS "wxWidgets: modo módulo (FindwxWidgets / wx-config)")
    if(DEFINED ENV{WX_CONFIG})
        set(wxWidgets_CONFIG_EXECUTABLE "$ENV{WX_CONFIG}")
        message(STATUS "wx-config: ${wxWidgets_CONFIG_EXECUTABLE}")
    endif()
    set(wxWidgets_USE_STATIC ON)
    find_package(wxWidgets REQUIRED COMPONENTS ${WX_COMPONENTS})
    include(${wxWidgets_USE_FILE})
    set(WX_LIBS ${wxWidgets_LIBRARIES})
endif()

# ---------------------------------------------------------------------------
# SQLite3 (amalgamation estática, FTS5 para búsqueda instantánea)
# ---------------------------------------------------------------------------
add_library(sqlite3 STATIC third_party/sqlite3/sqlite3.c)
target_include_directories(sqlite3 PUBLIC third_party/sqlite3)
target_compile_definitions(sqlite3 PRIVATE
    SQLITE_ENABLE_FTS5
    SQLITE_THREADSAFE=1
    SQLITE_OMIT_LOAD_EXTENSION
    SQLITE_SOUNDEX
)
if(MSVC)
    target_compile_options(sqlite3 PRIVATE /W3)
else()
    target_compile_options(sqlite3 PRIVATE -w)
endif()

# ---------------------------------------------------------------------------
# Aplicación
# ---------------------------------------------------------------------------
set(APP_SOURCES
    src/main.cpp
    src/core/Database.cpp
    src/core/Renderer.cpp
    src/net/WebServer.cpp
    src/ui/Icons.cpp
    src/ui/OutputFrame.cpp
    src/ui/PreviewPanel.cpp
    src/ui/AppEvents.cpp
    src/ui/SongsPanel.cpp
    src/ui/BiblePanel.cpp
    src/ui/MediaPanel.cpp
    src/ui/ThemesPanel.cpp
    src/ui/ServicePanel.cpp
    src/ui/SongEditor.cpp
    src/ui/ThemeEditor.cpp
    src/ui/SettingsDialog.cpp
    src/ui/MainFrame.cpp
)

if(WIN32)
    configure_file(${CMAKE_CURRENT_SOURCE_DIR}/resources/app.rc.in
                   ${CMAKE_CURRENT_BINARY_DIR}/app.rc @ONLY)
    list(APPEND APP_SOURCES ${CMAKE_CURRENT_BINARY_DIR}/app.rc)
endif()

add_executable(${APP_NAME} WIN32 ${APP_SOURCES})

# Manifest de Windows: DPI awareness (PerMonitorV2) + compatibilidad Win7.
if(MSVC)
    target_sources(${APP_NAME} PRIVATE app.manifest)
endif()

target_include_directories(${APP_NAME} PRIVATE src third_party)
target_compile_definitions(${APP_NAME} PRIVATE
    APP_VERSION="${APP_VER}"
    APP_NAME_UTF8="LuminaPresentationSuite"
    $<$<PLATFORM_ID:Windows>:NOMINMAX>
    $<$<PLATFORM_ID:Windows>:_CRT_SECURE_NO_WARNINGS>
)

target_link_libraries(${APP_NAME} PRIVATE ${WX_LIBS} sqlite3)

# Runtime estático (/MT) en TODOS los targets: el exe portable no depende de
# msvcp140/vcruntime140 (cero redistributables en Windows limpio).
if(MSVC)
    set_property(TARGET ${APP_NAME} PROPERTY
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    set_property(TARGET sqlite3 PROPERTY
        MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(MSVC)
    target_compile_options(${APP_NAME} PRIVATE /W3 /permissive- /Zi)
    # El paquete portable NO debe requerir redistributable: el CI compila
    # wxWidgets y la app con runtime estático (/MT).
else()
    target_compile_options(${APP_NAME} PRIVATE -Wall -Wextra -Wno-unused-parameter)
endif()

set_target_properties(${APP_NAME} PROPERTIES
    OUTPUT_NAME "LuminaPresentationSuite"
)
