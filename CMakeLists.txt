cmake_minimum_required(VERSION 3.25)

project(SomeCardGameShit VERSION 0.1.0 LANGUAGES CXX)

option(SCGS_WARNINGS_AS_ERRORS "Treat compiler warnings as errors" ON)
option(SCGS_ENABLE_SANITIZERS "Enable AddressSanitizer and UndefinedBehaviorSanitizer" OFF)
option(SCGS_ENABLE_LEGACY_YGO2_TESTS "Enable the frozen YGOPro2 overlay/protocol Python tests" ON)

add_library(scgs_engine
    engine/src/catalog.cpp
    engine/src/client_api.cpp
    engine/src/game.cpp
    engine/src/protocol.cpp
)

add_library(scgs::engine ALIAS scgs_engine)

target_include_directories(scgs_engine
    PUBLIC
        $<BUILD_INTERFACE:${CMAKE_CURRENT_SOURCE_DIR}/engine/include>
        $<INSTALL_INTERFACE:include>
)

target_compile_features(scgs_engine PUBLIC cxx_std_20)

if(MSVC)
    # /utf-8: sources (card names, comments) are UTF-8; without it MSVC misreads
    # them under the system codepage and C4819 becomes an error under /WX.
    # _CRT_SECURE_NO_WARNINGS: std::getenv triggers C4996 under MSVC, which /WX
    # escalates to an error (historically broke the windows-msvc CI job).
    target_compile_options(scgs_engine PRIVATE /W4 /permissive- /utf-8)
    target_compile_definitions(scgs_engine PRIVATE _CRT_SECURE_NO_WARNINGS)
    if(SCGS_WARNINGS_AS_ERRORS)
        target_compile_options(scgs_engine PRIVATE /WX)
    endif()
else()
    target_compile_options(scgs_engine PRIVATE -Wall -Wextra -Wpedantic)
    if(SCGS_WARNINGS_AS_ERRORS)
        target_compile_options(scgs_engine PRIVATE -Werror)
    endif()
endif()

add_executable(scgs_demo engine/apps/scgs_demo.cpp)
target_link_libraries(scgs_demo PRIVATE scgs::engine)

add_executable(scgs_tests engine/tests/test_main.cpp)
target_link_libraries(scgs_tests PRIVATE scgs::engine)

add_executable(scgs_client_api_tests engine/tests/test_client_api.cpp)
target_link_libraries(scgs_client_api_tests PRIVATE scgs::engine)

# Legacy v1 wire freeze regression (golden bytes + bridge projection).
add_executable(scgs_wire_tests engine/tests/test_wire.cpp)
target_link_libraries(scgs_wire_tests PRIVATE scgs::engine)

if(MSVC)
    target_compile_options(scgs_demo PRIVATE /W4 /permissive- /utf-8)
    target_compile_options(scgs_tests PRIVATE /W4 /permissive- /utf-8)
    target_compile_options(scgs_client_api_tests PRIVATE /W4 /permissive- /utf-8)
    target_compile_options(scgs_wire_tests PRIVATE /W4 /permissive- /utf-8)
    target_compile_definitions(scgs_demo PRIVATE _CRT_SECURE_NO_WARNINGS)
    target_compile_definitions(scgs_tests PRIVATE _CRT_SECURE_NO_WARNINGS)
    target_compile_definitions(scgs_client_api_tests PRIVATE _CRT_SECURE_NO_WARNINGS)
    target_compile_definitions(scgs_wire_tests PRIVATE _CRT_SECURE_NO_WARNINGS)
    if(SCGS_WARNINGS_AS_ERRORS)
        target_compile_options(scgs_demo PRIVATE /WX)
        target_compile_options(scgs_tests PRIVATE /WX)
        target_compile_options(scgs_client_api_tests PRIVATE /WX)
        target_compile_options(scgs_wire_tests PRIVATE /WX)
    endif()
else()
    target_compile_options(scgs_demo PRIVATE -Wall -Wextra -Wpedantic)
    target_compile_options(scgs_tests PRIVATE -Wall -Wextra -Wpedantic)
    target_compile_options(scgs_client_api_tests PRIVATE -Wall -Wextra -Wpedantic)
    target_compile_options(scgs_wire_tests PRIVATE -Wall -Wextra -Wpedantic)
    if(SCGS_WARNINGS_AS_ERRORS)
        target_compile_options(scgs_demo PRIVATE -Werror)
        target_compile_options(scgs_tests PRIVATE -Werror)
        target_compile_options(scgs_client_api_tests PRIVATE -Werror)
        target_compile_options(scgs_wire_tests PRIVATE -Werror)
    endif()
endif()

if(SCGS_ENABLE_SANITIZERS)
    if(MSVC)
        message(FATAL_ERROR "SCGS_ENABLE_SANITIZERS is currently supported only with GCC or Clang")
    endif()
    foreach(target scgs_engine scgs_demo scgs_tests scgs_client_api_tests scgs_wire_tests)
        target_compile_options(${target} PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer)
        target_link_options(${target} PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer)
    endforeach()
endif()

enable_testing()
add_test(NAME scgs_unit_tests COMMAND scgs_tests)
add_test(NAME scgs_client_api_contract COMMAND scgs_client_api_tests)
add_test(NAME scgs_documented_scenario COMMAND scgs_demo --verify)
add_test(NAME scgs_wire_frozen_golden COMMAND scgs_wire_tests)

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
endif()
