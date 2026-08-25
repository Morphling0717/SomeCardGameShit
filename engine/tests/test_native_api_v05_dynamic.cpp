// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/native_api_v05.h"

#include <array>
#include <cstring>
#include <iostream>
#include <string>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace {

class DynamicLibrary final {
public:
    explicit DynamicLibrary(const char* path) {
#if defined(_WIN32)
        library_ = LoadLibraryA(path);
#else
        library_ = dlopen(path, RTLD_NOW | RTLD_LOCAL);
#endif
    }

    DynamicLibrary(const DynamicLibrary&) = delete;
    DynamicLibrary& operator=(const DynamicLibrary&) = delete;

    ~DynamicLibrary() {
#if defined(_WIN32)
        if (library_ != nullptr) {
            FreeLibrary(library_);
        }
#else
        if (library_ != nullptr) {
            dlclose(library_);
        }
#endif
    }

    [[nodiscard]] bool loaded() const noexcept { return library_ != nullptr; }

    template <typename Function>
    [[nodiscard]] Function find(const char* name) const noexcept {
#if defined(_WIN32)
        return reinterpret_cast<Function>(GetProcAddress(library_, name));
#else
        void* address = dlsym(library_, name);
        Function function = nullptr;
        static_assert(sizeof(function) == sizeof(address));
        std::memcpy(&function, &address, sizeof(function));
        return function;
#endif
    }

private:
#if defined(_WIN32)
    HMODULE library_ = nullptr;
#else
    void* library_ = nullptr;
#endif
};

int fail(const std::string& message) {
    std::cerr << "v05 dynamic-load contract failed: " << message << '\n';
    return 1;
}

} // namespace

int main(const int argc, char** argv) {
    if (argc != 2) {
        return fail("expected the shared-library path as argv[1]");
    }
    const DynamicLibrary library(argv[1]);
    if (!library.loaded()) {
        return fail(std::string("could not load ") + argv[1]);
    }
    static constexpr std::array expected_symbols{
        "scgs_v05_abi_version",
        "scgs_v05_create",
        "scgs_v05_destroy",
        "scgs_v05_start",
        "scgs_v05_get_view_json",
        "scgs_v05_list_legal_actions_json",
        "scgs_v05_list_valid_targets_json",
        "scgs_v05_list_valid_slots_json",
        "scgs_v05_list_valid_donors_json",
        "scgs_v05_preview_payment_json",
        "scgs_v05_get_reaction_context_json",
        "scgs_v05_submit_command_json",
        "scgs_v05_read_events_json",
        "scgs_v05_get_last_error",
    };
    for (const char* symbol : expected_symbols) {
        if (library.find<void (*)()>(symbol) == nullptr) {
            return fail(std::string("missing export: ") + symbol);
        }
    }
    if (library.find<void (*)()>("scgs_v04_abi_version") != nullptr) {
        return fail("v05 library leaked a v04 export");
    }
    const auto abi = library.find<decltype(&scgs_v05_abi_version)>("scgs_v05_abi_version");
    if (abi() != SCGS_V05_ABI_VERSION) {
        return fail("loaded library reports the wrong ABI version");
    }
    std::cout << "v05 explicit dynamic-load contract passed: " << expected_symbols.size()
              << " exports\n";
    return 0;
}
