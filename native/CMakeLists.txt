# Núcleo nativo híbrido: FusionCore (DLL/SO) + selftest + PoCNative
include_directories(${CMAKE_CURRENT_SOURCE_DIR}/core/include)
include_directories(${CMAKE_CURRENT_SOURCE_DIR}/core/src)
include_directories(${CMAKE_SOURCE_DIR}/third_party/sqlite3)
include_directories(${CMAKE_SOURCE_DIR}/third_party/nlohmann)

set(FUSION_CORE_SOURCES
    core/src/fusion_api.cpp
    core/src/Engine.cpp
    core/src/Storage.cpp
    core/src/SongModel.cpp
    core/src/BibleBib.cpp
    core/src/Chords.cpp
    core/src/Lyrics.cpp
    core/src/BibleRef.cpp
    core/src/Scripture.cpp
    ${CMAKE_SOURCE_DIR}/third_party/sqlite3/sqlite3.c
)

if(WIN32)
    list(APPEND FUSION_CORE_SOURCES core/src/Renderer.cpp core/src/Projector.cpp)
    add_definitions(-DFUSION_HAS_WIN32=1)
endif()

add_definitions(
    -DFUSION_CORE_BUILD=1
    -DSQLITE_ENABLE_FTS5=1
    -DSQLITE_ENABLE_JSON1=1
    -DSQLITE_THREADSAFE=1
    -DSQLITE_OMIT_LOAD_EXTENSION=1
    -DNOMINMAX -DWIN32_LEAN_AND_MEAN
)

add_library(FusionCore SHARED ${FUSION_CORE_SOURCES})
if(NOT MSVC)
    target_link_libraries(FusionCore PRIVATE pthread dl)
    # Falso positivo de GCC en el amalgamado sqlite3 (código de terceros)
    set_source_files_properties(${CMAKE_SOURCE_DIR}/third_party/sqlite3/sqlite3.c
        PROPERTIES COMPILE_OPTIONS "-Wno-stringop-overread")
else()
    target_link_libraries(FusionCore PRIVATE user32 gdi32 gdiplus ole32)
endif()
set_target_properties(FusionCore PROPERTIES OUTPUT_NAME "FusionCore")

# Arnés de pruebas nativo (lógica del núcleo, sin GUI)
add_executable(fusion_selftest core/tests/native_tests.cpp)
target_link_libraries(fusion_selftest PRIVATE FusionCore)

# PoC nativo: ejercicio del API C en modo headless (equivalente C++ del PoC C#)
# NOTA (H-a): PoCNative.cpp aún no existe en el repo; el target se declara
# condicional para no romper la configuración de CMake hasta que se incorpore.
if(EXISTS ${CMAKE_CURRENT_SOURCE_DIR}/poc/PoCNative.cpp)
    add_executable(fusion_poc_native poc/PoCNative.cpp)
    target_link_libraries(fusion_poc_native PRIVATE FusionCore)
endif()
