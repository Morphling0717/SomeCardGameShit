#include <scgs/native_api_v04.h>

#include <stdint.h>
#include <stdlib.h>
#include <string.h>

_Static_assert(sizeof(scgs_v04_handle) == sizeof(uint64_t), "handle must be 64-bit");
_Static_assert(sizeof(scgs_v04_native_code) == sizeof(uint32_t), "code must be 32-bit");

int main(void) {
    static const char config[] =
        "{\"schema_version\":1,\"player0_deck\":\"midrange\","
        "\"player1_deck\":\"advance\",\"random_seed\":7,"
        "\"first_player_mode\":1,\"shuffle_decks\":false}";
    scgs_v04_handle handle = 0;
    uint32_t engine_code = SCGS_V04_NO_ENGINE_CODE;
    uint64_t required = 0;
    char* view = NULL;
    int result = 1;

    if (scgs_v04_abi_version() != SCGS_V04_ABI_VERSION ||
        scgs_v04_create(
            SCGS_V04_ABI_VERSION,
            config,
            (uint64_t)(sizeof(config) - 1U),
            &handle) != SCGS_V04_OK ||
        handle == 0U ||
        scgs_v04_start(handle, &engine_code) != SCGS_V04_OK ||
        engine_code != 0U ||
        scgs_v04_get_view_json(handle, 0U, NULL, 0U, &required) !=
            SCGS_V04_BUFFER_TOO_SMALL ||
        required < 2U) {
        goto cleanup;
    }

    view = (char*)malloc((size_t)required);
    if (view == NULL ||
        scgs_v04_get_view_json(handle, 0U, view, required, &required) != SCGS_V04_OK ||
        strstr(view, "\"schema_version\":1") == NULL ||
        strstr(view, "\"view\":") == NULL ||
        strstr(view, "\"revision\":0") == NULL) {
        goto cleanup;
    }
    result = 0;

cleanup:
    free(view);
    if (handle != 0U && scgs_v04_destroy(handle) != SCGS_V04_OK) {
        result = 1;
    }
    return result;
}
