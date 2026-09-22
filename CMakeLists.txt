cmake_minimum_required(VERSION 3.16)
project(FusionNative CXX C)

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
