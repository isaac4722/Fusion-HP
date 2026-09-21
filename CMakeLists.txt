# ============================================================================
#  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
# ============================================================================
#  CMakeLists.txt : Build multiplataforma (Windows x86/x64 MinGW + Linux).
#  Qt 5.15 LTS + SQLite3 (amalgamation vendorizada, FTS5) + miniz (OpenXML).
#  LibVLC se resuelve en RUNTIME (carga dinamica), no se enlaza.
# ============================================================================
cmake_minimum_required(VERSION 3.16)
project(LuminaPresentationSuite VERSION 1.2.0 LANGUAGES CXX C)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)
set(CMAKE_AUTORCC ON)

if (NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release)
endif()

# --------------------------- Qt 5.15 LTS ------------------------------------
find_package(Qt5 5.15 REQUIRED COMPONENTS Core Gui Widgets Network WebSockets PrintSupport)

# --------------------------- Optimizaciones ---------------------------------
# NOTA: se evita -march=native en binarios de distribucion (portabilidad
# entre CPUs; la spec original lo sugiere para builds locales).
if (MSVC)
    add_compile_options(/O2 /GL /MP)
    set(CMAKE_EXE_LINKER_FLAGS_RELEASE "${CMAKE_EXE_LINKER_FLAGS_RELEASE} /LTCG")
else()
    add_compile_options(-O2)
    if (WIN32)
        # Runtimes estaticos para el EXE (portabilidad Win7-11). OJO: las
        # Qt5*.dll precompiladas SI enlazan libgcc/libstdc++/libwinpthread de
        # forma dinamica -> el CI empaqueta esas 3 DLLs del runtime MinGW y
        # lo verifica con tools/verify_portable.py (gate anti-regresion).
        link_libraries(-static-libgcc -static-libstdc++)
    endif()
endif()

# --------------------------- Fuentes ----------------------------------------
set(LUMINA_SOURCES
    src/main.cpp
    src/core/Database.cpp
    # Paneles
    src/gui/SongPanel.cpp
    src/gui/BiblePanel.cpp
    src/gui/MediaPanel.cpp
    src/gui/PptxPanel.cpp
    src/gui/ThemePanel.cpp
    src/gui/CustomPanel.cpp
    src/gui/ServicePanel.cpp
    src/gui/CommsPanel.cpp
    src/gui/HistoryPanel.cpp
    src/gui/SettingsPanel.cpp
    src/gui/MainWindow.cpp
)

set(LUMINA_HEADERS
    src/core/Models.h
    src/core/BibleRef.h
    src/core/Chords.h
    src/core/Lyrics.h
    src/core/Renderer.h
    src/core/DisplayEngine.h
    src/core/MediaEngine.h
    src/core/PptxEngine.h
    src/core/MidiOut.h
    src/core/Triggers.h
    src/core/AppContext.h
    src/net/WebServer.h
    src/gui/StageWindow.h
    src/gui/VectorCanvas.h
    src/gui/SongPanel.h
    src/gui/BiblePanel.h
    src/gui/MediaPanel.h
    src/gui/PptxPanel.h
    src/gui/ThemePanel.h
    src/gui/CustomPanel.h
    src/gui/ServicePanel.h
    src/gui/CommsPanel.h
    src/gui/HistoryPanel.h
    src/gui/SettingsPanel.h
    src/gui/MainWindow.h
)

set(LUMINA_THIRD_PARTY
    third_party/sqlite3/sqlite3.c
    third_party/miniz/miniz.c
    third_party/miniz/miniz_tdef.c
    third_party/miniz/miniz_tinfl.c
    third_party/miniz/miniz_zip.c
)

# Recurso de icono/version para Windows
# Se genera via configure_file con ruta ABSOLUTA del icono (barras normales):
# windres resuelve rutas relativas desde el directorio de build, lo que
# rompia la compilacion del .rc en CI (no en builds in-source).
if (WIN32)
    set(ICON_RC_PATH "${CMAKE_CURRENT_SOURCE_DIR}/resources/img/app.ico")
    # Version del exe derivada de project(VERSION) -> bump unico en la cabecera
    set(RC_VERSION_QUADS   "${PROJECT_VERSION_MAJOR},${PROJECT_VERSION_MINOR},${PROJECT_VERSION_PATCH},0")
    set(RC_VERSION_DOTTED  "${PROJECT_VERSION}")
    configure_file(
        ${CMAKE_CURRENT_SOURCE_DIR}/windows/app.rc.in
        ${CMAKE_CURRENT_BINARY_DIR}/app.rc
        @ONLY)
    set(LUMINA_RC ${CMAKE_CURRENT_BINARY_DIR}/app.rc)
endif()

add_executable(LuminaPresentationSuite WIN32
    ${LUMINA_SOURCES}
    ${LUMINA_HEADERS}
    ${LUMINA_THIRD_PARTY}
    resources/app.qrc
    ${LUMINA_RC}
)

target_include_directories(LuminaPresentationSuite PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/src
    ${CMAKE_CURRENT_SOURCE_DIR}/third_party/sqlite3
    ${CMAKE_CURRENT_SOURCE_DIR}/third_party/miniz
)

# SQLite embebido: FTS5 + thread-safety + sin extensiones cargables
set_source_files_properties(third_party/sqlite3/sqlite3.c PROPERTIES
    COMPILE_OPTIONS "-DSQLITE_ENABLE_FTS5;-DSQLITE_ENABLE_FTS3_PARENTHESIS;-DSQLITE_THREADSAFE=1;-DSQLITE_OMIT_LOAD_EXTENSION;-DSQLITE_ENABLE_RTREE"
)

target_link_libraries(LuminaPresentationSuite PRIVATE
    Qt5::Core
    Qt5::Gui
    Qt5::Widgets
    Qt5::Network
    Qt5::WebSockets
    Qt5::PrintSupport
)

if (WIN32)
    target_link_libraries(LuminaPresentationSuite PRIVATE user32 gdi32 winmm ws2_32)
endif()

# ---------------------------------------------------------------------------
# Instalacion / empaquetado portable
# ---------------------------------------------------------------------------
install(TARGETS LuminaPresentationSuite RUNTIME DESTINATION .)
