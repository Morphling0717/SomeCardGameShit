cmake_minimum_required(VERSION 3.25)

project(SomeCardGameShit VERSION 0.1.0 LANGUAGES C CXX)

include(FetchContent)
include(GNUInstallDirs)
include(CMakePackageConfigHelpers)

option(SCGS_WARNINGS_AS_ERRORS "Treat compiler warnings as errors" ON)
option(SCGS_ENABLE_SANITIZERS "Enable AddressSanitizer and UndefinedBehaviorSanitizer" OFF)
option(SCGS_ENABLE_LEGACY_YGO2_TESTS "Enable the Python contract-test group, including frozen YGOPro2 compatibility" ON)
option(SCGS_MSVC_STATIC_RUNTIME "Link MSVC targets against the static runtime (/MT or /MTd)" ON)

# The exported Windows client DLL must not require a separately installed
# Visual C++ redistributable.  Set the runtime before creating any targets so
# the choice is initialized consistently for the engine, ABI library, and
# native test consumers.
if(MSVC AND SCGS_MSVC_STATIC_RUNTIME)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

# Keep the native JSON serializer identical on every builder.
# nlohmann/json is header-only; its tests and install rules are not part of ours.
set(JSON_BuildTests OFF CACHE INTERNAL "Disable nlohmann/json tests")
set(JSON_Install OFF CACHE INTERNAL "Disable nlohmann/json install rules")
FetchContent_Declare(
    nlohmann_json
    URL https://github.com/nlohmann/json/releases/download/v3.12.0/json.tar.xz
    URL_HASH SHA256=42f6e95cad6ec532fd372391373363b62a14af6d771056dbfc86160e6dfff7aa
    DOWNLOAD_EXTRACT_TIMESTAMP TRUE
)
FetchContent_MakeAvailable(nlohmann_json)

function(scgs_apply_project_options target)
    if(MSVC)
        # Sources and card names are UTF-8. /permissive- is C++-only.
        target_compile_options(${target} PRIVATE
            /W4
            /utf-8
            $<$<COMPILE_LANGUAGE:CXX>:/permissive->
        )
        target_compile_definitions(${target} PRIVATE _CRT_SECURE_NO_WARNINGS)
        if(SCGS_WARNINGS_AS_ERRORS)
            target_compile_options(${target} PRIVATE /WX)
        endif()
    else()
        target_compile_options(${target} PRIVATE -Wall -Wextra -Wpedantic)
        if(SCGS_WARNINGS_AS_ERRORS)
            target_compile_options(${target} PRIVATE -Werror)
        endif()
    endif()

    if(SCGS_ENABLE_SANITIZERS)
        if(MSVC)
            message(FATAL_ERROR "SCGS_ENABLE_SANITIZERS is currently supported only with GCC or Clang")
        endif()
        target_compile_options(${target} PRIVATE
            -fsanitize=address,undefined
            -fno-omit-frame-pointer
        )
        target_link_options(${target} PRIVATE
            -fsanitize=address,undefined
            -fno-omit-frame-pointer
        )
    endif()
endfunction()

add_library(scgs_engine STATIC
    engine/src/catalog.cpp
    engine/src/client_api.cpp
    engine/src/game.cpp
    engine/src/protocol.cpp
)
add_library(scgs::engine ALIAS scgs_engine)

target_include_directories(scgs_engine
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/engine/include>
        $<INSTALL_INTERFACE:${CMAKE_INSTALL_INCLUDEDIR}>
)
target_compile_features(scgs_engine PUBLIC cxx_std_20)
set_target_properties(scgs_engine PROPERTIES
    POSITION_INDEPENDENT_CODE ON
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
)
scgs_apply_project_options(scgs_engine)

add_library(scgs_native_v04 SHARED engine/src/native_api_v04.cpp)
add_library(scgs::native_v04 ALIAS scgs_native_v04)

