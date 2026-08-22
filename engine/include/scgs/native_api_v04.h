// SPDX-License-Identifier: GPL-3.0-or-later
#ifndef SCGS_NATIVE_API_V04_H
#define SCGS_NATIVE_API_V04_H

#include <stdint.h>

#if defined(_WIN32)
#define SCGS_V04_CALL __cdecl
#if defined(SCGS_V04_BUILDING_LIBRARY)
#define SCGS_V04_API __declspec(dllexport)
#elif defined(SCGS_V04_USING_LIBRARY)
#define SCGS_V04_API __declspec(dllimport)
#else
#define SCGS_V04_API
#endif
#elif defined(__GNUC__) || defined(__clang__)
#define SCGS_V04_CALL
#define SCGS_V04_API __attribute__((visibility("default")))
#else
#define SCGS_V04_CALL
#define SCGS_V04_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef uint64_t scgs_v04_handle;
typedef uint32_t scgs_v04_native_code;

#define SCGS_V04_ABI_VERSION ((uint32_t)0x00010000U)
#define SCGS_V04_SCHEMA_VERSION ((uint32_t)1U)
#define SCGS_V04_NO_ENGINE_CODE ((uint32_t)0xFFFFFFFFU)

enum {
    SCGS_V04_OK = 0,
    SCGS_V04_INVALID_ARGUMENT = 1,
    SCGS_V04_ABI_MISMATCH = 2,
    SCGS_V04_INVALID_HANDLE = 3,
    SCGS_V04_INVALID_UTF8 = 4,
    SCGS_V04_INVALID_JSON = 5,
    SCGS_V04_SCHEMA_MISMATCH = 6,
    SCGS_V04_BUFFER_TOO_SMALL = 7,
    SCGS_V04_PAYLOAD_TOO_LARGE = 8,
    SCGS_V04_OUT_OF_MEMORY = 9,
    SCGS_V04_INTERNAL_ERROR = 10
};

SCGS_V04_API uint32_t SCGS_V04_CALL scgs_v04_abi_version(void);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_create(
    uint32_t requested_abi,
    const char* config_json,
    uint64_t config_bytes,
    scgs_v04_handle* out_handle);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_destroy(
    scgs_v04_handle handle);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_start(
    scgs_v04_handle handle,
    uint32_t* out_engine_code);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_get_view_json(
    scgs_v04_handle handle,
    uint32_t viewer,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_list_legal_actions_json(
    scgs_v04_handle handle,
    const char* query_json,
    uint64_t query_bytes,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_list_valid_targets_json(
    scgs_v04_handle handle,
    const char* query_json,
    uint64_t query_bytes,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_list_valid_slots_json(
    scgs_v04_handle handle,
    const char* query_json,
    uint64_t query_bytes,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_list_valid_donors_json(
    scgs_v04_handle handle,
    const char* query_json,
    uint64_t query_bytes,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_preview_payment_json(
    scgs_v04_handle handle,
    const char* command_json,
    uint64_t command_bytes,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_get_reaction_context_json(
    scgs_v04_handle handle,
    uint32_t viewer,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_submit_command_json(
    scgs_v04_handle handle,
    const char* command_json,
    uint64_t command_bytes,
    uint32_t* out_engine_code);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_read_events_json(
    scgs_v04_handle handle,
    uint32_t viewer,
    uint64_t after_sequence,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V04_API scgs_v04_native_code SCGS_V04_CALL scgs_v04_get_last_error(
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

#ifdef __cplusplus
} // extern "C"
#endif

#endif // SCGS_NATIVE_API_V04_H
