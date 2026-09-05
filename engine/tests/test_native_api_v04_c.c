// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/native_api_v04.h"
#include "scgs/native_api_v04.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int fail(const char* message) {
    fprintf(stderr, "native C11 smoke failed: %s\n", message);
    return 1;
}

int main(void) {
    static const char config[] =
        "{\"schema_version\":1,\"player0_deck\":\"synthetic_alpha\","
        "\"player1_deck\":\"synthetic_beta\",\"random_seed\":3235823838,"
        "\"first_player_mode\":1,\"shuffle_decks\":false}";

    if (scgs_v04_abi_version() != SCGS_V04_ABI_VERSION) {
        return fail("ABI version mismatch");
    }

    scgs_v04_handle handle = 0;
    if (scgs_v04_create(
            SCGS_V04_ABI_VERSION,
            config,
            (uint64_t)(sizeof(config) - 1U),
            &handle) != SCGS_V04_OK ||
        handle == 0) {
        return fail("create did not return a live handle");
    }

    uint32_t engine_code = SCGS_V04_NO_ENGINE_CODE;
    if (scgs_v04_start(handle, &engine_code) != SCGS_V04_OK || engine_code != 0U) {
        (void)scgs_v04_destroy(handle);
        return fail("start failed");
    }

    uint64_t required = 0;
    if (scgs_v04_get_view_json(handle, 0U, NULL, 0U, &required) !=
            SCGS_V04_BUFFER_TOO_SMALL ||
        required < 2U) {
        (void)scgs_v04_destroy(handle);
        return fail("view length query failed");
    }

    char* view = (char*)malloc((size_t)required);
    if (view == NULL) {
        (void)scgs_v04_destroy(handle);
        return fail("allocation failed");
    }
    memset(view, 0x5A, (size_t)required);
    if (scgs_v04_get_view_json(handle, 0U, view, required, &required) != SCGS_V04_OK ||
        view[required - 1U] != '\0' || view[0] != '{') {
        free(view);
        (void)scgs_v04_destroy(handle);
        return fail("view retrieval failed");
    }
    free(view);

    if (scgs_v04_destroy(handle) != SCGS_V04_OK) {
        return fail("destroy failed");
    }

    puts("native C11 consumer smoke passed");
    return 0;
}
