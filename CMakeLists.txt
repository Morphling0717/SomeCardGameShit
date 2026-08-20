cmake_minimum_required(VERSION 3.20)

project(SomeCardGameShit VERSION 0.1.0 LANGUAGES CXX)

option(SCGS_WARNINGS_AS_ERRORS "Treat compiler warnings as errors" ON)
option(SCGS_ENABLE_SANITIZERS "Enable AddressSanitizer and UndefinedBehaviorSanitizer" OFF)

add_library(scgs_engine
    engine/src/catalog.cpp
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
    target_compile_options(scgs_engine PRIVATE /W4 /permissive-)
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

if(MSVC)
    target_compile_options(scgs_demo PRIVATE /W4 /permissive-)
    target_compile_options(scgs_tests PRIVATE /W4 /permissive-)
    if(SCGS_WARNINGS_AS_ERRORS)
        target_compile_options(scgs_demo PRIVATE /WX)
        target_compile_options(scgs_tests PRIVATE /WX)
    endif()
else()
    target_compile_options(scgs_demo PRIVATE -Wall -Wextra -Wpedantic)
    target_compile_options(scgs_tests PRIVATE -Wall -Wextra -Wpedantic)
    if(SCGS_WARNINGS_AS_ERRORS)
        target_compile_options(scgs_demo PRIVATE -Werror)
        target_compile_options(scgs_tests PRIVATE -Werror)
    endif()
endif()

if(SCGS_ENABLE_SANITIZERS)
    if(MSVC)
        message(FATAL_ERROR "SCGS_ENABLE_SANITIZERS is currently supported only with GCC or Clang")
    endif()
    foreach(target scgs_engine scgs_demo scgs_tests)
        target_compile_options(${target} PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer)
        target_link_options(${target} PRIVATE -fsanitize=address,undefined -fno-omit-frame-pointer)
    endforeach()
endif()

enable_testing()
add_test(NAME scgs_unit_tests COMMAND scgs_tests)
add_test(NAME scgs_documented_scenario COMMAND scgs_demo --verify)

find_package(Python3 COMPONENTS Interpreter QUIET)
if(Python3_Interpreter_FOUND)
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
