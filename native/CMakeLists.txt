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

# Variante ESTÁTICA para los consumidores C++ del propio repo (selftest y
# PoC nativo): en MSVC las funciones C++ internas NO se exportan del DLL,
# así que enlazarían igual que el lado gestionado lo hace con la API C.
add_library(FusionCoreStatic STATIC ${FUSION_CORE_SOURCES})
target_compile_definitions(FusionCoreStatic PRIVATE FUSION_CORE_STATIC=1)
if(NOT MSVC)
    target_link_libraries(FusionCoreStatic PRIVATE pthread dl)
else()
    target_link_libraries(FusionCoreStatic PRIVATE user32 gdi32 gdiplus ole32)
endif()
set_target_properties(FusionCoreStatic PROPERTIES OUTPUT_NAME "FusionCoreStatic")

# Arnés de pruebas nativo (lógica del núcleo, sin GUI)
add_executable(fusion_selftest core/tests/native_tests.cpp)
target_link_libraries(fusion_selftest PRIVATE FusionCoreStatic)

# PoC nativo: ejercicio del API C en modo headless (equivalente C++ del PoC C#)
add_executable(fusion_poc_native poc/PoCNative.cpp)
target_link_libraries(fusion_poc_native PRIVATE FusionCoreStatic)
