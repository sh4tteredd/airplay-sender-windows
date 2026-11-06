add_library(AirPlayCore SHARED airplay_core.cpp)

target_include_directories(AirPlayCore PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${CMAKE_SOURCE_DIR}/third_party/airplay2-sender-cpp/src)

target_link_libraries(AirPlayCore PRIVATE raop_sender)
target_compile_definitions(AirPlayCore PRIVATE AIRPLAYCORE_EXPORTS)

if(MSVC)
    target_compile_options(AirPlayCore PRIVATE /W4 /EHsc)
    target_compile_definitions(AirPlayCore PRIVATE _CRT_SECURE_NO_WARNINGS)
endif()

set_target_properties(AirPlayCore PROPERTIES
    OUTPUT_NAME "AirPlayCore"
    PREFIX ""
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/bin"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/bin/Release"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/bin/Debug")
