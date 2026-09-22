# ============================================================================
#  Fusion-HP — native/poc-clrhost/CMakeLists.txt
# ----------------------------------------------------------------------------
#  Proyecto AUTÓNOMO del PoC de CLR Hosting (no se agrega desde
#  native/CMakeLists para no tocar la base del núcleo). El CI lo compila con:
#      cmake -S native/poc-clrhost -B <build> -A Win32|x64 -DCMAKE_BUILD_TYPE=Release
#      cmake --build <build> --config Release
#
#  Notas:
#   * /MT (CRT estático): el binario no arrastra vcruntime/redistributables.
#   * /utf-8: comentarios y mensajes en español se leen como UTF-8.
#   * Subsistema CONSOLA (default): el PoC imprime "CLRHOST PASS/FAIL" por
#     stdout y su exit code es el gate del job clrhost-poc.
#   * Solo Win32: el hosting API (metahost/mscoree) no existe fuera de Windows.
# ============================================================================
cmake_minimum_required(VERSION 3.16)
project(FusionPocClrhost LANGUAGES CXX)

if(NOT WIN32)
    message(FATAL_ERROR "fusion_poc_clrhost solo se compila en Windows (Win32).")
endif()

add_executable(fusion_poc_clrhost main.cpp)

set_target_properties(fusion_poc_clrhost PROPERTIES
    OUTPUT_NAME "fusion_poc_clrhost"
    CXX_STANDARD 17
    CXX_STANDARD_REQUIRED ON
)

if(MSVC)
    set_property(TARGET fusion_poc_clrhost PROPERTY MSVC_RUNTIME_LIBRARY
        "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    target_compile_options(fusion_poc_clrhost PRIVATE /W4 /EHsc /utf-8)
    # ole32/oleaut32: CoInitializeEx, Variant*, SysAllocString.
    # uuid: __uuidof de interfaces MIDL (ICLRMetaHost, IDispatch, ...).
    target_link_libraries(fusion_poc_clrhost PRIVATE ole32 oleaut32 uuid)
endif()
