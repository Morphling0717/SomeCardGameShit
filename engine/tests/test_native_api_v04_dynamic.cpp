// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/native_api_v04.h"

#include <array>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <string>
#include <vector>

#if defined(_WIN32)
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#else
#include <dlfcn.h>
#endif

namespace {

class DynamicLibrary {
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
    std::cerr << "native dynamic-load test failed: " << message << '\n';
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
        "scgs_v04_abi_version",
        "scgs_v04_create",
        "scgs_v04_destroy",
        "scgs_v04_start",
        "scgs_v04_get_view_json",
        "scgs_v04_list_legal_actions_json",
        "scgs_v04_list_valid_targets_json",
        "scgs_v04_list_valid_slots_json",
        "scgs_v04_list_valid_donors_json",
        "scgs_v04_preview_payment_json",
        "scgs_v04_get_reaction_context_json",
        "scgs_v04_submit_command_json",
        "scgs_v04_read_events_json",
        "scgs_v04_get_last_error",
    };
    for (const char* symbol : expected_symbols) {
        if (library.find<void (*)()>(symbol) == nullptr) {
            return fail(std::string("missing export: ") + symbol);
        }
    }

    const auto abi_version = library.find<decltype(&scgs_v04_abi_version)>("scgs_v04_abi_version");
    const auto create = library.find<decltype(&scgs_v04_create)>("scgs_v04_create");
    const auto destroy = library.find<decltype(&scgs_v04_destroy)>("scgs_v04_destroy");
    const auto start = library.find<decltype(&scgs_v04_start)>("scgs_v04_start");
    const auto get_view = library.find<decltype(&scgs_v04_get_view_json)>("scgs_v04_get_view_json");

    if (abi_version() != SCGS_V04_ABI_VERSION) {
        return fail("loaded library reports the wrong ABI version");
    }

    static constexpr char config[] =
        "{\"schema_version\":1,\"player0_deck\":\"midrange\","
        "\"player1_deck\":\"advance\",\"random_seed\":1364283729,"
        "\"first_player_mode\":1,\"shuffle_decks\":false}";
    scgs_v04_handle handle = 0;
    if (create(SCGS_V04_ABI_VERSION, config, sizeof(config) - 1U, &handle) != SCGS_V04_OK ||
        handle == 0) {
        return fail("create through resolved function failed");
    }

    std::uint32_t engine_code = SCGS_V04_NO_ENGINE_CODE;
    if (start(handle, &engine_code) != SCGS_V04_OK || engine_code != 0U) {
        (void)destroy(handle);
        return fail("start through resolved function failed");
    }

    std::uint64_t required = 0;
    if (get_view(handle, 0U, nullptr, 0U, &required) != SCGS_V04_BUFFER_TOO_SMALL || required < 2U) {
        (void)destroy(handle);
        return fail("view length query through resolved function failed");
    }
    std::vector<char> buffer(static_cast<std::size_t>(required));
    if (get_view(handle, 0U, buffer.data(), buffer.size(), &required) != SCGS_V04_OK ||
        buffer.back() != '\0' || buffer.front() != '{') {
        (void)destroy(handle);
        return fail("view retrieval through resolved function failed");
    }

    if (destroy(handle) != SCGS_V04_OK) {
        return fail("destroy through resolved function failed");
    }

    std::cout << "native explicit dynamic-load contract passed: " << expected_symbols.size()
              << " exports\n";
    return 0;
}
