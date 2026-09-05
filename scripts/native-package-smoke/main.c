#include <scgs/native_api_v04.h>

#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

_Static_assert(sizeof(scgs_v04_handle) == sizeof(uint64_t), "handle must be 64-bit");
_Static_assert(sizeof(scgs_v04_native_code) == sizeof(uint32_t), "code must be 32-bit");

/* Installed v04 is retired transport compatibility. The separate successful
 * gameplay fixture library is intentionally never installed. */
int main(void) {
    static const char* decks[] = {
        "midrange", "advance", "synthetic_alpha", "synthetic_beta", "oathguard", "pactmage"
    };
    size_t index;
    if (scgs_v04_abi_version() != SCGS_V04_ABI_VERSION || scgs_v04_destroy(0U) != SCGS_V04_OK) {
        return 1;
    }
    for (index = 0U; index < sizeof(decks) / sizeof(decks[0]); ++index) {
        char config[256];
        scgs_v04_handle handle = 99U;
        uint64_t required = 0U;
        char* diagnostic;
        int count = snprintf(config, sizeof(config),
            "{\"schema_version\":1,\"player0_deck\":\"%s\",\"player1_deck\":\"%s\"}",
            decks[index], decks[index]);
        if (count < 0 || (size_t)count >= sizeof(config) ||
            scgs_v04_create(SCGS_V04_ABI_VERSION, config, (uint64_t)count, &handle) !=
                SCGS_V04_SCHEMA_MISMATCH || handle != 0U ||
            scgs_v04_get_last_error(NULL, 0U, &required) != SCGS_V04_BUFFER_TOO_SMALL ||
            required < 2U || required > 4096U) {
            return 1;
        }
        diagnostic = (char*)malloc((size_t)required);
        if (diagnostic == NULL) {
            return 1;
        }
        if (scgs_v04_get_last_error(diagnostic, required, &required) != SCGS_V04_OK ||
            diagnostic[required - 1U] != '\0' || strstr(diagnostic, decks[index]) != NULL) {
            free(diagnostic);
            return 1;
        }
        free(diagnostic);
    }
    return 0;
}