target_include_directories(scgs_native_v04
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/engine/include>
        $<INSTALL_INTERFACE:${CMAKE_INSTALL_INCLUDEDIR}>
)
target_compile_features(scgs_native_v04 PRIVATE cxx_std_20)
target_compile_definitions(scgs_native_v04 PRIVATE SCGS_V04_BUILDING_LIBRARY=1)
target_compile_definitions(scgs_native_v04 INTERFACE SCGS_V04_USING_LIBRARY=1)
target_link_libraries(scgs_native_v04
    PRIVATE
        scgs::engine
        nlohmann_json::nlohmann_json
)
set_target_properties(scgs_native_v04 PROPERTIES
    OUTPUT_NAME scgs_v04
    EXPORT_NAME native_v04
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN YES
    VERSION 1.0.0
    SOVERSION 1
)
if(UNIX AND NOT APPLE)
    set(scgs_native_v04_export_map
        "${CMAKE_CURRENT_SOURCE_DIR}/cmake/scgs_v04.exports.map"
    )
    target_link_options(scgs_native_v04 PRIVATE
        "-Wl,--version-script=${scgs_native_v04_export_map}"
    )
    set_property(TARGET scgs_native_v04 APPEND PROPERTY
        LINK_DEPENDS "${scgs_native_v04_export_map}"
    )
endif()
scgs_apply_project_options(scgs_native_v04)

add_executable(scgs_demo engine/apps/scgs_demo.cpp)
target_link_libraries(scgs_demo PRIVATE scgs::engine)
scgs_apply_project_options(scgs_demo)

add_executable(scgs_tests engine/tests/test_main.cpp)
target_link_libraries(scgs_tests PRIVATE scgs::engine)
scgs_apply_project_options(scgs_tests)

add_executable(scgs_client_api_tests engine/tests/test_client_api.cpp)
target_link_libraries(scgs_client_api_tests PRIVATE scgs::engine)
scgs_apply_project_options(scgs_client_api_tests)

# Legacy v1 wire freeze regression (golden bytes + bridge projection).
add_executable(scgs_wire_tests engine/tests/test_wire.cpp)
target_link_libraries(scgs_wire_tests PRIVATE scgs::engine)
scgs_apply_project_options(scgs_wire_tests)

# Gate 2 ABI tests: C header/link contract, JSON parity, and a true runtime
# loader that does not link to the native target.
add_executable(scgs_native_api_c_tests engine/tests/test_native_api_v04_c.c)
target_compile_features(scgs_native_api_c_tests PRIVATE c_std_11)
set_target_properties(scgs_native_api_c_tests PROPERTIES
    C_STANDARD 11
    C_STANDARD_REQUIRED YES
    C_EXTENSIONS NO
)
target_link_libraries(scgs_native_api_c_tests PRIVATE scgs::native_v04)
if(SCGS_ENABLE_SANITIZERS)
    # The source remains C11, but Clang's C++ sanitizer runtime is required by
    # the C++ shared library when this executable is linked under ASan/UBSan.
    set_property(TARGET scgs_native_api_c_tests PROPERTY LINKER_LANGUAGE CXX)
endif()
scgs_apply_project_options(scgs_native_api_c_tests)

add_executable(scgs_native_api_tests engine/tests/test_native_api_v04.cpp)
target_compile_features(scgs_native_api_tests PRIVATE cxx_std_20)
target_link_libraries(scgs_native_api_tests
    PRIVATE
        scgs::engine
        scgs::native_v04
        nlohmann_json::nlohmann_json
)
scgs_apply_project_options(scgs_native_api_tests)

add_executable(scgs_native_api_dynamic_tests engine/tests/test_native_api_v04_dynamic.cpp)
target_compile_features(scgs_native_api_dynamic_tests PRIVATE cxx_std_20)
target_include_directories(scgs_native_api_dynamic_tests PRIVATE engine/include)
target_link_libraries(scgs_native_api_dynamic_tests PRIVATE ${CMAKE_DL_LIBS})
add_dependencies(scgs_native_api_dynamic_tests scgs_native_v04)
scgs_apply_project_options(scgs_native_api_dynamic_tests)

