# ============================================================================
#  LuminaPresentation — native/launcher/CMakeLists.txt
# ----------------------------------------------------------------------------
#  Proyecto AUTÓNOMO del launcher nativo (no se agrega desde native/CMakeLists
#  para no tocar la base del núcleo). El CI lo compila con:
#      cmake -S native/launcher -B <build> -A Win32|x64 -DCMAKE_BUILD_TYPE=Release
#      cmake --build <build> --config Release
#
#  Notas:
#   * /MT (CRT estático): cero dependencias de redistributable.
#   * /utf-8: los literales y comentarios en español se leen como UTF-8.
#   * Subsistema WINDOWS (WIN32_EXECUTABLE): sin consola al hacer doble clic.
#     El punto de entrada es WinMain con APIs W explícitas.
#   * Solo Win32: el resto de la plataforma no tiene sentido aquí.
# ============================================================================
cmake_minimum_required(VERSION 3.16)
project(LuminaLauncher LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "LuminaLauncher solo se compila en Windows (Win32).")
endif()

add_executable(LuminaLauncher LuminaLauncher.cpp)

set_target_properties(LuminaLauncher PROPERTIES
    WIN32_EXECUTABLE TRUE              # /SUBSYSTEM:WINDOWS (GUI)
    OUTPUT_NAME "LuminaLauncher"
    CXX_STANDARD 17
    CXX_STANDARD_REQUIRED ON
)

if(MSVC)
    # CRT estático /MT (Release) — el paquete portable no lleva vcruntime.
    set_property(TARGET LuminaLauncher PROPERTY MSVC_RUNTIME_LIBRARY
        "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(LuminaLauncher PRIVATE /W4 /EHsc /utf-8)
    target_link_libraries(LuminaLauncher PRIVATE user32 advapi32)
else()
    # Rama informativa (p. ej. MinGW): se compila, pero el CI usa MSVC.
    target_compile_definitions(LuminaLauncher PRIVATE UNICODE _UNICODE)
endif()
