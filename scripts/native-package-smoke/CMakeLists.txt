cmake_minimum_required(VERSION 3.25)

project(scgs_native_v04_package_smoke LANGUAGES C)

find_package(scgs_native_v04 1.0 CONFIG REQUIRED)

add_executable(scgs_native_v04_package_smoke main.c)
target_compile_features(scgs_native_v04_package_smoke PRIVATE c_std_11)
target_link_libraries(scgs_native_v04_package_smoke PRIVATE scgs::native_v04)
