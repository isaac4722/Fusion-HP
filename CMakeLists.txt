cmake_minimum_required(VERSION 3.16)
project(LuminaNative CXX C)

# v5.2.0 «MOTOR»: MASM para el shim del import Win8+ en x86
# (native/core/src/Win7CompatX86.asm — ver comentario ahí). Solo Windows.
if(WIN32)
    enable_language(ASM_MASM OPTIONAL)
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

if(MSVC)
    # CRT estático (/MT) — cero dependencias redistribuibles
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    add_compile_options(/W4 /MP /EHsc)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

add_subdirectory(native)
