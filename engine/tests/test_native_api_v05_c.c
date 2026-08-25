// SPDX-License-Identifier: GPL-3.0-or-later
#include "scgs/native_api_v05.h"
// Include twice to keep the public C header's include guard in the contract.
#include "scgs/native_api_v05.h"

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

static int fail(const char* message) {
    fprintf(stderr, "v05 native C11 smoke failed: %s\n", message);
    return 1;
}

int main(void) {
    static const char config[] =
        "{\"schema_version\":2,"
        "\"player0_deck\":\"oathguard_luminous_oath_v1\","
        "\"player1_deck\":\"pactmage_abyssal_pact_v1\","
        "\"random_seed\":7,\"first_player_mode\":1,\"shuffle_decks\":false}";

    if (scgs_v05_abi_version() != SCGS_V05_ABI_VERSION) {
        return fail("ABI version mismatch");
    }
    scgs_v05_handle handle = 0;
    if (scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            config,
            (uint64_t)(sizeof(config) - 1U),
            &handle) != SCGS_V05_OK ||
        handle == 0U) {
        return fail("create did not return a live handle");
    }

    uint32_t engine_code = SCGS_V05_NO_ENGINE_CODE;
    if (scgs_v05_start(handle, &engine_code) != SCGS_V05_OK || engine_code != 0U) {
        (void)scgs_v05_destroy(handle);
        return fail("start failed");
    }

    uint64_t required = 0;
    if (scgs_v05_get_view_json(handle, 0U, NULL, 0U, &required) !=
            SCGS_V05_BUFFER_TOO_SMALL ||
        required < 2U) {
        (void)scgs_v05_destroy(handle);
        return fail("view length query failed");
    }
    char* view = (char*)malloc((size_t)required);
    if (view == NULL) {
        (void)scgs_v05_destroy(handle);
        return fail("allocation failed");
    }
    if (scgs_v05_get_view_json(handle, 0U, view, required, &required) != SCGS_V05_OK ||
        view[required - 1U] != '\0' ||
        strstr(view, "\"schema_version\":2") == NULL ||
        strstr(view, "\"main_board\"") == NULL ||
        strstr(view, "\"pending_choice\"") == NULL ||
        strstr(view, "random_seed") != NULL) {
        free(view);
        (void)scgs_v05_destroy(handle);
        return fail("schema-2 viewer-safe snapshot failed");
    }
    free(view);

    if (scgs_v05_destroy(handle) != SCGS_V05_OK) {
        return fail("destroy failed");
    }
    puts("v05 native C11 consumer smoke passed");
    return 0;
}
