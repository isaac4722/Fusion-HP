# Núcleo nativo híbrido: LuminaCore (DLL/SO) + selftest + PoCNative
include_directories(${CMAKE_CURRENT_SOURCE_DIR}/core/include)
include_directories(${CMAKE_CURRENT_SOURCE_DIR}/core/src)
include_directories(${CMAKE_SOURCE_DIR}/third_party/sqlite3)
include_directories(${CMAKE_SOURCE_DIR}/third_party/nlohmann)

set(LUMINA_CORE_SOURCES
    core/src/lumina_api.cpp
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
    list(APPEND LUMINA_CORE_SOURCES core/src/Renderer.cpp core/src/Projector.cpp
         core/src/Win7Compat.cpp)
    add_definitions(-DLUMINA_HAS_WIN32=1)
    # v5.2.0 «MOTOR»: nivel de compatibilidad explícito Win7 SP1. Sin esto el
    # SDK moderno asume 0x0A00 (Win10) y expone APIs inexistentes en Win7.
    add_definitions(-D_WIN32_WINNT=0x0601 -DWINVER=0x0601)
endif()

add_definitions(
    -DLUMINA_CORE_BUILD=1
    -DSQLITE_ENABLE_FTS5=1
    -DSQLITE_ENABLE_JSON1=1
    -DSQLITE_THREADSAFE=1
    -DSQLITE_OMIT_LOAD_EXTENSION=1
    -DNOMINMAX -DWIN32_LEAN_AND_MEAN
)

add_library(LuminaCore SHARED ${LUMINA_CORE_SOURCES})
if(NOT MSVC)
    target_link_libraries(LuminaCore PRIVATE pthread dl)
    # Falso positivo de GCC en el amalgamado sqlite3 (código de terceros)
    set_source_files_properties(${CMAKE_SOURCE_DIR}/third_party/sqlite3/sqlite3.c
        PROPERTIES COMPILE_OPTIONS "-Wno-stringop-overread")
else()
    target_link_libraries(LuminaCore PRIVATE user32 gdi32 gdiplus ole32 delayimp)
    # v5.2.0 «MOTOR» (bug de campo v5.1.1 en Win7 SP1 x86): el STL estático
    # de MSVC arrastra GetSystemTimePreciseAsFileTime (Win8+) como import
    # ESTÁTICO vía el objeto de std::condition_variable → LoadLibrary falla
    # en Win7 con ERROR_PROCEDURE_NOT_FOUND. Con /DELAYLOAD pasa a la tabla
    # delay y el hook de Win7Compat.cpp resuelve: API real en Win8+, fallback
    # sobre GetSystemTimeAsFileTime en Win7. El gate verify_win7_imports.py
    # de la CI vela porque ningún otro import Win8+ vuelva a colarse.
    target_link_options(LuminaCore PRIVATE
        "/DELAYLOAD:GetSystemTimePreciseAsFileTime")
endif()
set_target_properties(LuminaCore PROPERTIES OUTPUT_NAME "LuminaCore")

# Variante ESTÁTICA para los consumidores C++ del propio repo (selftest y
# PoC nativo): en MSVC las funciones C++ internas NO se exportan del DLL,
# así que enlazarían igual que el lado gestionado lo hace con la API C.
add_library(LuminaCoreStatic STATIC ${LUMINA_CORE_SOURCES})
target_compile_definitions(LuminaCoreStatic PRIVATE LUMINA_CORE_STATIC=1)
if(NOT MSVC)
    target_link_libraries(LuminaCoreStatic PRIVATE pthread dl)
else()
    target_link_libraries(LuminaCoreStatic PRIVATE user32 gdi32 gdiplus ole32)
endif()
set_target_properties(LuminaCoreStatic PROPERTIES OUTPUT_NAME "LuminaCoreStatic")

# Arnés de pruebas nativo (lógica del núcleo, sin GUI)
add_executable(lumina_selftest core/tests/native_tests.cpp)
target_link_libraries(lumina_selftest PRIVATE LuminaCoreStatic)

# PoC nativo: ejercicio del API C en modo headless (equivalente C++ del PoC C#)
add_executable(lumina_poc_native poc/PoCNative.cpp)
target_link_libraries(lumina_poc_native PRIVATE LuminaCoreStatic)
