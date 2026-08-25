#include <scgs/native_api_v05.h>

#include <stdint.h>
#include <stdlib.h>
#include <string.h>

_Static_assert(sizeof(scgs_v05_handle) == sizeof(uint64_t), "handle must be 64-bit");
_Static_assert(sizeof(scgs_v05_native_code) == sizeof(uint32_t), "code must be 32-bit");

int main(void) {
    static const char config[] =
        "{\"schema_version\":2,"
        "\"player0_deck\":\"oathguard_luminous_oath_v1\","
        "\"player1_deck\":\"pactmage_abyssal_pact_v1\","
        "\"random_seed\":7,\"first_player_mode\":1,\"shuffle_decks\":false}";
    scgs_v05_handle handle = 0;
    uint32_t engine_code = SCGS_V05_NO_ENGINE_CODE;
    uint64_t required = 0;
    char* view = NULL;
    int result = 1;

    if (scgs_v05_abi_version() != SCGS_V05_ABI_VERSION ||
        scgs_v05_create(
            SCGS_V05_ABI_VERSION,
            config,
            (uint64_t)(sizeof(config) - 1U),
            &handle) != SCGS_V05_OK ||
        handle == 0U ||
        scgs_v05_start(handle, &engine_code) != SCGS_V05_OK ||
        engine_code != 0U ||
        scgs_v05_get_view_json(handle, 0U, NULL, 0U, &required) !=
            SCGS_V05_BUFFER_TOO_SMALL ||
        required < 2U) {
        goto cleanup;
    }

    view = (char*)malloc((size_t)required);
    if (view == NULL ||
        scgs_v05_get_view_json(handle, 0U, view, required, &required) != SCGS_V05_OK ||
        strstr(view, "\"schema_version\":2") == NULL ||
        strstr(view, "\"main_board\"") == NULL ||
        strstr(view, "\"pending_choice\"") == NULL ||
        strstr(view, "random_seed") != NULL) {
        goto cleanup;
    }
    result = 0;

cleanup:
    free(view);
    if (handle != 0U && scgs_v05_destroy(handle) != SCGS_V05_OK) {
        result = 1;
    }
    return result;
}
