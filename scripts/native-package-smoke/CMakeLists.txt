cmake_minimum_required(VERSION 3.25)

project(scgs_native_package_smoke LANGUAGES C)

option(SCGS_NATIVE_SMOKE_USE_CXX_LINKER
    "Link the C11 smoke consumer with the C++ driver for sanitizer runtimes"
    OFF
)
if(SCGS_NATIVE_SMOKE_USE_CXX_LINKER)
    enable_language(CXX)
endif()

find_package(scgs_native_v04 1.0 CONFIG REQUIRED)
find_package(scgs_native_v05 2.0 CONFIG REQUIRED)

add_executable(scgs_native_v04_package_smoke main.c)
target_compile_features(scgs_native_v04_package_smoke PRIVATE c_std_11)
target_link_libraries(scgs_native_v04_package_smoke PRIVATE scgs::native_v04)
if(SCGS_NATIVE_SMOKE_USE_CXX_LINKER)
    set_property(TARGET scgs_native_v04_package_smoke PROPERTY LINKER_LANGUAGE CXX)
endif()

add_executable(scgs_native_v05_package_smoke main_v05.c)
target_compile_features(scgs_native_v05_package_smoke PRIVATE c_std_11)
target_link_libraries(scgs_native_v05_package_smoke PRIVATE scgs::native_v05)
if(SCGS_NATIVE_SMOKE_USE_CXX_LINKER)
    set_property(TARGET scgs_native_v05_package_smoke PROPERTY LINKER_LANGUAGE CXX)
endif()