enable_testing()
add_test(NAME scgs_unit_tests COMMAND scgs_tests)
add_test(NAME scgs_client_api_contract COMMAND scgs_client_api_tests)
add_test(NAME scgs_documented_scenario COMMAND scgs_demo --verify)
add_test(NAME scgs_wire_frozen_golden COMMAND scgs_wire_tests)
add_test(NAME scgs_native_api_c_contract COMMAND scgs_native_api_c_tests)
add_test(NAME scgs_native_api_contract COMMAND scgs_native_api_tests)
add_test(
    NAME scgs_native_api_dynamic_load
    COMMAND scgs_native_api_dynamic_tests $<TARGET_FILE:scgs_native_v04>
)

if(SCGS_ENABLE_LEGACY_YGO2_TESTS)
    find_package(Python3 3.10 REQUIRED COMPONENTS Interpreter)
    add_test(
        NAME scgs_ygo2_overlay_patcher
        COMMAND ${Python3_EXECUTABLE} -m unittest tools.tests.test_apply_ygo2_overlay
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
    )
    add_test(
        NAME scgs_protocol_contract
        COMMAND ${Python3_EXECUTABLE} -m unittest tools.tests.test_protocol_contract
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
    )
    add_test(
        NAME scgs_native_artifact_audit_contract
        COMMAND ${Python3_EXECUTABLE} -m unittest scripts.tests.test_audit_native_artifact
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
    )
    add_test(
        NAME scgs_godot_export_audit_contract
        COMMAND ${Python3_EXECUTABLE} -m unittest scripts.tests.test_audit_godot_export
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
    )
    add_test(
        NAME scgs_subprocess_timeout_contract
        COMMAND ${Python3_EXECUTABLE} -m unittest scripts.tests.test_run_with_timeout
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
    )
    add_test(
        NAME scgs_gate3b_report_contract
        COMMAND ${Python3_EXECUTABLE} -m unittest scripts.tests.test_validate_gate3b_report
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
    )
    add_test(
        NAME scgs_gate3c_report_contract
        COMMAND ${Python3_EXECUTABLE} -m unittest scripts.tests.test_validate_gate3c_report
        WORKING_DIRECTORY ${CMAKE_CURRENT_SOURCE_DIR}
    )
endif()

install(
    TARGETS scgs_native_v04
    EXPORT scgs_native_v04Targets
    RUNTIME DESTINATION ${CMAKE_INSTALL_BINDIR}
    LIBRARY DESTINATION ${CMAKE_INSTALL_LIBDIR}
    ARCHIVE DESTINATION ${CMAKE_INSTALL_LIBDIR}
)
install(
    FILES engine/include/scgs/native_api_v04.h
    DESTINATION ${CMAKE_INSTALL_INCLUDEDIR}/scgs
)
install(
    FILES
        LICENSE
        THIRD_PARTY_NOTICES.md
        docs/native-api-v04.md
    DESTINATION ${CMAKE_INSTALL_DATADIR}/scgs_native_v04
)
install(
    FILES ${nlohmann_json_SOURCE_DIR}/LICENSE.MIT
    DESTINATION ${CMAKE_INSTALL_DATADIR}/scgs_native_v04
    RENAME nlohmann-json-LICENSE.MIT
)
install(
    EXPORT scgs_native_v04Targets
    FILE scgs_native_v04Targets.cmake
    NAMESPACE scgs::
    DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/scgs_native_v04
)

configure_package_config_file(
    cmake/scgs_native_v04Config.cmake.in
    ${CMAKE_CURRENT_BINARY_DIR}/scgs_native_v04Config.cmake
    INSTALL_DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/scgs_native_v04
)
write_basic_package_version_file(
    ${CMAKE_CURRENT_BINARY_DIR}/scgs_native_v04ConfigVersion.cmake
    VERSION 1.0.0
    COMPATIBILITY SameMajorVersion
)
install(
    FILES
        ${CMAKE_CURRENT_BINARY_DIR}/scgs_native_v04Config.cmake
        ${CMAKE_CURRENT_BINARY_DIR}/scgs_native_v04ConfigVersion.cmake
    DESTINATION ${CMAKE_INSTALL_LIBDIR}/cmake/scgs_native_v04
)
