cmake_minimum_required(VERSION 3.20)
project(AirPlaySender LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

# Ship a self-contained native DLL without requiring the VC++ redistributable.
if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(NOT EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/third_party/airplay2-sender-cpp/CMakeLists.txt")
    message(FATAL_ERROR "Protocol core submodule is missing. Run: git submodule update --init --recursive")
endif()

# The upstream project fetches Mbed TLS at configure time.
set(AIRPLAY_BUILD_EXAMPLE OFF CACHE BOOL "" FORCE)
set(AIRPLAY_BUILD_TESTS   OFF CACHE BOOL "" FORCE)
set(AIRPLAY_BUILD_QT_HOST OFF CACHE BOOL "" FORCE)

add_subdirectory(third_party/airplay2-sender-cpp)
add_subdirectory(src/AirPlaySender.Native)
