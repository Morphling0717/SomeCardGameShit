// SPDX-License-Identifier: GPL-3.0-or-later
#ifndef SCGS_NATIVE_API_V05_H
#define SCGS_NATIVE_API_V05_H

#include <stdint.h>

#if defined(_WIN32)
#define SCGS_V05_CALL __cdecl
#if defined(SCGS_V05_BUILDING_LIBRARY)
#define SCGS_V05_API __declspec(dllexport)
#elif defined(SCGS_V05_USING_LIBRARY)
#define SCGS_V05_API __declspec(dllimport)
#else
#define SCGS_V05_API
#endif
#elif defined(__GNUC__) || defined(__clang__)
#define SCGS_V05_CALL
#define SCGS_V05_API __attribute__((visibility("default")))
#else
#define SCGS_V05_CALL
#define SCGS_V05_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef uint64_t scgs_v05_handle;
typedef uint32_t scgs_v05_native_code;

#define SCGS_V05_ABI_VERSION ((uint32_t)0x00020000U)
#define SCGS_V05_SCHEMA_VERSION ((uint32_t)2U)
#define SCGS_V05_NO_ENGINE_CODE ((uint32_t)0xFFFFFFFFU)

enum {
    SCGS_V05_OK = 0,
    SCGS_V05_INVALID_ARGUMENT = 1,
    SCGS_V05_ABI_MISMATCH = 2,
    SCGS_V05_INVALID_HANDLE = 3,
    SCGS_V05_INVALID_UTF8 = 4,
    SCGS_V05_INVALID_JSON = 5,
    SCGS_V05_SCHEMA_MISMATCH = 6,
    SCGS_V05_BUFFER_TOO_SMALL = 7,
    SCGS_V05_PAYLOAD_TOO_LARGE = 8,
    SCGS_V05_OUT_OF_MEMORY = 9,
    SCGS_V05_INTERNAL_ERROR = 10
};

SCGS_V05_API uint32_t SCGS_V05_CALL scgs_v05_abi_version(void);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_create(
    uint32_t requested_abi,
    const char* config_json,
    uint64_t config_bytes,
    scgs_v05_handle* out_handle);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_destroy(
    scgs_v05_handle handle);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_start(
    scgs_v05_handle handle,
    uint32_t* out_engine_code);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_get_view_json(
    scgs_v05_handle handle,
    uint32_t viewer,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_list_legal_actions_json(
    scgs_v05_handle handle,
    const char* query_json,
    uint64_t query_bytes,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_list_valid_targets_json(
    scgs_v05_handle handle,
    const char* query_json,
    uint64_t query_bytes,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_list_valid_slots_json(
    scgs_v05_handle handle,
    const char* query_json,
    uint64_t query_bytes,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_list_valid_donors_json(
    scgs_v05_handle handle,
    const char* query_json,
    uint64_t query_bytes,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_preview_payment_json(
    scgs_v05_handle handle,
    const char* command_json,
    uint64_t command_bytes,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_get_reaction_context_json(
    scgs_v05_handle handle,
    uint32_t viewer,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_submit_command_json(
    scgs_v05_handle handle,
    const char* command_json,
    uint64_t command_bytes,
    uint32_t* out_engine_code);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_read_events_json(
    scgs_v05_handle handle,
    uint32_t viewer,
    uint64_t after_sequence,
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

SCGS_V05_API scgs_v05_native_code SCGS_V05_CALL scgs_v05_get_last_error(
    char* buffer,
    uint64_t capacity,
    uint64_t* required_bytes);

#ifdef __cplusplus
} // extern "C"
#endif

#endif // SCGS_NATIVE_API_V05_H
